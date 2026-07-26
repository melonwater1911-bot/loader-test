using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine.Rendering;
using UnityEngine;
using static NobndlLog;
using static NobndlUtil;

sealed class NobndlMaterialFactory
{
    private readonly Dictionary<int,Material> clones=new Dictionary<int,Material>();
    private Dictionary<int,Material> bySource;
    private Dictionary<string,Material> byName;
    private int indexVersion=-1;
    private HashSet<int> cloneIds;
    private int cloneIdsVersion=-1;

    public int Version { get; private set; }

    private static void AddIds(HashSet<int> target,IEnumerable<Material> materials)
    {
        foreach (Material m in materials)
        {
            if (m!=null)
                target.Add(m.GetInstanceID());
        }
    }

    private void BuildCloneIndex(NobndlBundleCache cache)
    {
        if (bySource!=null && indexVersion==cache.Materials.Version)
            return;
        bySource=new Dictionary<int,Material>();
        byName=new Dictionary<string,Material>(StringComparer.InvariantCultureIgnoreCase);
        foreach (KeyValuePair<string,Material> pair in cache.Materials.Items)
        {
            Material source=pair.Value;
            if (source==null)
                continue;
            Material clone=CloneMaterial(source);
            if (clone==null)
                continue;
            bySource[source.GetInstanceID()]=clone;
            string key=NormalizeName(source.name);
            if (key.Length>0 && !byName.ContainsKey(key))
                byName[key]=clone;
        }
        indexVersion=cache.Materials.Version;
    }
    public List<Material> RuntimeMaterials(NobndlBundleCache cache)
    {
        BuildCloneIndex(cache);
        List<Material> result=new List<Material>();
        HashSet<int> seen=new HashSet<int>();
        foreach (KeyValuePair<string,Material> pair in cache.Materials.Items)
        {
            Material source=pair.Value;
            if (source==null)
                continue;
            Material runtime;
            if (!bySource.TryGetValue(source.GetInstanceID(),out runtime) || runtime==null)
                runtime=source;
            if (seen.Add(runtime.GetInstanceID()))
                result.Add(runtime);
        }
        return result;
    }
    public Material RuntimeClone(NobndlBundleCache cache,Material mat)
    {
        if (mat==null)
            return null;
        BuildCloneIndex(cache);
        Material clone;
        if (bySource.TryGetValue(mat.GetInstanceID(),out clone))
            return clone;
        if (byName.TryGetValue(NormalizeName(mat.name),out clone))
            return clone;
        return null;
    }
    public Material CloneMaterial(Material source)
    {
        if (source==null)
            return null;
        int sourceId=source.GetInstanceID();
        Material existing;
        if (clones.TryGetValue(sourceId,out existing) && existing!=null)
            return existing;
        Shader runtimeShader=FindShader(source);
        if (runtimeShader==null)
        {
            LogWarning("No runtime shader found, using source material as is: "+source.name +
                       " shader="+ShaderName(source.shader));
            return source;
        }
        Material clone=new Material(runtimeShader);
        clone.enableInstancing=source.enableInstancing;
        clone.renderQueue=source.renderQueue;
        clone.doubleSidedGI=source.doubleSidedGI;
        clone.globalIlluminationFlags=source.globalIlluminationFlags;
        CopyProps(source,clone);
        MakeTransparent(source,clone);
        clone.name=source.name+"_Runtime";
        clones[sourceId]=clone;
        Version++;
        LogWarning("Cloned material: "+source.name+" ("+ShaderName(source.shader)+") onto game shader "+ShaderName(runtimeShader));
        return clone;
    }
    //bundle shader messes up
    private Shader FindShader(Material source)
    {
        if (source==null)
            return null;
        string name=source.shader!=null ? source.shader.name : "";
        string[] tries =
        {
            NameMatches(name,"Standard") ? "" : name,
            ContainsIgnoreCase(name,"Particle") ? "Universal Render Pipeline/Particles/Unlit" : "",
            ContainsIgnoreCase(name,"Unlit") ? "Universal Render Pipeline/Unlit" : "",
            ContainsIgnoreCase(name,"Simple Lit") ? "Universal Render Pipeline/Simple Lit" : "",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Standard"
        };
        for (int i=0; i<tries.Length; i++)
        {
            if (string.IsNullOrEmpty(tries[i]))
                continue;
            Shader shader=Shader.Find(tries[i]);
            if (shader!=null)
                return shader;
        }
        return null;
    }
    private void CopyProps(Material source,Material target)
    {
        if (source==null || target==null)
            return;
        try
        {
            target.CopyPropertiesFromMaterial(source);
        }
        catch (Exception ex)
        {
            LogWarning("CopyPropertiesFromMaterial failed: source="+source.name +
                       " target="+target.name +
                       " error="+ex.Message);
        }
        CopyByShader(source,target);
        CopyRenamed(source,target);
        EnableKeyword(target,"_NORMALMAP","_BumpMap");
        EnableKeyword(target,"_METALLICSPECGLOSSMAP","_MetallicGlossMap");
        EnableKeyword(target,"_OCCLUSIONMAP","_OcclusionMap");
        EnableKeyword(target,"_EMISSION","_EmissionMap");
    }
    private void CopyByShader(Material source,Material target)
    {
        Shader shader=source.shader;
        if (shader==null)
            return;
        int count;
        try
        {
            count=shader.GetPropertyCount();
        }
        catch (Exception ex)
        {
            LogSuppressed("CopyByShader properties",ex);
            return;
        }
        for (int i=0; i<count; i++)
        {
            string name=shader.GetPropertyName(i);
            if (string.IsNullOrEmpty(name) || !target.HasProperty(name))
                continue;
            try
            {
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        Texture tex=source.GetTexture(name);
                        if (tex==null)
                            break;
                        target.SetTexture(name,tex);
                        target.SetTextureScale(name,source.GetTextureScale(name));
                        target.SetTextureOffset(name,source.GetTextureOffset(name));
                        break;
                    case ShaderPropertyType.Color:
                        target.SetColor(name,source.GetColor(name));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        target.SetFloat(name,source.GetFloat(name));
                        break;
                    case ShaderPropertyType.Vector:
                        target.SetVector(name,source.GetVector(name));
                        break;
                }
            }
            catch (Exception ex)
            {
                LogSuppressed("CopyByShader "+name,ex);
            }
        }
    }
    private void CopyRenamed(Material source,Material target)
    {
        CopyTexture(source,target,"_MainTex","_BaseMap");
        CopyTexture(source,target,"_BaseMap","_MainTex");
        CopyTexture(source,target,"_NormalMap","_BumpMap");
        CopyColor(source,target,"_Color","_BaseColor");
        CopyColor(source,target,"_BaseColor","_Color");
        CopyFloat(source,target,"_Glossiness","_Smoothness");
        CopyFloat(source,target,"_Smoothness","_Glossiness");
    }
    private void CopyTexture(Material source,Material target,string from,string to)
    {
        if (source==null || target==null)
            return;
        if (!source.HasProperty(from) || !target.HasProperty(to))
            return;
        try
        {
            Texture texture=source.GetTexture(from);
            if (texture!=null && target.GetTexture(to)==null)
            {
                target.SetTexture(to,texture);
                target.SetTextureScale(to,source.GetTextureScale(from));
                target.SetTextureOffset(to,source.GetTextureOffset(from));
            }
        }
        catch (Exception ex)
        {
            LogSuppressed("CopyTexture",ex);
        }
    }
    private void CopyColor(Material source,Material target,string from,string to)
    {
        if (source==null || target==null)
            return;
        if (!source.HasProperty(from) || !target.HasProperty(to))
            return;
        try
        {
            target.SetColor(to,source.GetColor(from));
        }
        catch (Exception ex)
        {
            LogSuppressed("CopyColor",ex);
        }
    }
    private void CopyFloat(Material source,Material target,string from,string to)
    {
        if (source==null || target==null)
            return;
        if (!source.HasProperty(from) || !target.HasProperty(to))
            return;
        try
        {
            target.SetFloat(to,source.GetFloat(from));
        }
        catch (Exception ex)
        {
            LogSuppressed("CopyFloat",ex);
        }
    }
    private void EnableKeyword(Material target,string keyword,string property)
    {
        if (target==null || string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(property))
            return;
        if (!target.HasProperty(property))
            return;
        try
        {
            if (target.GetTexture(property)!=null)
                target.EnableKeyword(keyword);
        }
        catch (Exception ex)
        {
            LogSuppressed("EnableKeyword",ex);
        }
    }
    private bool IsTransparent(Material material)
    {
        if (material==null)
            return false;
        if (material.renderQueue>(int)RenderQueue.GeometryLast)
            return true;
        if (HasKeyword(material,"_ALPHABLEND_ON") ||
            HasKeyword(material,"_ALPHAPREMULTIPLY_ON") ||
            HasKeyword(material,"_SURFACE_TYPE_TRANSPARENT"))
            return true;
        if (material.HasProperty("_Mode") && material.GetFloat("_Mode")>=2f)
            return true;
        if (material.HasProperty("_Surface") && material.GetFloat("_Surface")>=0.5f)
            return true;
        return false;
    }
    private void MakeTransparent(Material source,Material target)
    {
        if (source==null || target==null || !IsTransparent(source))
            return;
        try
        {
            SetFloatIfExists(target,"_Surface",1f);
            SetFloatIfExists(target,"_Blend",0f);
            SetFloatIfExists(target,"_SrcBlend",(float)BlendMode.SrcAlpha);
            SetFloatIfExists(target,"_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfExists(target,"_SrcBlendAlpha",(float)BlendMode.One);
            SetFloatIfExists(target,"_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            SetFloatIfExists(target,"_ZWrite",0f);
            SetFloatIfExists(target,"_AlphaClip",0f);
            SetFloatIfExists(target,"_AlphaToMask",0f);
            SetFloatIfExists(target,"_Mode",2f);
            DisableKeyword(target,"_ALPHATEST_ON");
            target.EnableKeyword("_ALPHABLEND_ON");
            target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            target.renderQueue=(int)RenderQueue.Transparent;
            target.SetOverrideTag("RenderType","Transparent");
            WarnIfBlack(target);
            LogWarning("Made runtime material transparent: "+target.name+" shader="+ShaderName(target.shader));
        }
        catch (Exception ex)
        {
            LogWarning("Could not make material transparent: "+source.name+" | "+ex.Message);
        }
    }
    private void WarnIfBlack(Material mat)
    {
        string prop=mat.HasProperty("_BaseColor") ? "_BaseColor" : (mat.HasProperty("_Color") ? "_Color" : "");
        if (prop.Length==0)
            return;
        Color color=mat.GetColor(prop);
        if (color.r<=0.001f && color.g<=0.001f && color.b<=0.001f)
        {
            LogWarning("Transparent material has a black tint: "+mat.name +
                       " colour="+ColorToString(color) +
                       " - if it renders invisible, this is why");
        }
    }
    private bool HasKeyword(Material material,string keyword)
    {
        if (material==null || string.IsNullOrEmpty(keyword))
            return false;
        return material.IsKeywordEnabled(keyword);
    }
    private void DisableKeyword(Material material,string keyword)
    {
        if (material==null || string.IsNullOrEmpty(keyword))
            return;
        try
        {
            material.DisableKeyword(keyword);
        }
        catch (Exception ex)
        {
            LogSuppressed("DisableKeyword",ex);
        }
    }
    private void SetFloatIfExists(Material material,string property,float value)
    {
        if (material==null || string.IsNullOrEmpty(property))
            return;
        try
        {
            if (material.HasProperty(property))
                material.SetFloat(property,value);
        }
        catch (Exception ex)
        {
            LogSuppressed("SetFloatIfExists",ex);
        }
    }
    private string ColorToString(Color color)
    {
        return color.r.ToString("R",CultureInfo.InvariantCulture)+"," +
               color.g.ToString("R",CultureInfo.InvariantCulture)+"," +
               color.b.ToString("R",CultureInfo.InvariantCulture)+"," +
               color.a.ToString("R",CultureInfo.InvariantCulture);
    }
    public static string ShaderName(Shader shader)
    {
        return shader!=null ? shader.name : "<null>";
    }
    public Material[] Prepare(NobndlBundleCache cache,Material[] materials)
    {
        if (materials==null)
            return null;
        Material[] result=new Material[materials.Length];
        for (int i=0; i<materials.Length; i++)
            result[i]=Prepare(cache,materials[i]);
        return result;
    }
    public Material Prepare(NobndlBundleCache cache,Material material)
    {
        if (material==null)
            return null;
        Material exactClone=ExactClone(cache,material);
        if (exactClone!=null)
            return exactClone;
        if (NeedsShaderFix(material))
            return CloneMaterial(material);
        return material;
    }
    private bool NeedsShaderFix(Material material)
    {
        if (material==null)
            return false;
        Shader shader=material.shader;
        if (shader==null)
            return true;
        string shaderName=shader.name ?? "";
        if (!shader.isSupported)
            return true;
        if (shaderName.IndexOf("InternalErrorShader",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        if (shaderName.IndexOf("Hidden/InternalError",StringComparison.InvariantCultureIgnoreCase)>=0)
            return true;
        return false;
    }
    public Material ExactClone(NobndlBundleCache cache,Material mat)
    {
        if (mat==null)
            return null;
        foreach (KeyValuePair<string,Material> pair in cache.Materials.Items)
        {
            Material source=pair.Value;
            if (source==null)
                continue;
            if (ReferenceEquals(source,mat))
                return CloneMaterial(source);
        }
        return null;
    }
    public bool IsOurClone(UnityEngine.Object obj)
    {
        if (cloneIds==null || cloneIdsVersion!=Version)
        {
            cloneIds=new HashSet<int>();
            AddIds(cloneIds,clones.Values);
            cloneIdsVersion=Version;
        }
        return cloneIds.Contains(obj.GetInstanceID());
    }
}
