using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void QueuePylons()
    {
        pendingPylonPatches.Clear();
        for (int i=0; i<packs.Count; i++)
        {
            LoadedNobndlPack pack=packs[i];
            if (pack==null || pack.Manifest==null || pack.Manifest.aircraftPylonPatches==null)
                continue;
            for (int p=0; p<pack.Manifest.aircraftPylonPatches.Length; p++)
            {
                AircraftPylonPatchManifestEntry patch=pack.Manifest.aircraftPylonPatches[p];
                if (patch==null)
                    continue;
                PendingAircraftPylonPatch pending=new PendingAircraftPylonPatch();
                pending.Pack=pack;
                pending.Patch=patch;
                pending.Applied=false;
                pendingPylonPatches.Add(pending);
                Log("Queued pylon patch: "+patch.aircraftName+" | "+patch.hardpointName+" | "+patch.pylonPrefabName);
            }
        }
    }

    internal void OnAircraftReady(WeaponManager wm,string reason)
    {
        if (!Installed || pendingPylonPatches.Count==0)
            return;
        ApplyPylons(wm,reason);
    }

    private void ApplyAllPylons(string reason)
    {
        if (pendingPylonPatches.Count==0)
            return;
        WeaponManager[] all;
        try
        {
            all=Resources.FindObjectsOfTypeAll<WeaponManager>();
        }
        catch (Exception ex)
        {
            LogWarning("Pylon sweep failed to enumerate WeaponManagers: "+ex.Message);
            return;
        }
        for (int i=0; i<all.Length; i++)
            ApplyPylons(all[i],reason);
    }

    private void ApplyPylons(WeaponManager wm,string reason)
    {
        if (wm==null || wm.gameObject==null)
            return;
        for (int i=0; i<pendingPylonPatches.Count; i++)
        {
            PendingAircraftPylonPatch pending=pendingPylonPatches[i];
            if (pending==null || pending.Patch==null)
                continue;
            AircraftPylonPatchManifestEntry patch=pending.Patch;
            if (!BelongsToAircraft(wm,patch.aircraftName))
                continue;
            if (!HasHardpointSet(wm,patch.hardpointName))
                continue;
            WeaponMount mount=ResolveWeaponMount(patch.pylonPrefabName);
            if (mount==null)
            {
                LogError("Pylon patch mount not resolved: pylon="+patch.pylonPrefabName +
                         " aircraft="+patch.aircraftName +
                         " hardpoint="+patch.hardpointName +
                         " | no loaded pack provides this mount");
                continue;
            }
            if (AttachMount(wm,mount,patch.hardpointName))
                pending.Applied=true;
        }
    }

    private WeaponMount ResolveWeaponMount(string name)
    {
        UnityEngine.Object obj=ResolveRuntimeObject(name);
        WeaponMount mount=obj as WeaponMount;
        if (mount!=null)
            return mount;
        foreach (UnityEngine.Object value in runtimeObjects.Values)
        {
            mount=value as WeaponMount;
            if (mount==null)
                continue;
            if (NameMatches(mount.name,name) || NameMatches(mount.mountName,name) || (mount.prefab!=null && NameMatches(mount.prefab.name,name)))
                return mount;
        }
        return null;
    }

    private bool BelongsToAircraft(WeaponManager wm,string aircraftName)
    {
        if (wm==null || string.IsNullOrEmpty(aircraftName))
            return false;
        string path=GetFullPath(wm.transform);
        if (NameMatches(wm.gameObject.name,aircraftName) || NameMatches(path,aircraftName))
            return true;
        if (path.StartsWith(aircraftName+"/",StringComparison.InvariantCultureIgnoreCase))
            return true;
        Transform t=wm.transform;
        while (t!=null)
        {
            if (NameMatches(t.name,aircraftName))
                return true;
            t=t.parent;
        }
        Aircraft aircraft=wm.GetComponentInParent<Aircraft>(true);
        if (aircraft!=null)
        {
            if (NameMatches(aircraft.name,aircraftName))
                return true;
            if (aircraft.gameObject!=null && NameMatches(aircraft.gameObject.name,aircraftName))
                return true;
        }
        return false;
    }

    private bool HasHardpointSet(WeaponManager wm,string hardpointSetName)
    {
        if (wm==null || wm.hardpointSets==null)
            return false;
        for (int i=0; i<wm.hardpointSets.Length; i++)
        {
            HardpointSet set=wm.hardpointSets[i];
            if (set!=null && NameMatches(set.name,hardpointSetName))
                return true;
        }
        return false;
    }

    private bool AttachMount(WeaponManager wm,WeaponMount mount,string hardpointSetName)
    {
        if (wm==null || mount==null || wm.hardpointSets==null)
            return false;
        bool applied=false;
        for (int i=0; i<wm.hardpointSets.Length; i++)
        {
            HardpointSet set=wm.hardpointSets[i];
            if (set==null || !NameMatches(set.name,hardpointSetName))
                continue;
            if (set.weaponOptions==null)
            {
                set.weaponOptions=new List<WeaponMount>();
                Log("Created weaponOptions list for hardpoint set: "+set.name);
            }
            bool exists=false;
            for (int j=0; j<set.weaponOptions.Count; j++)
            {
                WeaponMount existing=set.weaponOptions[j];
                if (existing==mount || (existing!=null && NameMatches(existing.name,mount.name)))
                {
                    exists=true;
                    break;
                }
            }
            if (!exists)
            {
                set.weaponOptions.Add(mount);
                Log("ADDED MOUNT: "+mount.name+" -> "+set.name+" on "+GetFullPath(wm.transform));
            }
            else
            {
                Log("Mount already exists: "+mount.name+" in "+set.name);
            }
            applied=true;
        }
        return applied;
    }

    private void InitMount(WeaponMount mount)
    {
        if (mount==null)
            return;
        try
        {
            MethodInfo method=mount.GetType().GetMethod("Initialize",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method!=null)
            {
                method.Invoke(mount,null);
                Log("WeaponMount.Initialize called: "+mount.name);
            }
        }
        catch (Exception ex)
        {
            LogWarning("WeaponMount.Initialize failed for "+mount.name+": "+ex.Message);
        }
    }

    private void RegisterManagedMount(string mountName)
    {
        if (string.IsNullOrEmpty(mountName))
            return;
        string normalized=NormalizeName(mountName);
        if (!ManagedMountNames.Contains(normalized))
        {
            ManagedMountNames.Add(normalized);
            Log("Registered managed mount: "+mountName);
        }
    }

    public static bool IsManagedMount(string mountName)
    {
        if (string.IsNullOrEmpty(mountName))
            return false;
        return ManagedMountNames.Contains(NormalizeName(mountName));
    }
}
