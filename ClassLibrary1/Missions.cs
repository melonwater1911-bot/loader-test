using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using static Logs;

partial class Loader
{
    private void InstallMissions()
    {
        if(missions.Count==0) return;
        MissionGroup.ResourceGroup builtIn=MissionGroup.BuiltIn;
        FieldInfo assetsField=AccessTools.Field(typeof(MissionGroup.ResourceGroup),"assets");
        FieldInfo namesField=AccessTools.Field(typeof(MissionGroup.ResourceGroup),"names");
        var assets = new List<TextAsset>((TextAsset[])assetsField.GetValue(builtIn));
        var names = new List<MissionKey>((MissionKey[])namesField.GetValue(builtIn));
        for (int i=0; i<missions.Count; i++)
        {
            if (names.Exists(x => string.Equals(x.Name,missions[i].Name,StringComparison.InvariantCultureIgnoreCase)))
            {
                LogWarning("Mission name already taken, skipped: "+missions[i].Name);
                continue;
            }
            var asset = new TextAsset(missions[i].Json);
            asset.name=missions[i].Name;
            assets.Add(asset);
            names.Add(new MissionKey(missions[i].Name,builtIn));
        }
        assetsField.SetValue(builtIn,assets.ToArray());
        namesField.SetValue(builtIn,names.ToArray());
    }
}
