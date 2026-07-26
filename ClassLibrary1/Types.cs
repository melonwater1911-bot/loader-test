using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static NobndlLog;
using static NobndlUtil;

partial class NobndlLoaderPlugin
{
    private FieldInfo FindFieldForRecord(Type type,NobndlFieldRecord record)
    {
        if (type==null || record==null)
            return null;
        Type t=type;
        while (t!=null)
        {
            if (!string.IsNullOrEmpty(record.declaringTypeName) && t.FullName!=record.declaringTypeName)
            {
                t=t.BaseType;
                continue;
            }
            FieldInfo field=t.GetField(record.name,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field!=null)
                return field;
            t=t.BaseType;
        }
        return FindFieldRecursive(type,record.name);
    }


    private void SetFieldIfExists(object obj,string fieldName,object value)
    {
        if (obj==null)
            return;
        Type type=obj.GetType();
        FieldInfo field=FindFieldRecursive(type,fieldName);
        if (field==null)
        {
            LogMissingMember("SetFieldIfExists",type,fieldName);
            return;
        }
        try
        {
            field.SetValue(obj,value);
        }
        catch (Exception ex)
        {
            LogSuppressed("SetFieldIfExists "+fieldName,ex);
        }
    }

    private Type FindScriptType(string typeName,string assemblyName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;
        if (!string.IsNullOrEmpty(assemblyName))
        {
            Assembly assembly;
            if (nobndlScriptAssemblies.TryGetValue(NormalizeName(assemblyName),out assembly))
            {
                Type type=TypeFromAssembly(assembly,typeName);
                if (type!=null)
                    return type;
            }
            if (assemblyName.StartsWith("nobndl:",StringComparison.InvariantCultureIgnoreCase))
            {
                string packId=assemblyName.Substring("nobndl:".Length);
                if (nobndlScriptAssemblies.TryGetValue(NormalizeName(packId),out assembly))
                {
                    Type type=TypeFromAssembly(assembly,typeName);
                    if (type!=null)
                        return type;
                }
            }
        }
        foreach (KeyValuePair<string,Assembly> pair in nobndlScriptAssemblies)
        {
            Type type=TypeFromAssembly(pair.Value,typeName);
            if (type!=null)
                return type;
        }
        return null;
    }

    private Type TypeFromAssembly(Assembly assembly,string typeName)
    {
        if (assembly==null || string.IsNullOrEmpty(typeName))
            return null;
        try
        {
            Type exact=assembly.GetType(typeName,false);
            if (exact!=null)
                return exact;
        }
        catch (Exception ex)
        {
            LogSuppressed("TypeFromAssembly",ex);
        }
        string shortName=typeName.Contains(".")
            ? typeName.Substring(typeName.LastIndexOf('.')+1)
            : typeName;
        Type[] types;
        try
        {
            types=assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types=ex.Types.Where(x => x!=null).ToArray();
        }
        catch (Exception ex)
        {
            LogSuppressed("GetTypes "+assembly.GetName().Name,ex);
            return null;
        }
        for (int i=0; i<types.Length; i++)
        {
            Type type=types[i];
            if (type==null)
                continue;
            if (string.Equals(type.FullName,typeName,StringComparison.InvariantCultureIgnoreCase))
                return type;
            if (string.Equals(type.Name,shortName,StringComparison.InvariantCultureIgnoreCase))
                return type;
        }
        return null;
    }

    private Type ResolveType(string typeName,string assemblyName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;
        Type nobndlType=FindScriptType(typeName,assemblyName);
        if (nobndlType!=null)
            return nobndlType;
        if (!string.IsNullOrEmpty(assemblyName) && !assemblyName.StartsWith("nobndl:",StringComparison.InvariantCultureIgnoreCase))
        {
            Type direct=Type.GetType(typeName+", "+assemblyName,false);
            if (direct!=null)
                return direct;
        }
        Assembly[] assemblies=AppDomain.CurrentDomain.GetAssemblies();
        for (int i=0; i<assemblies.Length; i++)
        {
            Assembly asm=assemblies[i];
            if (!string.IsNullOrEmpty(assemblyName) &&
                !assemblyName.StartsWith("nobndl:",StringComparison.InvariantCultureIgnoreCase) &&
                !string.Equals(asm.GetName().Name,assemblyName,StringComparison.InvariantCultureIgnoreCase))
            {
                continue;
            }
            Type type=null;
            try
            {
                type=asm.GetType(typeName,false);
            }
            catch (Exception ex)
            {
                LogSuppressed("ResolveType",ex);
            }
            if (type!=null)
                return type;
        }
        string shortName=typeName.Contains(".") ? typeName.Substring(typeName.LastIndexOf('.')+1) : typeName;
        for (int i=0; i<assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types=assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types=ex.Types.Where(x => x!=null).ToArray();
            }
            for (int t=0; t<types.Length; t++)
            {
                if (string.Equals(types[t].Name,shortName,StringComparison.InvariantCultureIgnoreCase))
                    return types[t];
            }
        }
        return null;
    }
}
