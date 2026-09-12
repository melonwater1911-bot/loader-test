using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using static Logs;
using static Util;

partial class Loader
{
    private sealed class AssetSpot
    {
        public AssetBundle Bundle;
        public string Path;
        public bool FromPack;
    }

    private readonly Dictionary<string,List<AssetSpot>> assetsByName=new Dictionary<string,List<AssetSpot>>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,AssetSpot> assetsByPath=new Dictionary<string,AssetSpot>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,AssetBundle> bundlesByName=new Dictionary<string,AssetBundle>(StringComparer.InvariantCultureIgnoreCase);

    private readonly Dictionary<string,string> rememberedSpots=new Dictionary<string,string>(StringComparer.InvariantCultureIgnoreCase);

    private const string AssetMapFile="nucmod_assetmap.bin";
    private const int AssetMapVersion=1;
    private const string ResourcesSpot="~resources";

    private bool assetIndexReady;
    private bool rememberedSpotsDirty;
    private string gameStamp;

    private UnityEngine.Object FindAsset(FieldRecord record,Type targetType)
    {
        if (record==null || targetType==null)
            return null;
        if (!assetIndexReady)
        {
            LogError("Asset lookup before the index was built, this is a loader bug");
            return null;
        }
        if (UseGameAsset(targetType))
        {
            UnityEngine.Object preferred=FindAssetByName(record.objectName,targetType) ?? FindAssetByName(record.value,targetType) ?? FindAssetByName(record.assetName,targetType);
            if (preferred!=null)
                return preferred;
        }
        if (!string.IsNullOrEmpty(record.assetPath))
        {
            UnityEngine.Object byPath=LoadByPath(record.assetPath,targetType);
            if (byPath!=null)
            {
                return byPath;
            }
        }
        return FindAssetByName(record.objectName,targetType) ??
               FindAssetByName(record.value,targetType) ??
               FindAssetByName(record.assetName,targetType);
    }

    private UnityEngine.Object FindAssetByName(string name,Type targetType)
    {
        if (string.IsNullOrEmpty(name) || targetType==null)
            return null;
        if (!assetIndexReady)
        {
            LogError("Asset lookup before the index was built, this is a loader bug");
            return null;
        }
        string memoKey=AssetKey(targetType,name);
        UnityEngine.Object obj;
        if (gameAssets.TryGetValue(memoKey,out obj) && obj!=null)
            return obj;
        if (UseGameAsset(targetType))
        {
            obj=FindInMemory(name,targetType,true);
            if (obj!=null)
            {
                Remember(memoKey,obj,ResourcesSpot);
                return obj;
            }
        }
        string spot;
        if (rememberedSpots.TryGetValue(memoKey,out spot))
        {
            obj=LoadSpot(spot,name,targetType);
            if (obj!=null)
            {
                gameAssets[memoKey]=obj;
                return obj;
            }
            rememberedSpots.Remove(memoKey);
            rememberedSpotsDirty=true;
        }
        obj=LoadFromCatalog(name,targetType,out spot);
        if (obj!=null)
        {
            Remember(memoKey,obj,spot);
            return obj;
        }
        obj=FindInMemory(name,targetType);
        if (obj!=null)
        {
            Remember(memoKey,obj,ResourcesSpot);
            return obj;
        }
        LogWarning("GameAsset not found: name="+name+" | type="+targetType.FullName+" | catalog="+assetsByName.Count);
        return null;
    }

    private void Remember(string memoKey,UnityEngine.Object obj,string spot)
    {
        gameAssets[memoKey]=obj;
        if (string.IsNullOrEmpty(spot))
            return;
        string existing;
        if (rememberedSpots.TryGetValue(memoKey,out existing) && existing==spot)
            return;
        rememberedSpots[memoKey]=spot;
        rememberedSpotsDirty=true;
    }

    private UnityEngine.Object LoadFromCatalog(string name,Type targetType,out string spot)
    {
        spot=null;
        List<AssetSpot> spots;
        if (!assetsByName.TryGetValue(NormalizeAssetKey(name),out spots))
            return null;
        for (int i=0; i<spots.Count; i++)
        {
            AssetSpot candidate=spots[i];
            if (candidate.FromPack && UseGameAsset(targetType))
                continue;
            UnityEngine.Object obj=LoadFromBundle(candidate.Bundle,candidate.Path,targetType);
            if (obj==null)
                continue;
            if (UseGameAsset(targetType) && IsPackAsset(obj))
                continue;
            spot=candidate.Bundle.name+"|"+candidate.Path;
            return obj;
        }
        return null;
    }

    private UnityEngine.Object LoadByPath(string assetPath,Type targetType)
    {
        AssetSpot spot;
        if (!assetsByPath.TryGetValue(NormalizePathKey(assetPath),out spot))
            return null;
        return LoadFromBundle(spot.Bundle,spot.Path,targetType);
    }

    private UnityEngine.Object LoadSpot(string spot,string name,Type targetType)
    {
        if (spot==ResourcesSpot)
            return FindInMemory(name,targetType);
        int bar=spot.IndexOf('|');
        if (bar<=0 || bar==spot.Length-1)
            return null;
        AssetBundle bundle;
        if (!bundlesByName.TryGetValue(spot.Substring(0,bar),out bundle))
            return null;
        return LoadFromBundle(bundle,spot.Substring(bar+1),targetType);
    }

    private UnityEngine.Object LoadFromBundle(AssetBundle bundle,string assetPath,Type targetType)
    {
        if (bundle==null || string.IsNullOrEmpty(assetPath))
            return null;
        try
        {
            return bundle.LoadAsset(assetPath,targetType);
        }
        catch (Exception ex)
        {
            LogSuppressed("LoadFromBundle "+assetPath,ex);
            return null;
        }
    }

    private UnityEngine.Object FindInMemory(string name,Type targetType,bool skipPackAssets=false)
    {
        if (string.IsNullOrEmpty(name) || targetType==null)
            return null;
        UnityEngine.Object[] all=Resources.FindObjectsOfTypeAll(targetType);
        string wanted=NormalizeName(name);
        for (int i=0; i<all.Length; i++)
        {
            UnityEngine.Object obj=all[i];
            if (obj==null)
                continue;
            if (!string.Equals(NormalizeName(obj.name),wanted,StringComparison.InvariantCultureIgnoreCase))
                continue;
            if (skipPackAssets && IsPackAsset(obj))
                continue;
            return obj;
        }
        return null;
    }

    private bool UseGameAsset(Type targetType)
    {
        if (targetType==null)
            return false;
        return targetType==typeof(Material) ||
               targetType==typeof(Shader) ||
               targetType==typeof(AudioClip) ||
               targetType==typeof(Sprite) ||
               targetType==typeof(Mesh);
    }

    private bool IsPackAsset(UnityEngine.Object obj)
    {
        if (obj==null)
            return false;
        return cache.Contains(obj) || mats.IsOurClone(obj);
    }

    private void AddInstanceIds<T>(HashSet<int> target,IEnumerable<T> objects) where T : UnityEngine.Object
    {
        foreach (T obj in objects)
        {
            if (obj!=null)
                target.Add(obj.GetInstanceID());
        }
    }

    private void BuildAssetIndex(string reason)
    {
        float began=Now();
        gameAssets.Clear();
        assetsByName.Clear();
        assetsByPath.Clear();
        bundlesByName.Clear();
        var packBundles = new HashSet<AssetBundle>();
        for (int i=0; i<packs.Count; i++)
        {
            if (packs[i]!=null && packs[i].Bundle!=null)
                packBundles.Add(packs[i].Bundle);
        }
        AssetBundle[] bundles;
        try
        {
            bundles=AssetBundle.GetAllLoadedAssetBundles().ToArray();
        }
        catch (Exception ex)
        {
            LogWarning("BuildAssetIndex failed to get loaded bundles: "+ex.Message);
            return;
        }
        int paths=0;
        for (int b=0; b<bundles.Length; b++)
        {
            AssetBundle bundle=bundles[b];
            if (bundle==null)
                continue;
            string bundleName=string.IsNullOrEmpty(bundle.name) ? "bundle#"+b : bundle.name;
            if (!bundlesByName.ContainsKey(bundleName))
                bundlesByName.Add(bundleName,bundle);
            bool fromPack=packBundles.Contains(bundle);
            string[] assetNames;
            try
            {
                assetNames=bundle.GetAllAssetNames();
            }
            catch
            {
                continue;
            }
            for (int n=0; n<assetNames.Length; n++)
            {
                AddSpot(bundle,assetNames[n],fromPack);
                paths++;
            }
        }
        LoadAssetMap();
        assetIndexReady=true;
        Log("Asset catalog built: reason="+reason +
            " bundles="+bundlesByName.Count +
            " paths="+paths +
            " names="+assetsByName.Count +
            " remembered="+rememberedSpots.Count +
            " ms="+Ms(began));
    }

    private void AddSpot(AssetBundle bundle,string assetPath,bool fromPack)
    {
        if (string.IsNullOrEmpty(assetPath))
            return;
        var spot = new AssetSpot { Bundle=bundle,Path=assetPath,FromPack=fromPack };
        string pathKey=NormalizePathKey(assetPath);
        if (!assetsByPath.ContainsKey(pathKey))
            assetsByPath.Add(pathKey,spot);
        string nameKey=NormalizeAssetKey(assetPath);
        if (nameKey.Length==0)
            return;
        List<AssetSpot> list;
        if (!assetsByName.TryGetValue(nameKey,out list))
        {
            list=new List<AssetSpot>();
            assetsByName.Add(nameKey,list);
        }
        list.Add(spot);
    }

    private void AddAsset(UnityEngine.Object obj,string assetPath)
    {
        if (obj==null || string.IsNullOrEmpty(obj.name))
            return;
        Type type=obj.GetType();
        while (type!=null && type!=typeof(object))
        {
            string key=AssetKey(type,obj.name);
            if (!gameAssets.ContainsKey(key))
                gameAssets.Add(key,obj);
            type=type.BaseType;
        }
    }

    private string AssetKey(Type type,string name)
    {
        string typeName=type!=null ? type.FullName : "";
        return "name|"+NormalizeName(typeName)+"|"+NormalizeName(name);
    }

    private string NormalizePathKey(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        return path.Trim().Replace("\\","/");
    }

    private string GameStamp()
    {
        if (gameStamp!=null)
            return gameStamp;
        gameStamp="unknown";
        Assembly[] loaded=AppDomain.CurrentDomain.GetAssemblies();
        for (int i=0; i<loaded.Length; i++)
        {
            if (loaded[i].GetName().Name=="Assembly-CSharp")
            {
                gameStamp=loaded[i].ManifestModule.ModuleVersionId.ToString("N");
                break;
            }
        }
        return gameStamp;
    }

    private string AssetMapPath()
    {
        return Path.Combine(Paths.CachePath,AssetMapFile);
    }

    private void LoadAssetMap()
    {
        rememberedSpots.Clear();
        rememberedSpotsDirty=false;
        string path=AssetMapPath();
        if (!File.Exists(path))
            return;
        try
        {
            using (FileStream stream=File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32()!=AssetMapVersion)
                    return;
                if (reader.ReadString()!=GameStamp())
                {
                    Log("Asset map is from another game build, resolving from scratch");
                    return;
                }
                int count=reader.ReadInt32();
                for (int i=0; i<count; i++)
                {
                    string key=reader.ReadString();
                    string spot=reader.ReadString();
                    rememberedSpots[key]=spot;
                }
            }
        }
        catch (Exception ex)
        {
            rememberedSpots.Clear();
            LogWarning("Asset map unreadable, will be rebuilt: "+ex.Message);
        }
    }

    private void SaveAssetMap()
    {
        if (!rememberedSpotsDirty)
            return;
        try
        {
            using (FileStream stream=File.Create(AssetMapPath()))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(AssetMapVersion);
                writer.Write(GameStamp());
                writer.Write(rememberedSpots.Count);
                foreach (KeyValuePair<string,string> pair in rememberedSpots)
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value);
                }
            }
            rememberedSpotsDirty=false;
            Log("Asset map saved: entries="+rememberedSpots.Count);
        }
        catch (Exception ex)
        {
            LogWarning("Asset map not saved: "+ex.Message);
        }
    }

    private UnityEngine.Object FindGameObject(string objectName,Type targetType)
    {
        if (string.IsNullOrEmpty(objectName))
            return null;
        UnityEngine.Object runtime=ResolveRuntimeObject(objectName);
        if (runtime!=null)
            return runtime;
        UnityEngine.Object bundle=ResolveBundleAsset(objectName);
        if (bundle!=null)
            return bundle;
        if (assetIndexReady)
        {
            string spot;
            UnityEngine.Object fromCatalog=LoadFromCatalog(objectName,targetType,out spot);
            if (fromCatalog!=null)
                return fromCatalog;
        }
        UnityEngine.Object[] all=Resources.FindObjectsOfTypeAll(targetType);
        for (int i=0; i<all.Length; i++)
        {
            UnityEngine.Object obj=all[i];
            if (obj!=null && NameMatches(obj.name,objectName))
                return obj;
        }
        return null;
    }
}
