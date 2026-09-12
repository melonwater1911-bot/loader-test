using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class Util
{
    public static string Bare(string name)
    {
        int clone=name.IndexOf("(Clone)",StringComparison.InvariantCultureIgnoreCase);
        if(clone>=0) name=name.Substring(0,clone);
        name=name.Trim();
        int open=name.LastIndexOf(" (",StringComparison.Ordinal);
        if(open>0 && name.EndsWith(")") && name.Substring(open+2,name.Length-open-3).All(char.IsDigit)) name=name.Substring(0,open);
        return name;
    }

    public static bool SameName(string a,string b)
    {
        return string.Equals(Bare(a),Bare(b),StringComparison.InvariantCultureIgnoreCase);
    }

    public static string GetRelPath(Transform root,Transform target)
    {
        var parts = new List<string>();
        for (Transform t=target; t!=null && t!=root; t=t.parent) parts.Add(Segment(t));
        parts.Reverse();
        return string.Join("/",parts.ToArray());
    }

    private static string Segment(Transform t)
    {
        Transform parent=t.parent;
        if(parent==null) return t.name;
        int occurrence=0;
        int total=0;
        for (int i=0; i<parent.childCount; i++)
        {
            Transform child=parent.GetChild(i);
            if(child.name!=t.name) continue;
            if(child==t) occurrence=total;
            total++;
        }
        return total<=1 ? t.name : t.name+"@"+occurrence;
    }

    public static int GetComponentIndex(Component component)
    {
        return Array.IndexOf(component.gameObject.GetComponents(component.GetType()),component);
    }

    public static FieldInfo FindField(Type type,string fieldName)
    {
        for (; type!=null; type=type.BaseType)
        {
            FieldInfo field=type.GetField(fieldName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if(field!=null) return field;
        }
        return null;
    }
}
