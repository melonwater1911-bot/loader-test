using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

sealed class NobndlBundleCache
{
    internal sealed class Store<T> where T : UnityEngine.Object
    {
        private struct Entry
        {
            public T Asset;
            public string SourcePath;
        }

        private readonly Dictionary<string,Entry> items =
            new Dictionary<string,Entry>(StringComparer.InvariantCultureIgnoreCase);

        private readonly string label;

        internal Store(string label)
        {
            this.label=label;
        }

        public int Count
        {
            get { return items.Count; }
        }

        public int Version { get; private set; }

        public IEnumerable<T> Values
        {
            get
            {
                foreach (KeyValuePair<string,Entry> pair in items)
                    yield return pair.Value.Asset;
            }
        }

        public IEnumerable<KeyValuePair<string,T>> Items
        {
            get
            {
                foreach (KeyValuePair<string,Entry> pair in items)
                    yield return new KeyValuePair<string,T>(pair.Key,pair.Value.Asset);
            }
        }

        public bool TryGet(string key,out T asset)
        {
            Entry entry;
            if (items.TryGetValue(key,out entry))
            {
                asset=entry.Asset;
                return true;
            }
            asset=null;
            return false;
        }

        public string PathOf(string key)
        {
            Entry entry;
            return items.TryGetValue(key,out entry) ? entry.SourcePath : "";
        }

        internal void Put(string key,T asset,string sourcePath)
        {
            UnityEngine.Object incoming=asset;
            if (string.IsNullOrEmpty(key) || incoming==null)
                return;
            sourcePath=sourcePath ?? "";
            Entry stored;
            if (!items.TryGetValue(key,out stored))
            {
                Keep(key,asset,sourcePath);
                return;
            }
            if ((UnityEngine.Object)stored.Asset==incoming)
            {
                if (IsAssetFile(sourcePath) && !IsAssetFile(stored.SourcePath))
                    Keep(key,asset,sourcePath);
                return;
            }
            if (IsAssetFile(stored.SourcePath) && !IsAssetFile(sourcePath))
            {
                ReportCollision(key,"kept existing",stored,asset,sourcePath);
                return;
            }
            ReportCollision(key,"replaced",stored,asset,sourcePath);
            Keep(key,asset,sourcePath);
        }

        private void Keep(string key,T asset,string sourcePath)
        {
            items[key]=new Entry { Asset=asset,SourcePath=sourcePath };
            Version++;
        }

        private void ReportCollision(string key,string outcome,Entry stored,T incoming,string sourcePath)
        {
            if (label==null)
                return;
            LogWarning("Bundle "+label+" key collision "+outcome+": key="+key +
                       " stored="+Label(stored.Asset)+" at "+stored.SourcePath +
                       " incoming="+Label(incoming)+" at "+sourcePath);
        }
    }

    public readonly Store<UnityEngine.Object> Assets=new Store<UnityEngine.Object>("asset");
    public readonly Store<UnityEngine.Object> Typed=new Store<UnityEngine.Object>(null);
    public readonly Store<Material> Materials=new Store<Material>(null);
    public readonly Store<GameObject> Prefabs=new Store<GameObject>("GameObject");

    private HashSet<int> allIds;
    private int allIdsStamp=-1;
    private HashSet<int> prefabIds;
    private int prefabIdsStamp=-1;

    public bool Contains(UnityEngine.Object obj)
    {
        if (obj==null)
            return false;
        int stamp=Assets.Version+Typed.Version+Materials.Version+Prefabs.Version;
        if (allIds==null || stamp!=allIdsStamp)
        {
            allIds=new HashSet<int>();
            AddIds(allIds,Assets.Values);
            AddIds(allIds,Typed.Values);
            AddIds(allIds,Materials.Values);
            AddIds(allIds,Prefabs.Values);
            allIdsStamp=stamp;
        }
        return allIds.Contains(obj.GetInstanceID());
    }

    public IEnumerable<GameObject> PrefabRoots
    {
        get
        {
            foreach (KeyValuePair<string,GameObject> pair in Prefabs.Items)
            {
                if (pair.Value!=null && IsPrefabPath(Prefabs.PathOf(pair.Key)))
                    yield return pair.Value;
            }
        }
    }

    public void ReportNonPrefabRoots()
    {
        foreach (KeyValuePair<string,GameObject> pair in Prefabs.Items)
        {
            if (pair.Value==null || IsPrefabPath(Prefabs.PathOf(pair.Key)))
                continue;
            NobndlLog.LogWarning("Bundle GameObject is not a .prefab, skipped as a pack root: "+pair.Value.name +
                                 " from "+Prefabs.PathOf(pair.Key) +
                                 " - a model file in the bundle shows up as a second copy of your object");
        }
    }

    public bool ContainsPrefab(GameObject g)
    {
        if (g==null)
            return false;
        if (prefabIds==null || prefabIdsStamp!=Prefabs.Version)
        {
            prefabIds=new HashSet<int>();
            AddIds(prefabIds,Prefabs.Values);
            prefabIdsStamp=Prefabs.Version;
        }
        return prefabIds.Contains(g.GetInstanceID());
    }

    private static void AddIds<T>(HashSet<int> target,IEnumerable<T> objects) where T : UnityEngine.Object
    {
        foreach (T obj in objects)
        {
            if (obj!=null)
                target.Add(obj.GetInstanceID());
        }
    }

    public void AddBundle(AssetBundle bundle)
    {
        string[] names=bundle.GetAllAssetNames();
        Log("AssetBundle asset count: "+names.Length);
        for (int i=0; i<names.Length; i++)
        {
            string assetPath=names[i];
            Log("Bundle asset: "+assetPath);
            UnityEngine.Object asset=bundle.LoadAsset(assetPath);
            if (asset==null)
                continue;
            string assetName=Path.GetFileNameWithoutExtension(assetPath);
            Material material=asset as Material;
            GameObject g=asset as GameObject;
            string[] keys={ assetPath,assetName,asset.name };
            for (int k=0; k<keys.Length; k++)
            {
                AddAsset(keys[k],asset,assetPath);
                AddByType(keys[k],asset,assetPath);
                if (material!=null)
                    Materials.Put(NormalizeAssetKey(keys[k]),material,assetPath);
                if (g!=null)
                    Prefabs.Put(NormalizeAssetKey(keys[k]),g,assetPath);
            }
            AddSubAssets(bundle,assetPath,assetName);
            if (g!=null)
                AddPrefabDependencies(g,assetPath,assetName);
        }
        Log("Cached bundle: assets="+Assets.Count +
                               " typed="+Typed.Count +
                               " materials="+Materials.Count +
                               " prefabs="+Prefabs.Count);
    }

    private void AddPrefabDependencies(GameObject root,string assetPath,string assetName)
    {
        if (root==null)
            return;
        int materialCount=0;
        int meshCount=0;
        int audioCount=0;
        Renderer[] renderers=root.GetComponentsInChildren<Renderer>(true);
        for (int i=0; i<renderers.Length; i++)
        {
            Renderer renderer=renderers[i];
            if (renderer==null)
                continue;
            Material[] materials=renderer.sharedMaterials;
            if (materials==null)
                continue;
            for (int m=0; m<materials.Length; m++)
            {
                Material material=materials[m];
                if (material==null)
                    continue;
                string sourcePath=assetPath+"::rendererMaterial::"+GetFullPath(renderer.transform) +
                                  "::"+m.ToString(CultureInfo.InvariantCulture);
                Materials.Put(NormalizeAssetKey(material.name),material,sourcePath);
                AddDependency(material,assetPath,assetName,sourcePath);
                materialCount++;
            }
        }
        MeshFilter[] meshFilters=root.GetComponentsInChildren<MeshFilter>(true);
        for (int i=0; i<meshFilters.Length; i++)
        {
            MeshFilter meshFilter=meshFilters[i];
            if (meshFilter==null || meshFilter.sharedMesh==null)
                continue;
            AddAt(meshFilter.sharedMesh,meshFilter.transform,"meshFilter",assetPath,assetName);
            meshCount++;
        }
        SkinnedMeshRenderer[] skinnedRenderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i=0; i<skinnedRenderers.Length; i++)
        {
            SkinnedMeshRenderer skinned=skinnedRenderers[i];
            if (skinned==null || skinned.sharedMesh==null)
                continue;
            AddAt(skinned.sharedMesh,skinned.transform,"skinnedMesh",assetPath,assetName);
            meshCount++;
        }
        MeshCollider[] meshColliders=root.GetComponentsInChildren<MeshCollider>(true);
        for (int i=0; i<meshColliders.Length; i++)
        {
            MeshCollider meshCollider=meshColliders[i];
            if (meshCollider==null || meshCollider.sharedMesh==null)
                continue;
            AddAt(meshCollider.sharedMesh,meshCollider.transform,"meshCollider",assetPath,assetName);
            meshCount++;
        }
        AudioSource[] audioSources=root.GetComponentsInChildren<AudioSource>(true);
        for (int i=0; i<audioSources.Length; i++)
        {
            AudioSource audioSource=audioSources[i];
            if (audioSource==null || audioSource.clip==null)
                continue;
            AddAt(audioSource.clip,audioSource.transform,"audioSource",assetPath,assetName);
            audioCount++;
        }
        if (materialCount>0 || meshCount>0 || audioCount>0)
        {
            Log("Cached bundle prefab native dependencies: root="+root.name +
                                   " materials="+materialCount +
                                   " meshes="+meshCount +
                                   " audioClips="+audioCount +
                                   " path="+assetPath);
        }
    }

    private void AddAt(UnityEngine.Object asset,Transform at,string kind,string assetPath,string assetName)
    {
        AddDependency(asset,assetPath,assetName,assetPath+"::"+kind+"::"+GetFullPath(at));
    }

    private void AddDependency(UnityEngine.Object asset,string assetPath,string assetName,string sourcePath)
    {
        if (asset==null)
            return;
        AddByType(asset.name,asset,sourcePath);
        AddByType(assetName+"::"+asset.name,asset,sourcePath);
        AddByType(assetPath+"::"+asset.name,asset,sourcePath);
    }

    private void AddSubAssets(AssetBundle bundle,string assetPath,string assetName)
    {
        if (bundle==null || string.IsNullOrEmpty(assetPath))
            return;
        UnityEngine.Object[] subAssets;
        try
        {
            subAssets=bundle.LoadAssetWithSubAssets(assetPath);
        }
        catch (Exception ex)
        {
            LogError("Bundle sub-assets unreadable, pack is broken: path="+assetPath+" | "+ex.Message);
            return;
        }
        if (subAssets==null)
            return;
        for (int i=0; i<subAssets.Length; i++)
        {
            UnityEngine.Object subAsset=subAssets[i];
            if (subAsset==null)
                continue;
            AddByType(assetPath,subAsset,assetPath);
            AddByType(assetName,subAsset,assetPath);
            AddDependency(subAsset,assetPath,assetName,assetPath);
            AddAsset(assetPath+"::"+subAsset.name,subAsset,assetPath);
            AddAsset(assetName+"::"+subAsset.name,subAsset,assetPath);
            if (subAsset is Sprite)
            {
                AddAsset(subAsset.name,subAsset,assetPath);
                LogWarning("Cached Sprite subasset from bundle: path="+assetPath +
                                              " name="+subAsset.name +
                                              " key="+assetName);
            }
        }
    }

    private void AddAsset(string key,UnityEngine.Object asset,string sourcePath)
    {
        Assets.Put(NormalizeAssetKey(key),asset,sourcePath);
    }

    private void AddByType(string key,UnityEngine.Object asset,string sourcePath)
    {
        if (string.IsNullOrEmpty(key) || asset==null)
            return;
        Type type=asset.GetType();
        while (type!=null && type!=typeof(object))
        {
            Typed.Put(TypedKey(type,key),asset,sourcePath);
            type=type.BaseType;
        }
    }

    public static string TypedKey(Type type,string key)
    {
        string typeName=type!=null ? type.FullName : "";
        return NormalizeName(typeName)+"|"+NormalizeAssetKey(key);
    }

    public static bool IsPrefabPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.EndsWith(".prefab",StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool IsAssetFile(string sourcePath)
    {
        return IsPrefabPath(sourcePath) ||
               (sourcePath ?? "").EndsWith(".mat",StringComparison.InvariantCultureIgnoreCase);
    }

    private static string Label(UnityEngine.Object obj)
    {
        if (obj==null)
            return "null";
        return obj.name+" | "+obj.GetType().FullName;
    }
}
