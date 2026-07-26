using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private UnityEngine.Object FindAsset(NobndlFieldRecord record,Type targetType)
    {
        if (record==null || targetType==null)
            return null;
        if (gameAssets.Count==0)
        {
            LogError("Asset lookup before the index was built, this is a loader bug");
            return null;
        }
        UnityEngine.Object obj;
        if (UseGameAsset(targetType))
        {
            UnityEngine.Object preferred =
                FindAssetByName(record.objectName,targetType) ??
                FindAssetByName(record.value,targetType) ??
                FindAssetByName(record.assetName,targetType);
            if (preferred!=null)
                return preferred;
        }
        if (!string.IsNullOrEmpty(record.assetPath))
        {
            string pathKey=AssetPathKey(targetType,record.assetPath);
            if (gameAssets.TryGetValue(pathKey,out obj) && obj!=null)
            {
                if (VerboseAssetResolveLogs)
                {
                    Log("Resolved GameAsset by assetPath: "+record.assetPath+" -> "+obj.name+" | "+obj.GetType().FullName);
                }
                return obj;
            }
        }
        return FindAssetByName(record.objectName,targetType) ??
               FindAssetByName(record.value,targetType) ??
               FindAssetByName(record.assetName,targetType);
    }

    //guid support, via addressables catalog
    private UnityEngine.Object FindAssetByName(string name,Type targetType)
    {
        if (string.IsNullOrEmpty(name) || targetType==null)
            return null;
        if (gameAssets.Count==0)
        {
            LogError("Asset lookup before the index was built, this is a loader bug");
            return null;
        }
        UnityEngine.Object obj;
        if (UseGameAsset(targetType))
        {
            UnityEngine.Object preferred=FindInMemory(name,targetType,true);
            if (preferred!=null)
            {
                if (VerboseAssetResolveLogs)
                {
                    Log("Resolved GameAsset from non-NOBNDL Resources by name: "+name+" -> "+preferred.name+" | "+preferred.GetType().FullName);
                }
                return preferred;
            }
        }
        if (gameAssets.TryGetValue(AssetKey(targetType,name),out obj) && obj!=null)
        {
            if (!UseGameAsset(targetType) || !IsPackAsset(obj))
            {
                if (VerboseAssetResolveLogs)
                {
                    Log("Resolved GameAsset from DB by name: "+name+" -> "+obj.name+" | "+obj.GetType().FullName);
                }
                return obj;
            }
        }
        foreach (KeyValuePair<string,UnityEngine.Object> pair in gameAssets)
        {
            obj=pair.Value;
            if (obj==null)
                continue;
            if (!targetType.IsAssignableFrom(obj.GetType()))
                continue;
            if (UseGameAsset(targetType) && IsPackAsset(obj))
                continue;
            if (NameMatches(obj.name,name))
            {
                if (VerboseAssetResolveLogs)
                {
                    Log("Resolved GameAsset from DB assignable by name: "+name+" -> "+obj.name+" | "+obj.GetType().FullName);
                }
                return obj;
            }
        }
        UnityEngine.Object resourceObj=FindInMemory(name,targetType);
        if (resourceObj!=null)
            return resourceObj;
        LogWarning("GameAsset not found: name="+name+" | type="+targetType.FullName+" | db="+gameAssets.Count);
        return null;
    }

    private UnityEngine.Object FindInMemory(string name,Type targetType)
    {
        return FindInMemory(name,targetType,false);
    }

    private UnityEngine.Object FindInMemory(string name,Type targetType,bool excludeNobndlBundleAssets)
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
            if (excludeNobndlBundleAssets && IsPackAsset(obj))
                continue;
            {
                if (VerboseAssetResolveLogs)
                {
                    Log("Resolved GameAsset from Resources: "+name+" -> "+obj.name+" | "+targetType.FullName+" excludeNobndl="+excludeNobndlBundleAssets);
                }
                return obj;
            }
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
        return cache.Contains(obj) || materialFactory.IsOurClone(obj);
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
        gameAssets.Clear();
        int bundleCount=0;
        int loadedCount=0;
        int skippedBundles=0;
        int pathCount=0;
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
        for (int b=0; b<bundles.Length; b++)
        {
            AssetBundle bundle=bundles[b];
            if (bundle==null)
                continue;
            bundleCount++;
            string[] assetNames;
            try
            {
                assetNames=bundle.GetAllAssetNames();
            }
            catch
            {
                assetNames=new string[0];
            }
            for (int n=0; n<assetNames.Length; n++)
            {
                string assetPath=assetNames[n];
                if (string.IsNullOrEmpty(assetPath))
                    continue;
                UnityEngine.Object obj=null;
                try
                {
                    obj=bundle.LoadAsset(assetPath);
                }
                catch (Exception ex)
                {
                    LogSuppressed("BuildAssetIndex LoadAsset "+assetPath,ex);
                }
                if (obj==null)
                    continue;
                pathCount++;
                AddAsset(obj,assetPath);
            }
            UnityEngine.Object[] assets;
            try
            {
                assets=bundle.LoadAllAssets();
            }
            catch
            {
                skippedBundles++;
                continue;
            }
            if (assets==null)
                continue;
            for (int i=0; i<assets.Length; i++)
            {
                UnityEngine.Object obj=assets[i];
                if (obj==null)
                    continue;
                loadedCount++;
                AddAsset(obj,"");
            }
        }
        UnityEngine.Object[] inMemory;
        try
        {
            inMemory=Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
        }
        catch
        {
            inMemory=new UnityEngine.Object[0];
        }
        for (int i=0; i<inMemory.Length; i++)
        {
            UnityEngine.Object obj=inMemory[i];
            if (obj==null)
                continue;
            AddAsset(obj,"");
        }
        Log("GameAsset DB built: reason="+reason +
            " bundles="+bundleCount +
            " skippedBundles="+skippedBundles +
            " loadedBundleAssets="+loadedCount +
            " assetPathAssets="+pathCount +
            " resources="+inMemory.Length +
            " db="+gameAssets.Count);
    }

    private void AddAsset(UnityEngine.Object obj,string assetPath)
    {
        if (obj==null || string.IsNullOrEmpty(obj.name))
            return;
        AddAssetKeys(obj.GetType(),obj.name,obj,assetPath);
        Type type=obj.GetType().BaseType;
        while (type!=null && type!=typeof(object))
        {
            AddAssetKeys(type,obj.name,obj,assetPath);
            type=type.BaseType;
        }
        if (obj is GameObject)
        {
            AddAssetKeys(typeof(GameObject),obj.name,obj,assetPath);
        }
        else if (obj is Component)
        {
            AddAssetKeys(typeof(Component),obj.name,obj,assetPath);
        }
        else if (obj is Sprite)
        {
            AddAssetKeys(typeof(Sprite),obj.name,obj,assetPath);
            AddAssetKeys(typeof(UnityEngine.Object),obj.name,obj,assetPath);
        }
        else if (obj is AudioClip)
        {
            AddAssetKeys(typeof(AudioClip),obj.name,obj,assetPath);
            AddAssetKeys(typeof(UnityEngine.Object),obj.name,obj,assetPath);
        }
        else if (obj is Material)
        {
            AddAssetKeys(typeof(Material),obj.name,obj,assetPath);
            AddAssetKeys(typeof(UnityEngine.Object),obj.name,obj,assetPath);
        }
    }

    private void AddAssetKeys(Type type,string name,UnityEngine.Object obj,string assetPath)
    {
        AddGameAssetKey(AssetKey(type,name),obj);
        if (!string.IsNullOrEmpty(assetPath))
        {
            AddGameAssetKey(AssetPathKey(type,assetPath),obj);
            AddGameAssetKey(AssetKey(type,Path.GetFileNameWithoutExtension(assetPath)),obj);
        }
    }

    private void AddGameAssetKey(string key,UnityEngine.Object obj)
    {
        if (string.IsNullOrEmpty(key) || obj==null)
            return;
        if (!gameAssets.ContainsKey(key))
            gameAssets.Add(key,obj);
    }

    private string AssetKey(Type type,string name)
    {
        string typeName=type!=null ? type.FullName : "";
        return "name|"+NormalizeName(typeName)+"|"+NormalizeName(name);
    }

    private string AssetPathKey(Type type,string assetPath)
    {
        string typeName=type!=null ? type.FullName : "";
        return "path|"+NormalizeName(typeName)+"|"+NormalizePathKey(assetPath);
    }

    private string NormalizePathKey(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        return path.Trim().Replace("\\","/");
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
