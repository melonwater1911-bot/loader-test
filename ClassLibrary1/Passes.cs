using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void BuildPack()
    {
        Log("BuildPack");
        RebindMaterials("BuildPack");
        FixParticles("BuildPack early");
        Dictionary<GameObject,bool> activeStates=HidePrefabs("BuildPack");
        try
        {
            for (int i=0; i<packs.Count; i++)
            {
                LoadedNobndlPack pack=packs[i];
                if (pack==null || pack.Manifest==null || pack.Bundle==null)
                    continue;
                MakeObjects(pack);
            }
            for (int i=0; i<packs.Count; i++)
            {
                LoadedNobndlPack pack=packs[i];
                if (pack==null || pack.Manifest==null || pack.Bundle==null)
                    continue;
                AddComponents(pack);
            }
            for (int i=0; i<packs.Count; i++)
            {
                LoadedNobndlPack pack=packs[i];
                if (pack==null || pack.Manifest==null || pack.Bundle==null)
                    continue;
                FillObjectFields(pack);
            }
            for (int i=0; i<packs.Count; i++)
            {
                LoadedNobndlPack pack=packs[i];
                if (pack==null || pack.Manifest==null || pack.Bundle==null)
                    continue;
                FillComponentFields(pack);
            }
            for (int i=0; i<packs.Count; i++)
            {
                LoadedNobndlPack pack=packs[i];
                if (pack==null || pack.Manifest==null || pack.Bundle==null)
                    continue;
                PostProcessPack(pack);
            }
        }
        finally
        {
            ShowPrefabs(activeStates,"BuildPack");
        }
    }

    private Dictionary<GameObject,bool> HidePrefabs(string reason)
    {
        Dictionary<GameObject,bool> states=new Dictionary<GameObject,bool>();
        List<GameObject> roots=GetPrefabRoots();
        for (int i=0; i<roots.Count; i++)
        {
            GameObject g=roots[i];
            if (g==null)
                continue;
            if (states.ContainsKey(g))
                continue;
            states.Add(g,g.activeSelf);
            if (g.activeSelf)
                g.SetActive(false);
        }
        LogWarning("Inactive prefab build phase: reason="+reason +
                   " prefabs="+states.Count);
        return states;
    }

    private void ShowPrefabs(Dictionary<GameObject,bool> states,string reason)
    {
        if (states==null)
            return;
        foreach (KeyValuePair<GameObject,bool> pair in states)
        {
            GameObject g=pair.Key;
            if (g==null)
                continue;
            if (g.activeSelf!=pair.Value)
                g.SetActive(pair.Value);
        }
        LogWarning("Restored prefab active states: reason="+reason +
                   " prefabs="+states.Count);
    }

    private void MakeObjects(LoadedNobndlPack pack)
    {
        NobndlScriptableObjectRecord[] records=pack.Manifest.scriptableObjects;
        if (records==null)
        {
            Log("No scriptableObjects in manifest for pack: "+pack.Manifest.packId);
            return;
        }
        Log("Creating ScriptableObjects: "+records.Length);
        for (int i=0; i<records.Length; i++)
        {
            NobndlScriptableObjectRecord record=records[i];
            if (record==null)
                continue;
            Type type=ResolveType(record.runtimeTypeName,record.runtimeAssemblyName);
            if (type==null)
            {
                LogError("Could not resolve SO type: "+record.runtimeTypeName+", "+record.runtimeAssemblyName);
                continue;
            }
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
            {
                LogError("Type is not ScriptableObject: "+type.FullName);
                continue;
            }
            ScriptableObject instance=ScriptableObject.CreateInstance(type);
            instance.name=!string.IsNullOrEmpty(record.objectName) ? record.objectName : record.assetName;
            AddRuntimeObjectKey(record.assetName,instance);
            AddRuntimeObjectKey(record.objectName,instance);
            AddRuntimeObjectKey(Path.GetFileNameWithoutExtension(record.assetPath),instance);
            Log("Created SO: "+instance.name+" | "+type.FullName+", "+type.Assembly.GetName().Name);
        }
    }

    private void AddComponents(LoadedNobndlPack pack)
    {
        NobndlPrefabScriptBindingRecord[] records=pack.Manifest.prefabScriptBindings;
        if (records==null)
        {
            Log("No prefabScriptBindings in manifest for pack: "+pack.Manifest.packId);
            return;
        }
        Log("Adding prefab script components: "+records.Length);
        for (int i=0; i<records.Length; i++)
        {
            NobndlPrefabScriptBindingRecord record=records[i];
            if (record==null)
                continue;
            if (record.runtimeTypeName=="<missing>")
            {
                LogWarning("Skipping missing component marker on prefab: "+record.prefabName);
                continue;
            }
            GameObject prefab=FindPrefab(record.prefabName);
            if (prefab==null)
            {
                LogError("Prefab not found for binding: "+record.prefabName);
                continue;
            }
            Transform targetTransform=FindChildByPath(prefab.transform,record.gameObjectPath);
            if (targetTransform==null)
            {
                LogError("Target GameObject path not found: prefab="+record.prefabName+" path="+record.gameObjectPath);
                continue;
            }
            Type type=ResolveType(record.runtimeTypeName,record.runtimeAssemblyName);
            if (type==null)
            {
                LogError("Could not resolve component type: "+record.runtimeTypeName+", "+record.runtimeAssemblyName);
                continue;
            }
            if (!typeof(Component).IsAssignableFrom(type))
            {
                LogError("Type is not Component: "+type.FullName);
                continue;
            }
            Component existing=FindComponentAt(targetTransform.gameObject,type,record.componentOrderOnGameObject);
            Component component=existing;
            if (component==null)
            {
                component=targetTransform.gameObject.AddComponent(type);
                Log("Added component: "+type.FullName+" to "+record.prefabName+"/"+record.gameObjectPath);
            }
            else
            {
                if (VerboseComponentApplyLogs)
                    Log("Component already exists: "+type.FullName+" on "+record.prefabName+"/"+record.gameObjectPath);
            }
            string key=BuildComponentKey(record.prefabName,record.gameObjectPath,record.runtimeTypeName,record.componentOrderOnGameObject);
            runtimeComponents[key]=component;
        }
    }

    private void FillObjectFields(LoadedNobndlPack pack)
    {
        NobndlScriptableObjectRecord[] records=pack.Manifest.scriptableObjects;
        if (records==null)
            return;
        Log("Applying ScriptableObject fields");
        for (int i=0; i<records.Length; i++)
        {
            NobndlScriptableObjectRecord record=records[i];
            if (record==null)
                continue;
            UnityEngine.Object obj=ResolveRuntimeObject(record.assetName) ?? ResolveRuntimeObject(record.objectName);
            if (obj==null)
            {
                LogError("Runtime SO not found for field apply: "+record.assetName);
                continue;
            }
            ApplyFields(obj,record.fields,null,record.assetName);
        }
    }

    private void FillComponentFields(LoadedNobndlPack pack)
    {
        NobndlPrefabScriptBindingRecord[] records=pack.Manifest.prefabScriptBindings;
        if (records==null)
            return;
        Log("Applying prefab component fields");
        NobndlPrefabScriptBindingRecord[] ordered=records
            .Where(x => x!=null && x.runtimeTypeName!="<missing>")
            .OrderBy(FillOrder)
            .ToArray();
        for (int i=0; i<ordered.Length; i++)
        {
            NobndlPrefabScriptBindingRecord record=ordered[i];
            string key=BuildComponentKey(record.prefabName,record.gameObjectPath,record.runtimeTypeName,record.componentOrderOnGameObject);
            Component component;
            if (!runtimeComponents.TryGetValue(key,out component) || component==null)
            {
                LogError("Runtime component not found for field apply: "+key);
                continue;
            }
            Type declared=ResolveType(record.runtimeTypeName,record.runtimeAssemblyName);
            if (declared!=null && !declared.IsInstanceOfType(component))
            {
                LogError("Binding type mismatch: manifest says "+declared.Name +
                         " but the prefab has "+component.GetType().Name+" | "+key);
                continue;
            }
            GameObject prefab=FindPrefab(record.prefabName);
            if (FillOrder(record)<PackScriptOrder)
                FillComponent(component,record.fields,prefab,record.prefabName);
            else
                ApplyFields(component,record.fields,prefab,record.prefabName);
        }
    }
}
