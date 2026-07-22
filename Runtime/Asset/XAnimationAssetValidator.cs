using System;
using System.Collections.Generic;
using UnityEngine;

namespace XAnimationEngine
{
    public sealed class XAnimationAssetValidator
    {
        private sealed class StateEntry
        {
            internal string ChannelName;
            internal string Key;
            internal XAnimationStateConfig Config;
        }

        private sealed class NodeEntry
        {
            internal string ChannelName;
            internal string Key;
            internal XAnimationStateNodeConfig Config;
        }

        private sealed class StateValidationResult
        {
            internal readonly Dictionary<string, StateEntry> StateByScopeKey = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, NodeEntry> NodeByScopeKey = new(StringComparer.Ordinal);
        }

        public void Validate(XAnimationAsset asset)
        {
            if (asset == null)
            {
                throw new XAnimationException("XAnimation asset is null.");
            }

            ValidateChannels(asset.channels);
            ValidateClips(asset.clips);
            Dictionary<string, XAnimationParameterConfig> parameterMap = ValidateParameters(asset.parameters);
            StateValidationResult stateValidation = ValidateStateNodes(asset.channels, asset.clips, parameterMap);
            ValidateTransitions(asset.channels, stateValidation);
            ValidateCues(asset.clips, asset.cues);
        }

        private static void ValidateChannels(IReadOnlyList<XAnimationChannelConfig> channels)
        {
            if (channels == null || channels.Count == 0)
            {
                throw new XAnimationException("XAnimation asset must contain at least one channel.");
            }

            bool hasBaseChannel = false;
            HashSet<string> channelNames = new(StringComparer.Ordinal);
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannelConfig channel = channels[i] ?? throw new XAnimationException($"XAnimation channel config at index {i} is null.");
                if (string.IsNullOrWhiteSpace(channel.name))
                {
                    throw new XAnimationException("XAnimation channel name cannot be empty.");
                }
                if (!channelNames.Add(channel.name))
                {
                    throw new XAnimationException($"XAnimation channel '{channel.name}' is duplicated.");
                }
                if (channel.defaultWeight < 0f)
                {
                    throw new XAnimationException($"XAnimation channel '{channel.name}' has negative defaultWeight.");
                }
                if (channel.defaultFadeIn < 0f || channel.defaultFadeOut < 0f)
                {
                    throw new XAnimationException($"XAnimation channel '{channel.name}' has negative fade settings.");
                }
                hasBaseChannel |= channel.layerType == XAnimationChannelLayerType.Base;
            }

            if (!hasBaseChannel)
            {
                throw new XAnimationException("XAnimation asset must contain at least one Base channel.");
            }
        }

        private static void ValidateClips(IReadOnlyList<XAnimationClipConfig> clips)
        {
            HashSet<string> clipKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                XAnimationClipConfig clip = clips[i] ?? throw new XAnimationException($"XAnimation clip config at index {i} is null.");
                if (string.IsNullOrWhiteSpace(clip.key))
                {
                    throw new XAnimationException("XAnimation clip key cannot be empty.");
                }
                if (!clipKeys.Add(clip.key))
                {
                    throw new XAnimationException($"XAnimation clip '{clip.key}' is duplicated.");
                }
                if (string.IsNullOrWhiteSpace(clip.clipPath))
                {
                    throw new XAnimationException($"XAnimation clip '{clip.key}' has an empty clipPath.");
                }
            }
        }

        private static Dictionary<string, XAnimationParameterConfig> ValidateParameters(IReadOnlyList<XAnimationParameterConfig> parameters)
        {
            Dictionary<string, XAnimationParameterConfig> parameterMap = new(StringComparer.Ordinal);
            if (parameters == null)
            {
                return parameterMap;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                XAnimationParameterConfig parameter = parameters[i] ?? throw new XAnimationException($"XAnimation parameter config at index {i} is null.");
                if (string.IsNullOrWhiteSpace(parameter.name))
                {
                    throw new XAnimationException("XAnimation parameter name cannot be empty.");
                }
                if (!parameterMap.TryAdd(parameter.name, parameter))
                {
                    throw new XAnimationException($"XAnimation parameter '{parameter.name}' is duplicated.");
                }
            }

            return parameterMap;
        }

        private static StateValidationResult ValidateStateNodes(
            IReadOnlyList<XAnimationChannelConfig> channels,
            IReadOnlyList<XAnimationClipConfig> clips,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            Dictionary<string, XAnimationClipConfig> clipMap = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                clipMap.Add(clips[i].key, clips[i]);
            }

            StateValidationResult result = new();
            for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                XAnimationChannelConfig channel = channels[channelIndex];
                XAnimationStateNodeConfig[] rootNodes = channel.stateNodes ?? Array.Empty<XAnimationStateNodeConfig>();
                ValidateSiblingNodes(channel.name, string.Empty, rootNodes, clipMap, parameterMap, result);
            }

            foreach (NodeEntry node in result.NodeByScopeKey.Values)
            {
                switch (node.Config.kind)
                {
                    case XAnimationStateNodeKind.Selector:
                        ValidateSelector(node, node.Config.selector.parameterName, XAnimationParameterType.Int, parameterMap);
                        break;
                    case XAnimationStateNodeKind.IntSelector:
                        ValidateIntSelector(node, parameterMap);
                        break;
                    case XAnimationStateNodeKind.StringSelector:
                        ValidateStringSelector(node, parameterMap);
                        break;
                }
            }

            foreach (StateEntry state in result.StateByScopeKey.Values)
            {
                ValidateStateTransitionGates(state, result);
            }

            return result;
        }

        private static void ValidateSiblingNodes(
            string channelName,
            string parentKey,
            IReadOnlyList<XAnimationStateNodeConfig> nodes,
            IReadOnlyDictionary<string, XAnimationClipConfig> clipMap,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap,
            StateValidationResult result)
        {
            HashSet<string> siblingNames = new(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationStateNodeConfig node = nodes[i] ?? throw new XAnimationException($"XAnimation channel '{channelName}' contains a null state node at index {i}.");
                if (string.IsNullOrWhiteSpace(node.name))
                {
                    throw new XAnimationException($"XAnimation state node under '{FormatParent(channelName, parentKey)}' has an empty name.");
                }
                if (node.name.Contains('/'))
                {
                    throw new XAnimationException($"XAnimation state node name '{node.name}' must be one path segment and cannot contain '/'.");
                }
                if (!siblingNames.Add(node.name))
                {
                    throw new XAnimationException($"XAnimation state node name '{node.name}' is duplicated under '{FormatParent(channelName, parentKey)}'.");
                }

                string key = XAnimationStatePathUtility.BuildPath(parentKey, node.name);
                string scopeKey = XAnimationCompiledAsset.BuildStateScopeKey(channelName, key);
                result.NodeByScopeKey.Add(scopeKey, new NodeEntry { ChannelName = channelName, Key = key, Config = node });

                XAnimationStateNodeConfig[] children = node.children ?? Array.Empty<XAnimationStateNodeConfig>();
                switch (node.kind)
                {
                    case XAnimationStateNodeKind.Normal:
                        if (node.state != null || node.selector != null || node.intSelector != null || node.stringSelector != null)
                        {
                            throw new XAnimationException($"XAnimation Normal state node '{key}' cannot contain state or selector payload.");
                        }
                        break;
                    case XAnimationStateNodeKind.Selector:
                        if (node.state != null || node.selector == null || node.intSelector != null || node.stringSelector != null)
                        {
                            throw new XAnimationException($"XAnimation Selector state node '{key}' must contain only selector payload.");
                        }
                        break;
                    case XAnimationStateNodeKind.IntSelector:
                        if (node.state != null || node.selector != null || node.intSelector == null || node.stringSelector != null)
                        {
                            throw new XAnimationException($"XAnimation Int Selector state node '{key}' must contain only intSelector payload.");
                        }
                        break;
                    case XAnimationStateNodeKind.StringSelector:
                        if (node.state != null || node.selector != null || node.intSelector != null || node.stringSelector == null)
                        {
                            throw new XAnimationException($"XAnimation String Selector state node '{key}' must contain only stringSelector payload.");
                        }
                        break;
                    case XAnimationStateNodeKind.State:
                        if (node.state == null || node.selector != null || node.intSelector != null || node.stringSelector != null)
                        {
                            throw new XAnimationException($"XAnimation State node '{key}' must contain only state payload.");
                        }
                        if (children.Length != 0)
                        {
                            throw new XAnimationException($"XAnimation State node '{key}' cannot contain child nodes.");
                        }
                        ValidateState(channelName, key, node.state, clipMap, parameterMap);
                        result.StateByScopeKey.Add(scopeKey, new StateEntry { ChannelName = channelName, Key = key, Config = node.state });
                        break;
                    default:
                        throw new XAnimationException($"XAnimation state node '{key}' has unsupported kind '{node.kind}'.");
                }

                if (children.Length > 0)
                {
                    ValidateSiblingNodes(channelName, key, children, clipMap, parameterMap, result);
                }
            }
        }

        private static void ValidateSelector(
            NodeEntry node,
            string parameterName,
            XAnimationParameterType parameterType,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new XAnimationException($"XAnimation Selector state node '{node.Key}' has an empty parameterName.");
            }
            if (!parameterMap.TryGetValue(parameterName, out XAnimationParameterConfig parameter))
            {
                throw new XAnimationException($"XAnimation Selector state node '{node.Key}' references unknown parameter '{parameterName}'.");
            }
            if (parameter.type != parameterType)
            {
                throw new XAnimationException($"XAnimation Selector state node '{node.Key}' parameter '{parameterName}' must be {parameterType}.");
            }

            for (int i = 0; i < node.Config.children.Length; i++)
            {
                XAnimationStateNodeConfig child = node.Config.children[i];
                if (child.kind == XAnimationStateNodeKind.Normal)
                {
                    throw new XAnimationException($"XAnimation Selector state node '{node.Key}' child '{child.name}' must be State or a Selector.");
                }
            }
        }

        private static void ValidateIntSelector(
            NodeEntry node,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            XAnimationIntSelectorStateNodeConfig selector = node.Config.intSelector;
            ValidateSelector(node, selector.parameterName, XAnimationParameterType.Int, parameterMap);

            XAnimationIntSelectorBranchConfig[] branches = selector.branches ?? Array.Empty<XAnimationIntSelectorBranchConfig>();
            HashSet<string> childNames = new(StringComparer.Ordinal);
            HashSet<int> values = new();
            for (int i = 0; i < branches.Length; i++)
            {
                XAnimationIntSelectorBranchConfig branch = branches[i] ??
                    throw new XAnimationException($"XAnimation Int Selector state node '{node.Key}' contains a null branch at index {i}.");
                ValidateSelectorBranchChild(node, branch.childName, childNames);
                if (!values.Add(branch.value))
                {
                    throw new XAnimationException($"XAnimation Int Selector state node '{node.Key}' value '{branch.value}' is duplicated.");
                }
            }

            ValidateSelectorBranchCoverage(node, childNames);
        }

        private static void ValidateStringSelector(
            NodeEntry node,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            XAnimationStringSelectorStateNodeConfig selector = node.Config.stringSelector;
            ValidateSelector(node, selector.parameterName, XAnimationParameterType.String, parameterMap);

            XAnimationStringSelectorBranchConfig[] branches = selector.branches ?? Array.Empty<XAnimationStringSelectorBranchConfig>();
            HashSet<string> childNames = new(StringComparer.Ordinal);
            HashSet<string> values = new(StringComparer.Ordinal);
            for (int i = 0; i < branches.Length; i++)
            {
                XAnimationStringSelectorBranchConfig branch = branches[i] ??
                    throw new XAnimationException($"XAnimation String Selector state node '{node.Key}' contains a null branch at index {i}.");
                ValidateSelectorBranchChild(node, branch.childName, childNames);
                if (branch.value == null)
                {
                    throw new XAnimationException($"XAnimation String Selector state node '{node.Key}' child '{branch.childName}' has a null value.");
                }
                if (!values.Add(branch.value))
                {
                    throw new XAnimationException($"XAnimation String Selector state node '{node.Key}' value '{branch.value}' is duplicated.");
                }
            }

            ValidateSelectorBranchCoverage(node, childNames);
        }

        private static void ValidateSelectorBranchChild(NodeEntry node, string childName, HashSet<string> childNames)
        {
            if (!childNames.Add(childName))
            {
                throw new XAnimationException($"XAnimation Selector state node '{node.Key}' child mapping '{childName}' is duplicated.");
            }

            for (int i = 0; i < node.Config.children.Length; i++)
            {
                if (string.Equals(node.Config.children[i].name, childName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new XAnimationException($"XAnimation Selector state node '{node.Key}' maps unknown child '{childName}'.");
        }

        private static void ValidateSelectorBranchCoverage(NodeEntry node, HashSet<string> childNames)
        {
            for (int i = 0; i < node.Config.children.Length; i++)
            {
                string childName = node.Config.children[i].name;
                if (!childNames.Contains(childName))
                {
                    throw new XAnimationException($"XAnimation Selector state node '{node.Key}' child '{childName}' has no value mapping.");
                }
            }
        }

        private static void ValidateState(
            string channelName,
            string stateKey,
            XAnimationStateConfig state,
            IReadOnlyDictionary<string, XAnimationClipConfig> clipMap,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            switch (state.stateType)
            {
                case XAnimationStateType.Single:
                    if (string.IsNullOrWhiteSpace(state.clipKey))
                    {
                        throw new XAnimationException($"XAnimation Single state '{stateKey}' has an empty clipKey.");
                    }
                    if (!clipMap.ContainsKey(state.clipKey))
                    {
                        throw new XAnimationException($"XAnimation Single state '{stateKey}' references unknown clip '{state.clipKey}'.");
                    }
                    break;
                case XAnimationStateType.Blend1D:
                    ValidateFloatParameter(stateKey, state.parameterName, "parameterName", parameterMap);
                    ValidateBlend1DSamples(stateKey, state.samples, clipMap);
                    break;
                case XAnimationStateType.Blend2DSimpleDirectional:
                    ValidateDirectionalParameters(stateKey, state, parameterMap);
                    ValidateDirectionalSamples(stateKey, state.directionalSamples, clipMap, false);
                    break;
                case XAnimationStateType.Blend2DFreeformDirectional:
                    ValidateDirectionalParameters(stateKey, state, parameterMap);
                    ValidateDirectionalSamples(stateKey, state.directionalSamples, clipMap, true);
                    break;
                default:
                    throw new XAnimationException($"XAnimation state '{stateKey}' in channel '{channelName}' has unsupported stateType '{state.stateType}'.");
            }
        }

        private static void ValidateFloatParameter(
            string stateKey,
            string parameterName,
            string fieldName,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' has an empty {fieldName}.");
            }
            if (!parameterMap.TryGetValue(parameterName, out XAnimationParameterConfig parameter))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' references unknown parameter '{parameterName}'.");
            }
            if (parameter.type != XAnimationParameterType.Float)
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' parameter '{parameterName}' must be Float.");
            }
        }

        private static void ValidateDirectionalParameters(
            string stateKey,
            XAnimationStateConfig state,
            IReadOnlyDictionary<string, XAnimationParameterConfig> parameterMap)
        {
            ValidateFloatParameter(stateKey, state.parameterXName, "parameterXName", parameterMap);
            ValidateFloatParameter(stateKey, state.parameterYName, "parameterYName", parameterMap);
        }

        private static void ValidateBlend1DSamples(
            string stateKey,
            IReadOnlyList<XAnimationBlend1DSampleConfig> samples,
            IReadOnlyDictionary<string, XAnimationClipConfig> clipMap)
        {
            if (samples == null || samples.Count < 2)
            {
                throw new XAnimationException($"XAnimation Blend1D state '{stateKey}' must contain at least two samples.");
            }

            float previousThreshold = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                XAnimationBlend1DSampleConfig sample = samples[i] ?? throw new XAnimationException($"XAnimation Blend1D state '{stateKey}' sample at index {i} is null.");
                ValidateSampleClip(stateKey, sample.clipKey, clipMap);
                if (i > 0 && sample.threshold <= previousThreshold)
                {
                    throw new XAnimationException($"XAnimation Blend1D state '{stateKey}' sample thresholds must be strictly increasing.");
                }
                previousThreshold = sample.threshold;
            }
        }

        private static void ValidateDirectionalSamples(
            string stateKey,
            IReadOnlyList<XAnimationBlend2DSimpleDirectionalSampleConfig> samples,
            IReadOnlyDictionary<string, XAnimationClipConfig> clipMap,
            bool requireSingleIdle)
        {
            if (samples == null || samples.Count < 2)
            {
                throw new XAnimationException($"XAnimation directional state '{stateKey}' must contain at least two samples.");
            }

            HashSet<string> positions = new(StringComparer.Ordinal);
            int idleCount = 0;
            bool hasDirection = false;
            for (int i = 0; i < samples.Count; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i] ?? throw new XAnimationException($"XAnimation directional state '{stateKey}' sample at index {i} is null.");
                ValidateSampleClip(stateKey, sample.clipKey, clipMap);
                string position = $"{sample.positionX:R},{sample.positionY:R}";
                if (!positions.Add(position))
                {
                    throw new XAnimationException($"XAnimation directional state '{stateKey}' contains duplicated sample position ({sample.positionX}, {sample.positionY}).");
                }
                if (Mathf.Approximately(sample.positionX, 0f) && Mathf.Approximately(sample.positionY, 0f))
                {
                    idleCount++;
                }
                else
                {
                    hasDirection = true;
                }
            }

            if (!hasDirection)
            {
                throw new XAnimationException($"XAnimation directional state '{stateKey}' must contain at least one non-zero directional sample.");
            }
            if (requireSingleIdle && idleCount != 1)
            {
                throw new XAnimationException($"XAnimation Blend2DFreeformDirectional state '{stateKey}' must contain exactly one idle sample at (0, 0).");
            }
        }

        private static void ValidateSampleClip(
            string stateKey,
            string clipKey,
            IReadOnlyDictionary<string, XAnimationClipConfig> clipMap)
        {
            if (string.IsNullOrWhiteSpace(clipKey))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' sample has an empty clipKey.");
            }
            if (!clipMap.ContainsKey(clipKey))
            {
                throw new XAnimationException($"XAnimation state '{stateKey}' sample references unknown clip '{clipKey}'.");
            }
        }

        private static void ValidateStateTransitionGates(StateEntry state, StateValidationResult result)
        {
            ValidateStateKeyList(state, state.Config.allowedNextStateKeys, result, true, "allowedNextStateKeys");
            ValidateStateKeyList(state, state.Config.allowedPreviousStateKeys, result, false, "allowedPreviousStateKeys");
        }

        private static void ValidateStateKeyList(
            StateEntry state,
            IReadOnlyList<string> values,
            StateValidationResult result,
            bool allowPlayableNode,
            string fieldName)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                string candidate = values[i];
                if (string.Equals(state.Key, candidate, StringComparison.Ordinal))
                {
                    throw new XAnimationException($"XAnimation state '{state.Key}' cannot include itself in {fieldName}.");
                }

                string scopeKey = XAnimationCompiledAsset.BuildStateScopeKey(state.ChannelName, candidate);
                if (allowPlayableNode)
                {
                    if (!result.NodeByScopeKey.TryGetValue(scopeKey, out NodeEntry node) || node.Config.kind == XAnimationStateNodeKind.Normal)
                    {
                        throw new XAnimationException($"XAnimation state '{state.Key}' {fieldName} references unknown or non-playable state node '{candidate}' in channel '{state.ChannelName}'.");
                    }
                }
                else if (!result.StateByScopeKey.ContainsKey(scopeKey))
                {
                    throw new XAnimationException($"XAnimation state '{state.Key}' {fieldName} references unknown State node '{candidate}' in channel '{state.ChannelName}'.");
                }
            }
        }

        private static void ValidateTransitions(IReadOnlyList<XAnimationChannelConfig> channels, StateValidationResult result)
        {
            for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
            {
                XAnimationChannelConfig channel = channels[channelIndex];
                ValidateAutoTransitions(channel.name, channel.autoTransitions, result);
                ValidateDefaultTransitions(channel.name, channel.defaultTransitions, result);
            }
        }

        private static void ValidateAutoTransitions(
            string channelName,
            IReadOnlyList<XAnimationAutoTransitionConfig> transitions,
            StateValidationResult result)
        {
            HashSet<string> preStateKeys = new(StringComparer.Ordinal);
            transitions ??= Array.Empty<XAnimationAutoTransitionConfig>();
            for (int i = 0; i < transitions.Count; i++)
            {
                XAnimationAutoTransitionConfig transition = transitions[i] ?? throw new XAnimationException($"XAnimation channel '{channelName}' auto transition at index {i} is null.");
                string preScopeKey = XAnimationCompiledAsset.BuildStateScopeKey(channelName, transition.preStateKey);
                if (!result.StateByScopeKey.TryGetValue(preScopeKey, out StateEntry preState))
                {
                    throw new XAnimationException($"XAnimation auto transition references unknown preStateKey '{transition.preStateKey}' in channel '{channelName}'.");
                }
                if (!preStateKeys.Add(transition.preStateKey))
                {
                    throw new XAnimationException($"XAnimation auto transition preState '{transition.preStateKey}' in channel '{channelName}' is duplicated.");
                }
                if (preState.Config.loop)
                {
                    throw new XAnimationException($"XAnimation state '{preState.Key}' is looping and cannot configure auto transition.");
                }
                if (transition.exitTime < 0f || transition.exitTime > 1f || transition.enterTime < 0f || transition.enterTime > 1f)
                {
                    throw new XAnimationException($"XAnimation auto transition '{transition.preStateKey}' ExitTime and EnterTime must be within [0, 1].");
                }
                if (transition.transitionDuration < 0f)
                {
                    throw new XAnimationException($"XAnimation auto transition '{transition.preStateKey}' TransitionDuration cannot be negative.");
                }
                if (!string.IsNullOrWhiteSpace(transition.nextStateKey))
                {
                    ValidatePlayableStateTarget(channelName, transition.nextStateKey, result, $"auto transition '{transition.preStateKey}'");
                }
            }
        }

        private static void ValidateDefaultTransitions(
            string channelName,
            IReadOnlyList<XAnimationDefaultTransitionConfig> transitions,
            StateValidationResult result)
        {
            HashSet<string> pairKeys = new(StringComparer.Ordinal);
            transitions ??= Array.Empty<XAnimationDefaultTransitionConfig>();
            for (int i = 0; i < transitions.Count; i++)
            {
                XAnimationDefaultTransitionConfig transition = transitions[i] ?? throw new XAnimationException($"XAnimation channel '{channelName}' default transition at index {i} is null.");
                if (transition.fadeIn < 0f || transition.fadeOut < 0f || transition.enterTime < 0f || transition.enterTime > 1f)
                {
                    throw new XAnimationException($"XAnimation default transition '{transition.preStateKey} -> {transition.nextStateKey}' has invalid fade or enter time.");
                }
                string preScopeKey = XAnimationCompiledAsset.BuildStateScopeKey(channelName, transition.preStateKey);
                if (!result.StateByScopeKey.ContainsKey(preScopeKey))
                {
                    throw new XAnimationException($"XAnimation default transition references unknown preStateKey '{transition.preStateKey}' in channel '{channelName}'.");
                }
                ValidateStateTarget(channelName, transition.nextStateKey, result, $"default transition '{transition.preStateKey}'");
                string pairKey = XAnimationCompiledAsset.BuildTransitionPairKey(channelName, transition.preStateKey, transition.nextStateKey);
                if (!pairKeys.Add(pairKey))
                {
                    throw new XAnimationException($"XAnimation default transition pair '{channelName}: {transition.preStateKey}' -> '{transition.nextStateKey}' is duplicated.");
                }
            }
        }

        private static void ValidatePlayableStateTarget(
            string channelName,
            string targetKey,
            StateValidationResult result,
            string owner)
        {
            string targetScopeKey = XAnimationCompiledAsset.BuildStateScopeKey(channelName, targetKey);
            if (!result.NodeByScopeKey.TryGetValue(targetScopeKey, out NodeEntry target) ||
                target.Config.kind == XAnimationStateNodeKind.Normal)
            {
                throw new XAnimationException(
                    $"XAnimation {owner} references unknown or non-playable nextStateKey '{targetKey}' in channel '{channelName}'.");
            }
        }

        private static void ValidateStateTarget(
            string channelName,
            string targetKey,
            StateValidationResult result,
            string owner)
        {
            string targetScopeKey = XAnimationCompiledAsset.BuildStateScopeKey(channelName, targetKey);
            if (!result.StateByScopeKey.ContainsKey(targetScopeKey))
            {
                throw new XAnimationException($"XAnimation {owner} references unknown State nextStateKey '{targetKey}' in channel '{channelName}'.");
            }
        }

        private static void ValidateCues(IReadOnlyList<XAnimationClipConfig> clips, IReadOnlyList<XAnimationCueConfig> cues)
        {
            if (cues == null)
            {
                return;
            }

            HashSet<string> clipKeys = new(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                clipKeys.Add(clips[i].key);
            }

            for (int i = 0; i < cues.Count; i++)
            {
                XAnimationCueConfig cue = cues[i] ?? throw new XAnimationException($"XAnimation cue config at index {i} is null.");
                if (!clipKeys.Contains(cue.clipKey))
                {
                    throw new XAnimationException($"XAnimation cue references unknown clip '{cue.clipKey}'.");
                }
                if (cue.time < 0f || cue.time > 1f)
                {
                    throw new XAnimationException($"XAnimation cue '{cue.eventKey}' on clip '{cue.clipKey}' has time outside [0, 1].");
                }
                if (string.IsNullOrWhiteSpace(cue.eventKey))
                {
                    throw new XAnimationException($"XAnimation cue on clip '{cue.clipKey}' has an empty eventKey.");
                }
            }
        }

        private static string FormatParent(string channelName, string parentKey)
        {
            return string.IsNullOrWhiteSpace(parentKey) ? channelName : $"{channelName}:{parentKey}";
        }
    }
}
