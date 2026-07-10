using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace XAnimationEngine
{
    public sealed class XAnimationStateBehaviorJsonConverter : JsonConverter<XAnimationStateBehavior>
    {
        private const string TypePropertyName = "type";
        private const string DataPropertyName = "data";

        public override XAnimationStateBehavior ReadJson(
            JsonReader reader,
            Type objectType,
            XAnimationStateBehavior existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            try
            {
                JObject jObject = JObject.Load(reader);
                string typeName = jObject[TypePropertyName]?.Value<string>();
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    Debug.LogWarning("XAnimationStateBehavior 缺少 type，已跳过。");
                    return null;
                }

                Type behaviorType = ResolveBehaviorType(typeName);
                if (behaviorType == null ||
                    behaviorType.IsAbstract ||
                    !typeof(XAnimationStateBehavior).IsAssignableFrom(behaviorType))
                {
                    Debug.LogWarning($"XAnimationStateBehavior type '{typeName}' 无效，已跳过。");
                    return null;
                }

                JToken dataToken = jObject[DataPropertyName];
                if (dataToken == null || dataToken.Type == JTokenType.Null)
                {
                    Debug.LogWarning($"XAnimationStateBehavior type '{typeName}' 缺少 data，已跳过。");
                    return null;
                }

                object behavior = dataToken.ToObject(behaviorType, CreateDataSerializer(serializer));
                if (behavior is XAnimationStateBehavior stateBehavior)
                {
                    return stateBehavior;
                }

                Debug.LogWarning($"XAnimationStateBehavior type '{typeName}' 反序列化结果无效，已跳过。");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"XAnimationStateBehavior 反序列化失败，已跳过。{ex.Message}");
                return null;
            }
        }

        public override void WriteJson(JsonWriter writer, XAnimationStateBehavior value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            JObject jObject = new()
            {
                [TypePropertyName] = value.GetType().FullName,
                [DataPropertyName] = JToken.FromObject(value, CreateDataSerializer(serializer))
            };
            jObject.WriteTo(writer);
        }

        internal static XAnimationStateBehavior CloneBehavior(XAnimationStateBehavior behavior)
        {
            if (behavior == null)
            {
                return null;
            }

            try
            {
                JsonSerializer dataSerializer = CreateDataSerializer(null);
                JToken token = JToken.FromObject(behavior, dataSerializer);
                return token.ToObject(behavior.GetType(), dataSerializer) as XAnimationStateBehavior;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"XAnimationStateBehavior '{behavior.GetType().FullName}' clone 失败，已跳过。{ex.Message}");
                return null;
            }
        }

        private static Type ResolveBehaviorType(string typeName)
        {
            Type type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    type = assemblies[i].GetType(typeName);
                }
                catch
                {
                    type = null;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static JsonSerializer CreateDataSerializer(JsonSerializer source)
        {
            JsonSerializer serializer = new()
            {
                ContractResolver = new XAnimationStateBehaviorDataContractResolver(),
                Culture = source?.Culture,
                DateFormatHandling = source?.DateFormatHandling ?? DateFormatHandling.IsoDateFormat,
                DateParseHandling = source?.DateParseHandling ?? DateParseHandling.DateTime,
                DateTimeZoneHandling = source?.DateTimeZoneHandling ?? DateTimeZoneHandling.RoundtripKind,
                DefaultValueHandling = source?.DefaultValueHandling ?? DefaultValueHandling.Include,
                FloatFormatHandling = source?.FloatFormatHandling ?? FloatFormatHandling.String,
                FloatParseHandling = source?.FloatParseHandling ?? FloatParseHandling.Double,
                Formatting = source?.Formatting ?? Formatting.None,
                MaxDepth = source?.MaxDepth,
                MetadataPropertyHandling = source?.MetadataPropertyHandling ?? MetadataPropertyHandling.Default,
                MissingMemberHandling = source?.MissingMemberHandling ?? MissingMemberHandling.Ignore,
                NullValueHandling = source?.NullValueHandling ?? NullValueHandling.Include,
                ObjectCreationHandling = source?.ObjectCreationHandling ?? ObjectCreationHandling.Auto,
                ReferenceLoopHandling = source?.ReferenceLoopHandling ?? ReferenceLoopHandling.Error,
                TypeNameHandling = TypeNameHandling.None
            };

            if (source != null)
            {
                serializer.Context = source.Context;
                serializer.ConstructorHandling = source.ConstructorHandling;
                serializer.PreserveReferencesHandling = source.PreserveReferencesHandling;
                serializer.ReferenceResolver = source.ReferenceResolver;
                serializer.StringEscapeHandling = source.StringEscapeHandling;
                serializer.TraceWriter = source.TraceWriter;
            }

            return serializer;
        }

        private sealed class XAnimationStateBehaviorDataContractResolver : DefaultContractResolver
        {
            protected override JsonConverter ResolveContractConverter(Type objectType)
            {
                return typeof(XAnimationStateBehavior).IsAssignableFrom(objectType)
                    ? null
                    : base.ResolveContractConverter(objectType);
            }
        }
    }
}
