using System;
using System.Collections.Generic;
using System.Reflection;

partial class Loader
{
    private readonly Dictionary<string,Type> types=new Dictionary<string,Type>();
    private Dictionary<string,Assembly> loadedAssemblies;

    private static FieldInfo FindField(Type type,string fieldName)
    {
        for (; type!=null; type=type.BaseType)
        {
            FieldInfo field=type.GetField(fieldName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if(field!=null) return field;
        }
        return null;
    }

    private static FieldInfo FindFieldForRecord(Type type,FieldRecord record)
    {
        Type t=type;
        while(t!=null && t.FullName!=record.declaringTypeName) t=t.BaseType;
        if(t==null) return null;
        return t.GetField(record.name,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
    }

    private Type ResolveType(string typeName,string assemblyName)
    {
        string key=typeName+"|"+assemblyName;
        Type type;
        if(types.TryGetValue(key,out type)) return type;
        Assembly assembly;
        if (!packAssemblies.TryGetValue(assemblyName,out assembly))
        {
            if (loadedAssemblies==null)
            {
                loadedAssemblies=new Dictionary<string,Assembly>(StringComparer.InvariantCultureIgnoreCase);
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) loadedAssemblies[a.GetName().Name]=a;
            }
            assembly=loadedAssemblies[assemblyName];
        }
        type=assembly.GetType(typeName,true);
        types[key]=type;
        return type;
    }
}
