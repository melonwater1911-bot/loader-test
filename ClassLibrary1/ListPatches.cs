using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

static class NobndlListPatcher
{
    private sealed class Rule
    {
        public Type TargetType;
        public string MemberName;
        public FieldInfo Field;
        public PropertyInfo Property;
        public Type MemberType;
        public Type ElementType;
        public UnityEngine.Object Value;
        public string PackId;
        public int Applied;
        public bool FirstApplyLogged;
    }

    private static readonly string[] LifecycleNames={ "Awake","OnEnable","Start" };
    private static readonly List<Rule> Rules=new List<Rule>();
    private static readonly HashSet<string> RuleKeys=new HashSet<string>(StringComparer.Ordinal);
    private static readonly HashSet<MethodBase> PatchedMethods=new HashSet<MethodBase>();
    private static readonly HarmonyMethod Prefix=new HarmonyMethod(
        typeof(NobndlListPatcher).GetMethod(
            "BeforeLifecycle",
            BindingFlags.Static | BindingFlags.NonPublic
        )
    );
    private static readonly HarmonyMethod Postfix=new HarmonyMethod(
        typeof(NobndlListPatcher).GetMethod(
            "AfterLifecycle",
            BindingFlags.Static | BindingFlags.NonPublic
        )
    );

    public static void Reset()
    {
        Rules.Clear();
        RuleKeys.Clear();
        PatchedMethods.Clear();
    }

    public static bool Register(Type targetType,string memberName,UnityEngine.Object value,string packId)
    {
        string context="pack="+packId+" "+SafeTypeName(targetType)+"."+(memberName ?? "");

        if (targetType==null || string.IsNullOrWhiteSpace(memberName) || value==null)
        {
            LogError("List patch: incomplete entry | "+context);
            return false;
        }

        if (!typeof(Component).IsAssignableFrom(targetType))
        {
            LogError("List patch: target type is not a Component | "+context);
            return false;
        }

        FieldInfo field=FindFieldRecursive(targetType,memberName);
        PropertyInfo property=field==null
            ? FindPropertyRecursive(targetType,memberName)
            : null;

        if (field==null && property==null)
        {
            LogError("List patch: member not found | "+context);
            return false;
        }

        if (property!=null && !property.CanRead)
        {
            LogError("List patch: property cannot be read | "+context);
            return false;
        }

        Type memberType=field!=null ? field.FieldType : property.PropertyType;
        Type elementType=GetElementType(memberType);

        if (elementType==null)
        {
            LogError("List patch: member is not an array or List<T> | "+context+" type="+memberType.FullName);
            return false;
        }

        if (!elementType.IsInstanceOfType(value))
        {
            LogError("List patch: value type mismatch | "+context+
                     " expects="+elementType.FullName+
                     " actual="+value.GetType().FullName+
                     " value="+value.name);
            return false;
        }

        string key=targetType.AssemblyQualifiedName+"|"+memberName+"|"+value.GetInstanceID();

        if (!RuleKeys.Add(key))
            return false;

        Rule rule=new Rule();
        rule.TargetType=targetType;
        rule.MemberName=memberName;
        rule.Field=field;
        rule.Property=property;
        rule.MemberType=memberType;
        rule.ElementType=elementType;
        rule.Value=value;
        rule.PackId=packId ?? "";
        Rules.Add(rule);
        return true;
    }

    public static void InstallHooks(Harmony harmony)
    {
        if (harmony==null)
        {
            LogError("List patch: Harmony instance is missing");
            return;
        }

        HashSet<Type> targetTypes=new HashSet<Type>();

        for (int i=0; i<Rules.Count; i++)
            targetTypes.Add(Rules[i].TargetType);

        int methodsAdded=0;

        foreach (Type targetType in targetTypes)
        {
            List<MethodBase> methods=FindLifecycleMethods(targetType);

            if (methods.Count==0)
            {
                LogError("List patch: no Awake, OnEnable or Start found on "+targetType.FullName+
                         " or its component base classes");
                continue;
            }

            for (int i=0; i<methods.Count; i++)
            {
                MethodBase method=methods[i];

                if (!PatchedMethods.Add(method))
                    continue;

                try
                {
                    harmony.Patch(method,Prefix,Postfix);
                    methodsAdded++;
                }
                catch (Exception ex)
                {
                    PatchedMethods.Remove(method);
                    LogError("List patch: Harmony hook failed | "+MethodName(method)+" | "+ex);
                }
            }
        }

        if (Rules.Count>0)
        {
            LogWarning("List patch hooks installed: rules="+Rules.Count+
                       " targetTypes="+targetTypes.Count+
                       " methods="+methodsAdded);
        }
    }

    private static void BeforeLifecycle(Component __instance)
    {
        ApplyToInstance(__instance);
    }

    private static void AfterLifecycle(Component __instance)
    {
        ApplyToInstance(__instance);
    }

    private static void ApplyToInstance(Component __instance)
    {
        if (__instance==null)
            return;

        Type runtimeType=__instance.GetType();

        for (int i=0; i<Rules.Count; i++)
        {
            Rule rule=Rules[i];

            if (runtimeType!=rule.TargetType)
                continue;

            try
            {
                if (!Apply(rule,__instance))
                    continue;

                rule.Applied++;

                if (!rule.FirstApplyLogged)
                {
                    rule.FirstApplyLogged=true;
                    LogWarning("List patch applied: "+rule.TargetType.FullName+"."+rule.MemberName+
                               " += "+rule.Value.name+
                               " | first="+ObjectPath(__instance)+
                               " | pack="+rule.PackId);
                }
            }
            catch (Exception ex)
            {
                LogSuppressed(
                    "List patch "+rule.TargetType.FullName+"."+rule.MemberName+
                    " on "+ObjectPath(__instance),
                    ex
                );
            }
        }
    }

    private static bool Apply(Rule rule,Component target)
    {
        object collection=rule.Field!=null
            ? rule.Field.GetValue(target)
            : rule.Property.GetValue(target,null);

        if (Contains(collection,rule.Value))
            return false;

        if (rule.MemberType.IsArray)
        {
            if (rule.Field==null && !rule.Property.CanWrite)
                throw new InvalidOperationException("array property has no setter");

            Array current=collection as Array;
            int count=current!=null ? current.Length : 0;
            Array grown=Array.CreateInstance(rule.ElementType,count+1);

            if (current!=null)
                Array.Copy(current,grown,count);

            grown.SetValue(rule.Value,count);
            Assign(rule,target,grown);
            return true;
        }

        IList list=collection as IList;

        if (list==null)
        {
            if (rule.Field==null && !rule.Property.CanWrite)
                throw new InvalidOperationException("list property is null and has no setter");

            list=CreateList(rule.MemberType,rule.ElementType);
            Assign(rule,target,list);
        }

        if (list.IsReadOnly || list.IsFixedSize)
            throw new InvalidOperationException("list is read-only");

        list.Add(rule.Value);
        return true;
    }

    private static void Assign(Rule rule,Component target,object value)
    {
        if (rule.Field!=null)
            rule.Field.SetValue(target,value);
        else
            rule.Property.SetValue(target,value,null);
    }

    private static IList CreateList(Type memberType,Type elementType)
    {
        Type concrete=memberType;

        if (memberType.IsInterface || memberType.IsAbstract)
            concrete=typeof(List<>).MakeGenericType(elementType);

        IList list=Activator.CreateInstance(concrete) as IList;

        if (list==null)
            throw new InvalidOperationException("cannot create "+memberType.FullName);

        return list;
    }

    private static bool Contains(object collection,UnityEngine.Object value)
    {
        IList list=collection as IList;

        if (list==null)
            return false;

        for (int i=0; i<list.Count; i++)
        {
            UnityEngine.Object existing=list[i] as UnityEngine.Object;

            if (existing==value)
                return true;
        }

        return false;
    }

    private static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
            return collectionType.GetElementType();

        if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition()==typeof(List<>))
            return collectionType.GetGenericArguments()[0];

        return null;
    }

    private static PropertyInfo FindPropertyRecursive(Type type,string name)
    {
        Type current=type;

        while (current!=null && current!=typeof(object))
        {
            PropertyInfo property=current.GetProperty(
                name,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly |
                BindingFlags.IgnoreCase
            );

            if (property!=null)
                return property;

            current=current.BaseType;
        }

        return null;
    }

    private static List<MethodBase> FindLifecycleMethods(Type targetType)
    {
        List<MethodBase> result=new List<MethodBase>();
        HashSet<MethodBase> seen=new HashSet<MethodBase>();
        Type current=targetType;

        while (current!=null && current!=typeof(MonoBehaviour) && current!=typeof(Component))
        {
            for (int i=0; i<LifecycleNames.Length; i++)
            {
                MethodInfo method=current.GetMethod(
                    LifecycleNames[i],
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null
                );

                if (method!=null && !method.IsAbstract && seen.Add(method))
                    result.Add(method);
            }

            current=current.BaseType;
        }

        return result;
    }

    private static string SafeTypeName(Type type)
    {
        return type!=null ? type.FullName : "<unknown type>";
    }

    private static string MethodName(MethodBase method)
    {
        if (method==null)
            return "<unknown method>";

        return method.DeclaringType.FullName+"."+method.Name;
    }

    private static string ObjectPath(Component component)
    {
        if (component==null)
            return "<null>";

        Transform transform=component.transform;
        List<string> parts=new List<string>();

        while (transform!=null)
        {
            parts.Add(transform.name);
            transform=transform.parent;
        }

        parts.Reverse();
        return string.Join("/",parts.ToArray());
    }
}
