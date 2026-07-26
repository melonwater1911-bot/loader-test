using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void ApplyFields(object target,NobndlFieldRecord[] fields,GameObject prefabRoot,string contextName)
    {
        if (target==null || fields==null)
            return;
        for (int i=0; i<fields.Length; i++)
        {
            NobndlFieldRecord record=fields[i];
            if (record==null)
                continue;
            try
            {
                FieldInfo field=FindFieldForRecord(target.GetType(),record);
                if (field==null)
                {
                    LogWarning("Field not found: target="+target.GetType().FullName+" field="+record.name+" context="+contextName);
                    continue;
                }
                if (record.kind=="GameAsset")
                {
                    UnityEngine.Object asset=FindAsset(record,field.FieldType);
                    object assetValue=asset!=null ? ConvertForField(asset,field.FieldType) : null;
                    if (asset==null || (assetValue==null && field.FieldType!=typeof(UnityEngine.Object)))
                    {
                        LogError("Game asset not found: field="+record.name +
                                 " on="+target.GetType().Name +
                                 " wants="+PickName(record) +
                                 " type="+field.FieldType.Name +
                                 " context="+contextName);
                        continue;
                    }
                    field.SetValue(target,assetValue);
                    continue;
                }
                object value=BuildFieldValue(field.FieldType,record,prefabRoot,contextName);
                if (value==null &&
                    typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType) &&
                    !string.Equals(record.kind,"Null",StringComparison.InvariantCultureIgnoreCase) &&
                    !string.Equals(record.kind,"Unsupported",StringComparison.InvariantCultureIgnoreCase))
                {
                    LogWarning("Field resolved to NULL: target="+target.GetType().FullName +
                               " field="+record.name +
                               " kind="+record.kind +
                               " value="+record.value +
                               " assetName="+record.assetName +
                               " context="+contextName);
                }
                field.SetValue(target,value);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to apply field "+record.name+" on "+target.GetType().FullName+": "+ex.Message);
            }
        }
        FixMissileAudio(target,prefabRoot,contextName);
    }






    private string PickName(NobndlFieldRecord record)
    {
        if (record==null)
            return "";
        if (!string.IsNullOrEmpty(record.objectName))
            return record.objectName;
        if (!string.IsNullOrEmpty(record.value))
            return record.value;
        if (!string.IsNullOrEmpty(record.assetName))
            return record.assetName;
        return "";
    }

    private int IdentityHash(object obj)
    {
        if (obj==null)
            return 0;
        return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private object BuildFieldValue(Type targetType,NobndlFieldRecord record,GameObject prefabRoot,string contextName)
    {
        if (record==null || record.kind=="Null")
            return null;
        object simple;
        if (TryParseValue(targetType,record,out simple))
            return simple;
        switch (record.kind)
        {
            case "GameAsset":
                return ConvertForField(FindAsset(record,targetType),targetType);
            case "BundleAsset":
                return ConvertForField(FindBundleObject(record,targetType,prefabRoot),targetType);
            case "NobndlScriptableObject":
                return ConvertForField(FindPackObject(record),targetType);
            case "UnityObjectName":
                return ConvertForField(FindGameObject(record.objectName,targetType),targetType);
            case "PrefabGameObject":
                return FindPrefabChild(record,prefabRoot);
            case "PrefabComponent":
                return FindPrefabComponent(record,targetType,prefabRoot);
            case "Array":
                return BuildArray(targetType,record,prefabRoot,contextName);
            case "List":
                return BuildList(targetType,record,prefabRoot,contextName);
            case "SerializableObject":
                return BuildObject(targetType,record,prefabRoot,contextName);
        }
        return null;
    }

    private static readonly Dictionary<Type,Func<string,object>> ValueParsers=new Dictionary<Type,Func<string,object>>
    {
        { typeof(string),     v => v ?? "" },
        { typeof(bool),       v => string.Equals(v,"true",StringComparison.InvariantCultureIgnoreCase) },
        { typeof(Vector2),    v => ParseVector2(v) },
        { typeof(Vector3),    v => ParseVector3(v) },
        { typeof(Vector4),    v => ParseVector4(v) },
        { typeof(Quaternion), v => ParseQuaternion(v) },
        { typeof(Color),      v => ParseColor(v) }
    };

    private bool TryParseValue(Type targetType,NobndlFieldRecord record,out object result)
    {
        Func<string,object> parse;
        if (ValueParsers.TryGetValue(targetType,out parse))
        {
            result=parse(record.value);
            return true;
        }
        if (targetType.IsEnum)
        {
            result=Enum.Parse(targetType,record.value);
            return true;
        }
        if (targetType.IsPrimitive || targetType==typeof(decimal))
        {
            result=Convert.ChangeType(record.value,targetType,CultureInfo.InvariantCulture);
            return true;
        }
        if (targetType==typeof(AnimationCurve) && record.kind=="AnimationCurve")
        {
            result=ParseAnimationCurve(record.value);
            return true;
        }
        result=null;
        return false;
    }

    private UnityEngine.Object FindBundleObject(NobndlFieldRecord record,Type targetType,GameObject prefabRoot)
    {
        UnityEngine.Object obj=ResolveRef(record,targetType,prefabRoot);
        if (obj!=null)
            return obj;
        string[] keys =
        {
            SubAssetKey(record.assetPath,record.objectName),
            SubAssetKey(record.assetName,record.objectName),
            record.objectName,
            record.value,
            record.assetName,
            record.assetPath
        };
        for (int i=0; i<keys.Length; i++)
        {
            obj=ResolveBundleAsset(keys[i],targetType);
            if (obj!=null)
                return obj;
        }
        for (int i=0; i<keys.Length; i++)
        {
            obj=ResolveBundleAsset(keys[i]);
            if (obj!=null)
                return obj;
        }
        return FindGameObject(record.objectName,targetType);
    }

    private UnityEngine.Object FindPackObject(NobndlFieldRecord record)
    {
        return ResolveRuntimeObject(record.value) ??
               ResolveRuntimeObject(record.assetName) ??
               ResolveRuntimeObject(record.objectName);
    }

    private object FindPrefabChild(NobndlFieldRecord record,GameObject prefabRoot)
    {
        if (prefabRoot==null)
            return null;
        Transform child=FindChildByPath(prefabRoot.transform,record.prefabPath);
        return child!=null ? child.gameObject : null;
    }

    private object FindPrefabComponent(NobndlFieldRecord record,Type targetType,GameObject prefabRoot)
    {
        if (prefabRoot==null)
            return null;
        Transform child=FindChildByPath(prefabRoot.transform,record.prefabPath);
        if (child==null)
        {
            LogWarning("PrefabComponent path not found: prefab="+prefabRoot.name+" path="+record.prefabPath+" component="+record.componentTypeName);
            return null;
        }
        Type componentType=ResolveType(record.componentTypeName,record.componentAssemblyName) ?? targetType;
        Component component=FindComponentAssignable(child.gameObject,componentType,record.componentOrderOnGameObject);
        if (component==null)
        {
            LogWarning("PrefabComponent not found: prefab="+prefabRoot.name +
                       " path="+record.prefabPath +
                       " component="+record.componentTypeName +
                       " order="+record.componentOrderOnGameObject.ToString(CultureInfo.InvariantCulture));
        }
        return component;
    }

    private object BuildArray(Type targetType,NobndlFieldRecord record,GameObject prefabRoot,string contextName)
    {
        if (!targetType.IsArray)
            return null;
        Type elementType=targetType.GetElementType();
        NobndlFieldRecord[] children=record.children ?? new NobndlFieldRecord[0];
        Array array=Array.CreateInstance(elementType,children.Length);
        for (int i=0; i<children.Length; i++)
            array.SetValue(BuildFieldValue(elementType,children[i],prefabRoot,contextName),i);
        return array;
    }

    private object BuildList(Type targetType,NobndlFieldRecord record,GameObject prefabRoot,string contextName)
    {
        if (!typeof(IList).IsAssignableFrom(targetType) || !targetType.IsGenericType)
            return null;
        Type elementType=targetType.GetGenericArguments()[0];
        IList list=Activator.CreateInstance(targetType) as IList ??
                   Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) as IList;
        NobndlFieldRecord[] children=record.children ?? new NobndlFieldRecord[0];
        for (int i=0; i<children.Length; i++)
            list.Add(BuildFieldValue(elementType,children[i],prefabRoot,contextName));
        return list;
    }

    private object BuildObject(Type targetType,NobndlFieldRecord record,GameObject prefabRoot,string contextName)
    {
        Type objectType=ResolveType(record.objectTypeName,record.objectAssemblyName) ?? targetType;
        bool hasDefaultCtor=objectType.IsValueType || objectType.GetConstructor(Type.EmptyTypes)!=null;
        object instance=hasDefaultCtor
            ? Activator.CreateInstance(objectType)
            : FormatterServices.GetUninitializedObject(objectType);
        ApplyFields(instance,record.children,prefabRoot,contextName);
        return instance;
    }

    private Sprite MakeSprite(Texture2D texture)
    {
        if (texture==null)
            return null;
        int id=texture.GetInstanceID();
        Sprite cached;
        if (runtimeSpritesFromTextures.TryGetValue(id,out cached) && cached!=null)
            return cached;
        Sprite sprite=Sprite.Create(
            texture,
            new Rect(0f,0f,texture.width,texture.height),
            new Vector2(0.5f,0.5f),
            100f
        );
        sprite.name=texture.name;
        runtimeSpritesFromTextures[id]=sprite;
        LogWarning("Created runtime Sprite from Texture2D bundle asset: "+texture.name);
        return sprite;
    }

    private string SubAssetKey(string assetKey,string objectName)
    {
        if (string.IsNullOrEmpty(assetKey) || string.IsNullOrEmpty(objectName))
            return "";
        return assetKey+"::"+objectName;
    }

    private UnityEngine.Object ResolveRef(NobndlFieldRecord record,Type targetType,GameObject contextPrefabRoot)
    {
        if (record==null || targetType==null)
            return null;
        GameObject root=FindPrefab(record.assetPath) ??
                          FindPrefab(record.value) ??
                          FindPrefab(record.assetName);
        if (root==null)
            root=contextPrefabRoot;
        if (root==null)
            return null;
        string objectName=!string.IsNullOrEmpty(record.objectName) ? record.objectName : record.value;
        if (targetType==typeof(GameObject))
        {
            GameObject g=FindChildByName(root,objectName);
            if (g!=null)
                return g;
            if (NameMatches(root.name,objectName))
                return root;
        }
        if (targetType==typeof(Transform))
        {
            Transform t=FindChildTransform(root,objectName);
            if (t!=null)
                return t;
            if (NameMatches(root.name,objectName))
                return root.transform;
        }
        if (typeof(Component).IsAssignableFrom(targetType))
        {
            if (NameMatches(root.name,objectName))
            {
                Component rootComponent=root.GetComponent(targetType);
                if (rootComponent!=null)
                    return rootComponent;
            }
            Transform child=FindChildTransform(root,objectName);
            if (child!=null)
            {
                Component childComponent=child.GetComponent(targetType);
                if (childComponent!=null)
                    return childComponent;
            }
            Component[] components=root.GetComponentsInChildren(targetType,true);
            for (int i=0; i<components.Length; i++)
            {
                Component component=components[i];
                if (component==null)
                    continue;
                if (NameMatches(component.name,objectName) ||
                    (component.gameObject!=null && NameMatches(component.gameObject.name,objectName)))
                {
                    return component;
                }
            }
            if (components.Length==1)
                return components[0];
        }
        if (targetType.IsAssignableFrom(root.GetType()) && NameMatches(root.name,objectName))
            return root;
        return null;
    }

    private GameObject FindChildByName(GameObject root,string objectName)
    {
        Transform t=FindChildTransform(root,objectName);
        return t!=null ? t.gameObject : null;
    }

    private Transform FindChildTransform(GameObject root,string objectName)
    {
        if (root==null || string.IsNullOrEmpty(objectName))
            return null;
        Transform[] transforms=root.GetComponentsInChildren<Transform>(true);
        for (int i=0; i<transforms.Length; i++)
        {
            Transform t=transforms[i];
            if (t!=null && NameMatches(t.name,objectName))
                return t;
        }
        return null;
    }

    private UnityEngine.Object ConvertForField(UnityEngine.Object obj,Type targetType)
    {
        if (obj==null || targetType==null)
            return null;
        if (targetType.IsAssignableFrom(obj.GetType()))
            return obj;
        if (targetType==typeof(Sprite))
        {
            Texture2D texture=obj as Texture2D;
            if (texture!=null)
                return MakeSprite(texture);
        }
        GameObject g=obj as GameObject;
        if (g!=null && typeof(Component).IsAssignableFrom(targetType))
            return g.GetComponent(targetType);
        Component component=obj as Component;
        if (component!=null)
        {
            if (targetType==typeof(GameObject))
                return component.gameObject;
            if (targetType.IsAssignableFrom(component.GetType()))
                return component;
        }
        return null;
    }
}
