using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void FixMissileAudio(object target,GameObject prefabRoot,string contextName)
    {
        if (target==null || prefabRoot==null)
            return;
        FieldInfo flightField=FindField(target.GetType(),"flightSound");
        FieldInfo motorsField=FindField(target.GetType(),"motors");
        bool missileLike =
            (flightField!=null && typeof(AudioSource).IsAssignableFrom(flightField.FieldType)) ||
            (motorsField!=null && motorsField.FieldType.IsArray);
        if (!missileLike)
            return;
        Component ownerComponent=target as Component;
        AudioSource loopSource=FindLoopSource(prefabRoot,ownerComponent);
        AudioSource startupSource=FindStartSource(prefabRoot,ownerComponent,loopSource);
        if (loopSource==null && startupSource==null)
            return;
        int repaired=0;
        if (flightField!=null && typeof(AudioSource).IsAssignableFrom(flightField.FieldType) && loopSource!=null)
        {
            AudioSource existing=flightField.GetValue(target) as AudioSource;
            if (existing==null)
            {
                try
                {
                    flightField.SetValue(target,loopSource);
                    repaired++;
                }
                catch (Exception ex)
                {
                    LogWarning("Failed to repair Missile.flightSound: context="+contextName+" error="+ex.Message);
                }
            }
        }
        if (motorsField!=null && motorsField.FieldType.IsArray)
        {
            try
            {
                Array motors=motorsField.GetValue(target) as Array;
                if (motors!=null)
                {
                    for (int i=0; i<motors.Length; i++)
                    {
                        object motor=motors.GetValue(i);
                        if (motor==null)
                            continue;
                        bool motorChanged=FixMotorAudio(motor,loopSource,startupSource,contextName,i);
                        if (motorChanged)
                        {
                            motors.SetValue(motor,i);
                            repaired++;
                        }
                    }
                    motorsField.SetValue(target,motors);
                }
            }
            catch (Exception ex)
            {
                LogWarning("Failed to repair Missile.motors audio refs: context="+contextName+" error="+ex.Message);
            }
        }
        if (repaired>0 && loopSource!=null)
        {
            loopSource.loop=true;
            if (loopSource.clip!=null && loopSource.volume<=0f)
                loopSource.volume=1f;
            AddLoopStarter(prefabRoot,loopSource,startupSource);
        }
        if (repaired>0)
        {
            LogWarning("Repaired missile audio refs: context="+contextName +
                       " loop="+AudioSourceLabel(loopSource) +
                       " startup="+AudioSourceLabel(startupSource) +
                       " repaired="+repaired);
        }
    }

    private bool FixMotorAudio(object motor,AudioSource loopSource,AudioSource startupSource,string contextName,int motorIndex)
    {
        if (motor==null)
            return false;
        bool changed=false;
        Type motorType=motor.GetType();
        if (loopSource!=null)
        {
            FieldInfo audioField=FindField(motorType,"audioSources");
            if (audioField!=null &&
                audioField.FieldType.IsArray &&
                typeof(AudioSource).IsAssignableFrom(audioField.FieldType.GetElementType()))
            {
                AudioSource[] existing=audioField.GetValue(motor) as AudioSource[];
                bool needsLoop=existing==null;
                if (needsLoop)
                {
                    try
                    {
                        audioField.SetValue(motor,new AudioSource[] { loopSource });
                        changed=true;
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to repair motor.audioSources: context="+contextName +
                                   " motor="+motorIndex +
                                   " error="+ex.Message);
                    }
                }
            }
        }
        if (startupSource!=null)
        {
            FieldInfo startField=FindField(motorType,"startupSource");
            if (startField!=null && typeof(AudioSource).IsAssignableFrom(startField.FieldType))
            {
                AudioSource oldStartup=startField.GetValue(motor) as AudioSource;
                if (oldStartup==null)
                {
                    try
                    {
                        startField.SetValue(motor,startupSource);
                        changed=true;
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Failed to repair motor.startupSource: context="+contextName +
                                   " motor="+motorIndex +
                                   " error="+ex.Message);
                    }
                }
            }
        }
        return changed;
    }

    private void AddLoopStarter(GameObject prefabRoot,AudioSource loopSource,AudioSource startupSource)
    {
        if (prefabRoot==null || loopSource==null)
            return;
        NobndlMissileLoopAudioStarter starter=prefabRoot.GetComponent<NobndlMissileLoopAudioStarter>();
        if (starter==null)
            starter=prefabRoot.AddComponent<NobndlMissileLoopAudioStarter>();
        starter.loopSource=loopSource;
        starter.startupSource=startupSource;
    }

    private FieldInfo FindField(Type type,string fieldName)
    {
        while (type!=null && type!=typeof(object))
        {
            FieldInfo field=type.GetField(fieldName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field!=null)
                return field;
            type=type.BaseType;
        }
        return null;
    }

    private AudioSource FindLoopSource(GameObject prefabRoot,Component ownerComponent)
    {
        if (prefabRoot==null)
            return null;
        GameObject owner=ownerComponent!=null ? ownerComponent.gameObject : prefabRoot;
        AudioSource[] sources=prefabRoot.GetComponentsInChildren<AudioSource>(true);
        if (sources==null || sources.Length==0)
            return null;
        AudioSource firstLocalLoop=null;
        AudioSource firstAnyLoop=null;
        for (int i=0; i<sources.Length; i++)
        {
            AudioSource source=sources[i];
            if (source==null || !source.loop)
                continue;
            if (firstAnyLoop==null)
                firstAnyLoop=source;
            if (owner!=null && source.gameObject==owner && firstLocalLoop==null)
                firstLocalLoop=source;
        }
        return firstLocalLoop ?? firstAnyLoop;
    }

    private AudioSource FindStartSource(GameObject prefabRoot,Component ownerComponent,AudioSource loopSource)
    {
        if (prefabRoot==null)
            return null;
        GameObject owner=ownerComponent!=null ? ownerComponent.gameObject : prefabRoot;
        AudioSource[] sources=prefabRoot.GetComponentsInChildren<AudioSource>(true);
        if (sources==null || sources.Length==0)
            return null;
        AudioSource firstLocal=null;
        AudioSource firstAny=null;
        for (int i=0; i<sources.Length; i++)
        {
            AudioSource source=sources[i];
            if (source==null || source==loopSource || source.loop)
                continue;
            if (source.clip==null)
                continue;
            if (firstAny==null)
                firstAny=source;
            if (owner!=null && source.gameObject==owner && firstLocal==null)
                firstLocal=source;
        }
        return firstLocal ?? firstAny;
    }

    private string AudioSourceLabel(AudioSource source)
    {
        if (source==null)
            return "<null>";
        return GetFullPath(source.transform) +
               " clip="+(source.clip!=null ? source.clip.name : "<null>") +
               " loop="+source.loop +
               " playOnAwake="+source.playOnAwake +
               " volume="+source.volume.ToString("R",CultureInfo.InvariantCulture);
    }

}
