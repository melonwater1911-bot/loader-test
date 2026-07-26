using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

sealed class NobndlPrefabHashes
{
    private readonly Dictionary<int,int> assigned=new Dictionary<int,int>();
    private List<Component> identities;
    private bool rescanRequested=true;

    public int Count
    {
        get { return identities!=null ? identities.Count : 0; }
    }

    public void RequestRescan()
    {
        rescanRequested=true;
    }

    public void Assign(NobndlBundleCache cache,Type idType)
    {
        if (cache==null || idType==null)
            return;
        if (!rescanRequested && identities!=null && StillOurs())
            return;
        rescanRequested=false;
        identities=Collect(cache,idType);
        if (identities.Count==0)
        {
            Log("PrefabHash: no NetworkIdentity components in bundle prefabs");
            return;
        }
        HashSet<int> taken=TakenByGame(idType,identities);
        int reused=0;
        int reassigned=0;
        int failed=0;
        for (int i=0; i<identities.Count; i++)
        {
            Component component=identities[i];
            string rootName=component.transform.root.name;
            int current=Read(component);
            int key=component.GetInstanceID();
            int ours;
            if (assigned.TryGetValue(key,out ours))
            {
                if (current==ours)
                {
                    taken.Add(current);
                    reused++;
                    continue;
                }
                LogWarning("PrefabHash was reset by the game, reapplying: prefab="+rootName +
                                              " assigned="+ours +
                                              " wasResetTo="+current);
            }
            else if (current!=0 && !taken.Contains(current))
            {
                taken.Add(current);
                assigned[key]=current;
                reused++;
                continue;
            }
            int replacement=ours!=0 ? ours : FreeHash(Seed(component),taken);
            Write(component,replacement);
            if (Read(component)==replacement)
            {
                taken.Add(replacement);
                assigned[key]=replacement;
                reassigned++;
                LogWarning("PrefabHash collision fixed: prefab="+rootName +
                                              " old="+current +
                                              " new="+replacement);
            }
            else
            {
                failed++;
                LogError("PrefabHash collision could NOT be fixed: prefab="+rootName +
                                            " hash="+current +
                                            " - mission start will fail with 'Prefab with hash already registered'.");
            }
        }
        LogWarning("PrefabHash: found="+identities.Count +
                                      " reused="+reused +
                                      " reassigned="+reassigned +
                                      " failed="+failed);
    }

    private bool StillOurs()
    {
        for (int i=0; i<identities.Count; i++)
        {
            Component component=identities[i];
            if (component==null)
                return false;
            int approved;
            if (!assigned.TryGetValue(component.GetInstanceID(),out approved))
                return false;
            if (Read(component)!=approved)
                return false;
        }
        return true;
    }

    private List<Component> Collect(NobndlBundleCache cache,Type idType)
    {
        List<Component> result=new List<Component>();
        HashSet<int> seen=new HashSet<int>();
        HashSet<string> packRootNames=new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        foreach (GameObject root in cache.PrefabRoots.Distinct().ToArray())
        {
            if (root==null)
                continue;
            packRootNames.Add(root.name);
            Component[] components=root.GetComponentsInChildren<Component>(true);
            for (int i=0; i<components.Length; i++)
            {
                Component component=components[i];
                if (component!=null &&
                    idType.IsAssignableFrom(component.GetType()) &&
                    seen.Add(component.GetInstanceID()))
                {
                    result.Add(component);
                }
            }
        }
        UnityEngine.Object[] all;
        try
        {
            all=Resources.FindObjectsOfTypeAll(idType);
        }
        catch (Exception ex)
        {
            LogWarning("PrefabHash duplicate scan failed: "+ex.Message);
            return result;
        }
        int extra=0;
        for (int i=0; i<all.Length; i++)
        {
            Component component=all[i] as Component;
            if (component==null || !seen.Add(component.GetInstanceID()))
                continue;
            if (!packRootNames.Contains(component.transform.root.name))
                continue;
            result.Add(component);
            extra++;
            LogWarning("PrefabHash: extra copy of a pack prefab found outside bundle cache: root=" +
                                          component.transform.root.name +
                                          " instanceId="+component.GetInstanceID() +
                                          " hash="+Read(component));
        }
        if (extra>0)
            LogWarning("PrefabHash: "+extra+" duplicate instance(s) of pack prefabs will also be fixed");
        return result;
    }

    private HashSet<int> TakenByGame(Type idType,List<Component> ourIdentities)
    {
        HashSet<int> taken=new HashSet<int>();
        HashSet<int> packIds=new HashSet<int>();
        for (int i=0; i<ourIdentities.Count; i++)
            packIds.Add(ourIdentities[i].GetInstanceID());
        UnityEngine.Object[] all;
        try
        {
            all=Resources.FindObjectsOfTypeAll(idType);
        }
        catch (Exception ex)
        {
            LogWarning("PrefabHash scan failed: "+ex.Message);
            return taken;
        }
        for (int i=0; i<all.Length; i++)
        {
            UnityEngine.Object obj=all[i];
            if (obj==null || packIds.Contains(obj.GetInstanceID()))
                continue;
            int hash=Read(obj);
            if (hash!=0)
                taken.Add(hash);
        }
        return taken;
    }

    private static string Seed(Component component)
    {
        Transform root=component.transform.root;
        string path=GetRelPath(root,component.transform);
        int order=GetComponentIndex(component);
        return root.name+"/"+path+"#"+order.ToString(CultureInfo.InvariantCulture);
    }

    private static int FreeHash(string seed,HashSet<int> taken)
    {
        for (int i=0; i<1000; i++)
        {
            int hash=HashText(seed+":"+i.ToString(CultureInfo.InvariantCulture));
            if (hash!=0 && !taken.Contains(hash))
                return hash;
        }
        throw new Exception("Unable to find a free PrefabHash for seed="+seed);
    }

    private static int HashText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        unchecked
        {
            int hash=(int)2166136261;
            for (int i=0; i<text.Length; i++)
                hash=(hash ^ text[i])*16777619;
            return hash==0 ? 1 : hash;
        }
    }

    private static FieldInfo HashField(Type type)
    {
        return FindFieldRecursive(type,"prefabHash") ??
               FindFieldRecursive(type,"_prefabHash") ??
               FindFieldRecursive(type,"PrefabHash");
    }

    private static int Read(object identity)
    {
        if (identity==null)
            return 0;
        PropertyInfo prop=identity.GetType().GetProperty("PrefabHash",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop!=null && prop.CanRead)
        {
            try
            {
                return AsHash(prop.GetValue(identity,null));
            }
            catch (Exception ex)
            {
                LogError("PrefabHash property unreadable on "+identity.GetType().FullName+": "+ex.Message);
            }
        }
        FieldInfo field=HashField(identity.GetType());
        if (field!=null)
        {
            try
            {
                return AsHash(field.GetValue(identity));
            }
            catch (Exception ex)
            {
                LogError("PrefabHash field unreadable on "+identity.GetType().FullName+": "+ex.Message);
            }
        }
        return 0;
    }

    private static int AsHash(object value)
    {
        if (value is int)
            return (int)value;
        if (value is uint)
            return unchecked((int)(uint)value);
        return 0;
    }

    private static void Write(object identity,int hash)
    {
        if (identity==null)
            return;
        PropertyInfo prop=identity.GetType().GetProperty("PrefabHash",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop!=null && prop.CanWrite && TryWrite(prop.PropertyType,hash,value => prop.SetValue(identity,value,null)))
            return;
        FieldInfo field=HashField(identity.GetType());
        if (field!=null && TryWrite(field.FieldType,hash,value => field.SetValue(identity,value)))
            return;
        LogError("PrefabHash is not writable on "+identity.GetType().FullName +
                                    " (property="+(prop!=null)+" field="+(field!=null)+")");
    }

    private static bool TryWrite(Type memberType,int hash,Action<object> setter)
    {
        try
        {
            if (memberType==typeof(int))
            {
                setter(hash);
                return true;
            }
            if (memberType==typeof(uint))
            {
                setter(unchecked((uint)hash));
                return true;
            }
        }
        catch (Exception ex)
        {
            LogWarning("PrefabHash write threw: "+ex.Message);
        }
        return false;
    }
}
