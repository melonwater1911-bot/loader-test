using BepInEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private void FixParticles(string reason)
    {
        if (CountMissiles()==0)
        {
            Log("Particle FX fallback skipped: pack has no custom MissileDefinition records | reason="+reason);
            return;
        }
        MissileDefinition donor=FindDonor();
        if (donor==null || donor.unitPrefab==null)
        {
            LogError("Particle FX fallback failed: no vanilla donor missile with a usable particle material | reason="+reason);
            return;
        }
        int customMissiles=0;
        int prefabsWithExplicitBindings=0;
        int prefabsPatched=0;
        int matsCopied=0;
        int trailCopied=0;
        for (int i=0; i<packs.Count; i++)
        {
            LoadedNobndlPack pack=packs[i];
            if (pack==null || pack.Manifest==null || pack.Manifest.scriptableObjects==null)
                continue;
            for (int s=0; s<pack.Manifest.scriptableObjects.Length; s++)
            {
                NobndlScriptableObjectRecord so=pack.Manifest.scriptableObjects[s];
                if (so==null)
                    continue;
                if (!NameMatches(so.runtimeTypeName,"MissileDefinition"))
                    continue;
                MissileDefinition def=ResolveRuntimeObject(so.objectName) as MissileDefinition ??
                                        ResolveRuntimeObject(so.assetName) as MissileDefinition;
                if (def==null || def.unitPrefab==null)
                    continue;
                customMissiles++;
                if (HasParticleBindings(pack,def.unitPrefab.name))
                {
                    prefabsWithExplicitBindings++;
                    Log("Particle FX fallback not needed: prefab has explicit ParticleSystemRenderer bindings | prefab="+def.unitPrefab.name+" reason="+reason);
                    continue;
                }
                int copiedBefore=matsCopied+trailCopied;
                CopyFromDonor(
                    donor.unitPrefab,
                    def.unitPrefab,
                    ref matsCopied,
                    ref trailCopied,
                    reason
                );
                if (matsCopied+trailCopied>copiedBefore)
                    prefabsPatched++;
            }
        }
        LogWarning("Particle FX fallback: reason="+reason +
                   " donor="+donor.name +
                   " customMissiles="+customMissiles +
                   " alreadyBound="+prefabsWithExplicitBindings +
                   " patched="+prefabsPatched +
                   " renderersCopied="+matsCopied +
                   " trailEmittersCopied="+trailCopied);
    }

    private int CountMissiles()
    {
        int count=0;
        for (int i=0; i<packs.Count; i++)
        {
            LoadedNobndlPack pack=packs[i];
            if (pack==null || pack.Manifest==null || pack.Manifest.scriptableObjects==null)
                continue;
            for (int s=0; s<pack.Manifest.scriptableObjects.Length; s++)
            {
                NobndlScriptableObjectRecord so=pack.Manifest.scriptableObjects[s];
                if (so==null)
                    continue;
                if (NameMatches(so.runtimeTypeName,"MissileDefinition"))
                    count++;
            }
        }
        return count;
    }

    private bool HasParticleBindings(LoadedNobndlPack pack,string prefabName)
    {
        if (pack==null || pack.Manifest==null || pack.Manifest.prefabScriptBindings==null || string.IsNullOrEmpty(prefabName))
            return false;
        NobndlPrefabScriptBindingRecord[] records=pack.Manifest.prefabScriptBindings;
        for (int i=0; i<records.Length; i++)
        {
            NobndlPrefabScriptBindingRecord record=records[i];
            if (record==null)
                continue;
            if (!IsParticleBinding(record))
                continue;
            if (string.Equals(record.prefabName,prefabName,StringComparison.InvariantCultureIgnoreCase))
                return true;
        }
        return false;
    }

    private MissileDefinition FindDonor()
    {
        MissileDefinition[] defs;
        try
        {
            defs=Resources.FindObjectsOfTypeAll<MissileDefinition>();
        }
        catch
        {
            defs=new MissileDefinition[0];
        }
        MissileDefinition best=null;
        int bestCount=0;
        for (int i=0; i<defs.Length; i++)
        {
            MissileDefinition def=defs[i];
            if (def==null || def.unitPrefab==null)
                continue;
            if (IsRuntimeObject(def) || IsBundlePrefab(def.unitPrefab))
                continue;
            int usable=CountGood(def.unitPrefab);
            if (usable==0)
                continue;
            if (best==null || usable>bestCount)
            {
                best=def;
                bestCount=usable;
                continue;
            }
            if (usable==bestCount &&
                string.Compare(def.name,best.name,StringComparison.InvariantCultureIgnoreCase)<0)
            {
                best=def;
            }
        }
        if (best==null)
            return null;
        Log("Particle FX donor selected: "+best.name+" usableParticleRenderers="+bestCount);
        return best;
    }

    private bool IsRuntimeObject(UnityEngine.Object obj)
    {
        if (obj==null)
            return false;
        if (runtimeObjectIds==null || runtimeObjectIds.Count!=runtimeObjects.Count)
        {
            runtimeObjectIds=new HashSet<int>();
            AddInstanceIds(runtimeObjectIds,runtimeObjects.Values);
        }
        return runtimeObjectIds.Contains(obj.GetInstanceID());
    }

    private bool IsBundlePrefab(GameObject g)
    {
        if (g==null)
            return false;
        return cache.ContainsPrefab(g);
    }

    private int CountGood(GameObject root)
    {
        if (root==null)
            return 0;
        ParticleSystemRenderer[] renderers=root.GetComponentsInChildren<ParticleSystemRenderer>(true);
        int count=0;
        for (int i=0; i<renderers.Length; i++)
        {
            if (HasTexture(renderers[i]))
                count++;
        }
        return count;
    }

    private void CopyFromDonor(
        GameObject donorRoot,
        GameObject targetRoot,
        ref int matsCopied,
        ref int trailCopied,
        string reason)
    {
        if (donorRoot==null || targetRoot==null)
            return;
        ParticleSystemRenderer[] targets=targetRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
        for (int i=0; i<targets.Length; i++)
        {
            ParticleSystemRenderer targetRenderer=targets[i];
            if (targetRenderer==null)
                continue;
            if (!IsEffect(targetRenderer))
                continue;
            ParticleSystemRenderer donorRenderer=FindDonorRend(donorRoot,targetRoot,targetRenderer);
            if (donorRenderer==null)
                continue;
            bool copiedRenderer=CopyMats(donorRenderer,targetRenderer,reason);
            bool copiedSheet=CopyTextureSheet(donorRenderer,targetRenderer,reason);
            if (copiedRenderer || copiedSheet)
                matsCopied++;
        }
        Component[] targetComps=targetRoot.GetComponentsInChildren<Component>(true);
        for (int i=0; i<targetComps.Length; i++)
        {
            Component targetComponent=targetComps[i];
            if (targetComponent==null)
                continue;
            if (!NameMatches(targetComponent.GetType().Name,"TrailEmitter"))
                continue;
            Component donorComponent=FindDonorComponent(donorRoot,targetRoot,targetComponent);
            if (donorComponent==null)
                continue;
            if (CopyMaterialFields(donorComponent,targetComponent,reason))
                trailCopied++;
        }
        LogWarning("Particle FX fallback applied: target="+targetRoot.name +
                   " donor="+donorRoot.name +
                   " renderersCopied="+matsCopied +
                   " trailEmittersCopied="+trailCopied +
                   " reason="+reason);
    }

    private bool CopyTextureSheet(ParticleSystemRenderer donorRenderer,ParticleSystemRenderer targetRenderer,string reason)
    {
        if (donorRenderer==null || targetRenderer==null)
            return false;
        ParticleSystem donorPs=donorRenderer.GetComponent<ParticleSystem>();
        ParticleSystem targetPs=targetRenderer.GetComponent<ParticleSystem>();
        if (donorPs==null || targetPs==null)
            return false;
        bool copiedAny=false;
        try
        {
            ParticleSystem.TextureSheetAnimationModule donorSheet=donorPs.textureSheetAnimation;
            ParticleSystem.TextureSheetAnimationModule targetSheet=targetPs.textureSheetAnimation;
            string[] propertyNames =
            {
                "enabled",
                "mode",
                "timeMode",
                "fps",
                "numTilesX",
                "numTilesY",
                "animation",
                "frameOverTime",
                "startFrame",
                "cycleCount",
                "rowIndex",
                "rowMode",
                "uvChannelMask"
            };
            for (int i=0; i<propertyNames.Length; i++)
            {
                if (CopyPropertyValue(donorSheet,targetSheet,propertyNames[i]))
                    copiedAny=true;
            }
            if (CopySprites(donorSheet,targetSheet))
                copiedAny=true;
            if (copiedAny)
            {
                LogWarning("Copied donor particle texture-sheet settings: target="+GetFullPath(targetRenderer.transform) +
                           " donor="+GetFullPath(donorRenderer.transform) +
                           " reason="+reason +
                           " intensityPreserved=true");
            }
        }
        catch (Exception ex)
        {
            LogWarning("Failed to copy donor particle texture-sheet settings: target="+GetFullPath(targetRenderer.transform) +
                       " donor="+GetFullPath(donorRenderer.transform) +
                       " error="+ex.Message);
        }
        return copiedAny;
    }

    private bool CopySprites(object donorSheet,object targetSheet)
    {
        if (donorSheet==null || targetSheet==null)
            return false;
        bool copied=false;
        try
        {
            Type donorType=donorSheet.GetType();
            Type targetType=targetSheet.GetType();
            MethodInfo donorGetSprite=donorType.GetMethod("GetSprite",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo targetRemoveSprite=targetType.GetMethod("RemoveSprite",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo targetAddSprite=targetType.GetMethod("AddSprite",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo donorSpriteCount=donorType.GetProperty("spriteCount",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo targetSpriteCount=targetType.GetProperty("spriteCount",BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (donorGetSprite==null || targetRemoveSprite==null || targetAddSprite==null ||
                donorSpriteCount==null || targetSpriteCount==null)
                return false;
            int targetCount=Convert.ToInt32(targetSpriteCount.GetValue(targetSheet,null),CultureInfo.InvariantCulture);
            for (int i=targetCount-1; i>=0; i--)
            {
                targetRemoveSprite.Invoke(targetSheet,new object[] { i });
            }
            int donorCount=Convert.ToInt32(donorSpriteCount.GetValue(donorSheet,null),CultureInfo.InvariantCulture);
            for (int i=0; i<donorCount; i++)
            {
                object sprite=donorGetSprite.Invoke(donorSheet,new object[] { i });
                if (sprite==null)
                    continue;
                targetAddSprite.Invoke(targetSheet,new object[] { sprite });
                copied=true;
            }
        }
        catch (Exception ex)
        {
            LogSuppressed("CopySprites",ex);
        }
        return copied;
    }

    private bool CopyPropertyValue(object source,object target,string propertyName)
    {
        if (source==null || target==null || string.IsNullOrEmpty(propertyName))
            return false;
        try
        {
            PropertyInfo sourceProperty=source.GetType().GetProperty(propertyName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo targetProperty=target.GetType().GetProperty(propertyName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (sourceProperty==null || targetProperty==null)
                return false;
            if (!sourceProperty.CanRead || !targetProperty.CanWrite)
                return false;
            object value=sourceProperty.GetValue(source,null);
            targetProperty.SetValue(target,value,null);
            return true;
        }
        catch (Exception ex)
        {
            LogSuppressed("CopyPropertyValue "+propertyName,ex);
            return false;
        }
    }

    private bool IsEffect(Renderer renderer)
    {
        if (renderer==null)
            return false;
        string full=GetFullPath(renderer.transform);
        if (full.IndexOf("fire",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        if (full.IndexOf("smoke",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        if (full.IndexOf("trail",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        if (full.IndexOf("flame",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        if (full.IndexOf("particle",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        return false;
    }

    private ParticleSystemRenderer FindDonorRend(GameObject donorRoot,GameObject targetRoot,ParticleSystemRenderer targetRenderer)
    {
        if (donorRoot==null || targetRoot==null || targetRenderer==null)
            return null;
        string targetPath=GetRelPath(targetRoot.transform,targetRenderer.transform);
        Transform samePath=FindChildByPath(donorRoot.transform,targetPath);
        if (samePath==null)
            return null;
        ParticleSystemRenderer renderer=samePath.GetComponent<ParticleSystemRenderer>();
        return HasTexture(renderer) ? renderer : null;
    }

    private Component FindDonorComponent(GameObject donorRoot,GameObject targetRoot,Component targetComponent)
    {
        if (donorRoot==null || targetRoot==null || targetComponent==null)
            return null;
        string targetPath=GetRelPath(targetRoot.transform,targetComponent.transform);
        Transform samePath=FindChildByPath(donorRoot.transform,targetPath);
        if (samePath==null)
            return null;
        return samePath.GetComponent(targetComponent.GetType());
    }

    private bool HasTexture(ParticleSystemRenderer renderer)
    {
        if (renderer==null)
            return false;
        if (renderer.sharedMaterial!=null && renderer.sharedMaterial.mainTexture!=null)
            return true;
        Material[] mats=renderer.sharedMaterials;
        if (mats==null)
            return false;
        for (int i=0; i<mats.Length; i++)
        {
            if (mats[i]!=null && mats[i].mainTexture!=null)
                return true;
        }
        return false;
    }



    private bool CopyMats(ParticleSystemRenderer donor,ParticleSystemRenderer target,string reason)
    {
        if (donor==null || target==null)
            return false;
        Material[] donorMaterials=donor.sharedMaterials;
        if (donorMaterials==null || donorMaterials.Length==0)
        {
            if (donor.sharedMaterial!=null)
                donorMaterials=new Material[] { donor.sharedMaterial };
        }
        if (donorMaterials==null || donorMaterials.Length==0)
            return false;
        bool hasUsable=false;
        for (int i=0; i<donorMaterials.Length; i++)
        {
            if (donorMaterials[i]!=null)
            {
                hasUsable=true;
                break;
            }
        }
        if (!hasUsable)
            return false;
        try
        {
            target.sharedMaterials=donorMaterials;
            try
            {
                target.trailMaterial=donor.trailMaterial;
            }
            catch (Exception ex)
            {
                LogSuppressed("CopyMats",ex);
            }
            target.renderMode=donor.renderMode;
            target.alignment=donor.alignment;
            target.sortMode=donor.sortMode;
            target.normalDirection=donor.normalDirection;
            target.sortingLayerID=donor.sortingLayerID;
            target.sortingOrder=donor.sortingOrder;
            LogWarning("Copied vanilla particle renderer materials: target="+GetFullPath(target.transform) +
                       " donor="+GetFullPath(donor.transform) +
                       " mats="+donorMaterials.Length +
                       " reason="+reason +
                       " preservedParticleIntensity=true");
            return true;
        }
        catch (Exception ex)
        {
            LogWarning("Failed to copy particle renderer materials: target="+GetFullPath(target.transform) +
                       " donor="+GetFullPath(donor.transform) +
                       " error="+ex.Message);
            return false;
        }
    }

    private bool CopyMaterialFields(Component donor,Component target,string reason)
    {
        if (donor==null || target==null)
            return false;
        bool changed=false;
        FieldInfo[] donorFields=donor.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i=0; i<donorFields.Length; i++)
        {
            FieldInfo donorField=donorFields[i];
            if (donorField==null)
                continue;
            FieldInfo targetField=FindFieldRecursive(target.GetType(),donorField.Name);
            if (targetField==null)
                continue;
            if (typeof(Material).IsAssignableFrom(donorField.FieldType) && targetField.FieldType.IsAssignableFrom(donorField.FieldType))
            {
                try
                {
                    Material value=donorField.GetValue(donor) as Material;
                    if (value==null)
                        continue;
                    targetField.SetValue(target,value);
                    changed=true;
                }
                catch (Exception ex)
                {
                    LogSuppressed("CopyMaterialFields",ex);
                }
            }
            else if (donorField.FieldType.IsArray && donorField.FieldType.GetElementType()!=null && typeof(Material).IsAssignableFrom(donorField.FieldType.GetElementType()))
            {
                try
                {
                    object value=donorField.GetValue(donor);
                    if (value==null)
                        continue;
                    targetField.SetValue(target,value);
                    changed=true;
                }
                catch (Exception ex)
                {
                    LogSuppressed("CopyMaterialFields",ex);
                }
            }
        }
        if (changed)
        {
            LogWarning("Copied vanilla TrailEmitter material fields: target="+GetFullPath(target.transform) +
                       " donor="+GetFullPath(donor.transform) +
                       " reason="+reason +
                       " preservedIntensityFields=true");
        }
        return changed;
    }



    private List<GameObject> GetPrefabRoots()
    {
        List<GameObject> roots=new List<GameObject>();
        HashSet<int> seen=new HashSet<int>();
        foreach (GameObject g in cache.PrefabRoots)
        {
            if (IsPackPrefab(g))
                AddPrefabRoot(roots,seen,g);
        }
        if (roots.Count>0)
            return roots;
        foreach (GameObject g in cache.PrefabRoots)
            AddPrefabRoot(roots,seen,g);
        return roots;
    }

    private bool IsPackPrefab(GameObject g)
    {
        if (g==null)
            return false;
        string gName=g.name ?? "";
        for (int p=0; p<packs.Count; p++)
        {
            LoadedNobndlPack pack=packs[p];
            if (pack==null || pack.Manifest==null)
                continue;
            NobndlPrefabScriptBindingRecord[] bindings=pack.Manifest.prefabScriptBindings;
            if (bindings!=null)
            {
                for (int i=0; i<bindings.Length; i++)
                {
                    NobndlPrefabScriptBindingRecord record=bindings[i];
                    if (record!=null && NameMatches(record.prefabName,gName))
                        return true;
                }
            }
            NobndlScriptableObjectRecord[] scriptableObjects=pack.Manifest.scriptableObjects;
            if (scriptableObjects!=null)
            {
                for (int s=0; s<scriptableObjects.Length; s++)
                {
                    if (ObjectUsesPrefab(scriptableObjects[s],gName))
                        return true;
                }
            }
        }
        return false;
    }

    private bool ObjectUsesPrefab(NobndlScriptableObjectRecord record,string prefabName)
    {
        if (record==null || record.fields==null || string.IsNullOrEmpty(prefabName))
            return false;
        for (int i=0; i<record.fields.Length; i++)
        {
            NobndlFieldRecord field=record.fields[i];
            if (field==null)
                continue;
            if (NameMatches(field.objectName,prefabName) ||
                NameMatches(field.assetName,prefabName) ||
                NameMatches(field.value,prefabName))
                return true;
        }
        return false;
    }

    private void AddPrefabRoot(List<GameObject> roots,HashSet<int> seen,GameObject g)
    {
        if (roots==null || seen==null || g==null)
            return;
        int id=g.GetInstanceID();
        if (!seen.Add(id))
            return;
        roots.Add(g);
    }
}
