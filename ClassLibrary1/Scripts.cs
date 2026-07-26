using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{

    private void InstallResolver()
    {
        AppDomain.CurrentDomain.AssemblyResolve-=OnAssemblyNeeded;
        AppDomain.CurrentDomain.AssemblyResolve+=OnAssemblyNeeded;
        LogEmbeddedNames(false);
    }


    private void LogEmbeddedNames(bool listThem)
    {
        try
        {
            string[] names=Assembly.GetExecutingAssembly().GetManifestResourceNames();
            int count=0;
            for (int i=0; i<names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i]) || !names[i].EndsWith(".dll",StringComparison.InvariantCultureIgnoreCase))
                    continue;
                count++;
                if (listThem)
                    LogWarning("Embedded DLL: "+names[i]);
            }
            if (!listThem)
                LogWarning("Assembly resolver installed, embedded DLLs: "+count);
        }
        catch (Exception ex)
        {
            LogSuppressed("LogEmbeddedNames",ex);
        }
    }

    private Assembly OnAssemblyNeeded(object sender,ResolveEventArgs args)
    {
        if (args==null || string.IsNullOrEmpty(args.Name))
            return null;
        string requestedName="";
        try
        {
            requestedName=new AssemblyName(args.Name).Name;
        }
        catch
        {
            requestedName="";
        }
        if (string.IsNullOrEmpty(requestedName))
            return null;
        return LoadEmbedded(requestedName,false);
    }

    private Assembly LoadEmbedded(string simpleName,bool logFailure)
    {
        if (string.IsNullOrEmpty(simpleName))
            return null;
        Assembly cached;
        if (embeddedAssemblyCache.TryGetValue(simpleName,out cached))
            return cached;
        Assembly alreadyLoaded=FindAssembly(simpleName);
        if (alreadyLoaded!=null)
        {
            embeddedAssemblyCache[simpleName]=alreadyLoaded;
            return alreadyLoaded;
        }
        string dllName=simpleName+".dll";
        Assembly self=Assembly.GetExecutingAssembly();
        string[] resourceNames=self.GetManifestResourceNames();
        string resourceName=null;
        for (int i=0; i<resourceNames.Length; i++)
        {
            string name=resourceNames[i];
            if (string.IsNullOrEmpty(name))
                continue;
            if (name.EndsWith(dllName,StringComparison.InvariantCultureIgnoreCase))
            {
                resourceName=name;
                break;
            }
        }
        if (string.IsNullOrEmpty(resourceName))
        {
            if (logFailure)
            {
                LogWarning("Embedded dependency not found as resource: "+dllName);
                LogEmbeddedNames(true);
            }
            return null;
        }
        try
        {
            using (Stream stream=self.GetManifestResourceStream(resourceName))
            {
                if (stream==null)
                    return null;
                byte[] bytes=new byte[stream.Length];
                int offset=0;
                while (offset<bytes.Length)
                {
                    int read=stream.Read(bytes,offset,bytes.Length-offset);
                    if (read<=0)
                        break;
                    offset+=read;
                }
                Assembly assembly=Assembly.Load(bytes);
                if (assembly!=null)
                {
                    embeddedAssemblyCache[simpleName]=assembly;
                    LogWarning("Loaded embedded dependency: "+simpleName+" resource="+resourceName+" bytes="+bytes.Length);
                }
                return assembly;
            }
        }
        catch (Exception ex)
        {
            if (logFailure)
                LogError("Failed to load embedded dependency: "+simpleName+" error="+ex);
            return null;
        }
    }

    private Assembly FindAssembly(string simpleName)
    {
        if (string.IsNullOrEmpty(simpleName))
            return null;
        Assembly[] assemblies=AppDomain.CurrentDomain.GetAssemblies();
        for (int i=0; i<assemblies.Length; i++)
        {
            Assembly assembly=assemblies[i];
            if (assembly==null)
                continue;
            try
            {
                AssemblyName name=assembly.GetName();
                if (name!=null && string.Equals(name.Name,simpleName,StringComparison.InvariantCultureIgnoreCase))
                    return assembly;
            }
            catch (Exception ex)
            {
                LogSuppressed("FindAssembly",ex);
            }
        }
        return null;
    }


    private void CompileAll()
    {
        int packsWithScripts=0;
        int compiled=0;
        bool roslynReady=false;
        LogWarning("Compiling pack scripts: packs="+packs.Count);
        for (int i=0; i<packs.Count; i++)
        {
            LoadedNobndlPack pack=packs[i];
            if (pack==null || pack.ScriptEntries==null || pack.ScriptEntries.Length==0)
                continue;
            packsWithScripts++;
            if (!roslynReady)
            {
                roslynReady=LoadRoslyn();
                if (!roslynReady)
                    break;
            }
            if (CompilePack(pack))
                compiled++;
        }
        LogWarning("Compile summary: packsWithScripts="+packsWithScripts +
                   " compiled="+compiled +
                   " registeredAssemblies="+nobndlScriptAssemblies.Count);
    }

    private bool CompilePack(LoadedNobndlPack pack)
    {
        if (pack==null || pack.Manifest==null || pack.ScriptEntries==null || pack.ScriptEntries.Length==0)
            return false;
        string packId=!string.IsNullOrEmpty(pack.Manifest.packId)
            ? pack.Manifest.packId
            : Path.GetFileNameWithoutExtension(pack.Path);
        List<string> fileNames=new List<string>();
        List<string> sources=new List<string>();
        for (int i=0; i<pack.ScriptEntries.Length; i++)
        {
            NobndlEntry entry=pack.ScriptEntries[i];
            if (entry==null || entry.Data==null || entry.Data.Length==0)
                continue;
            fileNames.Add(string.IsNullOrEmpty(entry.Name)
                ? ("script_"+i.ToString(CultureInfo.InvariantCulture)+".cs")
                : entry.Name);
            sources.Add(Encoding.UTF8.GetString(entry.Data));
        }
        if (sources.Count==0)
        {
            LogWarning("Compile skipped, no sources: pack="+packId);
            return false;
        }
        string assemblyName="nobndl_"+SafeName(packId)+"_"+HashSources(pack.ScriptEntries);
        List<string> diagnostics=new List<string>();
        byte[] assemblyBytes;
        try
        {
            assemblyBytes=NobndlRoslynCompiler.Compile(
                assemblyName,
                fileNames,
                sources,
                message => Log(message),
                message => LogWarning(message),
                diagnostics
            );
        }
        catch (Exception ex)
        {
            LogError("Compile threw: pack="+packId+" "+ex);
            return false;
        }
        if (assemblyBytes==null || assemblyBytes.Length==0)
        {
            LogError("Compile failed: pack="+packId);
            for (int i=0; i<diagnostics.Count; i++)
                LogError("  "+diagnostics[i]);
            return false;
        }
        for (int i=0; i<diagnostics.Count; i++)
            LogWarning("Compiler says: pack="+packId+" "+diagnostics[i]);
        Assembly assembly=Assembly.Load(assemblyBytes);
        pack.ScriptAssembly=assembly;
        pack.ScriptAssemblyName=assembly.GetName().Name;
        RegisterAssembly(packId,assembly);
        RegisterAssembly("nobndl:"+packId,assembly);
        RegisterAssembly(pack.ScriptAssemblyName,assembly);
        RegisterAssembly(assemblyName,assembly);
        LogWarning("Compiled: pack="+packId +
                   " assembly="+assembly.FullName +
                   " sources="+sources.Count +
                   " bytes="+assemblyBytes.Length);
        LogScriptTypes(packId,assembly);
        return true;
    }

    private bool LoadRoslyn()
    {
        string[] names =
        {
            "System.Runtime.CompilerServices.Unsafe",
            "System.Memory",
            "System.Buffers",
            "System.Numerics.Vectors",
            "System.Collections.Immutable",
            "System.Reflection.Metadata",
            "System.Threading.Tasks.Extensions",
            "System.Text.Encoding.CodePages",
            "Microsoft.CodeAnalysis",
            "Microsoft.CodeAnalysis.CSharp"
        };
        bool ok=true;
        for (int i=0; i<names.Length; i++)
        {
            Assembly assembly=LoadEmbedded(names[i],true);
            if (assembly==null)
            {
                LogError("Missing embedded Roslyn dependency: "+names[i]);
                ok=false;
            }
        }
        return ok;
    }

    private void RegisterAssembly(string key,Assembly assembly)
    {
        if (string.IsNullOrEmpty(key) || assembly==null)
            return;
        nobndlScriptAssemblies[NormalizeName(key)]=assembly;
    }

    private void LogScriptTypes(string packId,Assembly assembly)
    {
        if (assembly==null)
            return;
        Type[] types;
        try
        {
            types=assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types=ex.Types.Where(x => x!=null).ToArray();
            if (ex.LoaderExceptions!=null)
            {
                for (int i=0; i<ex.LoaderExceptions.Length; i++)
                {
                    Exception loaderException=ex.LoaderExceptions[i];
                    if (loaderException!=null)
                        LogWarning("Type load error: pack="+packId+" "+loaderException.Message);
                }
            }
        }
        catch (Exception ex)
        {
            LogWarning("Could not list compiled types: pack="+packId+" error="+ex.Message);
            return;
        }
        for (int i=0; i<types.Length; i++)
        {
            Type type=types[i];
            if (type==null)
                continue;
            LogWarning("Compiled type: pack="+packId +
                       " type="+type.FullName +
                       " base="+(type.BaseType!=null ? type.BaseType.FullName : "<null>"));
        }
    }

    private string HashSources(NobndlEntry[] entries)
    {
        if (entries==null || entries.Length==0)
            return "empty";
        using (SHA256 sha=SHA256.Create())
        {
            for (int i=0; i<entries.Length; i++)
            {
                NobndlEntry entry=entries[i];
                if (entry==null)
                    continue;
                byte[] nameBytes=Encoding.UTF8.GetBytes(entry.Name ?? "");
                sha.TransformBlock(nameBytes,0,nameBytes.Length,null,0);
                if (entry.Data!=null && entry.Data.Length>0)
                    sha.TransformBlock(entry.Data,0,entry.Data.Length,null,0);
            }
            sha.TransformFinalBlock(new byte[0],0,0);
            byte[] hash=sha.Hash;
            StringBuilder sb=new StringBuilder();
            for (int i=0; i<8 && i<hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    private string SafeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "pack";
        StringBuilder sb=new StringBuilder();
        for (int i=0; i<value.Length; i++)
        {
            char c=value[i];
            if ((c>='a' && c<='z') ||
                (c>='A' && c<='Z') ||
                (c>='0' && c<='9') ||
                c=='_')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        if (sb.Length==0)
            return "pack";
        return sb.ToString();
    }
}
