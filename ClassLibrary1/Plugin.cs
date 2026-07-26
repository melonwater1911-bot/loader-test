using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

[BepInPlugin("com.nobndl.loader","nobndlLoader","0.5.3")]
public sealed partial class NobndlLoaderPlugin : BaseUnityPlugin
{
    public static NobndlLoaderPlugin Instance;

    private const string NobndlMagic="NOBNDL";
    private const int SupportedVersion=2;
    private const bool VerboseAssetResolveLogs=false;
    private const bool VerboseMaterialSlotLogs=false;
    private const bool VerboseComponentApplyLogs=false;

    private readonly NobndlBundleCache cache=new NobndlBundleCache();
    private readonly NobndlPrefabHashes hashes=new NobndlPrefabHashes();
    private readonly NobndlMaterialFactory materialFactory=new NobndlMaterialFactory();
    private readonly List<LoadedNobndlPack> packs=new List<LoadedNobndlPack>();
    private readonly List<PendingAircraftPylonPatch> pendingPylonPatches=new List<PendingAircraftPylonPatch>();
    private readonly List<NobndlSkinFile> pendingSkins=new List<NobndlSkinFile>();

    private readonly Dictionary<string,UnityEngine.Object> runtimeObjects =
        new Dictionary<string,UnityEngine.Object>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,UnityEngine.Object> gameAssets =
        new Dictionary<string,UnityEngine.Object>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,Assembly> nobndlScriptAssemblies =
        new Dictionary<string,Assembly>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,Assembly> embeddedAssemblyCache =
        new Dictionary<string,Assembly>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<int,Sprite> runtimeSpritesFromTextures =
        new Dictionary<int,Sprite>();

    private readonly Dictionary<string,Component> runtimeComponents =
        new Dictionary<string,Component>(StringComparer.InvariantCultureIgnoreCase);

    private static readonly HashSet<string> ManagedMountNames =
        new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

    private enum InstallState
    {
        Idle,
        Installing,
        Installed,
        Failed
    }

    private Harmony harmony;
    private InstallState installState=InstallState.Idle;
    private bool encyclopediaFound;
    private int encyclopediaCount;
    private static float startTime;
    private HashSet<int> runtimeObjectIds;

    private void Awake()
    {
        startTime=Time.realtimeSinceStartup;
        InstallResolver();
        Instance=this;
        NobndlLog.Source=Logger;
        Trace("STAGE","awake begin | nobndlLoader 0.5.3");
        float t=Now();
        NobndlListPatcher.Reset();
        harmony=new Harmony("com.nobndl.loader.harmony");
        harmony.PatchAll();
        Trace("STAGE","harmony ms="+Ms(t));
        t=Now();
        LoadAllPacks();
        Trace("STAGE","loadPacks ms="+Ms(t)+" packs="+packs.Count+" bundleAssets="+cache.Assets.Count);
        t=Now();
        CompileAll();
        Trace("STAGE","compile ms="+Ms(t)+" assemblies="+nobndlScriptAssemblies.Count);
        t=Now();
        QueuePylons();
        Trace("STAGE","queuePylons ms="+Ms(t));
        Trace("STAGE","awake end ms="+Ms(startTime));
    }

    private void OnDestroy()
    {
        if (harmony!=null)
        {
            harmony.UnpatchSelf();
            harmony=null;
        }
        NobndlListPatcher.Reset();
    }

    private bool Installed
    {
        get { return installState==InstallState.Installed; }
    }

    private bool InstallStarted
    {
        get { return installState==InstallState.Installing || installState==InstallState.Installed; }
    }

    internal void OnGameLoaded()
    {
        if (InstallStarted)
            return;
        installState=InstallState.Installing;
        try
        {
            Install("MainMenu.Loaded");
            installState=InstallState.Installed;
            CommitSkins();
        }
        catch (Exception ex)
        {
            installState=InstallState.Failed;
            DiscardSkins();
            LogError("Install failed, packs are not active: "+ex);
        }
    }

    internal void OnEncyclopediaRebuilt()
    {
        if (!Installed)
            return;
        UpdateEncyclopedia("Encyclopedia.AfterLoad");
        hashes.RequestRescan();
        AssignHashes();
    }

    private void StageSkins(NobndlFile file,string packId)
    {
        List<NobndlSkinFile> staged=new List<NobndlSkinFile>();
        HashSet<string> seen=new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        for (int i=0; i<file.Entries.Count; i++)
        {
            NobndlEntry entry=file.Entries[i];
            if (entry==null || entry.Type!=NobndlEntryType.SkinFile)
                continue;
            if (!NobndlSkins.Validate(entry.Name,entry.Data,packId))
                throw new Exception("Skin entry rejected: "+entry.Name);
            if (!seen.Add(entry.Name.Replace('\\','/')))
                throw new Exception("Duplicate skin entry in pack: "+entry.Name);
            NobndlSkinFile skin=new NobndlSkinFile();
            skin.PackId=packId;
            skin.RelativePath=entry.Name.Replace('\\','/');
            skin.Data=entry.Data;
            staged.Add(skin);
        }
        if (staged.Count==0)
            return;
        pendingSkins.AddRange(staged);
        Log("Skin files read: pack="+packId+" files="+staged.Count+" (written to disk after install succeeds)");
    }

    private void CommitSkins()
    {
        if (pendingSkins.Count==0)
            return;
        Dictionary<string,List<NobndlSkinFile>> byPack =
            new Dictionary<string,List<NobndlSkinFile>>(StringComparer.InvariantCultureIgnoreCase);
        for (int i=0; i<pendingSkins.Count; i++)
        {
            NobndlSkinFile skin=pendingSkins[i];
            List<NobndlSkinFile> list;
            if (!byPack.TryGetValue(skin.PackId,out list))
            {
                list=new List<NobndlSkinFile>();
                byPack[skin.PackId]=list;
            }
            list.Add(skin);
        }
        foreach (KeyValuePair<string,List<NobndlSkinFile>> pair in byPack)
            NobndlSkins.Install(pair.Key,pair.Value);
        pendingSkins.Clear();
    }

    private void DiscardSkins()
    {
        HashSet<string> packIds=new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        for (int i=0; i<pendingSkins.Count; i++)
            packIds.Add(pendingSkins[i].PackId);
        foreach (string packId in packIds)
            NobndlSkins.Discard(packId);
        if (pendingSkins.Count>0)
            LogWarning("Skins not written, install did not succeed: files="+pendingSkins.Count);
        pendingSkins.Clear();
    }

    private void ApplyListPatches()
    {
        int registered=0;

        for (int p=0; p<packs.Count; p++)
        {
            LoadedNobndlPack pack=packs[p];

            if (pack==null || pack.Manifest==null || pack.Manifest.listPatches==null)
                continue;

            NobndlListPatchManifestEntry[] entries=pack.Manifest.listPatches;

            for (int i=0; i<entries.Length; i++)
            {
                if (RegisterListPatch(entries[i],pack.Manifest.packId))
                    registered++;
            }
        }

        NobndlListPatcher.InstallHooks(harmony);

        if (registered>0)
            LogWarning("List patches registered: "+registered);
    }

    private bool RegisterListPatch(NobndlListPatchManifestEntry entry,string packId)
    {
        if (entry==null)
            return false;

        Type targetType=ResolveType(entry.targetTypeName,entry.targetAssemblyName);

        if (targetType==null)
        {
            LogError("List patch: unknown target type "+entry.targetTypeName+" | pack="+packId);
            return false;
        }

        UnityEngine.Object value=ResolveRuntimeObject(entry.valueName) ??
                                 ResolveBundleAsset(entry.valueName) ??
                                 FindAssetByName(entry.valueName,typeof(UnityEngine.Object));

        if (value==null)
        {
            LogError("List patch: value not found '"+entry.valueName+"' | pack="+packId +
                     " - no pack object, bundle asset or game asset has that name");
            return false;
        }

        return NobndlListPatcher.Register(targetType,entry.listName,value,packId);
    }

    private void AssignHashes()
    {
        Type idType=ResolveType("Mirage.NetworkIdentity","Mirage");
        if (idType==null)
        {
            LogWarning("Mirage.NetworkIdentity not found, PrefabHash assignment skipped");
            return;
        }
        hashes.Assign(cache,idType);
    }

    private void Install(string reason)
    {
        float begin=Now();
        Trace("STAGE","install begin reason="+reason);
        float t=Now();
        BuildAssetIndex("install");
        Trace("STAGE","assetIndex ms="+Ms(t)+" keys="+gameAssets.Count);
        cache.ReportNonPrefabRoots();
        t=Now();
        BuildPack();
        Trace("STAGE","buildPack ms="+Ms(t)+" objects="+runtimeObjects.Count+" components="+runtimeComponents.Count);
        t=Now();
        UpdateEncyclopedia("install");
        Trace("STAGE","encyclopedia ms="+Ms(t)+" found="+encyclopediaFound+" instances="+encyclopediaCount);
        t=Now();
        ApplyAllPylons("install");
        Trace("STAGE","pylons ms="+Ms(t)+" unapplied="+CountPendingPatches());
        t=Now();
        ApplyListPatches();
        Trace("STAGE","listPatches ms="+Ms(t));
        t=Now();
        AssignHashes();
        Trace("STAGE","hashes ms="+Ms(t)+" identities="+hashes.Count);
        Trace("STAGE","install end ms="+Ms(begin)+" | "+StateLine());
    }


    private string StateLine()
    {
        return "encyclopedia="+encyclopediaFound +
               " gameAssets="+gameAssets.Count +
               " objects="+runtimeObjects.Count +
               " components="+runtimeComponents.Count +
               " pylonsLeft="+CountPendingPatches();
    }

    private static float Now()
    {
        return Time.realtimeSinceStartup;
    }

    private static string Ms(float since)
    {
        return ((Time.realtimeSinceStartup-since)*1000f).ToString("F1",CultureInfo.InvariantCulture);
    }

    private static void Trace(string kind,string message)
    {
        float elapsed=Time.realtimeSinceStartup-startTime;
        LogWarning("[T+"+elapsed.ToString("F3",CultureInfo.InvariantCulture)+"] "+kind+" "+message);
    }





    private void LoadAllPacks()
    {
        string pluginsFolder=Paths.PluginPath;
        Log("Searching .nobndl packs in plugins folder: "+pluginsFolder);
        if (!Directory.Exists(pluginsFolder))
        {
            LogError("Plugins folder does not exist: "+pluginsFolder);
            return;
        }
        string[] files=Directory
            .GetFiles(pluginsFolder,"*.nobndl",SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.InvariantCultureIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
        Log("Found .nobndl packs: "+files.Length);
        for (int i=0; i<files.Length; i++)
        {
            try
            {
                LoadedNobndlPack pack=LoadPack(files[i]);
                if (pack!=null)
                {
                    packs.Add(pack);
                    Log("Loaded pack: "+pack.Manifest.packId);
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to load pack: "+files[i]+"\n"+ex);
            }
        }
    }

    private LoadedNobndlPack LoadPack(string path)
    {
        Log("Reading pack: "+path);
        NobndlFile file=ReadPackFile(path);
        NobndlEntry manifestEntry=file.FindEntry("nobndl.json");
        if (manifestEntry==null)
            throw new Exception("nobndl.json entry not found");
        string manifestJson=Encoding.UTF8.GetString(manifestEntry.Data);
        NobndlManifest manifest=JsonConvert.DeserializeObject<NobndlManifest>(manifestJson);
        if (manifest==null)
            throw new Exception("Failed to parse nobndl.json");
        if (manifest.scripts==null)
            manifest.scripts=new string[0];
        if (manifest.assets==null)
            manifest.assets=new string[0];
        if (manifest.scriptableObjects==null)
            manifest.scriptableObjects=new NobndlScriptableObjectRecord[0];
        if (manifest.prefabScriptBindings==null)
            manifest.prefabScriptBindings=new NobndlPrefabScriptBindingRecord[0];
        if (manifest.aircraftPylonPatches==null)
            manifest.aircraftPylonPatches=new AircraftPylonPatchManifestEntry[0];
        FixManifest(manifest);
        if (string.IsNullOrEmpty(manifest.bundleFile))
            throw new Exception("Manifest bundleFile is empty");
        LogManifest(manifest);
        NobndlEntry bundleEntry=file.FindEntry(manifest.bundleFile);
        if (bundleEntry==null)
            throw new Exception("Bundle entry not found: "+manifest.bundleFile);
        AssetBundle bundle=AssetBundle.LoadFromMemory(bundleEntry.Data);
        if (bundle==null)
            throw new Exception("AssetBundle.LoadFromMemory returned null: "+manifest.bundleFile);
        Log("AssetBundle loaded: "+manifest.bundleFile);
        LoadedNobndlPack pack=new LoadedNobndlPack();
        pack.Path=path;
        pack.File=file;
        pack.Manifest=manifest;
        pack.Bundle=bundle;
        pack.ScriptEntries=file.Entries.Where(x => x.Type==NobndlEntryType.CSharpSource).ToArray();
        cache.AddBundle(bundle);
        Log("C# source entries: "+pack.ScriptEntries.Length);
        for (int i=0; i<pack.ScriptEntries.Length; i++)
            Log("C# source: "+pack.ScriptEntries[i].Name+" | "+pack.ScriptEntries[i].Data.Length+" bytes");
        StageSkins(file,manifest.packId);
        return pack;
    }
}
