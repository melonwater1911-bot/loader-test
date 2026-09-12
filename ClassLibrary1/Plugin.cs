using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using NuclearOption.SavedMission;
using UnityEngine;
using static Logs;

[BepInPlugin("com.nucmod.loader","nucmod",Version)]
public sealed partial class Loader : BaseUnityPlugin
{
    public const string Version="0.9.0";
    public static Loader Instance;

    private const string PackHeader="NUCMOD";
    private const int SupportedVersion=5;

    private readonly BundleCache cache=new BundleCache();
    private readonly PrefabHashes hashes=new PrefabHashes();
    private readonly AssignMats mats=new AssignMats();
    private readonly List<Pack> packs=new List<Pack>();
    private readonly Dictionary<string,UnityEngine.Object> runtimeObjects=new Dictionary<string,UnityEngine.Object>(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<BindingRecord,Component> runtimeComponents=new Dictionary<BindingRecord,Component>();
    private readonly Dictionary<string,Assembly> packAssemblies=new Dictionary<string,Assembly>(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string,Assembly> embedded=new Dictionary<string,Assembly>(StringComparer.InvariantCultureIgnoreCase);

    internal string lobbyTag="";

    private void Awake()
    {
        Instance=this;
        Logs.Source=Logger;
        AppDomain.CurrentDomain.AssemblyResolve+=OnAssemblyNeeded;
        new Harmony("com.nucmod.loader.harmony").PatchAll();
        foreach (string file in Directory.GetFiles(Paths.PluginPath,"*.nucmod",SearchOption.AllDirectories).OrderBy(x => x)) packs.Add(LoadPack(file));
        List<string> tags=packs.Select(p => p.Manifest.packId+"-"+p.Manifest.packVersion).OrderBy(x => x,StringComparer.OrdinalIgnoreCase).ToList();
        lobbyTag="_nucmod"+Version+(tags.Count>0 ? "+"+string.Join("+",tags.ToArray()) : "");
        for (int i=0; i<packs.Count; i++) Compile(packs[i]);
    }

    internal void OnGameLoaded()
    {
        float began=Time.realtimeSinceStartup;
        BuildPack();
        UpdateEncyclopedia();
        ApplyPylons();
        ApplyListPatches();
        hashes.Assign(cache);
        InstallFiles(NuclearOption.AddressablePaths.AppDataSkinPath,EntryType.SkinFile);
        InstallFiles(MissionGroup.UserGroup.UserMissionDirectory,EntryType.MissionFile);
        Log("Installed "+packs.Count+" pack(s) in "+((Time.realtimeSinceStartup-began)*1000f).ToString("F0")+" ms: objects="+runtimeObjects.Count+" components="+runtimeComponents.Count+" lobby="+lobbyTag);
    }

    private Pack LoadPack(string path)
    {
        var pack = new Pack();
        long bundleOffset;
        pack.Entries=ReadEntries(path,out bundleOffset);
        pack.Manifest=JsonConvert.DeserializeObject<Manifest>(Encoding.UTF8.GetString(pack.Entries.First(x => x.Type==EntryType.ManifestJson).Data));
        pack.Bundle=AssetBundle.LoadFromFile(path,0,(ulong)bundleOffset);
        cache.Add(pack.Bundle);
        Manifest m=pack.Manifest;
        Log("Pack "+m.packId+" "+m.packVersion+": "+Path.GetFileName(path)+" | so="+m.scriptableObjects.Length+" bindings="+m.prefabScriptBindings.Length+" pylons="+m.aircraftPylonPatches.Length+" lists="+m.listPatches.Length+" scripts="+Count(pack,EntryType.CSharpSource)+" skins="+Count(pack,EntryType.SkinFile)+" missions="+Count(pack,EntryType.MissionFile));
        return pack;
    }

    private static int Count(Pack pack,EntryType type)
    {
        return pack.Entries.Count(x => x.Type==type);
    }

    private void InstallFiles(string root,EntryType type)
    {
        int written=0;
        int total=0;
        for (int p=0; p<packs.Count; p++)
        {
            List<PackEntry> entries=packs[p].Entries;
            for (int i=0; i<entries.Count; i++)
            {
                if(entries[i].Type!=type) continue;
                total++;
                string target=Path.Combine(root,entries[i].Name);
                if(File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(entries[i].Data)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target,entries[i].Data);
                written++;
            }
        }
        if(written>0) Log(type+": wrote "+written+" of "+total+" files to "+root);
    }
}
