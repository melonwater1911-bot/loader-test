using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

partial class Loader
{
    public const string Version="0.7.0";

    internal string LobbyTag()
    {
        var tags = new List<string>();
        for (int i=0; i<packs.Count; i++)
        {
            Manifest m=packs[i].Manifest;
            tags.Add(m.packId+"-"+(string.IsNullOrEmpty(m.packVersion) ? "0" : m.packVersion));
        }
        tags.Sort(System.StringComparer.OrdinalIgnoreCase);
        return "_nucmod"+Version+(tags.Count>0 ? "+"+string.Join("+",tags.ToArray()) : "");
    }
}

[HarmonyPatch(typeof(Application),"version",MethodType.Getter)]
static class LobbyVersionPatch
{
    private static void Postfix(ref string __result)
    {
        if(Loader.Instance!=null) __result+=Loader.Instance.LobbyTag();
    }
}
