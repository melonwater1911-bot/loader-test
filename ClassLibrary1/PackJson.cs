using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

static class PackJson
{
    public static readonly JsonSerializer Serializer=JsonSerializer.Create(new JsonSerializerSettings { ContractResolver=new FieldResolver(),Converters=new List<JsonConverter> { new CurveConverter() } });

    public static List<FieldInfo> Fields(Type type)
    {
        var result = new List<FieldInfo>();
        for (Type t=type; t!=null && t!=typeof(object); t=t.BaseType)
        {
            bool engine=t.IsValueType && t.Namespace=="UnityEngine";
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if(f.IsInitOnly || f.IsNotSerialized || f.Name[0]=='<' || typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                if(f.IsPublic || engine || f.IsDefined(typeof(SerializeField),true)) result.Add(f);
            }
        }
        return result;
    }

    private sealed class FieldResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization memberSerialization)
        {
            var result = new List<JsonProperty>();
            foreach (FieldInfo f in Fields(type))
            {
                JsonProperty p=CreateProperty(f,memberSerialization);
                p.Readable=true;
                p.Writable=true;
                result.Add(p);
            }
            return result;
        }
    }

    private sealed class CurveConverter : JsonConverter
    {
        public override bool CanConvert(Type type)
        {
            return type==typeof(AnimationCurve);
        }

        public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
        {
            AnimationCurve curve=(AnimationCurve)value;
            var a = new JArray((int)curve.preWrapMode,(int)curve.postWrapMode);
            foreach (Keyframe k in curve.keys) a.Add(new JArray(k.time,k.value,k.inTangent,k.outTangent,k.inWeight,k.outWeight,(int)k.weightedMode));
            a.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)
        {
            JArray a=JArray.Load(reader);
            var curve = new AnimationCurve();
            curve.preWrapMode=(WrapMode)(int)a[0];
            curve.postWrapMode=(WrapMode)(int)a[1];
            var keys = new Keyframe[a.Count-2];
            for (int i=0; i<keys.Length; i++)
            {
                JArray k=(JArray)a[i+2];
                keys[i]=new Keyframe((float)k[0],(float)k[1],(float)k[2],(float)k[3],(float)k[4],(float)k[5]);
                keys[i].weightedMode=(WeightedMode)(int)k[6];
            }
            curve.keys=keys;
            return curve;
        }
    }
}
