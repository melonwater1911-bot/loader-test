using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{

    private static readonly string[,] EncyclopediaListByDefinitionType =
    {
        { "VehicleDefinition",  "vehicles"   },
        { "ShipDefinition",     "ships"      },
        { "BuildingDefinition", "buildings"  },
        { "AircraftDefinition", "aircraft"   },
        { "SceneryDefinition",  "scenery"    },
        { "UnitDefinition",     "otherUnits" }
    };
    private void PostProcessPack(LoadedNobndlPack pack)
    {
        foreach (KeyValuePair<string,UnityEngine.Object> pair in runtimeObjects.ToArray())
        {
            WeaponInfo info=pair.Value as WeaponInfo;
            if (info!=null)
            {
                info.hideInDisplay=false;
                Log("Post WeaponInfo: "+info.name+" prefab="+ObjName(info.weaponPrefab));
            }
            MissileDefinition def=pair.Value as MissileDefinition;
            if (def!=null)
            {
                def.dontAutomaticallyAddToEncyclopedia=false;
                SetFieldIfExists(def,"disabled",false);
                SetFieldIfExists(def,"isEventContent",false);
                if (def.mapIconSize<=0f)
                    def.mapIconSize=1f;
                PatchMissilePrefab(def);
                Log("Post MissileDefinition: "+def.name+" jsonKey="+def.jsonKey+" unitPrefab="+ObjName(def.unitPrefab));
            }
            WeaponMount mount=pair.Value as WeaponMount;
            if (mount!=null)
            {
                if (mount.prefab!=null)
                    mount.prefab.SetActive(false);
                if (mount.info!=null)
                    mount.info.hideInDisplay=false;
                PatchWeaponMount(mount);
                InitMount(mount);
                RegisterManagedMount(mount.name);
                Log("Post WeaponMount: "+mount.name+" mountName="+mount.mountName+" info="+ObjName(mount.info)+" prefab="+ObjName(mount.prefab));
            }
            if (def==null && mount==null)
            {
                PostProcessUnit(pair.Value);
            }
        }
    }

    private void PostProcessUnit(UnityEngine.Object obj)
    {
        if (obj==null)
            return;
        if (!IsUnitDefinition(obj))
            return;
        SetMemberIfExists(obj,"disabled",false);
        SetMemberIfExists(obj,"isEventContent",false);
        SetMemberIfExists(obj,"dontAutomaticallyAddToEncyclopedia",false);
        string jsonKey=GetStringMember(obj,"jsonKey");
        if (string.IsNullOrEmpty(jsonKey))
        {
            SetMemberIfExists(obj,"jsonKey",obj.name);
            jsonKey=obj.name;
        }
        object mapIconSize=GetMemberValue(obj,"mapIconSize");
        if (mapIconSize is float && (float)mapIconSize<=0f)
            SetMemberIfExists(obj,"mapIconSize",1f);
        GameObject unitPrefab=GetGameObjectMember(obj,"unitPrefab");
        if (unitPrefab!=null)
        {
            AddAsset(unitPrefab,"");
            AddAsset(obj,"");
            PatchUnitPrefab(obj,unitPrefab);
        }
        LogWarning("Post "+obj.GetType().Name +
                   ": "+obj.name +
                   " jsonKey="+jsonKey +
                   " unitPrefab="+ObjName(unitPrefab) +
                   " encyclopediaList="+GetListName(obj));
    }

    private void PatchUnitPrefab(UnityEngine.Object definition,GameObject unitPrefab)
    {
        if (definition==null || unitPrefab==null)
            return;
        int patchedFields=0;
        Component[] components=unitPrefab.GetComponentsInChildren<Component>(true);
        for (int i=0; i<components.Length; i++)
        {
            Component component=components[i];
            if (component==null)
                continue;
            patchedFields+=LinkDef(component,definition);
        }
        LogWarning("Patched generic unit prefab: def="+definition.name +
                   " type="+definition.GetType().Name +
                   " prefab="+unitPrefab.name +
                   " patchedDefinitionFields="+patchedFields);
    }

    private int LinkDef(object target,UnityEngine.Object definition)
    {
        if (target==null || definition==null)
            return 0;
        int count=0;
        Type targetType=target.GetType();
        Type defType=definition.GetType();
        while (targetType!=null)
        {
            FieldInfo[] fields=targetType.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly
            );
            for (int i=0; i<fields.Length; i++)
            {
                FieldInfo field=fields[i];
                if (field==null)
                    continue;
                if (!field.FieldType.IsAssignableFrom(defType))
                    continue;
                string fieldName=field.Name ?? "";
                bool isDefField =
                    string.Equals(fieldName,"definition",StringComparison.InvariantCultureIgnoreCase) ||
                    fieldName.EndsWith("Definition",StringComparison.InvariantCultureIgnoreCase);
                if (!isDefField)
                    continue;
                try
                {
                    field.SetValue(target,definition);
                    count++;
                }
                catch (Exception ex)
                {
                    LogSuppressed("LinkDef "+targetType.Name+"."+fieldName,ex);
                }
            }
            targetType=targetType.BaseType;
        }
        return count;
    }

    private bool IsUnitDefinition(UnityEngine.Object obj)
    {
        return GetListName(obj).Length>0;
    }

    private bool TypeIsCalled(Type type,string name)
    {
        if (type==null || string.IsNullOrEmpty(name))
            return false;
        while (type!=null)
        {
            if (string.Equals(type.Name,name,StringComparison.InvariantCultureIgnoreCase) ||
                string.Equals(type.FullName,name,StringComparison.InvariantCultureIgnoreCase) ||
                (!string.IsNullOrEmpty(type.FullName) && type.FullName.EndsWith("."+name,StringComparison.InvariantCultureIgnoreCase)))
            {
                return true;
            }
            type=type.BaseType;
        }
        return false;
    }

    private string GetListName(UnityEngine.Object obj)
    {
        if (obj==null)
            return "";
        Type type=obj.GetType();
        for (int i=0; i<EncyclopediaListByDefinitionType.GetLength(0); i++)
        {
            if (TypeIsCalled(type,EncyclopediaListByDefinitionType[i,0]))
                return EncyclopediaListByDefinitionType[i,1];
        }
        return "";
    }

    private GameObject GetGameObjectMember(object obj,string memberName)
    {
        return GetMemberValue(obj,memberName) as GameObject;
    }

    private string GetStringMember(object obj,string memberName)
    {
        object value=GetMemberValue(obj,memberName);
        return value as string ?? "";
    }

    private object GetMemberValue(object obj,string memberName)
    {
        if (obj==null || string.IsNullOrEmpty(memberName))
            return null;
        Type type=obj.GetType();
        FieldInfo field=FindFieldRecursive(type,memberName);
        if (field!=null)
        {
            try
            {
                return field.GetValue(obj);
            }
            catch (Exception ex)
            {
                LogSuppressed("GetMemberValue",ex);
            }
        }
        PropertyInfo property=type.GetProperty(
            memberName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic
        );
        if (property!=null && property.CanRead)
        {
            try
            {
                return property.GetValue(obj,null);
            }
            catch (Exception ex)
            {
                LogSuppressed("GetMemberValue",ex);
            }
        }
        return null;
    }

    private void SetMemberIfExists(object obj,string memberName,object value)
    {
        if (obj==null || string.IsNullOrEmpty(memberName))
            return;
        Type type=obj.GetType();
        FieldInfo field=FindFieldRecursive(type,memberName);
        if (field!=null)
        {
            try
            {
                field.SetValue(obj,value);
                return;
            }
            catch (Exception ex)
            {
                LogSuppressed("SetMemberIfExists field "+memberName,ex);
            }
        }
        PropertyInfo property=type.GetProperty(
            memberName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic
        );
        if (property!=null && property.CanWrite)
        {
            try
            {
                property.SetValue(obj,value,null);
                return;
            }
            catch (Exception ex)
            {
                LogSuppressed("SetMemberIfExists property "+memberName,ex);
                return;
            }
        }
        LogMissingMember("SetMemberIfExists",type,memberName);
    }

    private void PatchMissilePrefab(MissileDefinition def)
    {
        if (def==null || def.unitPrefab==null)
            return;
        Missile[] missiles=def.unitPrefab.GetComponentsInChildren<Missile>(true);
        for (int i=0; i<missiles.Length; i++)
        {
            Missile missile=missiles[i];
            if (missile==null)
                continue;
            SetFieldIfExists(missile,"definition",def);
            WeaponInfo info=FindInfoFor(def.unitPrefab);
            if (info!=null)
                SetFieldIfExists(missile,"info",info);
            MissileSeeker seeker=missile.GetComponent<MissileSeeker>();
            if (seeker!=null)
            {
                SetFieldIfExists(missile,"seeker",seeker);
                SetFieldIfExists(seeker,"missile",missile);
            }
            Log("Patched missile prefab: "+missile.name+" def="+def.name+" info="+ObjName(info)+" seeker="+ObjName(seeker));
        }
    }

    private void PatchWeaponMount(WeaponMount mount)
    {
        if (mount==null)
            return;
        if (mount.prefab!=null)
            mount.prefab.SetActive(false);
        if (mount.prefab!=null && mount.info!=null)
        {
            Weapon[] weapons=mount.prefab.GetComponentsInChildren<Weapon>(true);
            for (int i=0; i<weapons.Length; i++)
            {
                Weapon weapon=weapons[i];
                if (weapon==null)
                    continue;
                if (weapon.info==null)
                {
                    weapon.info=mount.info;
                    LogWarning("Repaired null Weapon.info: mount="+mount.name +
                               " weapon="+GetFullPath(weapon.transform) +
                               " info="+mount.info.name);
                }
            }
        }
        if (mount.prefab!=null)
        {
            Missile[] missiles=mount.prefab.GetComponentsInChildren<Missile>(true);
            for (int i=0; i<missiles.Length; i++)
            {
                Missile missile=missiles[i];
                if (missile==null)
                    continue;
                if (mount.info!=null)
                    SetFieldIfExists(missile,"info",mount.info);
                if (mount.info!=null && mount.info.weaponPrefab!=null)
                {
                    MissileDefinition def=FindMissileFor(mount.info.weaponPrefab);
                    if (def!=null)
                        SetFieldIfExists(missile,"definition",def);
                }
            }
        }
    }

    private WeaponInfo FindInfoFor(GameObject prefab)
    {
        if (prefab==null)
            return null;
        foreach (UnityEngine.Object obj in runtimeObjects.Values)
        {
            WeaponInfo info=obj as WeaponInfo;
            if (info!=null && info.weaponPrefab==prefab)
                return info;
        }
        return null;
    }

    private MissileDefinition FindMissileFor(GameObject prefab)
    {
        if (prefab==null)
            return null;
        foreach (UnityEngine.Object obj in runtimeObjects.Values)
        {
            MissileDefinition def=obj as MissileDefinition;
            if (def!=null && def.unitPrefab==prefab)
                return def;
        }
        return null;
    }

    private void AddToEncyclopedia(Encyclopedia encyclopedia)
    {
        if (encyclopedia==null)
            return;
        Log("AddToEncyclopedia");
        int added=0;
        foreach (UnityEngine.Object obj in runtimeObjects.Values.Distinct().ToArray())
        {
            MissileDefinition missile=obj as MissileDefinition;
            if (missile!=null)
            {
                if (AddMissile(encyclopedia,missile))
                    added++;
                continue;
            }
            WeaponMount mount=obj as WeaponMount;
            if (mount!=null)
            {
                if (AddMount(encyclopedia,mount))
                    added++;
                continue;
            }
            if (AddUnit(encyclopedia,obj))
                added++;
        }
        if (added>0)
            RebuildIndex(encyclopedia,added);
    }

    private void RebuildIndex(Encyclopedia encyclopedia,int addedThisPass)
    {
        if (encyclopedia==null)
            return;
        MethodInfo afterLoad=typeof(Encyclopedia).GetMethod(
            "AfterLoad",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null
        );
        if (afterLoad==null)
        {
            LogSuppressed("Encyclopedia.AfterLoad",new MissingMethodException(
                "Encyclopedia.AfterLoad() not found, custom units will not resolve by key"));
            return;
        }
        try
        {
            afterLoad.Invoke(encyclopedia,null);
            Log("Encyclopedia lookups rebuilt via AfterLoad: added="+addedThisPass);
        }
        catch (Exception ex)
        {
            LogError("Encyclopedia.AfterLoad() failed: "+ex.Message);
            return;
        }
    }

    private bool AddUnit(Encyclopedia encyclopedia,UnityEngine.Object definition)
    {
        if (encyclopedia==null || definition==null)
            return false;
        if (!IsUnitDefinition(definition))
            return false;
        string listName=GetListName(definition);
        if (listName.Length==0)
            return false;
        IList list=GetListField(encyclopedia,listName);
        if (list==null)
        {
            LogError("Encyclopedia."+listName+" not found - the game may have renamed it. " +
                     "Definition not registered: "+definition.GetType().Name+" "+definition.name);
            return false;
        }
        if (ListHasDef(list,definition))
        {
            Log("Encyclopedia already contains "+definition.GetType().Name+": "+definition.name+" list="+listName);
            return false;
        }
        try
        {
            list.Add(definition);
            LogWarning("Added "+definition.GetType().Name+" to Encyclopedia."+listName+": "+definition.name);
            return true;
        }
        catch (Exception ex)
        {
            LogError("Could not add "+definition.GetType().Name+" to Encyclopedia."+listName+": "+ex.Message);
            return false;
        }
    }

    private bool ListHasDef(IList list,UnityEngine.Object definition)
    {
        if (list==null || definition==null)
            return false;
        string defName=definition.name;
        string defJsonKey=GetStringMember(definition,"jsonKey");
        for (int i=0; i<list.Count; i++)
        {
            UnityEngine.Object existing=list[i] as UnityEngine.Object;
            if (existing==null)
                continue;
            if (ReferenceEquals(existing,definition))
                return true;
            if (!string.IsNullOrEmpty(defName) && NameMatches(existing.name,defName))
                return true;
            string existingJsonKey=GetStringMember(existing,"jsonKey");
            if (!string.IsNullOrEmpty(defJsonKey) &&
                !string.IsNullOrEmpty(existingJsonKey) &&
                NameMatches(existingJsonKey,defJsonKey))
            {
                return true;
            }
        }
        return false;
    }

    private bool AddMissile(Encyclopedia encyclopedia,MissileDefinition missile)
    {
        IList list=GetListField(encyclopedia,"missiles");
        if (list==null)
        {
            LogError("Encyclopedia.missiles not found");
            return false;
        }
        for (int i=0; i<list.Count; i++)
        {
            MissileDefinition existing=list[i] as MissileDefinition;
            if (existing!=null && NameMatches(existing.name,missile.name))
                return false;
        }
        list.Add(missile);
        Log("Added MissileDefinition to Encyclopedia: "+missile.name);
        return true;
    }

    private bool AddMount(Encyclopedia encyclopedia,WeaponMount mount)
    {
        IList list=GetListField(encyclopedia,"weaponMounts");
        if (list==null)
        {
            LogError("Encyclopedia.weaponMounts not found");
            return false;
        }
        for (int i=0; i<list.Count; i++)
        {
            WeaponMount existing=list[i] as WeaponMount;
            if (existing!=null && NameMatches(existing.name,mount.name))
                return false;
        }
        list.Add(mount);
        Log("Added WeaponMount to Encyclopedia: "+mount.name);
        return true;
    }

    private IList GetListField(object obj,string fieldName)
    {
        if (obj==null)
            return null;
        FieldInfo field=FindFieldRecursive(obj.GetType(),fieldName);
        if (field==null)
            return null;
        return field.GetValue(obj) as IList;
    }


    private void UpdateEncyclopedia(string reason)
    {
        if (!InstallStarted)
            return;
        Encyclopedia[] all;
        try
        {
            all=Resources.FindObjectsOfTypeAll<Encyclopedia>();
        }
        catch (Exception ex)
        {
            LogWarning("Encyclopedia lookup failed: "+reason+" | "+ex.Message);
            return;
        }
        int count=0;
        for (int i=0; i<all.Length; i++)
        {
            Encyclopedia encyclopedia=all[i];
            if (encyclopedia==null)
                continue;
            count++;
            encyclopediaFound=true;
            AddToEncyclopedia(encyclopedia);
        }
        if (count!=encyclopediaCount)
        {
            encyclopediaCount=count;
            LogWarning("Encyclopedia instance count changed: reason="+reason+" count="+count);
        }
    }
}
