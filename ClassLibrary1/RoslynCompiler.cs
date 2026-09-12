using BepInEx;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

static class RoslynCompiler
{
    private static List<MetadataReference> refs;

    public static byte[] Compile(string assemblyName,List<string> fileNames,List<string> sources,List<string> diags)
    {
        CSharpParseOptions parse=CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>();
        for (int i=0; i<sources.Count; i++) trees.Add(CSharpSyntaxTree.ParseText(sources[i],parse,fileNames[i],Encoding.UTF8));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel: OptimizationLevel.Release,assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default);
        CSharpCompilation compilation=CSharpCompilation.Create(assemblyName,trees,References(),options);
        using (var pe = new MemoryStream())
        {
            EmitResult result=compilation.Emit(pe);
            foreach (Diagnostic d in result.Diagnostics) if(d.Severity>=DiagnosticSeverity.Warning) diags.Add(d.Severity+": "+d);
            return result.Success ? pe.ToArray() : null;
        }
    }

    private static List<MetadataReference> References()
    {
        if(refs!=null) return refs;
        refs=new List<MetadataReference>();
        var paths = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(!a.IsDynamic && a.Location.Length>0) paths.Add(Path.GetFullPath(a.Location));
        foreach (string file in Directory.GetFiles(Paths.ManagedPath,"*.dll")) paths.Add(Path.GetFullPath(file));
        foreach (string path in paths) refs.Add(MetadataReference.CreateFromFile(path));
        return refs;
    }
}
