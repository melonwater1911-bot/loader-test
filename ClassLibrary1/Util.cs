using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

static class NobndlUtil
{
    public static string NormalizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        string normalized=value.Trim().Replace("\\","/");
        int clone=normalized.IndexOf("(Clone)",StringComparison.InvariantCultureIgnoreCase);
        if (clone>=0)
            normalized=normalized.Substring(0,clone).Trim();
        return normalized;
    }

    public static string NormalizeAssetKey(string value)
    {
        string normalized=NormalizeName(value);
        if (normalized.Length==0)
            return "";
        int sep=normalized.LastIndexOf("::",StringComparison.InvariantCulture);
        if (sep>=0)
        {
            string assetPart=normalized.Substring(0,sep);
            string subAssetPart=normalized.Substring(sep+2).Trim();
            return StripPath(assetPart)+"::"+subAssetPart;
        }
        return StripPath(normalized);
    }

    public static bool NameMatches(string a,string b)
    {
        return string.Equals(NormalizeName(a),NormalizeName(b),StringComparison.InvariantCultureIgnoreCase);
    }

    public static bool ContainsIgnoreCase(string text,string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            return false;
        return text.IndexOf(value,StringComparison.InvariantCultureIgnoreCase)>=0;
    }

    private static string StripPath(string value)
    {
        int slash=value.LastIndexOf('/');
        if (slash>=0)
            value=value.Substring(slash+1);
        int dot=value.LastIndexOf('.');
        if (dot>0 && IsLetterExtension(value,dot))
            value=value.Substring(0,dot);
        return value;
    }

    private static bool IsLetterExtension(string value,int dotIndex)
    {
        if (dotIndex+1>=value.Length)
            return false;
        for (int i=dotIndex+1; i<value.Length; i++)
        {
            if (!char.IsLetter(value[i]))
                return false;
        }
        return true;
    }

    public static string GetFullPath(Transform t)
    {
        if (t==null)
            return "<null>";
        string path=t.name;
        while (t.parent!=null)
        {
            t=t.parent;
            path=t.name+"/"+path;
        }
        return path;
    }

    public static string GetRelPath(Transform root,Transform target)
    {
        if (root==null || target==null || root==target)
            return "";
        List<string> parts=new List<string>();
        Transform current=target;
        while (current!=null && current!=root)
        {
            parts.Add(GetPathSegment(current));
            current=current.parent;
        }
        parts.Reverse();
        return string.Join("/",parts.ToArray());
    }

    private static string GetPathSegment(Transform t)
    {
        Transform parent=t.parent;
        if (parent==null)
            return t.name;
        int occurrence=0;
        int total=0;
        for (int i=0; i<parent.childCount; i++)
        {
            Transform child=parent.GetChild(i);
            if (!string.Equals(child.name,t.name,StringComparison.Ordinal))
                continue;
            if (child==t)
                occurrence=total;
            total++;
        }
        if (total<=1)
            return t.name;
        return t.name+"@"+occurrence.ToString(CultureInfo.InvariantCulture);
    }

    public static int GetComponentIndex(Component component)
    {
        if (component==null)
            return -1;
        Component[] components=component.gameObject.GetComponents(component.GetType());
        if (components==null)
            return -1;
        for (int i=0; i<components.Length; i++)
        {
            if (components[i]==component)
                return i;
        }
        return -1;
    }

    public static FieldInfo FindFieldRecursive(Type type,string fieldName)
    {
        while (type!=null)
        {
            FieldInfo field=type.GetField(
                fieldName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly
            );
            if (field!=null)
                return field;
            type=type.BaseType;
        }
        return null;
    }

    public static string ObjName(UnityEngine.Object obj)
    {
        return obj!=null ? obj.name : "null";
    }

    public static byte[] Sha256(byte[] data)
    {
        using (SHA256 sha=SHA256.Create())
            return sha.ComputeHash(data);
    }

    public static bool ByteArrayEquals(byte[] a,byte[] b)
    {
        if (a==null || b==null || a.Length!=b.Length)
            return false;
        for (int i=0; i<a.Length; i++)
        {
            if (a[i]!=b[i])
                return false;
        }
        return true;
    }
}
