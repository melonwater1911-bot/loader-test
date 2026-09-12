using System;
using HarmonyLib;
using UnityEngine;
using static Logs;

[HarmonyPatch(typeof(WeaponManager),"Awake")]
static class WeaponManagerPatch
{
    private static void Postfix(WeaponManager __instance)
    {
        Loader plugin=Loader.Instance;
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
