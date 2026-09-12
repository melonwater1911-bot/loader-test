using HarmonyLib;

[HarmonyPatch(typeof(Encyclopedia),"AfterLoad")]
static class EncyclopediaPatch
{
    private static void Postfix()
    {
        if(Loader.Instance!=null) Loader.Instance.OnEncyclopediaRebuilt();
    }
}
