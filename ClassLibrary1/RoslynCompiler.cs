using BepInEx;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using static NobndlLog;
using static NobndlUtil;

internal static class NobndlRoslynCompiler
{
    private static readonly string[] ReferenceNamePrefixes =
    {
        "UnityEngine","Unity.","Assembly-CSharp","System","Microsoft",
        "mscorlib","netstandard","0Harmony","Harmony","BepInEx",
        "Mirage","Newtonsoft.Json","Rewired_Core"
    };

    private static List<MetadataReference> cachedReferences;

    internal static byte[] Compile(
        string assemblyName,
        List<string> fileNames,
        List<string> sources,
        Action<string> logInfo,
        Action<string> logWarning,
        List<string> diags)
    {
        CSharpParseOptions parseOptions=CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        List<SyntaxTree> trees=new List<SyntaxTree>();
        for (int i=0; i<sources.Count; i++)
            trees.Add(CSharpSyntaxTree.ParseText(sources[i],parseOptions,fileNames[i],Encoding.UTF8));
        List<MetadataReference> references=GetReferences(logInfo,logWarning);
        CSharpCompilationOptions options=new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default
        );
        CSharpCompilation compilation=CSharpCompilation.Create(assemblyName,trees,references,options);
        using (MemoryStream peStream=new MemoryStream())
        {
            EmitResult result=compilation.Emit(peStream);
            foreach (Diagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity==DiagnosticSeverity.Error ||
                    diagnostic.Severity==DiagnosticSeverity.Warning)
                {
                    diags.Add(diagnostic.Severity+": "+diagnostic);
                }
            }
            if (!result.Success)
                return null;
            logInfo("Roslyn emit ok: assembly="+assemblyName +
                    " sources="+trees.Count +
                    " refs="+references.Count);
            return peStream.ToArray();
        }
    }

    private static List<MetadataReference> GetReferences(Action<string> logInfo,Action<string> logWarning)
    {
        if (cachedReferences!=null)
            return cachedReferences;
        List<MetadataReference> references=new List<MetadataReference>();
        HashSet<string> paths=new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        Assembly[] assemblies=AppDomain.CurrentDomain.GetAssemblies();
        for (int i=0; i<assemblies.Length; i++)
            AddReferencePath(references,paths,GetAssemblyLocation(assemblies[i]),logWarning);
        HashSet<string> directories=CollectDirectories(assemblies,logWarning);
        foreach (string directory in directories)
        {
            string[] files;
            try
            {
                files=Directory.GetFiles(directory,"*.dll",SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                logWarning("Roslyn reference scan failed: dir="+directory+" | "+ex.Message);
                continue;
            }
            for (int i=0; i<files.Length; i++)
            {
                if (IsRefCandidate(Path.GetFileName(files[i])))
                    AddReferencePath(references,paths,files[i],logWarning);
            }
        }
        cachedReferences=references;
        logInfo("Roslyn metadata refs built: refs="+references.Count+" dirs="+directories.Count);
        return references;
    }

    private static HashSet<string> CollectDirectories(Assembly[] assemblies,Action<string> logWarning)
    {
        HashSet<string> directories=new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        for (int i=0; i<assemblies.Length; i++)
        {
            string location=GetAssemblyLocation(assemblies[i]);
            if (location.Length>0)
                AddDirectory(directories,Path.GetDirectoryName(location),logWarning);
        }
        try
        {
            AddDirectory(directories,Paths.ManagedPath,logWarning);
            AddDirectory(directories,Paths.BepInExRootPath,logWarning);
            AddDirectory(directories,Paths.PluginPath,logWarning);
        }
        catch (Exception ex)
        {
            logWarning("Roslyn reference dirs: BepInEx paths unavailable: "+ex.Message);
        }
        return directories;
    }

    private static void AddDirectory(HashSet<string> directories,string directory,Action<string> logWarning)
    {
        if (string.IsNullOrEmpty(directory))
            return;
        try
        {
            string fullPath=Path.GetFullPath(directory);
            if (Directory.Exists(fullPath))
                directories.Add(fullPath);
        }
        catch (Exception ex)
        {
            logWarning("Roslyn reference dir rejected: "+directory+" | "+ex.Message);
        }
    }

    private static void AddReferencePath(List<MetadataReference> references,HashSet<string> paths,string location,Action<string> logWarning)
    {
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
            return;
        string fullPath;
        try
        {
            fullPath=Path.GetFullPath(location);
        }
        catch (Exception ex)
        {
            logWarning("Roslyn reference rejected: "+location+" | "+ex.Message);
            return;
        }
        if (!paths.Add(fullPath))
            return;
        try
        {
            references.Add(MetadataReference.CreateFromFile(fullPath));
        }
        catch (Exception ex)
        {
            paths.Remove(fullPath);
            logWarning("Roslyn reference skipped: "+fullPath+" | "+ex.Message);
        }
    }

    private static string GetAssemblyLocation(Assembly assembly)
    {
        if (assembly==null || assembly.IsDynamic)
            return "";
        return assembly.Location ?? "";
    }

    private static bool IsRefCandidate(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;
        for (int i=0; i<ReferenceNamePrefixes.Length; i++)
        {
            if (fileName.StartsWith(ReferenceNamePrefixes[i],StringComparison.InvariantCultureIgnoreCase))
                return true;
        }
        return false;
    }
}
