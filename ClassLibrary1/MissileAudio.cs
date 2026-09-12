using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

sealed class LoopAudioStarter : MonoBehaviour
{
    public AudioSource loopSource;
    public AudioSource startupSource;

    private IEnumerator Start()
    {
        if(loopSource==null) yield break;
        float delay=startupSource!=null && startupSource.clip!=null ? Mathf.Clamp(startupSource.clip.length,0f,4f) : 0f;
        if(delay>0.01f) yield return new WaitForSeconds(delay);
        if(loopSource==null || loopSource.clip==null || loopSource.isPlaying) yield break;
        loopSource.loop=true;
        loopSource.Play();
    }
}

partial class Loader
{
    private static readonly FieldInfo flightSound=AccessTools.Field(typeof(Missile),"flightSound");
    private static readonly FieldInfo motors=AccessTools.Field(typeof(Missile),"motors");
    private static readonly Type motorType=AccessTools.Inner(typeof(Missile),"Motor");
    private static readonly FieldInfo motorSources=AccessTools.Field(motorType,"audioSources");
    private static readonly FieldInfo motorStartup=AccessTools.Field(motorType,"startupSource");

    private void FixMissileAudio(object target,GameObject prefabRoot)
    {
        Missile missile=target as Missile;
        if(missile==null || prefabRoot==null) return;
        AudioSource loop=null;
        AudioSource startup=null;
        AudioSource[] sources=prefabRoot.GetComponentsInChildren<AudioSource>(true);
        for (int i=0; i<sources.Length; i++)
        {
            AudioSource s=sources[i];
            bool own=s.gameObject==missile.gameObject;
            if(s.loop && (loop==null || (own && loop.gameObject!=missile.gameObject))) loop=s;
            if(!s.loop && s.clip!=null && (startup==null || (own && startup.gameObject!=missile.gameObject))) startup=s;
        }
        if(loop==null && startup==null) return;
        bool repaired=false;
        if (loop!=null && flightSound.GetValue(missile)==null)
        {
            flightSound.SetValue(missile,loop);
            repaired=true;
        }
        Array list=motors.GetValue(missile) as Array;
        for (int i=0; list!=null && i<list.Length; i++)
        {
            object motor=list.GetValue(i);
            if(motor==null) continue;
            bool sources0=loop!=null && motorSources.GetValue(motor)==null;
            bool startup0=startup!=null && motorStartup.GetValue(motor)==null;
            if(!sources0 && !startup0) continue;
            if(sources0) motorSources.SetValue(motor,new AudioSource[] { loop });
            if(startup0) motorStartup.SetValue(motor,startup);
            list.SetValue(motor,i);
            repaired=true;
        }
        if(!repaired || loop==null) return;
        loop.loop=true;
        if(loop.clip!=null && loop.volume<=0f) loop.volume=1f;
        LoopAudioStarter starter=prefabRoot.GetComponent<LoopAudioStarter>() ?? prefabRoot.AddComponent<LoopAudioStarter>();
        starter.loopSource=loop;
        starter.startupSource=startup;
    }
}
