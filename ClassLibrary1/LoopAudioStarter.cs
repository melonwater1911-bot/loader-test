using System;
using System.Collections;
using UnityEngine;
using static Logs;
using static Util;

sealed class LoopAudioStarter : MonoBehaviour
{
    public AudioSource loopSource;
    public AudioSource startupSource;

    private IEnumerator Start()
    {
        if (loopSource==null)
            yield break;
        float delay=0f;
        if (startupSource!=null && startupSource.clip!=null)
            delay=Mathf.Clamp(startupSource.clip.length,0f,4f);
        if (delay>0.01f)
            yield return new WaitForSeconds(delay);
        if (loopSource!=null && loopSource.clip!=null && !loopSource.isPlaying)
        {
            loopSource.loop=true;
            loopSource.Play();
        }
    }
}
