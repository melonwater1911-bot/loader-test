using System;
using System.Collections.Generic;
using UnityEngine;
using static Logs;

partial class Loader
{
    private readonly Dictionary<string,UnityEngine.Object> gameAssets=new Dictionary<string,UnityEngine.Object>(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<Type,Dictionary<string,UnityEngine.Object>> inMemory=new Dictionary<Type,Dictionary<string,UnityEngine.Object>>();

    private UnityEngine.Object GameAsset(string name,Type type)
    {
        string key=type.FullName+"|"+name;
        UnityEngine.Object obj;
        if(gameAssets.TryGetValue(key,out obj)) return obj;
        Dictionary<string,UnityEngine.Object> byName;
        if (!inMemory.TryGetValue(type,out byName))
        {
            byName=new Dictionary<string,UnityEngine.Object>(StringComparer.InvariantCultureIgnoreCase);
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(type)) if(o!=null && !cache.Contains(o) && !byName.ContainsKey(o.name)) byName[o.name]=o;
            inMemory[type]=byName;
        }
        if(!byName.TryGetValue(name,out obj)) LogWarning("Game asset not found: "+type.Name+" "+name);
        gameAssets[key]=obj;
        return obj;
    }

    private static GameObject GamePrefab(string key)
    {
        return Encyclopedia.Lookup[key].unitPrefab;
    }

    private static Transform Child(Transform root,string path)
    {
        Transform current=root;
        if(path.Length==0) return current;
        foreach (string segment in path.Split('/'))
        {
            int at=segment.LastIndexOf('@');
            if(at<0) current=current.Find(segment);
            else
            {
                string name=segment.Substring(0,at);
                int index=int.Parse(segment.Substring(at+1));
                Transform parent=current;
                for (int i=0; i<parent.childCount; i++) if(parent.GetChild(i).name==name && index--==0) current=parent.GetChild(i);
            }
        }
        return current;
    }

    private static Component ComponentAt(GameObject g,Type type,int order)
    {
        return g.GetComponents(type)[order];
    }
}
