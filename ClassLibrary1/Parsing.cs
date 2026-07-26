using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private static Vector2 ParseVector2(string value)
    {
        string[] p=SplitFloatList(value,2);
        return new Vector2(ParseFloat(p[0]),ParseFloat(p[1]));
    }

    private static Vector3 ParseVector3(string value)
    {
        string[] p=SplitFloatList(value,3);
        return new Vector3(ParseFloat(p[0]),ParseFloat(p[1]),ParseFloat(p[2]));
    }

    private static Vector4 ParseVector4(string value)
    {
        string[] p=SplitFloatList(value,4);
        return new Vector4(ParseFloat(p[0]),ParseFloat(p[1]),ParseFloat(p[2]),ParseFloat(p[3]));
    }

    private static Quaternion ParseQuaternion(string value)
    {
        string[] p=SplitFloatList(value,4);
        return new Quaternion(ParseFloat(p[0]),ParseFloat(p[1]),ParseFloat(p[2]),ParseFloat(p[3]));
    }

    private static Color ParseColor(string value)
    {
        string[] p=SplitFloatList(value,4);
        return new Color(ParseFloat(p[0]),ParseFloat(p[1]),ParseFloat(p[2]),ParseFloat(p[3]));
    }

    private static AnimationCurve ParseAnimationCurve(string value)
    {
        AnimationCurve curve=new AnimationCurve();
        if (string.IsNullOrEmpty(value))
            return curve;
        string[] parts=value.Split('|');
        if (parts.Length<3)
            return curve;
        try
        {
            curve.preWrapMode=(WrapMode)int.Parse(parts[0],CultureInfo.InvariantCulture);
            curve.postWrapMode=(WrapMode)int.Parse(parts[1],CultureInfo.InvariantCulture);
            int keyCount=int.Parse(parts[2],CultureInfo.InvariantCulture);
            List<Keyframe> keys=new List<Keyframe>();
            for (int i=0; i<keyCount; i++)
            {
                int partIndex=3+i;
                if (partIndex>=parts.Length)
                    break;
                string[] k=SplitFloatList(parts[partIndex],7);
                Keyframe key=new Keyframe(
                    ParseFloat(k[0]),
                    ParseFloat(k[1]),
                    ParseFloat(k[2]),
                    ParseFloat(k[3])
                );
                key.inWeight=ParseFloat(k[4]);
                key.outWeight=ParseFloat(k[5]);
                key.weightedMode=(WeightedMode)int.Parse(k[6],CultureInfo.InvariantCulture);
                keys.Add(key);
            }
            curve.keys=keys.ToArray();
        }
        catch (Exception ex)
        {
            LogWarning("Failed to parse AnimationCurve: "+ex.Message+" value="+value);
        }
        return curve;
    }

    private static string[] SplitFloatList(string value,int count)
    {
        string[] parts=(value ?? "").Split(',');
        if (parts.Length<count)
        {
            Array.Resize(ref parts,count);
        }
        for (int i=0; i<count; i++)
        {
            if (string.IsNullOrEmpty(parts[i]))
                parts[i]="0";
        }
        return parts;
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value,CultureInfo.InvariantCulture);
    }
}
