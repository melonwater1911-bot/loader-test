using System.Collections.Generic;
using Mirage;
using UnityEngine;
using static Logs;

sealed class PrefabHashes
{
    public void Assign(BundleCache cache)
    {
        var ours = new List<NetworkIdentity>();
        var ourIds = new HashSet<int>();
        for (int i=0; i<cache.Prefabs.Count; i++) foreach (NetworkIdentity id in cache.Prefabs[i].GetComponentsInChildren<NetworkIdentity>(true)) if(ourIds.Add(id.GetInstanceID())) ours.Add(id);
        if(ours.Count==0) return;
        var taken = new HashSet<int>();
        foreach (NetworkIdentity id in Resources.FindObjectsOfTypeAll<NetworkIdentity>()) if(!ourIds.Contains(id.GetInstanceID())) taken.Add(id.PrefabHash);
        for (int i=0; i<ours.Count; i++)
        {
            NetworkIdentity id=ours[i];
            if(id.PrefabHash!=0 && taken.Add(id.PrefabHash)) continue;
            string seed=id.transform.root.name+"/"+id.name;
            int hash=HashText(seed+":0");
            for (int n=1; hash==0 || !taken.Add(hash); n++) hash=HashText(seed+":"+n);
            LogWarning("PrefabHash "+id.PrefabHash+" of "+seed+(id.PrefabHash==0 ? " is zero" : " collides")+", now "+hash);
            id.PrefabHash=hash;
        }
    }

    private static int HashText(string text)
    {
        unchecked
        {
            int hash=(int)2166136261;
            for (int i=0; i<text.Length; i++) hash=(hash ^ text[i])*16777619;
            return hash;
        }
    }
}
