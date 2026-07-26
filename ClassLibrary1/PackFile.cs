using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    //unpack/pack tool, fix manifest without unity
    private NobndlFile ReadPackFile(string path)
    {
        byte[] all=File.ReadAllBytes(path);
        using (MemoryStream stream=new MemoryStream(all))
        using (BinaryReader reader=new BinaryReader(stream,Encoding.UTF8))
        {
            string magic=Encoding.ASCII.GetString(reader.ReadBytes(6));
            if (magic!=NobndlMagic)
                throw new Exception("Invalid magic: "+magic);
            int version=reader.ReadInt32();
            if (version!=SupportedVersion)
                throw new Exception("Unsupported NOBNDL version: "+version+". Rebuild pack with new v2 builder.");
            int count=reader.ReadInt32();
            if (count<0 || count>100000)
                throw new Exception("Invalid entry count: "+count);
            NobndlFile file=new NobndlFile();
            file.Version=version;
            for (int i=0; i<count; i++)
                file.Entries.Add(ReadEntry(reader,i));
            Log("Pack parsed. Version="+version+" Entries="+file.Entries.Count);
            for (int i=0; i<file.Entries.Count; i++)
            {
                NobndlEntry e=file.Entries[i];
                Log("Entry["+i+"] "+e.Name+" type="+e.Type+" size="+e.Data.Length);
            }
            return file;
        }
    }

    private NobndlEntry ReadEntry(BinaryReader reader,int index)
    {
        int nameLength=reader.ReadInt32();
        if (nameLength<=0 || nameLength>4096)
            throw new Exception("Invalid entry name length at "+index+": "+nameLength);
        string name=Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        NobndlEntryType type=(NobndlEntryType)reader.ReadInt32();
        long dataLength=reader.ReadInt64();
        if (dataLength<0 || dataLength>int.MaxValue)
            throw new Exception("Invalid data length for "+name+": "+dataLength);
        int hashLength=reader.ReadInt32();
        if (hashLength<=0 || hashLength>1024)
            throw new Exception("Invalid hash length for "+name+": "+hashLength);
        byte[] expectedHash=reader.ReadBytes(hashLength);
        byte[] data=reader.ReadBytes((int)dataLength);
        byte[] actualHash=Sha256(data);
        bool hashOk=ByteArrayEquals(expectedHash,actualHash);
        if (!hashOk)
            throw new Exception("SHA256 mismatch for "+name);
        NobndlEntry entry=new NobndlEntry();
        entry.Name=name;
        entry.Type=type;
        entry.Data=data;
        entry.ExpectedHash=expectedHash;
        return entry;
    }

    private void FixManifest(NobndlManifest manifest)
    {
        if (manifest==null)
            return;
        if (manifest.scripts==null)
            manifest.scripts=new string[0];
        if (manifest.assets==null)
            manifest.assets=new string[0];
        if (manifest.scriptableObjects==null)
            manifest.scriptableObjects=new NobndlScriptableObjectRecord[0];
        if (manifest.prefabScriptBindings==null)
            manifest.prefabScriptBindings=new NobndlPrefabScriptBindingRecord[0];
        if (manifest.aircraftPylonPatches==null)
            manifest.aircraftPylonPatches=new AircraftPylonPatchManifestEntry[0];
        if (manifest.listPatches==null)
            manifest.listPatches=new NobndlListPatchManifestEntry[0];
        for (int i=0; i<manifest.scriptableObjects.Length; i++)
        {
            NobndlScriptableObjectRecord record=manifest.scriptableObjects[i];
            if (record==null)
                continue;
            if (record.fields==null)
                record.fields=new NobndlFieldRecord[0];
            FixFields(record.fields);
        }
        for (int i=0; i<manifest.prefabScriptBindings.Length; i++)
        {
            NobndlPrefabScriptBindingRecord record=manifest.prefabScriptBindings[i];
            if (record==null)
                continue;
            if (record.fields==null)
                record.fields=new NobndlFieldRecord[0];
            FixFields(record.fields);
        }
    }

    private void FixFields(NobndlFieldRecord[] records)
    {
        if (records==null)
            return;
        for (int i=0; i<records.Length; i++)
        {
            NobndlFieldRecord record=records[i];
            if (record==null)
                continue;
            if (record.children==null)
                record.children=new NobndlFieldRecord[0];
            FixFields(record.children);
        }
    }

    private void LogManifest(NobndlManifest m)
    {
        Log("Manifest:");
        Log("  format: "+m.format);
        Log("  formatVersion: "+m.formatVersion);
        Log("  packId: "+m.packId);
        Log("  packVersion: "+m.packVersion);
        Log("  unityVersion: "+m.unityVersion);
        Log("  buildTarget: "+m.buildTarget);
        Log("  builtAtUtc: "+m.builtAtUtc);
        Log("  bundleFile: "+m.bundleFile);
        Log("  scripts: "+(m.scripts!=null ? m.scripts.Length.ToString() : "null"));
        Log("  assets: "+(m.assets!=null ? m.assets.Length.ToString() : "null"));
        Log("  scriptableObjects: "+(m.scriptableObjects!=null ? m.scriptableObjects.Length.ToString() : "null"));
        Log("  prefabScriptBindings: "+(m.prefabScriptBindings!=null ? m.prefabScriptBindings.Length.ToString() : "null"));
        Log("  aircraftPylonPatches: "+(m.aircraftPylonPatches!=null ? m.aircraftPylonPatches.Length.ToString() : "null"));
    }
}
