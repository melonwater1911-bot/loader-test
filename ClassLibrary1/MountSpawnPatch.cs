using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Hardpoint),"SpawnMount")]
static class MountSpawnPatch
{
    private static bool Managed(WeaponMount mount)
    {
        return mount!=null && mount.prefab!=null && Loader.IsManagedMount(mount.name);
    }

    private static void Prefix(WeaponMount weaponMount)
    {
        if(!Managed(weaponMount)) return;
        weaponMount.prefab.SetActive(true);
        weaponMount.prefab.transform.position=Vector3.zero;
    }

    private static void Postfix(WeaponMount weaponMount,GameObject ___spawnedPrefab)
    {
        if(!Managed(weaponMount)) return;
        if(___spawnedPrefab!=null) ___spawnedPrefab.SetActive(true);
        weaponMount.prefab.SetActive(false);
    }
}
