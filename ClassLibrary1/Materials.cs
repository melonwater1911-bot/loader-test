using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System;
using UnityEngine.Rendering;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void RebindMaterials(string reason)
    {
        int rendererCount=0;
        int replaced=0;
        int managedByManifest=0;
        int unresolved=0;
        List<GameObject> roots=GetPrefabRoots();
        for (int rootIndex=0; rootIndex<roots.Count; rootIndex++)
        {
            GameObject root=roots[rootIndex];
            if (root==null)
                continue;
            Renderer[] renderers=root.GetComponentsInChildren<Renderer>(true);
            for (int r=0; r<renderers.Length; r++)
            {
                Renderer renderer=renderers[r];
                if (renderer==null)
                    continue;
                rendererCount++;
                if (HasMaterialBinding(root,renderer))
                {
                    managedByManifest++;
                    continue;
                }
                Material[] slots=renderer.sharedMaterials;
                if (slots==null || slots.Length==0)
                {
                    unresolved++;
                    ReportUnresolvedSlot(root,renderer,-1);
                    continue;
                }
                bool changed=false;
                for (int m=0; m<slots.Length; m++)
                {
                    Material mat=slots[m];
                    if (mat==null)
                    {
                        unresolved++;
                        ReportUnresolvedSlot(root,renderer,m);
                        continue;
                    }
                    Material runtime=materialFactory.RuntimeClone(cache,mat);
                    if (runtime==null || runtime==mat)
                        continue;
                    slots[m]=runtime;
                    changed=true;
                    replaced++;
                    if (VerboseMaterialSlotLogs)
                    {
                        Log("Bundle material swapped for its runtime clone: prefab="+root.name +
                            " renderer="+GetFullPath(renderer.transform) +
                            " slot="+m +
                            " "+mat.name+" ["+NobndlMaterialFactory.ShaderName(mat.shader)+"]" +
                            " -> "+runtime.name+" ["+NobndlMaterialFactory.ShaderName(runtime.shader)+"]");
                    }
                }
                if (changed)
                    renderer.sharedMaterials=slots;
            }
        }
        Log("Material rebind: reason="+reason +
            " renderers="+rendererCount +
            " managedByManifest="+managedByManifest +
            " clonedSlots="+replaced +
            " unresolvedSlots="+unresolved);
    }

    private void ReportUnresolvedSlot(GameObject root,Renderer renderer,int slot)
    {
        StringBuilder text=new StringBuilder();
        text.Append("Material slot not resolved: pack=").Append(PackIdForPrefab(root))
            .Append(" prefab=").Append(root.name)
            .Append(" renderer=").Append(GetFullPath(renderer.transform))
            .Append(" slot=").Append(slot<0 ? "<no slots>" : slot.ToString(CultureInfo.InvariantCulture))
            .Append(" reason=")
            .Append(slot<0 ? "renderer has an empty sharedMaterials array" : "slot is null")
            .Append(" | manifest has no material binding for this renderer, nothing was substituted");
        List<Material> candidates=materialFactory.RuntimeMaterials(cache);
        text.Append(" | pack materials (").Append(candidates.Count).Append("):");
        for (int i=0; i<candidates.Count; i++)
        {
            if (candidates[i]==null)
                continue;
            text.Append(' ').Append(candidates[i].name);
        }
        LogWarning(text.ToString());
    }

    private string PackIdForPrefab(GameObject root)
    {
        if (root==null)
            return "<unknown>";
        string prefabName=root.name ?? "";
        for (int p=0; p<packs.Count; p++)
        {
            LoadedNobndlPack pack=packs[p];
            if (pack==null || pack.Manifest==null || pack.Manifest.prefabScriptBindings==null)
                continue;
            NobndlPrefabScriptBindingRecord[] records=pack.Manifest.prefabScriptBindings;
            for (int i=0; i<records.Length; i++)
            {
                if (records[i]!=null && NameMatches(records[i].prefabName,prefabName))
                    return pack.Manifest.packId;
            }
        }
        return "<unknown>";
    }

    private bool HasMaterialBinding(GameObject prefabRoot,Renderer renderer)
    {
        if (prefabRoot==null || renderer==null)
            return false;
        string prefabName=prefabRoot.name ?? "";
        string path=GetRelPath(prefabRoot.transform,renderer.transform);
        for (int p=0; p<packs.Count; p++)
        {
            LoadedNobndlPack pack=packs[p];
            if (pack==null || pack.Manifest==null || pack.Manifest.prefabScriptBindings==null)
                continue;
            NobndlPrefabScriptBindingRecord[] records=pack.Manifest.prefabScriptBindings;
            for (int i=0; i<records.Length; i++)
            {
                NobndlPrefabScriptBindingRecord record=records[i];
                if (record==null)
                    continue;
                if (!NameMatches(record.prefabName,prefabName))
                    continue;
                if (!PathsMatch(record.gameObjectPath,path))
                    continue;
                if (!BindingTargetsRenderer(record,renderer))
                    continue;
                if (SetsMaterials(record))
                    return true;
            }
        }
        return false;
    }

    private bool BindingTargetsRenderer(NobndlPrefabScriptBindingRecord record,Renderer renderer)
    {
        if (record==null || renderer==null)
            return false;
        if (IsBindingOfType(record,renderer.GetType()))
            return true;
        return IsRendererBinding(record) || IsParticleBinding(record);
    }

    private bool SetsMaterials(NobndlPrefabScriptBindingRecord record)
    {
        if (record==null || record.fields==null)
            return false;
        for (int i=0; i<record.fields.Length; i++)
        {
            NobndlFieldRecord field=record.fields[i];
            if (field==null || string.IsNullOrEmpty(field.name))
                continue;
            if (IsMaterialFieldName(field.name))
                return true;
        }
        return false;
    }

    private static bool IsMaterialFieldName(string name)
    {
        string text=(name ?? "").Replace(" ","");
        if (text.Length==0)
            return false;
        int dot=text.LastIndexOf('.');
        if (dot>=0 && dot+1<text.Length)
            text=text.Substring(dot+1);
        int bracket=text.IndexOf('[');
        if (bracket>0)
            text=text.Substring(0,bracket);
        return string.Equals(text,"sharedMaterial",StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(text,"sharedMaterials",StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(text,"material",StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(text,"materials",StringComparison.InvariantCultureIgnoreCase) ||
               string.Equals(text,"trailMaterial",StringComparison.InvariantCultureIgnoreCase);
    }

    private bool PathsMatch(string recordPath,string runtimePath)
    {
        string a=recordPath ?? "";
        string b=runtimePath ?? "";
        if (string.Equals(a,b,StringComparison.InvariantCultureIgnoreCase))
            return true;
        return string.Equals(StripPathIndices(a),StripPathIndices(b),StringComparison.InvariantCultureIgnoreCase);
    }

    private static string StripPathIndices(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        return System.Text.RegularExpressions.Regex.Replace(path,"@\\d+(?=/|$)","");
    }
}
