using System;
using System.Collections.Generic;
using UnityEngine;

sealed class AssignMats
{
    private readonly Dictionary<int,Material> clones=new Dictionary<int,Material>();
    private Dictionary<string,Shader> live;

    public Material Prepare(BundleCache cache,Material source)
    {
        if(source==null) return null;
        Material clone;
        if(clones.TryGetValue(source.GetInstanceID(),out clone)) return clone;
        if(live==null) live=LiveShaders(cache);
        Shader shader=Live(source.shader);
        if(shader==null) clone=source;
        else
        {
            clone=new Material(source);
            clone.shader=shader;
            clone.renderQueue=source.renderQueue;
            clone.name=source.name+"_Runtime";
        }
        clones[source.GetInstanceID()]=clone;
        return clone;
    }

    public Material[] Prepare(BundleCache cache,Material[] materials)
    {
        var result = new Material[materials.Length];
        for (int i=0; i<materials.Length; i++) result[i]=Prepare(cache,materials[i]);
        return result;
    }

    private static Dictionary<string,Shader> LiveShaders(BundleCache cache)
    {
        var own = new HashSet<int>();
        for (int i=0; i<cache.Materials.Count; i++) own.Add(cache.Materials[i].shader.GetInstanceID());
        var result = new Dictionary<string,Shader>();
        foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>()) if(s.isSupported && !own.Contains(s.GetInstanceID()) && !result.ContainsKey(s.name)) result[s.name]=s;
        return result;
    }

    private Shader Live(Shader own)
    {
        Shader found;
        if(live.TryGetValue(own.name,out found)) return found;
        if(own.isSupported && own.name.IndexOf("InternalError",StringComparison.OrdinalIgnoreCase)<0) return null;
        return live["Universal Render Pipeline/Lit"];
    }
}

partial class Loader
{
    private void RebindMaterials()
    {
        for (int r=0; r<cache.Prefabs.Count; r++)
        {
            foreach (Renderer renderer in cache.Prefabs[r].GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots=renderer.sharedMaterials;
                bool changed=false;
                for (int m=0; m<slots.Length; m++)
                {
                    Material runtime=mats.Prepare(cache,slots[m]);
                    if(runtime==slots[m]) continue;
                    slots[m]=runtime;
                    changed=true;
                }
                if(changed) renderer.sharedMaterials=slots;
            }
        }
    }
}
