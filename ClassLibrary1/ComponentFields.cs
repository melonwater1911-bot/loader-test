using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{

    private bool IsBindingOfType(NobndlPrefabScriptBindingRecord record,Type type)
    {
        if (record==null || type==null)
            return false;
        string runtime=record.runtimeTypeName ?? "";
        string editor=record.editorTypeName ?? "";
        return string.Equals(runtime,type.FullName,StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(editor,type.FullName,StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(runtime,type.Name,StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(editor,type.Name,StringComparison.InvariantCultureIgnoreCase);
    }

    private bool IsAudioBinding(NobndlPrefabScriptBindingRecord record)
    {
        return IsBindingOfType(record,typeof(AudioSource));
    }

    private bool IsMeshBinding(NobndlPrefabScriptBindingRecord record)
    {
        return IsBindingOfType(record,typeof(MeshFilter));
    }

    private bool IsColliderBinding(NobndlPrefabScriptBindingRecord record)
    {
        return IsBindingOfType(record,typeof(MeshCollider));
    }

    private bool IsParticleBinding(NobndlPrefabScriptBindingRecord record)
    {
        return IsBindingOfType(record,typeof(ParticleSystemRenderer));
    }

    private void FillComponent(Component target,NobndlFieldRecord[] fields,GameObject prefabRoot,string contextName)
    {
        if (target==null || fields==null)
            return;
        int applied=0;
        for (int i=0; i<fields.Length; i++)
        {
            NobndlFieldRecord record=fields[i];
            if (record==null || string.IsNullOrEmpty(record.name))
                continue;
            if (SetComponentField(target,record,prefabRoot,contextName))
                applied++;
            else
                LogWarning("Component field not applied: "+target.GetType().Name+"."+record.name +
                           " on "+GetFullPath(target.transform)+" | context="+contextName);
        }
        if (VerboseComponentApplyLogs)
        {
            LogWarning("Applied component: context="+contextName +
                       " type="+target.GetType().Name +
                       " path="+GetFullPath(target.transform) +
                       " fields="+applied+"/"+fields.Length);
        }
    }

    private bool SetComponentField(Component target,NobndlFieldRecord record,GameObject prefabRoot,string contextName)
    {
        PropertyInfo property=target.GetType().GetProperty(record.name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property==null || !property.CanWrite)
            return false;
        object value=BuildFieldValue(property.PropertyType,record,prefabRoot,contextName);
        if (value==null && record.kind!="Null" && typeof(UnityEngine.Object).IsAssignableFrom(property.PropertyType))
            value=ConvertForField(FindAsset(record,property.PropertyType),property.PropertyType);
        if (value==null && record.kind!="Null")
            return false;
        object prepared=PrepareIfMaterial(value);
        try
        {
            property.SetValue(target,prepared,null);
            if (VerboseMaterialSlotLogs)
                ReportMaterials(target,record,prepared);
            return true;
        }
        catch (Exception ex)
        {
            ReportMaterials(target,record,prepared);
            LogSuppressed("SetComponentField "+target.GetType().Name+"."+record.name,ex);
            return false;
        }
    }

    private void ReportMaterials(Component target,NobndlFieldRecord record,object value)
    {
        Material[] array=value as Material[];
        Material single=value as Material;
        if (array==null && single==null)
            return;
        Material[] applied=array ?? new Material[] { single };
        NobndlFieldRecord[] asked=record.children ?? new NobndlFieldRecord[0];
        var text = new StringBuilder();
        text.Append("Materials: ").Append(GetFullPath(target.transform))
            .Append('.').Append(record.name).Append(" slots=").Append(applied.Length);
        for (int i=0; i<applied.Length; i++)
        {
            NobndlFieldRecord want=i<asked.Length ? asked[i] : null;
            text.Append(" | ").Append(i).Append(": ");
            text.Append(want!=null ? want.kind+" "+PickName(want) : "<no record>");
            text.Append(" -> ");
            text.Append(applied[i]!=null ? applied[i].name+" ["+AssignMats.ShaderName(applied[i].shader)+"]" : "NULL");
        }
        LogWarning(text.ToString());
    }

    private object PrepareIfMaterial(object value)
    {
        Material material=value as Material;
        if (material!=null)
            return mats.Prepare(cache,material);
        Material[] array=value as Material[];
        if (array!=null)
            return mats.Prepare(cache,array);
        return value;
    }

    private const int PackScriptOrder=4;

    private int FillOrder(NobndlPrefabScriptBindingRecord record)
    {
        if (IsAudioBinding(record))
            return 0;
        if (IsMeshBinding(record) || IsColliderBinding(record))
            return 1;
        if (IsRendererBinding(record))
            return 2;
        if (IsParticleBinding(record))
            return 3;
        return PackScriptOrder;
    }

    private bool IsRendererBinding(NobndlPrefabScriptBindingRecord record)
    {
        if (record==null)
            return false;
        if (IsParticleBinding(record))
            return false;
        string runtime=record.runtimeTypeName ?? "";
        string editor=record.editorTypeName ?? "";
        return IsRendererType(runtime) || IsRendererType(editor);
    }

    private bool IsRendererType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return false;
        if (string.Equals(typeName,typeof(Renderer).FullName,StringComparison.InvariantCultureIgnoreCase))
            return true;
        if (string.Equals(typeName,"Renderer",StringComparison.InvariantCultureIgnoreCase))
            return true;
        if (typeName.EndsWith(".Renderer",StringComparison.InvariantCultureIgnoreCase))
            return true;
        if (typeName.EndsWith("Renderer",StringComparison.InvariantCultureIgnoreCase) &&
            !typeName.EndsWith("ParticleSystemRenderer",StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }
        return false;
    }

}
