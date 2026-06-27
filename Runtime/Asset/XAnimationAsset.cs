using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace XAnimationEngine
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class XAnimationAssetAliasAttribute : Attribute
    {
        public XAnimationAssetAliasAttribute(string alias)
        {
            Alias = alias;
        }

        public string Alias { get; }
    }

    public struct XAnimationMetaInfo
    {
        public string typeAlias;
        public string assetPath;
    }

    public class XAnimationAssetBase
    {
        [JsonProperty]
        private XAnimationMetaInfo m_MetaInfo;

        public string Serialize()
        {
            XAnimationAssetAliasAttribute aliasAttribute = GetType().GetCustomAttribute<XAnimationAssetAliasAttribute>(true);
            m_MetaInfo = new XAnimationMetaInfo
            {
                typeAlias = aliasAttribute?.Alias ?? m_MetaInfo.typeAlias,
                assetPath = m_MetaInfo.assetPath
            };
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

#if UNITY_EDITOR
        public void SetAssetPath(string path)
        {
            m_MetaInfo = new XAnimationMetaInfo
            {
                typeAlias = m_MetaInfo.typeAlias,
                assetPath = path
            };
        }

        public void SaveAsset()
        {
            string json = Serialize();
            string path = m_MetaInfo.assetPath;
            if (string.IsNullOrEmpty(path))
            {
                throw new Exception("Asset path is not set. Please call SetAssetPath before saving.");
            }

            System.IO.File.WriteAllText(path, json);
            UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
        }
#endif
    }

    public static class XAnimationAssetUtility
    {
        public const string AnimationAssetAlias = "xframework.animation.asset";
        public const string AnimationOverrideAlias = "xframework.animation.override";
        public const string AnimationAssetExtension = ".xanimation";
        public const string AnimationOverrideExtension = ".xanimationoverride";

        public static bool TryReadMetaInfo(string text, out XAnimationMetaInfo metaInfo)
        {
            metaInfo = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                JObject root = JObject.Parse(text);
                JToken token = root["m_MetaInfo"];
                if (token == null || token.Type != JTokenType.Object)
                {
                    return false;
                }

                XAnimationMetaInfo parsedInfo = token.ToObject<XAnimationMetaInfo>();
                metaInfo = parsedInfo;
                return !string.IsNullOrWhiteSpace(parsedInfo.typeAlias);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static T ToXAnimationAsset<T>(this TextAsset textAsset) where T : XAnimationAssetBase
        {
            T asset = JsonConvert.DeserializeObject<T>(textAsset.text);
#if UNITY_EDITOR
            if (asset != null)
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(textAsset);
                asset.SetAssetPath(path);
            }
#endif
            return asset;
        }

        public static T ToXAnimationAsset<T>(this TextAsset textAsset, Type type) where T : XAnimationAssetBase
        {
            object deserialized = JsonConvert.DeserializeObject(textAsset.text, type);
            T asset = deserialized as T;
#if UNITY_EDITOR
            if (asset != null)
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(textAsset);
                asset.SetAssetPath(path);
            }
#endif
            return asset;
        }

        public static bool IsAnimationAssetExtension(string assetPath)
        {
            return HasExtension(assetPath, AnimationAssetExtension) ||
                   HasExtension(assetPath, AnimationOverrideExtension);
        }

        public static bool HasExtension(string assetPath, string extension)
        {
            return string.Equals(
                System.IO.Path.GetExtension(assetPath),
                extension,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyList<string> GetReferencedAnimationAssetPaths(IReadOnlyList<XAnimationClipConfig> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> paths = new();
            HashSet<string> uniquePaths = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                AddReferencedAnimationAssetPath(paths, uniquePaths, clips[i]?.clipPath);
            }

            return paths.Count == 0 ? Array.Empty<string>() : paths;
        }

        internal static IReadOnlyList<string> GetReferencedAnimationAssetPaths(IReadOnlyList<XAnimationOverrideClipConfig> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> paths = new();
            HashSet<string> uniquePaths = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                AddReferencedAnimationAssetPath(paths, uniquePaths, clips[i]?.clipPath);
            }

            return paths.Count == 0 ? Array.Empty<string>() : paths;
        }

        private static void AddReferencedAnimationAssetPath(List<string> paths, HashSet<string> uniquePaths, string clipPath)
        {
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                return;
            }

            XAnimationClipPathUtility.Split(clipPath, out string assetPath, out _);
            if (string.IsNullOrWhiteSpace(assetPath) || !uniquePaths.Add(assetPath))
            {
                return;
            }

            paths.Add(assetPath);
        }
    }

    public static class XAnimationStatePathUtility
    {
        public static string NormalizePath(string path)
        {
            List<string> segments = new();
            if (!string.IsNullOrWhiteSpace(path))
            {
                string[] rawSegments = path.Split('/');
                for (int i = 0; i < rawSegments.Length; i++)
                {
                    string segment = rawSegments[i]?.Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        segments.Add(segment);
                    }
                }
            }

            return segments.Count == 0 ? string.Empty : string.Join("/", segments);
        }

        public static string FormatDisplayPath(string path)
        {
            string normalizedPath = NormalizePath(path);
            return string.IsNullOrWhiteSpace(normalizedPath)
                ? string.Empty
                : normalizedPath.Replace("/", " / ");
        }

        public static string GetParentPath(string path)
        {
            string normalizedPath = NormalizePath(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex > 0 ? normalizedPath[..slashIndex] : string.Empty;
        }

        public static string GetLeafName(string path)
        {
            string normalizedPath = NormalizePath(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex + 1 < normalizedPath.Length
                ? normalizedPath[(slashIndex + 1)..]
                : normalizedPath;
        }

        public static string BuildPath(string parentPath, string leafName)
        {
            parentPath = NormalizePath(parentPath);
            leafName = NormalizePath(leafName);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return leafName;
            }

            return string.IsNullOrWhiteSpace(leafName) ? parentPath : $"{parentPath}/{leafName}";
        }

        public static bool IsInPath(string key, string path)
        {
            key = NormalizePath(key);
            path = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string parentPath = GetParentPath(key);
            return string.Equals(parentPath, path, StringComparison.Ordinal) ||
                   parentPath.StartsWith($"{path}/", StringComparison.Ordinal);
        }

        public static string GetSuffixInPath(string key, string path)
        {
            key = NormalizePath(key);
            path = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return key;
            }

            string parentPath = GetParentPath(key);
            if (string.Equals(parentPath, path, StringComparison.Ordinal))
            {
                return GetLeafName(key);
            }

            if (parentPath.StartsWith($"{path}/", StringComparison.Ordinal))
            {
                return BuildPath(parentPath[(path.Length + 1)..], GetLeafName(key));
            }

            return key;
        }
    }

    public enum XAnimationChannelLayerType
    {
        Base,
        Override,
        Additive,
    }

    public enum XAnimationUpdateMode
    {
        Manual,
        GameTime,
    }

    public enum XAnimationParameterType
    {
        Float = 0,
        Bool = 1,
        Trigger = 2,
        Int = 3,
    }

    public enum XAnimationStateType
    {
        Single,
        Blend1D,
        Blend2DSimpleDirectional,
        Blend2DFreeformDirectional,
    }

    [Serializable]
    public class XAnimationChannelConfig
    {
        public string name;
        public XAnimationChannelLayerType layerType = XAnimationChannelLayerType.Base;
        public float defaultWeight = 1f;
        public string maskPath;
        public bool allowInterrupt = true;
        public float defaultFadeIn = 0.15f;
        public float defaultFadeOut = 0.15f;
    }

    [Serializable]
    public class XAnimationClipConfig
    {
        public string key;
        public string clipPath;
    }

    [Serializable]
    public class XAnimationParameterConfig
    {
        public string name;
        public XAnimationParameterType type;
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public object defaultValue;
    }

    [Serializable]
    public class XAnimationCueConfig
    {
        public string clipKey;
        [Range(0f, 1f)]
        public float time;
        public string eventKey;
        public string payload;
    }

    [Serializable]
    public class XAnimationStateConfig
    {
        public string key;
        public XAnimationStateType stateType = XAnimationStateType.Single;
        public string clipKey;
        public string channelName;
        public string[] allowedNextStateKeys = Array.Empty<string>();
        public string[] allowedPreviousStateKeys = Array.Empty<string>();
        public float speed = 1f;
        public bool loop = true;
        public string parameterName;
        public string parameterXName;
        public string parameterYName;
        public XAnimationBlend1DSampleConfig[] samples = Array.Empty<XAnimationBlend1DSampleConfig>();
        public XAnimationBlend2DSimpleDirectionalSampleConfig[] directionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
    }

    [Serializable]
    public class XAnimationAutoTransitionConfig
    {
        public string channelName;
        public string preStateKey;
        public string nextStateKey;
        [JsonProperty("ExitTime")]
        public float exitTime = 1f;
        [JsonProperty("TransitionDuration")]
        public float transitionDuration;
        [JsonProperty("EnterTime")]
        public float enterTime;
    }

    [Serializable]
    public class XAnimationDefaultTransitionConfig
    {
        public string channelName;
        public string preStateKey;
        public string nextStateKey;
        public float fadeIn;
        public float fadeOut;
        public float enterTime;
        public int priority;
        public bool interruptible = true;
    }

    [Serializable]
    public class XAnimationBlend1DSampleConfig
    {
        public string clipKey;
        public float threshold;
    }

    [Serializable]
    public class XAnimationBlend2DSimpleDirectionalSampleConfig
    {
        public string clipKey;
        public float positionX;
        public float positionY;
    }

    [Serializable]
    [XAnimationAssetAlias(XAnimationAssetUtility.AnimationAssetAlias)]
    public class XAnimationAsset : XAnimationAssetBase
    {
        public string alias;
        public string DefaultPrefabPath;
        public bool preload;
        public bool rootMotion;
        public XAnimationChannelConfig[] channels = Array.Empty<XAnimationChannelConfig>();
        public XAnimationClipConfig[] clips = Array.Empty<XAnimationClipConfig>();
        public XAnimationStateConfig[] states = Array.Empty<XAnimationStateConfig>();
        public XAnimationAutoTransitionConfig[] autoTransitions = Array.Empty<XAnimationAutoTransitionConfig>();
        public XAnimationDefaultTransitionConfig[] defaultTransitions = Array.Empty<XAnimationDefaultTransitionConfig>();
        public XAnimationParameterConfig[] parameters = Array.Empty<XAnimationParameterConfig>();
        public XAnimationCueConfig[] cues = Array.Empty<XAnimationCueConfig>();

        public IReadOnlyList<string> GetReferencedAnimationAssetPaths()
        {
            return XAnimationAssetUtility.GetReferencedAnimationAssetPaths(clips);
        }
    }

    [Serializable]
    public class XAnimationOverrideClipConfig
    {
        public string key;
        public string clipPath;
    }

    [Serializable]
    [XAnimationAssetAlias(XAnimationAssetUtility.AnimationOverrideAlias)]
    public class XAnimationOverrideAsset : XAnimationAssetBase
    {
        public string baseAssetPath;
        public string DefaultPrefabPath;
        public XAnimationOverrideClipConfig[] clips = Array.Empty<XAnimationOverrideClipConfig>();

        public IReadOnlyList<string> GetReferencedAnimationAssetPaths()
        {
            return XAnimationAssetUtility.GetReferencedAnimationAssetPaths(clips);
        }
    }

    public sealed class XAnimationCompiledChannel
    {
        public XAnimationCompiledChannel(XAnimationChannelConfig config, AvatarMask mask, int layerIndex)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Mask = mask;
            LayerIndex = layerIndex;
        }

        public XAnimationChannelConfig Config { get; }
        public AvatarMask Mask { get; }
        public int LayerIndex { get; }
        public string Name => Config.name;
    }

    public sealed class XAnimationCompiledClip
    {
        private readonly IXAnimationAssetResolver m_Resolver;
        private readonly XAnimationLoadedAssetRegistry m_LoadedAssets;
        private AnimationClip m_Clip;
        private AnimationClip m_PlaybackClip;
        private XAnimationCompiledCue[] m_AnimationEventCues;

        public XAnimationCompiledClip(XAnimationClipConfig config, IXAnimationAssetResolver resolver)
            : this(config, resolver, null)
        {
        }

        internal XAnimationCompiledClip(
            XAnimationClipConfig config,
            IXAnimationAssetResolver resolver,
            XAnimationLoadedAssetRegistry loadedAssets = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            m_Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            m_LoadedAssets = loadedAssets;
        }

        public XAnimationCompiledClip(
            XAnimationClipConfig config,
            AnimationClip clip,
            AnimationClip playbackClip = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            m_Clip = clip ? clip : throw new ArgumentNullException(nameof(clip));
            m_PlaybackClip = playbackClip ? playbackClip : m_Clip;
            m_AnimationEventCues = CompileAnimationEventCues(Key, m_Clip, 0);
        }

        public XAnimationClipConfig Config { get; }
        public AnimationClip Clip => LoadClip();
        public AnimationClip PlaybackClip => LoadPlaybackClip();
        internal IReadOnlyList<XAnimationCompiledCue> AnimationEventCues => LoadAnimationEventCues();
        public string Key => Config.key;
        public string ClipPath => Config.clipPath;

        public void Preload()
        {
            _ = PlaybackClip;
            _ = AnimationEventCues;
        }

        private AnimationClip LoadClip()
        {
            if (m_Clip)
            {
                return m_Clip;
            }

            if (m_Resolver == null)
            {
                throw new XAnimationException($"XAnimation clip '{Key}' has no asset resolver.");
            }

            AnimationClip clip = m_Resolver.LoadAnimationClip(Config.clipPath);
            if (clip == null)
            {
                string message = $"XAnimation clip '{Key}' failed to load AnimationClip at '{Config.clipPath}'. Resolver: {m_Resolver.GetType().Name}";
                throw new XAnimationException(message);
            }

            m_Clip = m_LoadedAssets?.Track(clip) ?? clip;
            return m_Clip;
        }

        private AnimationClip LoadPlaybackClip()
        {
            if (m_PlaybackClip)
            {
                return m_PlaybackClip;
            }

            AnimationClip clip = LoadClip();
            if (m_AnimationEventCues == null)
            {
                m_AnimationEventCues = CompileAnimationEventCues(Key, clip, 0);
            }
            m_PlaybackClip = clip;
            return m_PlaybackClip;
        }

        private XAnimationCompiledCue[] LoadAnimationEventCues()
        {
            if (m_AnimationEventCues != null)
            {
                return m_AnimationEventCues;
            }

            m_AnimationEventCues = CompileAnimationEventCues(Key, Clip, 0);
            return m_AnimationEventCues;
        }

        internal static XAnimationCompiledCue[] CompileAnimationEventCues(
            string clipKey,
            AnimationClip clip,
            int cueIndexOffset)
        {
            if (clip == null)
            {
                return Array.Empty<XAnimationCompiledCue>();
            }

            AnimationEvent[] events = clip.events;
            if (events == null || events.Length == 0)
            {
                return Array.Empty<XAnimationCompiledCue>();
            }

            float clipLength = Mathf.Max(clip.length, 0.0001f);
            List<XAnimationCompiledCue> compiledCues = new(events.Length);
            for (int i = 0; i < events.Length; i++)
            {
                AnimationEvent animationEvent = events[i];
                if (animationEvent == null || string.IsNullOrWhiteSpace(animationEvent.functionName))
                {
                    continue;
                }

                compiledCues.Add(new XAnimationCompiledCue(
                    new XAnimationCueConfig
                    {
                        clipKey = clipKey ?? string.Empty,
                        time = Mathf.Clamp01(animationEvent.time / clipLength),
                        eventKey = animationEvent.functionName,
                        payload = ResolvePayload(animationEvent),
                    },
                    cueIndexOffset + i));
            }

            compiledCues.Sort((left, right) => left.Config.time.CompareTo(right.Config.time));
            return compiledCues.ToArray();
        }

        private static string ResolvePayload(AnimationEvent animationEvent)
        {
            if (animationEvent == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(animationEvent.stringParameter))
            {
                return animationEvent.stringParameter;
            }

            if (animationEvent.intParameter != 0)
            {
                return animationEvent.intParameter.ToString();
            }

            if (!Mathf.Approximately(animationEvent.floatParameter, 0f))
            {
                return animationEvent.floatParameter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (animationEvent.objectReferenceParameter != null)
            {
                return animationEvent.objectReferenceParameter.name ?? string.Empty;
            }

            return string.Empty;
        }
    }

    public sealed class XAnimationCompiledParameter
    {
        public XAnimationCompiledParameter(XAnimationParameterConfig config, int index)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Index = index;
        }

        public XAnimationParameterConfig Config { get; }
        public int Index { get; }
        public string Name => Config.name;
        public XAnimationParameterType Type => Config.type;
    }

    public sealed class XAnimationCompiledCue
    {
        public XAnimationCompiledCue(XAnimationCueConfig config, int cueIndex)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            CueIndex = cueIndex;
        }

        public XAnimationCueConfig Config { get; }
        public int CueIndex { get; }
    }

    public sealed class XAnimationCompiledAutoTransition
    {
        public XAnimationCompiledAutoTransition(XAnimationAutoTransitionConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public XAnimationAutoTransitionConfig Config { get; }
        public string ChannelName => Config.channelName;
        public string PreStateKey => Config.preStateKey;
        public string NextStateKey => Config.nextStateKey;
        public float ExitTime => Config.exitTime;
        public float TransitionDuration => Config.transitionDuration;
        public float EnterTime => Config.enterTime;
        public bool HasNextState => !string.IsNullOrWhiteSpace(Config.nextStateKey);
    }

    public sealed class XAnimationCompiledDefaultTransition
    {
        public XAnimationCompiledDefaultTransition(XAnimationDefaultTransitionConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public XAnimationDefaultTransitionConfig Config { get; }
        public string ChannelName => Config.channelName;
        public string PreStateKey => Config.preStateKey;
        public string NextStateKey => Config.nextStateKey;

        public XAnimationTransitionOptions CreateTransitionOptions()
        {
            return new XAnimationTransitionOptions
            {
                fadeIn = Config.fadeIn,
                fadeOut = Config.fadeOut,
                enterTime = Config.enterTime,
                priority = Config.priority,
                interruptible = Config.interruptible,
            };
        }
    }

    public enum XAnimationTransitionRequestSource
    {
        ExplicitPlay,
        DefaultTransition,
        AutoTransition,
    }

    public enum XAnimationTransitionRejectReason
    {
        None = 0,
        ChannelDisallowInterrupt = 1,
        CurrentUninterruptible = 2,
        LowerPriority = 3,
        SourceStateDisallowTarget = 4,
        TargetStateDisallowSource = 5,
    }

    public abstract class XAnimationCompiledState
    {
        protected XAnimationCompiledState(XAnimationStateConfig config, int defaultChannelIndex)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            DefaultChannelIndex = defaultChannelIndex;
        }

        public XAnimationStateConfig Config { get; }
        public int DefaultChannelIndex { get; }
        public string Key => Config.key;
        public XAnimationStateType StateType => Config.stateType;
        public virtual XAnimationCompiledClip DirectClip => null;
        public abstract IReadOnlyList<int> ClipIndices { get; }
    }

    public sealed class XAnimationCompiledSingleState : XAnimationCompiledState
    {
        private readonly int[] m_ClipIndices;

        public XAnimationCompiledSingleState(XAnimationStateConfig config, int defaultChannelIndex, int clipIndex)
            : base(config, defaultChannelIndex)
        {
            ClipIndex = clipIndex;
            m_ClipIndices = new[] { clipIndex };
        }

        public XAnimationCompiledSingleState(XAnimationStateConfig config, int defaultChannelIndex, XAnimationCompiledClip directClip)
            : base(config, defaultChannelIndex)
        {
            DirectClip = directClip ?? throw new ArgumentNullException(nameof(directClip));
            ClipIndex = -1;
            m_ClipIndices = Array.Empty<int>();
        }

        public int ClipIndex { get; }
        public override XAnimationCompiledClip DirectClip { get; }
        public override IReadOnlyList<int> ClipIndices => m_ClipIndices;
        public bool HasDirectClip => DirectClip != null;
    }

    public sealed class XAnimationCompiledBlend1DSample
    {
        public XAnimationCompiledBlend1DSample(XAnimationBlend1DSampleConfig config, int clipIndex)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            ClipIndex = clipIndex;
        }

        public XAnimationBlend1DSampleConfig Config { get; }
        public int ClipIndex { get; }
        public float Threshold => Config.threshold;
    }

    public sealed class XAnimationCompiledBlend1DState : XAnimationCompiledState
    {
        private readonly int[] m_ClipIndices;

        public XAnimationCompiledBlend1DState(
            XAnimationStateConfig config,
            int defaultChannelIndex,
            int parameterIndex,
            XAnimationCompiledBlend1DSample[] samples)
            : base(config, defaultChannelIndex)
        {
            ParameterIndex = parameterIndex;
            Samples = samples ?? Array.Empty<XAnimationCompiledBlend1DSample>();
            m_ClipIndices = new int[Samples.Count];
            for (int i = 0; i < Samples.Count; i++)
            {
                m_ClipIndices[i] = Samples[i].ClipIndex;
            }
        }

        public int ParameterIndex { get; }
        public IReadOnlyList<XAnimationCompiledBlend1DSample> Samples { get; }
        public override IReadOnlyList<int> ClipIndices => m_ClipIndices;
    }

    public sealed class XAnimationCompiledBlend2DSimpleDirectionalSample
    {
        public XAnimationCompiledBlend2DSimpleDirectionalSample(XAnimationBlend2DSimpleDirectionalSampleConfig config, int clipIndex)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            ClipIndex = clipIndex;
        }

        public XAnimationBlend2DSimpleDirectionalSampleConfig Config { get; }
        public int ClipIndex { get; }
        public Vector2 Position => new(Config.positionX, Config.positionY);
    }

    public sealed class XAnimationCompiledBlend2DSimpleDirectionalState : XAnimationCompiledState
    {
        private readonly int[] m_ClipIndices;

        public XAnimationCompiledBlend2DSimpleDirectionalState(
            XAnimationStateConfig config,
            int defaultChannelIndex,
            int parameterXIndex,
            int parameterYIndex,
            XAnimationCompiledBlend2DSimpleDirectionalSample[] samples)
            : base(config, defaultChannelIndex)
        {
            ParameterXIndex = parameterXIndex;
            ParameterYIndex = parameterYIndex;
            Samples = samples ?? Array.Empty<XAnimationCompiledBlend2DSimpleDirectionalSample>();
            m_ClipIndices = new int[Samples.Count];
            for (int i = 0; i < Samples.Count; i++)
            {
                m_ClipIndices[i] = Samples[i].ClipIndex;
            }
        }

        public int ParameterXIndex { get; }
        public int ParameterYIndex { get; }
        public IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> Samples { get; }
        public override IReadOnlyList<int> ClipIndices => m_ClipIndices;
    }

    public sealed class XAnimationCompiledBlend2DFreeformDirectionalState : XAnimationCompiledState
    {
        private readonly int[] m_ClipIndices;

        public XAnimationCompiledBlend2DFreeformDirectionalState(
            XAnimationStateConfig config,
            int defaultChannelIndex,
            int parameterXIndex,
            int parameterYIndex,
            XAnimationCompiledBlend2DSimpleDirectionalSample[] samples)
            : base(config, defaultChannelIndex)
        {
            ParameterXIndex = parameterXIndex;
            ParameterYIndex = parameterYIndex;
            Samples = samples ?? Array.Empty<XAnimationCompiledBlend2DSimpleDirectionalSample>();
            m_ClipIndices = new int[Samples.Count];
            for (int i = 0; i < Samples.Count; i++)
            {
                m_ClipIndices[i] = Samples[i].ClipIndex;
            }
        }

        public int ParameterXIndex { get; }
        public int ParameterYIndex { get; }
        public IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> Samples { get; }
        public override IReadOnlyList<int> ClipIndices => m_ClipIndices;
    }

    public sealed class XAnimationCompiledAsset : IDisposable
    {
        private readonly Dictionary<string, int> m_ChannelIndexByName;
        private readonly Dictionary<string, int> m_ClipIndexByKey;
        private readonly Dictionary<string, int> m_ParameterIndexByName;
        private readonly Dictionary<string, int> m_StateIndexByKey;
        private readonly Dictionary<string, int> m_StateIndexByScopeKey;
        private readonly HashSet<string> m_AmbiguousStateKeys;
        private readonly Dictionary<string, int> m_AutoTransitionIndexByStateScopeKey;
        private readonly Dictionary<string, int> m_DefaultTransitionIndexByPairKey;
        private readonly XAnimationLoadedAssetRegistry m_LoadedAssets;

        public XAnimationCompiledAsset(
            XAnimationAsset asset,
            XAnimationCompiledChannel[] channels,
            XAnimationCompiledClip[] clips,
            XAnimationCompiledState[] states,
            XAnimationCompiledAutoTransition[] autoTransitions,
            XAnimationCompiledDefaultTransition[] defaultTransitions,
            XAnimationCompiledParameter[] parameters,
            Dictionary<string, List<XAnimationCompiledCue>> cuesByClipKey,
            Dictionary<string, int> channelIndexByName,
            Dictionary<string, int> clipIndexByKey,
            Dictionary<string, int> parameterIndexByName,
            Dictionary<string, int> stateIndexByKey,
            Dictionary<string, int> autoTransitionIndexByStateScopeKey,
            Dictionary<string, int> defaultTransitionIndexByPairKey)
            : this(
                asset,
                channels,
                clips,
                states,
                autoTransitions,
                defaultTransitions,
                parameters,
                cuesByClipKey,
                channelIndexByName,
                clipIndexByKey,
                parameterIndexByName,
                stateIndexByKey,
                null,
                null,
                autoTransitionIndexByStateScopeKey,
                defaultTransitionIndexByPairKey,
                null)
        {
        }

        public XAnimationCompiledAsset(
            XAnimationAsset asset,
            XAnimationCompiledChannel[] channels,
            XAnimationCompiledClip[] clips,
            XAnimationCompiledState[] states,
            XAnimationCompiledAutoTransition[] autoTransitions,
            XAnimationCompiledDefaultTransition[] defaultTransitions,
            XAnimationCompiledParameter[] parameters,
            Dictionary<string, List<XAnimationCompiledCue>> cuesByClipKey,
            Dictionary<string, int> channelIndexByName,
            Dictionary<string, int> clipIndexByKey,
            Dictionary<string, int> parameterIndexByName,
            Dictionary<string, int> stateIndexByKey,
            Dictionary<string, int> stateIndexByScopeKey,
            Dictionary<string, int> autoTransitionIndexByStateScopeKey,
            Dictionary<string, int> defaultTransitionIndexByPairKey)
            : this(
                asset,
                channels,
                clips,
                states,
                autoTransitions,
                defaultTransitions,
                parameters,
                cuesByClipKey,
                channelIndexByName,
                clipIndexByKey,
                parameterIndexByName,
                stateIndexByKey,
                stateIndexByScopeKey,
                null,
                autoTransitionIndexByStateScopeKey,
                defaultTransitionIndexByPairKey,
                null)
        {
        }

        internal XAnimationCompiledAsset(
            XAnimationAsset asset,
            XAnimationCompiledChannel[] channels,
            XAnimationCompiledClip[] clips,
            XAnimationCompiledState[] states,
            XAnimationCompiledAutoTransition[] autoTransitions,
            XAnimationCompiledDefaultTransition[] defaultTransitions,
            XAnimationCompiledParameter[] parameters,
            Dictionary<string, List<XAnimationCompiledCue>> cuesByClipKey,
            Dictionary<string, int> channelIndexByName,
            Dictionary<string, int> clipIndexByKey,
            Dictionary<string, int> parameterIndexByName,
            Dictionary<string, int> stateIndexByKey,
            Dictionary<string, int> stateIndexByScopeKey,
            HashSet<string> ambiguousStateKeys,
            Dictionary<string, int> autoTransitionIndexByStateScopeKey,
            Dictionary<string, int> defaultTransitionIndexByPairKey,
            XAnimationLoadedAssetRegistry loadedAssets)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            Channels = channels ?? Array.Empty<XAnimationCompiledChannel>();
            Clips = clips ?? Array.Empty<XAnimationCompiledClip>();
            States = states ?? Array.Empty<XAnimationCompiledState>();
            AutoTransitions = autoTransitions ?? Array.Empty<XAnimationCompiledAutoTransition>();
            DefaultTransitions = defaultTransitions ?? Array.Empty<XAnimationCompiledDefaultTransition>();
            Parameters = parameters ?? Array.Empty<XAnimationCompiledParameter>();
            CuesByClipKey = cuesByClipKey ?? new Dictionary<string, List<XAnimationCompiledCue>>(StringComparer.Ordinal);
            m_ChannelIndexByName = channelIndexByName ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_ClipIndexByKey = clipIndexByKey ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_ParameterIndexByName = parameterIndexByName ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_StateIndexByKey = stateIndexByKey ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_StateIndexByScopeKey = stateIndexByScopeKey ?? CreateStateIndexByScopeKey(States);
            m_AmbiguousStateKeys = ambiguousStateKeys ?? CreateAmbiguousStateKeys(States);
            m_AutoTransitionIndexByStateScopeKey = autoTransitionIndexByStateScopeKey ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_DefaultTransitionIndexByPairKey = defaultTransitionIndexByPairKey ?? new Dictionary<string, int>(StringComparer.Ordinal);
            m_LoadedAssets = loadedAssets;
        }

        public XAnimationAsset Asset { get; }
        public IReadOnlyList<XAnimationCompiledChannel> Channels { get; }
        public IReadOnlyList<XAnimationCompiledClip> Clips { get; }
        public IReadOnlyList<XAnimationCompiledState> States { get; }
        public IReadOnlyList<XAnimationCompiledAutoTransition> AutoTransitions { get; }
        public IReadOnlyList<XAnimationCompiledDefaultTransition> DefaultTransitions { get; }
        public IReadOnlyList<XAnimationCompiledParameter> Parameters { get; }
        public IReadOnlyDictionary<string, List<XAnimationCompiledCue>> CuesByClipKey { get; }
        public bool RootMotionEnabled => Asset.rootMotion;

        public void Dispose()
        {
            m_LoadedAssets?.Dispose();
        }

        public bool TryGetChannelIndex(string channelName, out int channelIndex)
        {
            return m_ChannelIndexByName.TryGetValue(channelName, out channelIndex);
        }

        public bool TryGetClipIndex(string clipKey, out int clipIndex)
        {
            return m_ClipIndexByKey.TryGetValue(clipKey, out clipIndex);
        }

        public bool TryGetParameterIndex(string parameterName, out int parameterIndex)
        {
            return m_ParameterIndexByName.TryGetValue(parameterName, out parameterIndex);
        }

        public bool TryGetStateIndex(string stateKey, out int stateIndex)
        {
            if (IsStateKeyAmbiguous(stateKey))
            {
                stateIndex = -1;
                return false;
            }

            return m_StateIndexByKey.TryGetValue(stateKey, out stateIndex);
        }

        public bool IsStateKeyAmbiguous(string stateKey)
        {
            return !string.IsNullOrWhiteSpace(stateKey) && m_AmbiguousStateKeys.Contains(stateKey);
        }

        public bool TryGetStateIndex(string channelName, string stateKey, out int stateIndex)
        {
            if (string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(stateKey))
            {
                stateIndex = -1;
                return false;
            }

            return m_StateIndexByScopeKey.TryGetValue(BuildStateScopeKey(channelName, stateKey), out stateIndex);
        }

        public bool TryGetAutoTransition(string channelName, string preStateKey, out XAnimationCompiledAutoTransition transition)
        {
            if (string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(preStateKey) ||
                !m_AutoTransitionIndexByStateScopeKey.TryGetValue(BuildStateScopeKey(channelName, preStateKey), out int transitionIndex))
            {
                transition = null;
                return false;
            }

            transition = AutoTransitions[transitionIndex];
            return true;
        }

        public bool TryGetDefaultTransition(
            string channelName,
            string preStateKey,
            string nextStateKey,
            out XAnimationCompiledDefaultTransition transition)
        {
            if (string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(preStateKey) ||
                string.IsNullOrWhiteSpace(nextStateKey) ||
                !m_DefaultTransitionIndexByPairKey.TryGetValue(BuildTransitionPairKey(channelName, preStateKey, nextStateKey), out int transitionIndex))
            {
                transition = null;
                return false;
            }

            transition = DefaultTransitions[transitionIndex];
            return true;
        }

        public XAnimationCompiledChannel GetChannel(string channelName)
        {
            if (!TryGetChannelIndex(channelName, out int channelIndex))
            {
                throw new XAnimationException($"XAnimation channel '{channelName}' does not exist.");
            }

            return Channels[channelIndex];
        }

        public XAnimationCompiledClip GetClip(string clipKey)
        {
            if (!TryGetClipIndex(clipKey, out int clipIndex))
            {
                throw new XAnimationException($"XAnimation clip '{clipKey}' does not exist.");
            }

            return Clips[clipIndex];
        }

        public void PreloadAll()
        {
            for (int i = 0; i < Clips.Count; i++)
            {
                PreloadClipAtIndex(i);
            }
        }

        public void PreloadState(string stateKey)
        {
            XAnimationCompiledState state = GetState(stateKey);
            PreloadState(state);
        }

        public void PreloadState(string channelName, string stateKey)
        {
            XAnimationCompiledState state = GetState(channelName, stateKey);
            PreloadState(state);
        }

        private void PreloadState(XAnimationCompiledState state)
        {
            if (state.DirectClip != null)
            {
                state.DirectClip.Preload();
                return;
            }

            IReadOnlyList<int> clipIndices = state.ClipIndices;
            for (int i = 0; i < clipIndices.Count; i++)
            {
                PreloadClipAtIndex(clipIndices[i]);
            }
        }

        public XAnimationCompiledParameter GetParameter(string parameterName)
        {
            if (!TryGetParameterIndex(parameterName, out int parameterIndex))
            {
                throw new XAnimationException($"XAnimation parameter '{parameterName}' does not exist.");
            }

            return Parameters[parameterIndex];
        }

        public XAnimationCompiledState GetState(string stateKey)
        {
            if (IsStateKeyAmbiguous(stateKey))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' is ambiguous; use channelName.");
            }

            if (!TryGetStateIndex(stateKey, out int stateIndex))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' does not exist.");
            }

            return States[stateIndex];
        }

        public XAnimationCompiledState GetState(string channelName, string stateKey)
        {
            if (!TryGetStateIndex(channelName, stateKey, out int stateIndex))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' in channel '{channelName}' does not exist.");
            }

            return States[stateIndex];
        }

        public XAnimationCompiledAutoTransition GetAutoTransition(string channelName, string preStateKey)
        {
            if (!TryGetAutoTransition(channelName, preStateKey, out XAnimationCompiledAutoTransition transition))
            {
                throw new XAnimationException($"XAnimation auto transition for state '{preStateKey}' in channel '{channelName}' does not exist.");
            }

            return transition;
        }

        public static string BuildStateScopeKey(string channelName, string stateKey)
        {
            return $"{channelName}\u001F{stateKey}";
        }

        private static Dictionary<string, int> CreateStateIndexByScopeKey(IReadOnlyList<XAnimationCompiledState> states)
        {
            Dictionary<string, int> stateIndexByScopeKey = new(StringComparer.Ordinal);
            if (states == null)
            {
                return stateIndexByScopeKey;
            }

            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                string channelName = state?.Config.channelName;
                string stateKey = state?.Key;
                if (string.IsNullOrWhiteSpace(channelName) ||
                    string.IsNullOrWhiteSpace(stateKey))
                {
                    continue;
                }

                stateIndexByScopeKey[BuildStateScopeKey(channelName, stateKey)] = i;
            }

            return stateIndexByScopeKey;
        }

        private static HashSet<string> CreateAmbiguousStateKeys(IReadOnlyList<XAnimationCompiledState> states)
        {
            HashSet<string> seenStateKeys = new(StringComparer.Ordinal);
            HashSet<string> ambiguousStateKeys = new(StringComparer.Ordinal);
            if (states == null)
            {
                return ambiguousStateKeys;
            }

            for (int i = 0; i < states.Count; i++)
            {
                string stateKey = states[i]?.Key;
                if (string.IsNullOrWhiteSpace(stateKey))
                {
                    continue;
                }

                if (!seenStateKeys.Add(stateKey))
                {
                    ambiguousStateKeys.Add(stateKey);
                }
            }

            return ambiguousStateKeys;
        }

        public static string BuildTransitionPairKey(string channelName, string preStateKey, string nextStateKey)
        {
            return $"{channelName}\u001F{preStateKey}\u001F{nextStateKey}";
        }

        public float GetStateDuration(string stateKey)
        {
            if (!TryGetStateDuration(stateKey, out float duration))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' does not provide a fixed duration.");
            }

            return duration;
        }

        public float GetStateDuration(string channelName, string stateKey)
        {
            if (!TryGetStateDuration(channelName, stateKey, out float duration))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' in channel '{channelName}' does not provide a fixed duration.");
            }

            return duration;
        }

        public bool TryGetStateDuration(string stateKey, out float duration)
        {
            XAnimationCompiledState state = GetState(stateKey);
            return TryGetStateDuration(state, out duration);
        }

        public bool TryGetStateDuration(string channelName, string stateKey, out float duration)
        {
            XAnimationCompiledState state = GetState(channelName, stateKey);
            return TryGetStateDuration(state, out duration);
        }

        private bool TryGetStateDuration(XAnimationCompiledState state, out float duration)
        {
            float speed = Mathf.Approximately(state.Config.speed, 0f) ? 1f : state.Config.speed;
            float maxClipLength = 0f;

            if (state.DirectClip != null)
            {
                maxClipLength = state.DirectClip.Clip.length;
            }
            else
            {
                IReadOnlyList<int> clipIndices = state.ClipIndices;
                for (int i = 0; i < clipIndices.Count; i++)
                {
                    XAnimationCompiledClip clip = (XAnimationCompiledClip)Clips[clipIndices[i]];
                    maxClipLength = Mathf.Max(maxClipLength, clip.Clip.length);
                }
            }

            if (maxClipLength <= 0f)
            {
                duration = 0f;
                return false;
            }

            duration = maxClipLength / speed;
            return true;
        }

        public float GetClipDuration(string clipKey)
        {
            if (!TryGetClipDuration(clipKey, out float duration))
            {
                throw new XAnimationException($"XAnimation clip '{clipKey}' does not exist.");
            }

            return duration;
        }

        public bool TryGetClipDuration(string clipKey, out float duration)
        {
            if (!TryGetClipIndex(clipKey, out int clipIndex))
            {
                duration = 0f;
                return false;
            }

            XAnimationCompiledClip clip = (XAnimationCompiledClip)Clips[clipIndex];
            duration = clip.Clip.length;
            return true;
        }

        private void PreloadClipAtIndex(int clipIndex)
        {
            XAnimationCompiledClip clip = (XAnimationCompiledClip)Clips[clipIndex];
            clip.Preload();
        }
    }

    [Serializable]
    public class XAnimationTransitionOptions
    {
        public float fadeIn;
        public float fadeOut;
        public float enterTime;
        public int priority;
        public bool interruptible = true;
    }

    internal sealed class XAnimationTransitionRequest
    {
        public XAnimationTransitionRequest(
            string channelName,
            string targetStateKey,
            string targetClipKey,
            XAnimationTransitionRequestSource source,
            float fadeIn,
            float fadeOut,
            float enterTime,
            int priority,
            bool interruptible,
            bool force)
        {
            ChannelName = channelName ?? string.Empty;
            TargetStateKey = targetStateKey ?? string.Empty;
            TargetClipKey = targetClipKey ?? string.Empty;
            Source = source;
            FadeIn = Mathf.Max(0f, fadeIn);
            FadeOut = Mathf.Max(0f, fadeOut);
            EnterTime = Mathf.Clamp01(enterTime);
            Priority = priority;
            Interruptible = interruptible;
            Force = force;
        }

        public string ChannelName { get; }
        public string TargetStateKey { get; }
        public string TargetClipKey { get; }
        public XAnimationTransitionRequestSource Source { get; }
        public float FadeIn { get; }
        public float FadeOut { get; }
        public float EnterTime { get; }
        public int Priority { get; }
        public bool Interruptible { get; }
        public bool Force { get; }

        public XAnimationPlaybackRuntimeOptions CreateRuntimeOptions(bool skipFadeIn)
        {
            return new XAnimationPlaybackRuntimeOptions(
                skipFadeIn ? 0f : FadeIn,
                FadeOut,
                1f,
                EnterTime,
                Priority,
                Interruptible,
                Source);
        }
    }

    public sealed class XAnimationChannelState
    {
        public string channelName;
        public string stateKey;
        public XAnimationStateType stateType;
        public string clipKey;
        public XAnimationBlendClipState[] blendClips;
        public int playbackId;
        public float normalizedTime;
        public float totalNormalizedTime;
        public float weight;
        public float channelWeight;
        public float stateSpeed;
        public float globalSpeed;
        public float speed;
        public float blendParameterX;
        public float blendParameterY;
        public bool isLooping;
        public bool isFading;
        public int priority;
        public bool interruptible;
        public bool isTemporaryState;
        public string nextStateKey;
        public bool isTransitioning;
        public string previousStateKey;
        public int previousPlaybackId;
        public XAnimationTransitionRequestSource transitionSource;
        public string transitionTargetStateKey;
        public XAnimationTransitionRejectReason lastRejectReason;
        public string lastRejectedStateKey;
        public string lastRejectedClipKey;
        public int lastRejectedPriority;
        public XAnimationTransitionRequestSource lastRejectedSource;
    }

    public enum XAnimationStateExitReason
    {
        Interrupted,
        Completed,
        Stopped,
        Disposed,
    }

    public sealed class XAnimationStateEvent
    {
        public string stateKey;
        public string channelName;
        public int playbackId;
        public bool isTemporaryState;
        public float normalizedTime;
        public float totalNormalizedTime;
        public XAnimationStateExitReason? exitReason;
    }

    public sealed class XAnimationBlendClipState
    {
        public string clipKey;
        public float weight;
        public float normalizedTime;
        public float totalNormalizedTime;
        public float positionX;
        public float positionY;
    }

    public sealed class XAnimationCueEvent
    {
        public int playbackId;
        public string clipKey;
        public string channelName;
        public string eventKey;
        public string payload;
        public float weight;
        public float normalizedTime;
        public int loopCount;
    }
}
