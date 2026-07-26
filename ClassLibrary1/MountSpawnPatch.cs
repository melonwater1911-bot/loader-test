using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

[HarmonyPatch(typeof(Hardpoint),"SpawnMount")]
static class NobndlMountSpawnPatch
{
    private static readonly Dictionary<int,bool> OriginalPrefabState=new Dictionary<int,bool>();
    private static readonly Dictionary<int,int> ActivationDepth=new Dictionary<int,int>();

    private static bool Prefix(Hardpoint __instance,Aircraft aircraft,WeaponMount weaponMount)
    {
        if (weaponMount==null || weaponMount.prefab==null)
            return true;
        if (!NobndlLoaderPlugin.IsManagedMount(weaponMount.name))
            return true;
        int id=weaponMount.GetInstanceID();
        if (!ActivationDepth.ContainsKey(id))
        {
            ActivationDepth[id]=0;
            OriginalPrefabState[id]=weaponMount.prefab.activeSelf;
        }
        ActivationDepth[id]=ActivationDepth[id]+1;
        weaponMount.prefab.SetActive(true);
        weaponMount.prefab.transform.position=Vector3.zero;
        return true;
    }

    private static void Postfix(Hardpoint __instance,Aircraft aircraft,WeaponMount weaponMount,GameObject ___spawnedPrefab)
    {
        if (weaponMount==null || weaponMount.prefab==null)
            return;
        if (!NobndlLoaderPlugin.IsManagedMount(weaponMount.name))
            return;
        if (___spawnedPrefab!=null)
            ___spawnedPrefab.SetActive(true);
        int id=weaponMount.GetInstanceID();
        if (!ActivationDepth.ContainsKey(id))
            return;
        int depth=ActivationDepth[id]-1;
        if (depth<=0)
        {
            ActivationDepth.Remove(id);
            bool original=false;
            if (OriginalPrefabState.ContainsKey(id))
            {
                original=OriginalPrefabState[id];
                OriginalPrefabState.Remove(id);
            }
            weaponMount.prefab.SetActive(original);
        }
        else
        {
            ActivationDepth[id]=depth;
        }
    }
}
