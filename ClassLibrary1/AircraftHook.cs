using System;
using HarmonyLib;
using UnityEngine;
using static NobndlLog;

[HarmonyPatch(typeof(WeaponManager),"Awake")]
static class NobndlWeaponManagerReadyPatch
{
    private static void Postfix(WeaponManager __instance)
    {
        NobndlLoaderPlugin plugin=NobndlLoaderPlugin.Instance;
        if(plugin==null || __instance==null) return;
        try
        {
            plugin.OnAircraftReady(__instance,"WeaponManager hook");
        }
        catch (Exception ex)
        {
            LogWarning("WeaponManager hook failed: "+ex.Message);
        }
    }
}
