using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

partial class Loader
{
    private enum EntryType
    {
        ManifestJson=1,
        AssetBundle=2,
        CSharpSource=3,
        SkinFile=4,
        MissionFile=5
    }

    private sealed class PackEntry
    {
        public string Name;
        public EntryType Type;
        public byte[] Data;
    }

    private sealed class Pack
    {
        public Manifest Manifest;
        public AssetBundle Bundle;
        public List<PackEntry> Entries;
    }

    public sealed class Manifest
    {
        public string packId;
        public string packVersion;
        public SoRecord[] scriptableObjects;
        public BindingRecord[] prefabScriptBindings;
        public PylonPatchEntry[] aircraftPylonPatches;
        public ListPatchEntry[] listPatches;
    }

    public sealed class SoRecord
    {
        public string assetPath;
        public string objectName;
        public string runtimeTypeName;
        public string runtimeAssemblyName;
        public FieldRecord[] fields;
    }

    public sealed class BindingRecord
    {
        public string prefabAssetPath;
        public string gameObjectPath;
        public string runtimeTypeName;
        public string runtimeAssemblyName;
        public int componentOrderOnGameObject;
        public FieldRecord[] fields;
    }

    public sealed class FieldRecord
    {
        public string name;
        public string declaringTypeName;
        public string kind;
        public JToken value;
        public string objectName;
        public string assetPath;
        public string prefabPath;
        public string componentTypeName;
        public string componentAssemblyName;
        public int componentOrderOnGameObject;
        public FieldRecord[] children;
    }

    public sealed class PylonPatchEntry
    {
        public string aircraftKey;
        public string hardpointName;
        public FieldRecord pylon;
    }

    public sealed class ListPatchEntry
    {
        public string targetKey;
        public string componentPath;
        public int componentOrderOnGameObject;
        public string targetTypeName;
        public string targetAssemblyName;
        public string listName;
        public FieldRecord value;
    }
}
