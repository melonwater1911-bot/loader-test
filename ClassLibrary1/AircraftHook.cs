using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

[HarmonyPatch]
static class NobndlWeaponManagerReadyPatch
{
    private static readonly string[] CandidateMethodNames={ "Start","Awake","OnEnable" };

    public static bool HookInstalled;

    private static bool Prepare()
    {
        MethodBase target=ResolveTargetMethod();
        if (target==null)
        {
            Debug.LogWarning("[nobndlLoader] WeaponManager hook not installed: no Start/Awake/OnEnable declared on WeaponManager. Pylon patches will only run on scene load.");
            return false;
        }
        HookInstalled=true;
        return true;
    }

    private static MethodBase TargetMethod()
    {
        return ResolveTargetMethod();
    }

    private static MethodBase ResolveTargetMethod()
    {
        for (int i=0; i<CandidateMethodNames.Length; i++)
        {
            MethodInfo method=typeof(WeaponManager).GetMethod(
                CandidateMethodNames[i],
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null
            );
            if (method!=null)
                return method;
        }
        return null;
    }

    private static void Postfix(WeaponManager __instance)
    {
        NobndlLoaderPlugin plugin=NobndlLoaderPlugin.Instance;
        if (plugin==null || __instance==null)
            return;
        try
        {
            plugin.OnAircraftReady(__instance,"WeaponManager hook");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[nobndlLoader] WeaponManager hook failed: "+ex.Message);
        }
    }
}
