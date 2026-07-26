using BepInEx.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;

static class NobndlLog
{
    public static ManualLogSource Source;

    private static readonly HashSet<string> alreadySaid=new HashSet<string>(StringComparer.Ordinal);

    public static void Log(string message)
    {
        if (Source!=null)
            Source.LogInfo(message);
        Debug.Log("[nobndlLoader] "+message);
    }

    public static void LogWarning(string message)
    {
        if (Source!=null)
            Source.LogWarning(message);
        Debug.LogWarning("[nobndlLoader] "+message);
    }

    public static void LogError(string message)
    {
        if (Source!=null)
            Source.LogError(message);
        Debug.LogError("[nobndlLoader] "+message);
    }

    public static void LogSuppressed(string context,Exception ex)
    {
        if (ex==null)
            return;
        if (!alreadySaid.Add(context+"|"+ex.GetType().Name+"|"+ex.Message))
            return;
        LogWarning("Suppressed error: "+context+" | "+ex.GetType().Name+": "+ex.Message);
    }

    public static void LogMissingMember(string context,Type type,string memberName)
    {
        string typeName=type!=null ? type.FullName : "?";
        if (!alreadySaid.Add(context+"|"+typeName+"|"+memberName))
            return;
        LogWarning("Member not found: "+context +
                   " type="+typeName +
                   " member="+memberName +
                   " - the game may have renamed it");
    }
}
