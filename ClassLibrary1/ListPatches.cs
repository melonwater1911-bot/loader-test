using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

partial class Loader
{
    private void ApplyPylons()
    {
        for (int p=0; p<packs.Count; p++)
        {
            PylonPatchEntry[] entries=packs[p].Manifest.aircraftPylonPatches;
            for (int i=0; i<entries.Length; i++)
            {
                PylonPatchEntry e=entries[i];
                HardpointSet set=Array.Find(GamePrefab(e.aircraftKey).GetComponent<Aircraft>().weaponManager.hardpointSets,s => s.name==e.hardpointName);
                set.weaponOptions.Add((WeaponMount)Value(typeof(WeaponMount),e.pylon));
            }
        }
    }

    private void ApplyListPatches()
    {
        for (int p=0; p<packs.Count; p++)
        {
            ListPatchEntry[] entries=packs[p].Manifest.listPatches;
            for (int i=0; i<entries.Length; i++) ApplyListPatch(entries[i],GamePrefab(entries[i].targetKey).transform);
        }
    }

    internal void OnMapLoaded(MapSettings map)
    {
        if(map==null) return;
        int added=0;
        int matched=0;
        int failed=0;
        foreach (Unit unit in map.GetComponentsInChildren<Unit>(true))
        {
            if(unit==null || unit.definition==null) continue;
            for (int p=0; p<packs.Count; p++)
            {
                ListPatchEntry[] entries=packs[p].Manifest.listPatches;
                for (int i=0; i<entries.Length; i++)
                {
                    if(entries[i].targetKey!=unit.definition.jsonKey) continue;
                    matched++;
                    try
                    {
                        if(ApplyListPatch(entries[i],unit.transform)) added++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Logs.LogWarning("Map list patch failed: pack="+packs[p].Manifest.packId+" unit="+unit.name+" target="+entries[i].targetKey+" path="+entries[i].componentPath+" field="+entries[i].listName+" | "+ex);
                    }
                }
            }
        }
        Logs.Log("Map list patches: map="+map.name+" matched="+matched+" added="+added+" already present="+(matched-added-failed)+" failed="+failed);
    }

    private bool ApplyListPatch(ListPatchEntry e,Transform root)
    {
        Type type=ResolveType(e.targetTypeName,e.targetAssemblyName);
        Component target=ComponentAt(Child(root,e.componentPath).gameObject,type,e.componentOrderOnGameObject);
        FieldInfo field=FindField(type,e.listName);
        Type elem=ElementType(field.FieldType);
        object value=Value(elem,e.value);
        if(value==null) throw new InvalidOperationException("Patch value could not be resolved: "+e.value.assetPath);
        IList items=(IList)field.GetValue(target);
        if(items.Contains(value)) return false;
        if (field.FieldType.IsArray)
        {
            Array grown=Array.CreateInstance(elem,items.Count+1);
            Array.Copy((Array)items,grown,items.Count);
            grown.SetValue(value,items.Count);
            field.SetValue(target,grown);
        }
        else items.Add(value);
        return true;
    }

    private static Type ElementType(Type type)
    {
        return type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
    }
}
