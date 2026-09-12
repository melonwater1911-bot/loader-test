using HarmonyLib;

[HarmonyPatch(typeof(MainMenu),"State",MethodType.Setter)]
static class GameLoadedPatch
{
    private static void Postfix()
    {
        if(MainMenu.State!=MainMenu.LoadingState.Loaded) return;
        if(Loader.Instance!=null) Loader.Instance.OnGameLoaded();
    }
}
