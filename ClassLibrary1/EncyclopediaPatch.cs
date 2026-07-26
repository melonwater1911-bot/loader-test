using HarmonyLib;
using System.Reflection;
using static NobndlLog;
using static NobndlUtil;

[HarmonyPatch]
static class NobndlEncyclopediaPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Encyclopedia),"AfterLoad");
    }

    private static bool Prepare()
    {
        return TargetMethod()!=null;
    }

    private static void Postfix()
    {
        if (NobndlLoaderPlugin.Instance!=null)
            NobndlLoaderPlugin.Instance.OnEncyclopediaRebuilt();
    }
}
