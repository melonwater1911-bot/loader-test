using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(MainMenu),"State",MethodType.Setter)]
static class GameLoadedPatch
{
    private static void Postfix()
    {
        if(MainMenu.State==MainMenu.LoadingState.Loaded) Loader.Instance.OnGameLoaded();
    }
}

[HarmonyPatch(typeof(MapSettingsManager),"LoadMap")]
static class MapLoadedPatch
{
    private static void Postfix(MapSettings __result)
    {
        Loader.Instance.OnMapLoaded(__result);
    }
}

[HarmonyPatch(typeof(Application),"version",MethodType.Getter)]
static class LobbyVersionPatch
{
    private static void Postfix(ref string __result)
    {
        __result+=Loader.Instance.lobbyTag;
    }
}
