using System;
using System.Reflection;
using UnityEngine;
using static Logs;
using static Util;

partial class Loader
{
    private const int PackScriptOrder=4;

    private static int FillOrder(Type type)
    {
        if(type==typeof(AudioSource)) return 0;
        if(type==typeof(MeshFilter) || type==typeof(MeshCollider)) return 1;
        if(type==typeof(ParticleSystemRenderer)) return 3;
        if(typeof(Renderer).IsAssignableFrom(type)) return 2;
        return PackScriptOrder;
    }

    private void FillComponent(Component target,FieldRecord[] fields,GameObject prefabRoot,string context)
    {
        for (int i=0; i<fields.Length; i++)
        {
            FieldRecord record=fields[i];
            if(string.IsNullOrEmpty(record.name)) continue;
            PropertyInfo property=target.GetType().GetProperty(record.name,BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if(property==null || !property.CanWrite) continue;
            object value=BuildFieldValue(property.PropertyType,record,prefabRoot,context);
            if (value==null && record.kind!="Null")
            {
                LogWarning("Reference not resolved: "+target.GetType().Name+"."+record.name+" kind="+record.kind+" "+(record.assetPath ?? "")+" "+(record.objectName ?? "")+" | "+context);
                continue;
            }
            if(value is Material) value=mats.Prepare(cache,(Material)value);
            else if(value is Material[]) value=mats.Prepare(cache,(Material[])value);
            try
            {
                property.SetValue(target,value,null);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to set "+target.GetType().Name+"."+record.name+" on "+GetFullPath(target.transform)+": "+ex.Message);
            }
        }
    }
}
