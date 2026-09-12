using System.Collections.Generic;
using UnityEngine;
using static Logs;
using static Util;

partial class Loader
{
    private void QueuePylons()
    {
        for (int p=0; p<packs.Count; p++)
        {
            PylonPatchEntry[] entries=packs[p].Manifest.aircraftPylonPatches;
            for (int i=0; i<entries.Length; i++)
            {
                WeaponMount mount=PackObject(entries[i].pylonPath) as WeaponMount;
                if (mount==null)
                {
                    LogError("Pylon patch: "+entries[i].pylonPath+" is not a WeaponMount of pack "+packs[p].Manifest.packId);
                    continue;
                }
                pylonPatches.Add(new PylonPatch { aircraft=entries[i].aircraftName,hardpoint=entries[i].hardpointName,mount=mount });
            }
        }
    }

    internal void OnAircraftReady(WeaponManager wm)
    {
        if(installed) ApplyPylons(wm);
    }

    private void ApplyAllPylons()
    {
        if(pylonPatches.Count==0) return;
        foreach (WeaponManager wm in Resources.FindObjectsOfTypeAll<WeaponManager>()) ApplyPylons(wm);
    }

    private void ApplyPylons(WeaponManager wm)
    {
        if(wm.hardpointSets==null) return;
        for (int i=0; i<pylonPatches.Count; i++)
        {
            PylonPatch p=pylonPatches[i];
            if(!UnderAircraft(wm.transform,p.aircraft)) continue;
            for (int s=0; s<wm.hardpointSets.Length; s++)
            {
                HardpointSet set=wm.hardpointSets[s];
                if(set==null || !SameName(set.name,p.hardpoint)) continue;
                if(set.weaponOptions==null) set.weaponOptions=new List<WeaponMount>();
                if(!set.weaponOptions.Contains(p.mount)) set.weaponOptions.Add(p.mount);
            }
        }
    }

    private static bool UnderAircraft(Transform t,string aircraftName)
    {
        for (; t!=null; t=t.parent) if(SameName(t.name,aircraftName)) return true;
        return false;
    }
}
