using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using static Logs;

partial class Loader
{
    private Assembly OnAssemblyNeeded(object sender,ResolveEventArgs args)
    {
        string name=new AssemblyName(args.Name).Name;
        Assembly found;
        if(embedded.TryGetValue(name,out found)) return found;
        Assembly self=Assembly.GetExecutingAssembly();
        foreach (string resource in self.GetManifestResourceNames())
        {
            if(!resource.EndsWith(name+".dll",StringComparison.InvariantCultureIgnoreCase)) continue;
            using (Stream stream=self.GetManifestResourceStream(resource))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                found=Assembly.Load(copy.ToArray());
            }
            break;
        }
        embedded[name]=found;
        return found;
    }

    private void Compile(Pack pack)
    {
        var names = new List<string>();
        var sources = new List<string>();
        for (int i=0; i<pack.Entries.Count; i++)
        {
            if(pack.Entries[i].Type!=EntryType.CSharpSource) continue;
            names.Add(pack.Entries[i].Name);
            sources.Add(Encoding.UTF8.GetString(pack.Entries[i].Data));
        }
        if(sources.Count==0) return;
        string packId=pack.Manifest.packId;
        var diagnostics = new List<string>();
        byte[] bytes=RoslynCompiler.Compile("nucmod_"+packId,names,sources,diagnostics);
        for (int i=0; i<diagnostics.Count; i++) if(bytes==null) LogError(packId+": "+diagnostics[i]); else LogWarning(packId+": "+diagnostics[i]);
        if(bytes==null) return;
        Assembly assembly=Assembly.Load(bytes);
        packAssemblies["nucmod:"+packId]=assembly;
        Log("Compiled "+packId+": "+sources.Count+" files, "+assembly.GetTypes().Length+" types");
    }
}
