using BepInEx.Logging;
using UnityEngine;

static class Logs
{
    public static ManualLogSource Source;

    public static void Log(string message)
    {
        Source.LogInfo(message);
        Debug.Log("[nucmod] "+message);
    }

    public static void LogWarning(string message)
    {
        Source.LogWarning(message);
        Debug.LogWarning("[nucmod] "+message);
    }

    public static void LogError(string message)
    {
        Source.LogError(message);
        Debug.LogError("[nucmod] "+message);
    }
}
