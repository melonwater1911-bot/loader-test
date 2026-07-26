using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private UnityEngine.Object ResolveRuntimeObject(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        UnityEngine.Object obj;
        if (runtimeObjects.TryGetValue(NormalizeAssetKey(key),out obj))
            return obj;
        return null;
    }

    private UnityEngine.Object ResolveBundleAsset(string key,Type targetType)
    {
        if (string.IsNullOrEmpty(key) || targetType==null)
            return null;
        UnityEngine.Object obj;
        if (cache.Typed.TryGet(NobndlBundleCache.TypedKey(targetType,key),out obj) && obj!=null)
            return obj;
        foreach (KeyValuePair<string,UnityEngine.Object> pair in cache.Typed.Items)
        {
            obj=pair.Value;
            if (obj==null)
                continue;
            if (!targetType.IsAssignableFrom(obj.GetType()))
                continue;
            string normalizedKey=NormalizeAssetKey(key);
            if (pair.Key.EndsWith("|"+normalizedKey,StringComparison.InvariantCultureIgnoreCase))
                return obj;
        }
        obj=ResolveBundleAsset(key);
        if (obj!=null && targetType.IsAssignableFrom(obj.GetType()))
            return obj;
        return null;
    }

    private UnityEngine.Object ResolveBundleAsset(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        UnityEngine.Object obj;
        if (cache.Assets.TryGet(NormalizeAssetKey(key),out obj))
            return obj;
        return null;
    }

    private GameObject FindPrefab(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        GameObject g;
        if (cache.Prefabs.TryGet(NormalizeAssetKey(key),out g))
            return g;
        UnityEngine.Object obj=ResolveBundleAsset(key);
        return obj as GameObject;
    }

    private void AddRuntimeObjectKey(string key,UnityEngine.Object obj)
    {
        if (string.IsNullOrEmpty(key) || obj==null)
            return;
        key=NormalizeAssetKey(key);
        UnityEngine.Object existing;
        if (runtimeObjects.TryGetValue(key,out existing) && existing!=obj)
        {
            LogWarning("Runtime object key overwritten: key="+key +
                       " old="+ObjName(existing) +
                       " new="+ObjName(obj));
        }
        runtimeObjects[key]=obj;
    }

    private Transform FindChildByPath(Transform root,string path)
    {
        if (root==null)
            return null;
        if (string.IsNullOrEmpty(path))
            return root;
        string[] parts=path.Split('/');
        Transform current=root;
        for (int i=0; i<parts.Length; i++)
        {
            if (string.IsNullOrEmpty(parts[i]))
                continue;
            Transform next=FindChildBySegment(current,parts[i]);
            if (next==null)
                return null;
            current=next;
        }
        return current;
    }

    private Transform FindChildBySegment(Transform parent,string segment)
    {
        if (parent==null || string.IsNullOrEmpty(segment))
            return null;
        Transform literal=parent.Find(segment);
        if (literal!=null)
            return literal;
        int at=segment.LastIndexOf('@');
        if (at<=0 || at==segment.Length-1)
            return null;
        int index;
        if (!int.TryParse(segment.Substring(at+1),NumberStyles.None,CultureInfo.InvariantCulture,out index))
            return null;
        string name=segment.Substring(0,at);
        int occurrence=0;
        for (int i=0; i<parent.childCount; i++)
        {
            Transform child=parent.GetChild(i);
            if (!string.Equals(child.name,name,StringComparison.Ordinal))
                continue;
            if (occurrence==index)
                return child;
            occurrence++;
        }
        return null;
    }

    private Component FindComponentAt(GameObject g,Type type,int order)
    {
        if (g==null || type==null)
            return null;
        Component[] components=g.GetComponents(type);
        if (components==null || components.Length==0)
            return null;
        if (order<0)
            return components[0];
        if (order>=components.Length)
        {
            LogError("Component order out of range: "+g.name +
                     " type="+type.Name +
                     " requested="+order +
                     " available="+components.Length);
            return null;
        }
        return components[order];
    }

    private Component FindComponentAssignable(GameObject g,Type type)
    {
        return FindComponentAssignable(g,type,-1);
    }

    private Component FindComponentAssignable(GameObject g,Type type,int orderOnGameObject)
    {
        if (g==null || type==null)
            return null;
        Component[] components=g.GetComponents<Component>();
        int matchingIndex=0;
        for (int i=0; i<components.Length; i++)
        {
            Component component=components[i];
            if (component==null || !type.IsAssignableFrom(component.GetType()))
                continue;
            if (orderOnGameObject<0 || matchingIndex==orderOnGameObject)
                return component;
            matchingIndex++;
        }
        return null;
    }

    private string BuildComponentKey(string prefabName,string path,string typeName,int order)
    {
        return NormalizeName(prefabName)+"|"+NormalizePathKey(path)+"|"+NormalizeName(typeName)+"|"+order.ToString(CultureInfo.InvariantCulture);
    }
}
