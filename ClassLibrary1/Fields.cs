using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using static Logs;

partial class Loader
{
    private void ApplyFields(object target,FieldRecord[] fields)
    {
        bool native=target is Component && target.GetType().Namespace=="UnityEngine";
        for (int i=0; i<fields.Length; i++)
        {
            FieldRecord record=fields[i];
            FieldInfo field=native ? null : FindFieldForRecord(target.GetType(),record);
            PropertyInfo property=native ? target.GetType().GetProperty(record.name,BindingFlags.Instance | BindingFlags.Public) : null;
            if (!native && field==null)
            {
                LogWarning("Field skipped: "+target.GetType().FullName+"."+record.name+" | field or declaring type not found in runtime inheritance chain: "+record.declaringTypeName+". Rebuild the pack with the updated builder.");
                continue;
            }
            object value=Value(native ? property.PropertyType : field.FieldType,record);
            if(value is Material) value=mats.Prepare(cache,(Material)value);
            else if(value is Material[]) value=mats.Prepare(cache,(Material[])value);
            if(native) property.SetValue(target,value,null);
            else field.SetValue(target,value);
        }
    }

    private object Value(Type type,FieldRecord r)
    {
        switch (r.kind)
        {
            case "Plain": return r.value.ToObject(type,PackJson.Serializer);
            case "Object":
            {
                object instance=type.IsValueType || type.GetConstructor(Type.EmptyTypes)!=null ? Activator.CreateInstance(type) : FormatterServices.GetUninitializedObject(type);
                ApplyFields(instance,r.children);
                return instance;
            }
            case "Array":
            {
                Array array=Array.CreateInstance(type.GetElementType(),r.children.Length);
                for (int i=0; i<r.children.Length; i++) array.SetValue(Value(type.GetElementType(),r.children[i]),i);
                return array;
            }
            case "List":
            {
                IList list=(IList)Activator.CreateInstance(type);
                for (int i=0; i<r.children.Length; i++) list.Add(Value(type.GetGenericArguments()[0],r.children[i]));
                return list;
            }
            case "PackScriptableObject": return runtimeObjects[r.assetPath];
            case "BundleAsset": return cache.Sub(r.assetPath,r.objectName,type);
            case "BundleGameObject": return Node(r).gameObject;
            case "BundleComponent": return ComponentAt(Node(r).gameObject,ResolveType(r.componentTypeName,r.componentAssemblyName),r.componentOrderOnGameObject);
            case "GameAsset": return GameAsset(r.objectName,type);
        }
        return null;
    }

    private Transform Node(FieldRecord r)
    {
        return Child(((GameObject)cache.Main(r.assetPath)).transform,r.prefabPath);
    }
}
