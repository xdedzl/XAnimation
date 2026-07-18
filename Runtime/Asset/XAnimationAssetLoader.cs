using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace XAnimationEngine
{
    public sealed class XAnimationAssetLoader
    {
        private readonly XAnimationAssetValidator m_Validator = new();
        private readonly IXAnimationAssetResolver m_Resolver;

        public XAnimationAssetLoader()
            : this(new XAnimationRuntimeAssetResolver())
        {
        }

        public XAnimationAssetLoader(IXAnimationAssetResolver resolver)
        {
            m_Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public XAnimationCompiledAsset Load(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new XAnimationException("XAnimation assetPath cannot be empty.");
            }

            XAnimationLoadedAssetRegistry loadedAssets = new(m_Resolver);
            try
            {
                TextAsset textAsset = loadedAssets.Track(m_Resolver.LoadTextAsset(assetPath));
                if (textAsset == null)
                {
                    throw new XAnimationException($"XAnimation asset missing at '{assetPath}'.");
                }

                XAnimationAsset asset = LoadAsset(textAsset, assetPath, loadedAssets);
                return Compile(asset, loadedAssets);
            }
            catch
            {
                loadedAssets.Dispose();
                throw;
            }
        }

        public XAnimationCompiledAsset Load(TextAsset textAsset)
        {
            if (textAsset == null)
            {
                throw new XAnimationException("XAnimation TextAsset cannot be null.");
            }

            XAnimationLoadedAssetRegistry loadedAssets = new(m_Resolver);
            try
            {
                XAnimationAsset asset = LoadAsset(textAsset, textAsset.name, loadedAssets);
                return Compile(asset, loadedAssets);
            }
            catch
            {
                loadedAssets.Dispose();
                throw;
            }
        }

        public static bool IsXAnimationAssetText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                JObject json = JObject.Parse(text);
                if (json["baseAssetPath"] != null)
                {
                    return true;
                }

                return json["channels"] is JArray && json["clips"] is JArray;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public XAnimationCompiledAsset Compile(XAnimationAsset asset)
        {
            XAnimationLoadedAssetRegistry loadedAssets = new(m_Resolver);
            try
            {
                return Compile(asset, loadedAssets);
            }
            catch
            {
                loadedAssets.Dispose();
                throw;
            }
        }

        private XAnimationCompiledAsset Compile(
            XAnimationAsset asset,
            XAnimationLoadedAssetRegistry loadedAssets)
        {
            NormalizeAsset(asset);
            m_Validator.Validate(asset);

            XAnimationCompiledChannel[] compiledChannels = new XAnimationCompiledChannel[asset.channels.Length];
            Dictionary<string, int> channelIndexByName = new(StringComparer.Ordinal);
            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channelConfig = asset.channels[i];
                AvatarMask mask = null;
                if (!string.IsNullOrWhiteSpace(channelConfig.maskPath))
                {
                    mask = loadedAssets.Track(m_Resolver.LoadAvatarMask(channelConfig.maskPath));
                    if (mask == null)
                    {
                        throw new XAnimationException($"XAnimation channel '{channelConfig.name}' failed to load AvatarMask at '{channelConfig.maskPath}'.");
                    }
                }

                compiledChannels[i] = new XAnimationCompiledChannel(channelConfig, mask, i);
                channelIndexByName[channelConfig.name] = i;
            }

            XAnimationCompiledClip[] compiledClips = new XAnimationCompiledClip[asset.clips.Length];
            Dictionary<string, int> clipIndexByKey = new(StringComparer.Ordinal);
            for (int i = 0; i < asset.clips.Length; i++)
            {
                XAnimationClipConfig clipConfig = asset.clips[i];
                compiledClips[i] = new XAnimationCompiledClip(clipConfig, m_Resolver, loadedAssets);
                clipIndexByKey[clipConfig.key] = i;
            }

            XAnimationCompiledParameter[] compiledParameters = new XAnimationCompiledParameter[asset.parameters.Length];
            Dictionary<string, int> parameterIndexByName = new(StringComparer.Ordinal);
            for (int i = 0; i < asset.parameters.Length; i++)
            {
                XAnimationCompiledParameter parameter = new(asset.parameters[i], i);
                compiledParameters[i] = parameter;
                parameterIndexByName[parameter.Name] = i;
            }

            List<XAnimationCompiledState> compiledStates = new();
            List<XAnimationCompiledStateNode> compiledStateNodes = new();
            Dictionary<string, XAnimationCompiledStateNode> stateNodeByScopeKey = new(StringComparer.Ordinal);
            for (int channelIndex = 0; channelIndex < asset.channels.Length; channelIndex++)
            {
                XAnimationChannelConfig channelConfig = asset.channels[channelIndex];
                XAnimationStateNodeConfig[] rootNodeConfigs = channelConfig.stateNodes;
                XAnimationCompiledStateNode[] rootNodes = new XAnimationCompiledStateNode[rootNodeConfigs.Length];
                for (int nodeIndex = 0; nodeIndex < rootNodeConfigs.Length; nodeIndex++)
                {
                    rootNodes[nodeIndex] = CompileStateNode(
                        rootNodeConfigs[nodeIndex],
                        channelConfig.name,
                        channelIndex,
                        string.Empty,
                        clipIndexByKey,
                        parameterIndexByName,
                        compiledStates,
                        compiledStateNodes,
                        stateNodeByScopeKey);
                }
                compiledChannels[channelIndex].RootStateNodes = rootNodes;
            }

            XAnimationCompiledState[] compiledStateArray = compiledStates.ToArray();
            XAnimationCompiledStateNode[] compiledStateNodeArray = compiledStateNodes.ToArray();
            Dictionary<string, int> stateIndexByKey = new(StringComparer.Ordinal);
            Dictionary<string, int> stateIndexByScopeKey = new(StringComparer.Ordinal);
            HashSet<string> ambiguousStateKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < compiledStateArray.Length; i++)
            {
                XAnimationCompiledState state = compiledStateArray[i];
                string stateScopeKey = XAnimationCompiledAsset.BuildStateScopeKey(state.ChannelName, state.Key);
                if (stateIndexByKey.ContainsKey(state.Key))
                {
                    stateIndexByKey.Remove(state.Key);
                    ambiguousStateKeys.Add(state.Key);
                }
                else if (!ambiguousStateKeys.Contains(state.Key))
                {
                    stateIndexByKey.Add(state.Key, i);
                }
                stateIndexByScopeKey[stateScopeKey] = i;
            }

            Dictionary<string, int> stateNodeIndexByKey = new(StringComparer.Ordinal);
            Dictionary<string, int> stateNodeIndexByScopeKey = new(StringComparer.Ordinal);
            HashSet<string> ambiguousStateNodeKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < compiledStateNodeArray.Length; i++)
            {
                XAnimationCompiledStateNode stateNode = compiledStateNodeArray[i];
                if (stateNodeIndexByKey.ContainsKey(stateNode.Key))
                {
                    stateNodeIndexByKey.Remove(stateNode.Key);
                    ambiguousStateNodeKeys.Add(stateNode.Key);
                }
                else if (!ambiguousStateNodeKeys.Contains(stateNode.Key))
                {
                    stateNodeIndexByKey.Add(stateNode.Key, i);
                }
                stateNodeIndexByScopeKey.Add(XAnimationCompiledAsset.BuildStateScopeKey(stateNode.ChannelName, stateNode.Key), i);
            }

            List<XAnimationCompiledAutoTransition> compiledAutoTransitions = new();
            Dictionary<string, int> autoTransitionIndexByStateScopeKey = new(StringComparer.Ordinal);
            List<XAnimationCompiledDefaultTransition> compiledDefaultTransitions = new();
            Dictionary<string, int> defaultTransitionIndexByPairKey = new(StringComparer.Ordinal);
            for (int channelIndex = 0; channelIndex < asset.channels.Length; channelIndex++)
            {
                XAnimationChannelConfig channel = asset.channels[channelIndex];
                for (int i = 0; i < channel.autoTransitions.Length; i++)
                {
                    int transitionIndex = compiledAutoTransitions.Count;
                    XAnimationAutoTransitionConfig config = channel.autoTransitions[i];
                    compiledAutoTransitions.Add(new XAnimationCompiledAutoTransition(channel.name, config));
                    autoTransitionIndexByStateScopeKey.Add(
                        XAnimationCompiledAsset.BuildStateScopeKey(channel.name, config.preStateKey),
                        transitionIndex);
                }

                for (int i = 0; i < channel.defaultTransitions.Length; i++)
                {
                    int transitionIndex = compiledDefaultTransitions.Count;
                    XAnimationDefaultTransitionConfig config = channel.defaultTransitions[i];
                    compiledDefaultTransitions.Add(new XAnimationCompiledDefaultTransition(channel.name, config));
                    defaultTransitionIndexByPairKey.Add(
                        XAnimationCompiledAsset.BuildTransitionPairKey(channel.name, config.preStateKey, config.nextStateKey),
                        transitionIndex);
                }
            }

            Dictionary<string, List<XAnimationCompiledCue>> cuesByClipKey = new(StringComparer.Ordinal);
            for (int i = 0; i < asset.cues.Length; i++)
            {
                XAnimationCueConfig cueConfig = asset.cues[i];
                AddCue(cuesByClipKey, cueConfig.clipKey, new XAnimationCompiledCue(cueConfig, i));
            }

            int animationEventCueIndexOffset = asset.cues.Length;
            for (int clipIndex = 0; clipIndex < compiledClips.Length; clipIndex++)
            {
                XAnimationCompiledClip clip = compiledClips[clipIndex];
                IReadOnlyList<XAnimationCompiledCue> animationEventCues = clip.AnimationEventCues;
                for (int cueIndex = 0; cueIndex < animationEventCues.Count; cueIndex++)
                {
                    AddCue(
                        cuesByClipKey,
                        clip.Key,
                        new XAnimationCompiledCue(
                            animationEventCues[cueIndex].Config,
                            animationEventCueIndexOffset++));
                }
            }

            foreach (List<XAnimationCompiledCue> cueList in cuesByClipKey.Values)
            {
                cueList.Sort((left, right) => left.Config.time.CompareTo(right.Config.time));
            }

            return new XAnimationCompiledAsset(
                asset,
                compiledChannels,
                compiledClips,
                compiledStateArray,
                compiledStateNodeArray,
                compiledAutoTransitions.ToArray(),
                compiledDefaultTransitions.ToArray(),
                compiledParameters,
                cuesByClipKey,
                channelIndexByName,
                clipIndexByKey,
                parameterIndexByName,
                stateIndexByKey,
                stateIndexByScopeKey,
                ambiguousStateKeys,
                stateNodeIndexByKey,
                stateNodeIndexByScopeKey,
                ambiguousStateNodeKeys,
                autoTransitionIndexByStateScopeKey,
                defaultTransitionIndexByPairKey,
                loadedAssets);
        }

        private static void AddCue(
            Dictionary<string, List<XAnimationCompiledCue>> cuesByClipKey,
            string clipKey,
            XAnimationCompiledCue cue)
        {
            if (!cuesByClipKey.TryGetValue(clipKey, out List<XAnimationCompiledCue> compiledCues))
            {
                compiledCues = new List<XAnimationCompiledCue>();
                cuesByClipKey.Add(clipKey, compiledCues);
            }

            compiledCues.Add(cue);
        }

        private static XAnimationCompiledStateNode CompileStateNode(
            XAnimationStateNodeConfig nodeConfig,
            string channelName,
            int channelIndex,
            string parentKey,
            IReadOnlyDictionary<string, int> clipIndexByKey,
            IReadOnlyDictionary<string, int> parameterIndexByName,
            List<XAnimationCompiledState> compiledStates,
            List<XAnimationCompiledStateNode> compiledStateNodes,
            Dictionary<string, XAnimationCompiledStateNode> stateNodeByScopeKey)
        {
            string key = XAnimationStatePathUtility.BuildPath(parentKey, nodeConfig.name);
            int nodeInsertIndex = compiledStateNodes.Count;
            XAnimationStateNodeConfig[] childConfigs = nodeConfig.children ?? Array.Empty<XAnimationStateNodeConfig>();
            XAnimationCompiledStateNode[] children = new XAnimationCompiledStateNode[childConfigs.Length];
            for (int i = 0; i < childConfigs.Length; i++)
            {
                XAnimationCompiledStateNode child = CompileStateNode(
                    childConfigs[i],
                    channelName,
                    channelIndex,
                    key,
                    clipIndexByKey,
                    parameterIndexByName,
                    compiledStates,
                    compiledStateNodes,
                    stateNodeByScopeKey);
                children[i] = child;
            }

            XAnimationCompiledStateNode compiledNode;
            switch (nodeConfig.kind)
            {
                case XAnimationStateNodeKind.Normal:
                    compiledNode = new XAnimationCompiledNormalStateNode(nodeConfig, key, channelName, channelIndex, parentKey);
                    break;
                case XAnimationStateNodeKind.Selector:
                    XAnimationSelectorStateNodeConfig selector = nodeConfig.selector;
                    compiledNode = new XAnimationCompiledSelectorStateNode(
                        nodeConfig,
                        key,
                        channelName,
                        channelIndex,
                        parentKey,
                        parameterIndexByName[selector.parameterName]);
                    break;
                case XAnimationStateNodeKind.IntSelector:
                    XAnimationIntSelectorStateNodeConfig intSelector = nodeConfig.intSelector;
                    compiledNode = new XAnimationCompiledIntSelectorStateNode(
                        nodeConfig,
                        key,
                        channelName,
                        channelIndex,
                        parentKey,
                        parameterIndexByName[intSelector.parameterName],
                        BuildIntSelectorBranches(intSelector.branches, children));
                    break;
                case XAnimationStateNodeKind.StringSelector:
                    XAnimationStringSelectorStateNodeConfig stringSelector = nodeConfig.stringSelector;
                    compiledNode = new XAnimationCompiledStringSelectorStateNode(
                        nodeConfig,
                        key,
                        channelName,
                        channelIndex,
                        parentKey,
                        parameterIndexByName[stringSelector.parameterName],
                        BuildStringSelectorBranches(stringSelector.branches, children));
                    break;
                case XAnimationStateNodeKind.State:
                    compiledNode = CompileState(
                        nodeConfig,
                        key,
                        channelName,
                        channelIndex,
                        parentKey,
                        clipIndexByKey,
                        parameterIndexByName);
                    compiledStates.Add((XAnimationCompiledState)compiledNode);
                    break;
                default:
                    throw new XAnimationException($"XAnimation state node '{key}' has unsupported kind '{nodeConfig.kind}'.");
            }

            compiledNode.SetChildren(children);
            compiledStateNodes.Insert(nodeInsertIndex, compiledNode);
            stateNodeByScopeKey.Add(XAnimationCompiledAsset.BuildStateScopeKey(channelName, key), compiledNode);
            return compiledNode;
        }

        private static IReadOnlyDictionary<int, XAnimationCompiledStateNode> BuildIntSelectorBranches(
            IReadOnlyList<XAnimationIntSelectorBranchConfig> branches,
            IReadOnlyList<XAnimationCompiledStateNode> children)
        {
            Dictionary<string, XAnimationCompiledStateNode> childrenByName = BuildSelectorChildrenByName(children);
            Dictionary<int, XAnimationCompiledStateNode> result = new();
            for (int i = 0; i < branches.Count; i++)
            {
                XAnimationIntSelectorBranchConfig branch = branches[i];
                result.Add(branch.value, childrenByName[branch.childName]);
            }

            return result;
        }

        private static IReadOnlyDictionary<string, XAnimationCompiledStateNode> BuildStringSelectorBranches(
            IReadOnlyList<XAnimationStringSelectorBranchConfig> branches,
            IReadOnlyList<XAnimationCompiledStateNode> children)
        {
            Dictionary<string, XAnimationCompiledStateNode> childrenByName = BuildSelectorChildrenByName(children);
            Dictionary<string, XAnimationCompiledStateNode> result = new(StringComparer.Ordinal);
            for (int i = 0; i < branches.Count; i++)
            {
                XAnimationStringSelectorBranchConfig branch = branches[i];
                result.Add(branch.value, childrenByName[branch.childName]);
            }

            return result;
        }

        private static Dictionary<string, XAnimationCompiledStateNode> BuildSelectorChildrenByName(
            IReadOnlyList<XAnimationCompiledStateNode> children)
        {
            Dictionary<string, XAnimationCompiledStateNode> result = new(StringComparer.Ordinal);
            for (int i = 0; i < children.Count; i++)
            {
                result.Add(children[i].Name, children[i]);
            }

            return result;
        }

        private static XAnimationCompiledState CompileState(
            XAnimationStateNodeConfig nodeConfig,
            string key,
            string channelName,
            int channelIndex,
            string parentKey,
            IReadOnlyDictionary<string, int> clipIndexByKey,
            IReadOnlyDictionary<string, int> parameterIndexByName)
        {
            XAnimationStateConfig stateConfig = nodeConfig.state;
            return stateConfig.stateType switch
            {
                XAnimationStateType.Single => new XAnimationCompiledSingleState(
                    nodeConfig,
                    key,
                    channelName,
                    channelIndex,
                    parentKey,
                    clipIndexByKey[stateConfig.clipKey]),
                XAnimationStateType.Blend1D => CompileBlend1DState(
                    nodeConfig,
                    key,
                    channelName,
                    channelIndex,
                    parentKey,
                    clipIndexByKey,
                    parameterIndexByName),
                XAnimationStateType.Blend2DSimpleDirectional => CompileBlend2DSimpleDirectionalState(
                    nodeConfig,
                    key,
                    channelName,
                    channelIndex,
                    parentKey,
                    clipIndexByKey,
                    parameterIndexByName),
                XAnimationStateType.Blend2DFreeformDirectional => CompileBlend2DFreeformDirectionalState(
                    nodeConfig,
                    key,
                    channelName,
                    channelIndex,
                    parentKey,
                    clipIndexByKey,
                    parameterIndexByName),
                _ => throw new XAnimationException($"XAnimation state '{key}' has unsupported stateType '{stateConfig.stateType}'."),
            };
        }

        private static XAnimationCompiledBlend1DState CompileBlend1DState(
            XAnimationStateNodeConfig nodeConfig,
            string key,
            string channelName,
            int channelIndex,
            string parentKey,
            IReadOnlyDictionary<string, int> clipIndexByKey,
            IReadOnlyDictionary<string, int> parameterIndexByName)
        {
            XAnimationStateConfig stateConfig = nodeConfig.state;
            XAnimationBlend1DSampleConfig[] samples = stateConfig.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            XAnimationCompiledBlend1DSample[] compiledSamples = new XAnimationCompiledBlend1DSample[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                compiledSamples[i] = new XAnimationCompiledBlend1DSample(samples[i], clipIndexByKey[samples[i].clipKey]);
            }

            return new XAnimationCompiledBlend1DState(
                nodeConfig,
                key,
                channelName,
                channelIndex,
                parentKey,
                parameterIndexByName[stateConfig.parameterName],
                compiledSamples);
        }

        private static XAnimationCompiledBlend2DSimpleDirectionalState CompileBlend2DSimpleDirectionalState(
            XAnimationStateNodeConfig nodeConfig,
            string key,
            string channelName,
            int channelIndex,
            string parentKey,
            IReadOnlyDictionary<string, int> clipIndexByKey,
            IReadOnlyDictionary<string, int> parameterIndexByName)
        {
            XAnimationStateConfig stateConfig = nodeConfig.state;
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                stateConfig.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            XAnimationCompiledBlend2DSimpleDirectionalSample[] compiledSamples =
                new XAnimationCompiledBlend2DSimpleDirectionalSample[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                compiledSamples[i] = new XAnimationCompiledBlend2DSimpleDirectionalSample(
                    samples[i],
                    clipIndexByKey[samples[i].clipKey]);
            }

            return new XAnimationCompiledBlend2DSimpleDirectionalState(
                nodeConfig,
                key,
                channelName,
                channelIndex,
                parentKey,
                parameterIndexByName[stateConfig.parameterXName],
                parameterIndexByName[stateConfig.parameterYName],
                compiledSamples);
        }

        private static XAnimationCompiledBlend2DFreeformDirectionalState CompileBlend2DFreeformDirectionalState(
            XAnimationStateNodeConfig nodeConfig,
            string key,
            string channelName,
            int channelIndex,
            string parentKey,
            IReadOnlyDictionary<string, int> clipIndexByKey,
            IReadOnlyDictionary<string, int> parameterIndexByName)
        {
            XAnimationStateConfig stateConfig = nodeConfig.state;
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                stateConfig.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            XAnimationCompiledBlend2DSimpleDirectionalSample[] compiledSamples =
                new XAnimationCompiledBlend2DSimpleDirectionalSample[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                compiledSamples[i] = new XAnimationCompiledBlend2DSimpleDirectionalSample(
                    samples[i],
                    clipIndexByKey[samples[i].clipKey]);
            }

            return new XAnimationCompiledBlend2DFreeformDirectionalState(
                nodeConfig,
                key,
                channelName,
                channelIndex,
                parentKey,
                parameterIndexByName[stateConfig.parameterXName],
                parameterIndexByName[stateConfig.parameterYName],
                compiledSamples);
        }

        private XAnimationAsset LoadAsset(
            TextAsset textAsset,
            string assetPath,
            XAnimationLoadedAssetRegistry loadedAssets)
        {
            if (IsOverrideAssetText(textAsset.text))
            {
                return LoadOverrideAsset(textAsset, assetPath, loadedAssets);
            }

            XAnimationAsset asset = textAsset.ToXAnimationAsset<XAnimationAsset>();
            if (asset == null)
            {
                throw new XAnimationException($"Failed to deserialize XAnimation asset at '{assetPath}'.");
            }

            return asset;
        }

        private XAnimationAsset LoadOverrideAsset(
            TextAsset textAsset,
            string assetPath,
            XAnimationLoadedAssetRegistry loadedAssets)
        {
            XAnimationOverrideAsset overrideAsset = textAsset.ToXAnimationAsset<XAnimationOverrideAsset>();
            if (overrideAsset == null)
            {
                throw new XAnimationException($"Failed to deserialize XAnimation override asset at '{assetPath}'.");
            }

            ValidateOverrideAsset(overrideAsset, assetPath);

            TextAsset baseTextAsset = loadedAssets.Track(m_Resolver.LoadTextAsset(overrideAsset.baseAssetPath));
            if (baseTextAsset == null)
            {
                throw new XAnimationException($"XAnimation override '{assetPath}' base asset missing at '{overrideAsset.baseAssetPath}'.");
            }

            if (IsOverrideAssetText(baseTextAsset.text))
            {
                throw new XAnimationException($"XAnimation override '{assetPath}' baseAssetPath must reference a base XAnimationAsset, not another override asset.");
            }

            XAnimationAsset baseAsset = baseTextAsset.ToXAnimationAsset<XAnimationAsset>();
            if (baseAsset == null)
            {
                throw new XAnimationException($"Failed to deserialize XAnimation override base asset at '{overrideAsset.baseAssetPath}'.");
            }

            m_Validator.Validate(baseAsset);
            XAnimationAsset mergedAsset = CloneAsset(baseAsset);
            if (mergedAsset == null)
            {
                throw new XAnimationException($"Failed to clone XAnimation override base asset at '{overrideAsset.baseAssetPath}'.");
            }

            ApplyOverrideClips(mergedAsset, overrideAsset, assetPath);
            return mergedAsset;
        }

        private static bool IsOverrideAssetText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                JObject json = JObject.Parse(text);
                return json["baseAssetPath"] != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static XAnimationAsset CloneAsset(XAnimationAsset asset)
        {
            string json = JsonConvert.SerializeObject(asset);
            return JsonConvert.DeserializeObject<XAnimationAsset>(json);
        }

        private static void NormalizeAsset(XAnimationAsset asset)
        {
            asset.channels ??= Array.Empty<XAnimationChannelConfig>();
            asset.clips ??= Array.Empty<XAnimationClipConfig>();
            asset.parameters ??= Array.Empty<XAnimationParameterConfig>();
            asset.cues ??= Array.Empty<XAnimationCueConfig>();
            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channel = asset.channels[i];
                if (channel == null)
                {
                    continue;
                }

                channel.name = channel.name?.Trim();
                channel.stateNodes ??= Array.Empty<XAnimationStateNodeConfig>();
                channel.autoTransitions ??= Array.Empty<XAnimationAutoTransitionConfig>();
                channel.defaultTransitions ??= Array.Empty<XAnimationDefaultTransitionConfig>();
                NormalizeStateNodes(channel.stateNodes);
                NormalizeAutoTransitions(channel.autoTransitions);
                NormalizeDefaultTransitions(channel.defaultTransitions);
            }
        }

        private static void NormalizeStateNodes(IReadOnlyList<XAnimationStateNodeConfig> stateNodes)
        {
            for (int i = 0; i < stateNodes.Count; i++)
            {
                XAnimationStateNodeConfig node = stateNodes[i];
                if (node == null)
                {
                    continue;
                }

                node.name = node.name?.Trim();
                node.children ??= Array.Empty<XAnimationStateNodeConfig>();
                if (node.state != null)
                {
                    node.state.allowedNextStateKeys = NormalizeStateKeyList(node.state.allowedNextStateKeys);
                    node.state.allowedPreviousStateKeys = NormalizeStateKeyList(node.state.allowedPreviousStateKeys);
                    node.state.samples ??= Array.Empty<XAnimationBlend1DSampleConfig>();
                    node.state.directionalSamples ??= Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                    node.state.behaviors ??= Array.Empty<XAnimationStateBehavior>();
                }
                if (node.selector != null)
                {
                    node.selector.parameterName = node.selector.parameterName?.Trim();
                }
                if (node.intSelector != null)
                {
                    node.intSelector.parameterName = node.intSelector.parameterName?.Trim();
                    node.intSelector.branches ??= Array.Empty<XAnimationIntSelectorBranchConfig>();
                    for (int branchIndex = 0; branchIndex < node.intSelector.branches.Length; branchIndex++)
                    {
                        XAnimationIntSelectorBranchConfig branch = node.intSelector.branches[branchIndex];
                        if (branch != null)
                        {
                            branch.childName = branch.childName?.Trim();
                        }
                    }
                }
                if (node.stringSelector != null)
                {
                    node.stringSelector.parameterName = node.stringSelector.parameterName?.Trim();
                    node.stringSelector.branches ??= Array.Empty<XAnimationStringSelectorBranchConfig>();
                    for (int branchIndex = 0; branchIndex < node.stringSelector.branches.Length; branchIndex++)
                    {
                        XAnimationStringSelectorBranchConfig branch = node.stringSelector.branches[branchIndex];
                        if (branch != null)
                        {
                            branch.childName = branch.childName?.Trim();
                        }
                    }
                }
                NormalizeStateNodes(node.children);
            }
        }

        private static void NormalizeAutoTransitions(IReadOnlyList<XAnimationAutoTransitionConfig> transitions)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i];
                if (transition != null)
                {
                    transition.preStateKey = transition.preStateKey?.Trim();
                    transition.nextStateKey = string.IsNullOrWhiteSpace(transition.nextStateKey) ? string.Empty : transition.nextStateKey.Trim();
                }
            }
        }

        private static string[] NormalizeStateKeyList(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> normalized = new(values.Length);
            HashSet<string> unique = new(StringComparer.Ordinal);
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i]?.Trim();
                if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
                {
                    continue;
                }

                normalized.Add(value);
            }

            return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
        }

        private static void NormalizeDefaultTransitions(IReadOnlyList<XAnimationDefaultTransitionConfig> transitions)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                XAnimationDefaultTransitionConfig transition = transitions[i];
                if (transition != null)
                {
                    transition.preStateKey = transition.preStateKey?.Trim();
                    transition.nextStateKey = transition.nextStateKey?.Trim();
                }
            }
        }

        private static void ValidateOverrideAsset(XAnimationOverrideAsset overrideAsset, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(overrideAsset.baseAssetPath))
            {
                throw new XAnimationException($"XAnimation override '{assetPath}' baseAssetPath cannot be empty.");
            }

            HashSet<string> overrideKeys = new(StringComparer.Ordinal);
            XAnimationOverrideClipConfig[] clips = overrideAsset.clips ?? Array.Empty<XAnimationOverrideClipConfig>();
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationOverrideClipConfig clip = clips[i];
                if (clip == null)
                {
                    throw new XAnimationException($"XAnimation override '{assetPath}' clip config at index {i} is null.");
                }

                if (string.IsNullOrWhiteSpace(clip.key))
                {
                    throw new XAnimationException($"XAnimation override '{assetPath}' clip key at index {i} cannot be empty.");
                }

                if (!overrideKeys.Add(clip.key))
                {
                    throw new XAnimationException($"XAnimation override '{assetPath}' clip key '{clip.key}' is duplicated.");
                }

                if (string.IsNullOrWhiteSpace(clip.clipPath))
                {
                    throw new XAnimationException($"XAnimation override '{assetPath}' clip '{clip.key}' clipPath cannot be empty.");
                }
            }
        }

        private static void ApplyOverrideClips(
            XAnimationAsset baseAsset,
            XAnimationOverrideAsset overrideAsset,
            string assetPath)
        {
            Dictionary<string, XAnimationClipConfig> baseClipMap = new(StringComparer.Ordinal);
            for (int i = 0; i < baseAsset.clips.Length; i++)
            {
                XAnimationClipConfig clip = baseAsset.clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.key))
                {
                    baseClipMap[clip.key] = clip;
                }
            }

            XAnimationOverrideClipConfig[] overrideClips = overrideAsset.clips ?? Array.Empty<XAnimationOverrideClipConfig>();
            for (int i = 0; i < overrideClips.Length; i++)
            {
                XAnimationOverrideClipConfig overrideClip = overrideClips[i];
                if (!baseClipMap.TryGetValue(overrideClip.key, out XAnimationClipConfig baseClip))
                {
                    throw new XAnimationException($"XAnimation override '{assetPath}' clip '{overrideClip.key}' does not exist in base asset '{overrideAsset.baseAssetPath}'.");
                }

                baseClip.clipPath = overrideClip.clipPath;
            }
        }
    }
}
