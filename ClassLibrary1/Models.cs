using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private enum NobndlEntryType
    {
        ManifestJson=1,
        AssetBundle=2,
        CSharpSource=3,
        SkinFile=4
    }

    private sealed class NobndlFile
    {
        public int Version;
        public readonly List<NobndlEntry> Entries=new List<NobndlEntry>();
        public NobndlEntry FindEntry(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            for (int i=0; i<Entries.Count; i++)
            {
                NobndlEntry entry=Entries[i];
                if (entry!=null && string.Equals(entry.Name,name,StringComparison.InvariantCultureIgnoreCase))
                    return entry;
            }
            return null;
        }
    }

    private sealed class NobndlEntry
    {
        public string Name;
        public NobndlEntryType Type;
        public byte[] Data;
        public byte[] ExpectedHash;
    }

    private sealed class LoadedNobndlPack
    {
        public string Path;
        public NobndlFile File;
        public NobndlManifest Manifest;
        public AssetBundle Bundle;
        public NobndlEntry[] ScriptEntries;
        public Assembly ScriptAssembly;
        public string ScriptAssemblyName;
    }

    private sealed class PendingAircraftPylonPatch
    {
        public LoadedNobndlPack Pack;
        public AircraftPylonPatchManifestEntry Patch;
        public bool Applied;
    }


    [Serializable]
    public sealed class NobndlManifest
    {
        public string format;
        public int formatVersion;
        public string packId;
        public string packVersion;
        public string unityVersion;
        public string buildTarget;
        public string builtAtUtc;
        public string bundleFile;
        public string[] scripts;
        public string[] assets;
        public NobndlScriptableObjectRecord[] scriptableObjects;
        public NobndlPrefabScriptBindingRecord[] prefabScriptBindings;
        public AircraftPylonPatchManifestEntry[] aircraftPylonPatches;
        public NobndlListPatchManifestEntry[] listPatches;
    }

    [Serializable]
    public sealed class NobndlScriptableObjectRecord
    {
        public string assetName;
        public string assetPath;
        public string objectName;
        public string editorTypeName;
        public string editorAssemblyName;
        public string runtimeTypeName;
        public string runtimeAssemblyName;
        public NobndlFieldRecord[] fields;
    }

    [Serializable]
    public sealed class NobndlPrefabScriptBindingRecord
    {
        public string prefabName;
        public string prefabAssetPath;
        public string gameObjectPath;
        public string editorTypeName;
        public string editorAssemblyName;
        public string runtimeTypeName;
        public string runtimeAssemblyName;
        public int componentOrderOnGameObject;
        public NobndlFieldRecord[] fields;
    }

    [Serializable]
    public sealed class NobndlFieldRecord
    {
        public string name;
        public string declaringTypeName;
        public string fieldTypeName;
        public string fieldAssemblyName;
        public string kind;
        public string value;
        public string objectName;
        public string objectTypeName;
        public string objectAssemblyName;
        public string assetName;
        public string assetPath;
        public string prefabPath;
        public string componentTypeName;
        public string componentAssemblyName;
        public int componentOrderOnGameObject;
        public NobndlFieldRecord[] children;
    }

    [Serializable]
    public sealed class AircraftPylonPatchManifestEntry
    {
        public string aircraftName;
        public string hardpointName;
        public string pylonPrefabName;
    }

    public sealed class NobndlListPatchManifestEntry
    {
        public string targetPrefabName;
        public string targetPrefabAssetPath;
        public string componentPath;
        public int componentOrderOnGameObject;
        public string targetTypeName;
        public string targetAssemblyName;
        public string listName;
        public string valueName;
        public string valueTypeName;
        public string valueAssemblyName;
        public string valueAssetPath;
        public string valueSource;
    }
}
