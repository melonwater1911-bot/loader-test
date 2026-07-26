using HarmonyLib;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

[HarmonyPatch(typeof(MainMenu),"State",MethodType.Setter)]
static class NobndlGameLoadedPatch
{
    private static bool Prepare()
    {
        if (AccessTools.PropertySetter(typeof(MainMenu),"State")!=null)
            return true;
        Debug.LogError("[nobndlLoader] MainMenu.State setter not found, packs will never install. Game update?");
        return false;
    }

    private static void Postfix()
    {
        if (MainMenu.State!=MainMenu.LoadingState.Loaded)
            return;
        if (NobndlLoaderPlugin.Instance!=null)
            NobndlLoaderPlugin.Instance.OnGameLoaded();
    }
}
