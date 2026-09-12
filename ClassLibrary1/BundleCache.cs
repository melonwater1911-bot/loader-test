using System;
using System.Collections.Generic;
using UnityEngine;

sealed class BundleCache
{
    private readonly Dictionary<string,UnityEngine.Object[]> byPath=new Dictionary<string,UnityEngine.Object[]>(StringComparer.InvariantCultureIgnoreCase);
    private readonly HashSet<int> ids=new HashSet<int>();
    public readonly List<GameObject> Prefabs=new List<GameObject>();
    public readonly List<Material> Materials=new List<Material>();

    public void Add(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            UnityEngine.Object main=bundle.LoadAsset(name);
            bool prefab=name.EndsWith(".prefab",StringComparison.InvariantCultureIgnoreCase);
            var all = new List<UnityEngine.Object> { main };
            if(!prefab) foreach (UnityEngine.Object sub in bundle.LoadAssetWithSubAssets(name)) if(sub!=main) all.Add(sub);
            byPath[name]=all.ToArray();
            for (int k=0; k<all.Count; k++) ids.Add(all[k].GetInstanceID());
            if(main is Material) Materials.Add((Material)main);
            if(!prefab) continue;
            GameObject g=(GameObject)main;
            Prefabs.Add(g);
            foreach (Renderer renderer in g.GetComponentsInChildren<Renderer>(true)) foreach (Material m in renderer.sharedMaterials) if(m!=null && ids.Add(m.GetInstanceID())) Materials.Add(m);
        }
    }

    public UnityEngine.Object Main(string path)
    {
        return byPath[path][0];
    }

    public UnityEngine.Object Sub(string path,string name,Type type)
    {
        UnityEngine.Object[] found=byPath[path];
        return Array.Find(found,x => x.name==name && type.IsInstanceOfType(x));
    }

    public bool Contains(UnityEngine.Object obj)
    {
        return ids.Contains(obj.GetInstanceID());
    }
}
