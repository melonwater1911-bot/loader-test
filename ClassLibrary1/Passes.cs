using System;
using System.Reflection;
using UnityEngine;
using static Logs;

partial class Loader
{
    private void BuildPack()
    {
        RebindMaterials();
        for (int p=0; p<packs.Count; p++)
        {
            SoRecord[] records=packs[p].Manifest.scriptableObjects;
            for (int i=0; i<records.Length; i++)
            {
                ScriptableObject instance=ScriptableObject.CreateInstance(ResolveType(records[i].runtimeTypeName,records[i].runtimeAssemblyName));
                instance.name=records[i].objectName;
                runtimeObjects[records[i].assetPath]=instance;
            }
        }
        for (int p=0; p<packs.Count; p++)
        {
            BindingRecord[] records=packs[p].Manifest.prefabScriptBindings;
            for (int i=0; i<records.Length; i++)
            {
                GameObject target=Child(((GameObject)cache.Main(records[i].prefabAssetPath)).transform,records[i].gameObjectPath).gameObject;
                Type type=ResolveType(records[i].runtimeTypeName,records[i].runtimeAssemblyName);
                runtimeComponents[records[i]]=type.Namespace=="UnityEngine" ? ComponentAt(target,type,records[i].componentOrderOnGameObject) : target.AddComponent(type);
            }
        }
        for (int p=0; p<packs.Count; p++)
        {
            SoRecord[] records=packs[p].Manifest.scriptableObjects;
            for (int i=0; i<records.Length; i++)
            {
                ApplyFields(runtimeObjects[records[i].assetPath],records[i].fields);
            }
        }
        for (int p=0; p<packs.Count; p++)
        {
            BindingRecord[] records=packs[p].Manifest.prefabScriptBindings;
            for (int i=0; i<records.Length; i++)
            {
                ApplyFields(runtimeComponents[records[i]],records[i].fields);
            }
        }
    }
}
