using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationPlaybackController
    {
        private const string TemporaryClipStateKeyPrefix = "__xanimation_temp_clip_state:";
        private const string DirectClipKeyPrefix = "__xanimation_direct_clip:";

        private readonly XAnimationRuntime m_Runtime;
        private readonly Dictionary<string, string> m_PendingSelectorStateKeyByChannel = new(StringComparer.Ordinal);
        private int m_NextTemporaryStateId = 1;

        private sealed class StateNodeResolution
        {
            internal StateNodeResolution(
                XAnimationCompiledStateNode requestedNode,
                XAnimationCompiledState state,
                XAnimationCompiledStateNode[] activeNodes)
            {
                RequestedNode = requestedNode;
                State = state;
                ActiveNodes = activeNodes;
                ActiveNodeKeys = new string[activeNodes.Length];
                for (int i = 0; i < activeNodes.Length; i++)
                {
                    ActiveNodeKeys[i] = activeNodes[i].Key;
                }
            }

            internal XAnimationCompiledStateNode RequestedNode { get; }
            internal XAnimationCompiledState State { get; }
            internal XAnimationCompiledStateNode[] ActiveNodes { get; }
            internal string[] ActiveNodeKeys { get; }
        }

        internal XAnimationPlaybackController(XAnimationRuntime runtime)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        private XAnimationCompiledAsset CompiledAsset => m_Runtime.CompiledAsset;
        private Animator Animator => m_Runtime.Animator;
        private PlayableGraph Graph => m_Runtime.Graph;

        internal void Reset()
        {
            m_NextTemporaryStateId = 1;
            m_PendingSelectorStateKeyByChannel.Clear();
        }

        internal XAnimationPlaybackStartInfo StartClipPlayback(string clipKey, string channelName, XAnimationTransitionOptions transition = default)
        {
            if (!CompiledAsset.TryGetClipIndex(clipKey, out int clipIndex))
            {
                throw new XAnimationException($"XAnimation clip '{clipKey}' does not exist.");
            }

            XAnimationCompiledClip clip = (XAnimationCompiledClip)CompiledAsset.Clips[clipIndex];
            XAnimationCompiledChannel channel = ResolveClipChannel(clip, channelName);
            if (!CompiledAsset.TryGetChannelIndex(channel.Name, out int channelIndex))
            {
                throw new XAnimationException($"XAnimation channel '{channel.Name}' does not exist.");
            }

            XAnimationCompiledSingleState temporaryState = CreateTemporaryClipState(clip, clipIndex, channel, channelIndex);
            StateNodeResolution resolution = CreateLeafResolution(temporaryState);
            XAnimationTransitionRequest request = BuildTransitionRequest(resolution, channel, transition, XAnimationTransitionRequestSource.ExplicitPlay);
            return TryPlayCompiledState(resolution, channel, request);
        }

        internal XAnimationPlaybackStartInfo StartClipPlayback(AnimationClip animationClip, string channelName, XAnimationTransitionOptions transition = default)
        {
            if (animationClip == null)
            {
                throw new XAnimationException("XAnimation direct AnimationClip cannot be null.");
            }

            string clipKey = CreateDirectClipKey(animationClip);
            XAnimationCompiledClip clip = new(new XAnimationClipConfig
            {
                key = clipKey,
                clipPath = string.Empty,
            }, animationClip);
            m_Runtime.RegisterDirectClipCues(clip.Key, clip.AnimationEventCues);
            m_Runtime.EnsureCuePlayable();
            XAnimationCompiledChannel channel = ResolveClipChannel(clip, channelName);
            if (!CompiledAsset.TryGetChannelIndex(channel.Name, out int channelIndex))
            {
                throw new XAnimationException($"XAnimation channel '{channel.Name}' does not exist.");
            }

            XAnimationCompiledSingleState temporaryState = CreateTemporaryDirectClipState(clip, channel, channelIndex);
            StateNodeResolution resolution = CreateLeafResolution(temporaryState);
            XAnimationTransitionRequest request = BuildTransitionRequest(resolution, channel, transition, XAnimationTransitionRequestSource.ExplicitPlay);
            return TryPlayCompiledState(resolution, channel, request);
        }

        internal XAnimationPlaybackStartInfo StartStatePlayback(string stateKey, XAnimationTransitionOptions transition, bool force)
        {
            return StartStatePlayback(CompiledAsset.GetStateNode(stateKey), transition, force);
        }

        internal XAnimationPlaybackStartInfo StartStatePlayback(string channelName, string stateKey, XAnimationTransitionOptions transition, bool force)
        {
            return StartStatePlayback(CompiledAsset.GetStateNode(channelName, stateKey), transition, force);
        }

        private XAnimationPlaybackStartInfo StartStatePlayback(XAnimationCompiledStateNode requestedNode, XAnimationTransitionOptions transition, bool force)
        {
            if (!TryResolveStateNode(requestedNode, out StateNodeResolution resolution))
            {
                return XAnimationPlaybackStartInfo.CreateFailed(
                    requestedNode.ChannelName,
                    requestedNode.Key,
                    string.Empty,
                    false,
                    XAnimationTransitionRejectReason.None);
            }

            return StartStatePlayback(resolution, transition, force);
        }

        private XAnimationPlaybackStartInfo StartStatePlayback(StateNodeResolution resolution, XAnimationTransitionOptions transition, bool force)
        {
            XAnimationCompiledState state = resolution.State;
            XAnimationCompiledChannel channel = GetStateChannel(state);
            if (!force &&
                !CanTransitionFromCurrentPlayback(m_Runtime.GetChannel(channel.Name), resolution, out XAnimationTransitionRejectReason gateRejectReason))
            {
                string clipKey = state is XAnimationCompiledSingleState singleState
                    ? ResolveSingleStateClip(singleState).Key
                    : string.Empty;
                return XAnimationPlaybackStartInfo.CreateFailed(channel.Name, state.Key, clipKey, IsTemporaryClipState(state.Key), gateRejectReason);
            }

            XAnimationTransitionRequest request = BuildTransitionRequest(resolution, channel, transition, transition != null ? XAnimationTransitionRequestSource.ExplicitPlay : ResolveRequestSource(resolution, channel), force);
            return TryPlayCompiledState(resolution, channel, request);
        }

        internal void ProcessSelectorParameterChange(string parameterName)
        {
            IReadOnlyList<XAnimationChannel> channels = m_Runtime.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannel channel = channels[i];
                if (!channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance playback) ||
                    playback == null ||
                    playback.IsTemporaryState ||
                    !ActiveChainUsesSelectorParameter(channel.Name, playback.ActiveStateNodeKeys, parameterName))
                {
                    continue;
                }

                ReselectActiveStateNode(channel, playback);
            }
        }

        internal void ProcessCompletedNonLoopPlayback()
        {
            ProcessPendingSelectorTransitions();
            IReadOnlyList<XAnimationChannel> channels = m_Runtime.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannel channel = channels[i];
                if (channel.IsPaused ||
                    !channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance playback) ||
                    playback == null ||
                    playback.IsLooping ||
                    playback.IsTemporaryState ||
                    playback.HasCompletedExitOrTransition ||
                    !CompiledAsset.TryGetStateIndex(channel.Name, playback.StateKey, out int stateIndex))
                {
                    continue;
                }

                XAnimationCompiledState state = (XAnimationCompiledState)CompiledAsset.States[stateIndex];
                bool hasAutoTransition = CompiledAsset.TryGetAutoTransition(channel.Name, state.Key, out XAnimationCompiledAutoTransition autoTransition);
                float exitThreshold = hasAutoTransition && autoTransition.HasNextState
                    ? autoTransition.ExitTime
                    : 1f;
                if (playback.GetTotalNormalizedTime() < exitThreshold)
                {
                    continue;
                }

                if (!hasAutoTransition || !autoTransition.HasNextState)
                {
                    if (channel.TryMarkCompletedExit(out _))
                    {
                        m_Runtime.StopChannel(channel, channel.CompiledChannel.Config.defaultFadeOut);
                    }

                    continue;
                }

                StateNodeResolution nextResolution = CreateLeafResolution(
                    CompiledAsset.GetState(channel.Name, autoTransition.NextStateKey));
                XAnimationCompiledState nextState = nextResolution.State;
                float fadeIn = autoTransition.TransitionDuration > 0f ? autoTransition.TransitionDuration : channel.CompiledChannel.Config.defaultFadeIn;
                float fadeOut = autoTransition.TransitionDuration > 0f ? autoTransition.TransitionDuration : channel.CompiledChannel.Config.defaultFadeOut;
                XAnimationTransitionRequest request = new(channel.Name, nextState.Key, nextState is XAnimationCompiledSingleState singleState ? ResolveSingleStateClip(singleState).Key : string.Empty,
                    XAnimationTransitionRequestSource.AutoTransition, fadeIn, fadeOut, autoTransition.EnterTime, playback.Priority, true, false);

                if (!CanTransitionFromCurrentPlayback(channel, nextResolution, out _))
                {
                    continue;
                }

                TryPlayCompiledState(nextResolution, channel.CompiledChannel, request);
            }
        }

        private XAnimationPlaybackStartInfo TryPlayCompiledState(StateNodeResolution resolution, XAnimationCompiledChannel channel, XAnimationTransitionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            XAnimationCompiledState state = resolution.State;
            if (!m_Runtime.TryPlay(m_Runtime.GetChannel(channel.Name), (playbackId, actualOptions) =>
                {
                    XAnimationStatePlaybackInstance createdPlayback = CreateStatePlayback(playbackId, channel.Name, state, actualOptions);
                    createdPlayback.SetStateNodeContext(resolution.RequestedNode.Key, resolution.ActiveNodeKeys);
                    return createdPlayback;
                },
                    request, out XAnimationStatePlaybackInstance playback, out XAnimationTransitionRejectReason rejectReason))
            {
                string clipKey = state switch
                {
                    XAnimationCompiledSingleState singleState => ResolveSingleStateClip(singleState).Key,
                    _ => string.Empty,
                };
                return XAnimationPlaybackStartInfo.CreateFailed(channel.Name, state.Key, clipKey, IsTemporaryClipState(state.Key), rejectReason);
            }

            m_PendingSelectorStateKeyByChannel.Remove(channel.Name);
            return XAnimationPlaybackStartInfo.CreateStarted(playback);
        }

        private XAnimationTransitionRequest BuildTransitionRequest(StateNodeResolution resolution, XAnimationCompiledChannel channel, XAnimationTransitionOptions transition, XAnimationTransitionRequestSource explicitSource, bool force = false)
        {
            XAnimationCompiledState state = resolution.State;
            XAnimationTransitionOptions resolvedTransition = ResolveTransitionOptions(resolution, channel, transition);
            float fadeIn = resolvedTransition.fadeIn > 0f ? resolvedTransition.fadeIn : channel.Config.defaultFadeIn;
            float fadeOut = resolvedTransition.fadeOut > 0f ? resolvedTransition.fadeOut : channel.Config.defaultFadeOut;
            string clipKey = state is XAnimationCompiledSingleState singleState
                ? ResolveSingleStateClip(singleState).Key
                : string.Empty;
            XAnimationTransitionRequestSource source = transition != null ? explicitSource : ResolveRequestSource(resolution, channel);

            return new XAnimationTransitionRequest(channel.Name, state.Key, clipKey, source, fadeIn, fadeOut, resolvedTransition.enterTime, resolvedTransition.priority,
                resolvedTransition.interruptible, force);
        }

        private XAnimationTransitionOptions ResolveTransitionOptions(StateNodeResolution resolution, XAnimationCompiledChannel channel, XAnimationTransitionOptions transition)
        {
            if (transition != null)
            {
                return transition;
            }

            XAnimationCompiledState state = resolution.State;
            if (!IsTemporaryClipState(state.Key) &&
                m_Runtime.TryGetChannel(channel.Name, out XAnimationChannel runtimeChannel) &&
                runtimeChannel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) &&
                currentPlayback != null &&
                !currentPlayback.IsTemporaryState &&
                TryGetDefaultTransition(channel.Name, currentPlayback.StateKey, resolution.State, out XAnimationCompiledDefaultTransition defaultTransition))
            {
                return defaultTransition.CreateTransitionOptions();
            }

            return new XAnimationTransitionOptions();
        }

        private XAnimationTransitionRequestSource ResolveRequestSource(StateNodeResolution resolution, XAnimationCompiledChannel channel)
        {
            XAnimationCompiledState state = resolution.State;
            if (IsTemporaryClipState(state.Key))
            {
                return XAnimationTransitionRequestSource.ExplicitPlay;
            }

            if (m_Runtime.TryGetChannel(channel.Name, out XAnimationChannel runtimeChannel) &&
                runtimeChannel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) &&
                currentPlayback != null &&
                !currentPlayback.IsTemporaryState &&
                TryGetDefaultTransition(channel.Name, currentPlayback.StateKey, resolution.State, out _))
            {
                return XAnimationTransitionRequestSource.DefaultTransition;
            }

            return XAnimationTransitionRequestSource.ExplicitPlay;
        }

        private bool CanTransitionFromCurrentPlayback(XAnimationChannel channel, StateNodeResolution target, out XAnimationTransitionRejectReason rejectReason)
        {
            rejectReason = XAnimationTransitionRejectReason.None;
            if (channel == null ||
                !channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) ||
                currentPlayback == null)
            {
                return true;
            }

            string[] allowedNext = GetAllowedNextStateKeys(channel.Name, currentPlayback);
            if (allowedNext.Length > 0 && !ContainsAnyNodeKey(allowedNext, target.ActiveNodeKeys))
            {
                rejectReason = XAnimationTransitionRejectReason.SourceStateDisallowTarget;
                return false;
            }

            string[] allowedPrevious = target.State.Config.allowedPreviousStateKeys ?? Array.Empty<string>();
            if (allowedPrevious.Length > 0 && Array.IndexOf(allowedPrevious, currentPlayback.StateKey) < 0)
            {
                rejectReason = XAnimationTransitionRejectReason.TargetStateDisallowSource;
                return false;
            }

            return true;
        }

        private string[] GetAllowedNextStateKeys(string channelName, XAnimationStatePlaybackInstance playback)
        {
            if (string.IsNullOrWhiteSpace(channelName) || playback == null)
            {
                return Array.Empty<string>();
            }

            if (CompiledAsset.TryGetStateIndex(channelName, playback.StateKey, out int stateIndex))
            {
                return ((XAnimationCompiledState)CompiledAsset.States[stateIndex]).Config.allowedNextStateKeys ?? Array.Empty<string>();
            }

            return Array.Empty<string>();
        }

        private bool TryResolveStateNode(XAnimationCompiledStateNode requestedNode, out StateNodeResolution resolution)
        {
            if (requestedNode is XAnimationCompiledNormalStateNode)
            {
                throw new XAnimationException($"XAnimation Normal state node '{requestedNode.Key}' cannot be played.");
            }

            List<XAnimationCompiledStateNode> activeNodes = new() { requestedNode };
            XAnimationCompiledStateNode current = requestedNode;
            while (current is XAnimationCompiledSelectorStateNode selector)
            {
                if (!m_Runtime.Context.TryGetInt(selector.ParameterIndex, out int value))
                {
                    throw new XAnimationException($"XAnimation Selector state node '{selector.Key}' parameter '{selector.Config.parameterName}' is unavailable.");
                }

                if (!selector.TryResolveChild(value, out current))
                {
                    resolution = null;
                    return false;
                }
                activeNodes.Add(current);
            }

            resolution = new StateNodeResolution(requestedNode, (XAnimationCompiledState)current, activeNodes.ToArray());
            return true;
        }

        private static StateNodeResolution CreateLeafResolution(XAnimationCompiledState state)
        {
            return new StateNodeResolution(state, state, new XAnimationCompiledStateNode[] { state });
        }

        private bool TryGetDefaultTransition(
            string channelName,
            string preStateKey,
            XAnimationCompiledState targetState,
            out XAnimationCompiledDefaultTransition transition)
        {
            return CompiledAsset.TryGetDefaultTransition(channelName, preStateKey, targetState.Key, out transition);
        }

        private static bool ContainsAnyNodeKey(IReadOnlyList<string> allowedKeys, IReadOnlyList<string> activeNodeKeys)
        {
            for (int i = 0; i < activeNodeKeys.Count; i++)
            {
                for (int j = 0; j < allowedKeys.Count; j++)
                {
                    if (string.Equals(activeNodeKeys[i], allowedKeys[j], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ActiveChainUsesSelectorParameter(string channelName, IReadOnlyList<string> activeNodeKeys, string parameterName)
        {
            for (int i = 0; i < activeNodeKeys.Count; i++)
            {
                XAnimationCompiledStateNode node = CompiledAsset.GetStateNode(channelName, activeNodeKeys[i]);
                if (node is XAnimationCompiledSelectorStateNode selector &&
                    string.Equals(selector.Config.parameterName, parameterName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReselectActiveStateNode(XAnimationChannel channel, XAnimationStatePlaybackInstance playback)
        {
            if (!TryResolveStateNode(
                    CompiledAsset.GetStateNode(channel.Name, playback.RequestedStateKey),
                    out StateNodeResolution resolution))
            {
                m_PendingSelectorStateKeyByChannel.Remove(channel.Name);
                m_Runtime.StopChannel(channel, channel.CompiledChannel.Config.defaultFadeOut);
                return;
            }
            if (string.Equals(playback.StateKey, resolution.State.Key, StringComparison.Ordinal))
            {
                playback.SetStateNodeContext(resolution.RequestedNode.Key, resolution.ActiveNodeKeys);
                m_PendingSelectorStateKeyByChannel.Remove(channel.Name);
                return;
            }

            XAnimationPlaybackStartInfo startInfo = StartStatePlayback(resolution, null, false);
            if (!startInfo.Started)
            {
                m_PendingSelectorStateKeyByChannel[channel.Name] = resolution.RequestedNode.Key;
            }
        }

        private void ProcessPendingSelectorTransitions()
        {
            if (m_PendingSelectorStateKeyByChannel.Count == 0)
            {
                return;
            }

            List<KeyValuePair<string, string>> pendingRequests = new(m_PendingSelectorStateKeyByChannel);
            for (int i = 0; i < pendingRequests.Count; i++)
            {
                KeyValuePair<string, string> pending = pendingRequests[i];
                XAnimationChannel channel = m_Runtime.GetChannel(pending.Key);
                if (!channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance playback) ||
                    playback == null ||
                    !string.Equals(playback.RequestedStateKey, pending.Value, StringComparison.Ordinal))
                {
                    m_PendingSelectorStateKeyByChannel.Remove(pending.Key);
                    continue;
                }

                ReselectActiveStateNode(channel, playback);
            }
        }

        private XAnimationCompiledChannel ResolveClipChannel(XAnimationCompiledClip clip, string channelName)
        {
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                return CompiledAsset.GetChannel(channelName);
            }

            throw new XAnimationException($"XAnimation clip '{clip.Key}' direct playback requires an explicit channelName.");
        }

        private XAnimationCompiledChannel GetStateChannel(XAnimationCompiledState state)
        {
            return (XAnimationCompiledChannel)CompiledAsset.Channels[state.DefaultChannelIndex];
        }

        private static float ResolvePlaybackSpeed(XAnimationCompiledState state)
        {
            float speed = state?.Config.speed ?? 1f;
            return Mathf.Approximately(speed, 0f) ? 1f : speed;
        }

        private XAnimationCompiledSingleState CreateTemporaryClipState(XAnimationCompiledClip clip, int clipIndex, XAnimationCompiledChannel channel, int channelIndex)
        {
            string key = CreateTemporaryClipStateKey(clip.Key);
            XAnimationStateNodeConfig config = CreateTemporaryClipStateConfig(clip, key);
            return new XAnimationCompiledSingleState(config, key, channel.Name, channelIndex, string.Empty, clipIndex);
        }

        private XAnimationCompiledSingleState CreateTemporaryDirectClipState(XAnimationCompiledClip clip, XAnimationCompiledChannel channel, int channelIndex)
        {
            string key = CreateTemporaryClipStateKey(clip.Key);
            XAnimationStateNodeConfig config = CreateTemporaryClipStateConfig(clip, key);
            return new XAnimationCompiledSingleState(config, key, channel.Name, channelIndex, string.Empty, clip);
        }

        private static XAnimationStateNodeConfig CreateTemporaryClipStateConfig(XAnimationCompiledClip clip, string key)
        {
            return new XAnimationStateNodeConfig
            {
                name = key,
                kind = XAnimationStateNodeKind.State,
                state = new XAnimationStateConfig
                {
                    stateType = XAnimationStateType.Single,
                    clipKey = clip.Key,
                    speed = 1f,
                    loop = clip.PlaybackClip.isLooping,
                    parameterName = string.Empty,
                    parameterXName = string.Empty,
                    parameterYName = string.Empty,
                    samples = Array.Empty<XAnimationBlend1DSampleConfig>(),
                    directionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>(),
                    behaviors = Array.Empty<XAnimationStateBehavior>(),
                },
            };
        }

        private string CreateTemporaryClipStateKey(string clipKey)
        {
            string stateKey;
            do
            {
                stateKey = $"{TemporaryClipStateKeyPrefix}{clipKey}:{m_NextTemporaryStateId++}";
            }
            while (CompiledAsset.TryGetStateIndex(stateKey, out _));

            return stateKey;
        }

        private static string CreateDirectClipKey(AnimationClip clip)
        {
            string clipName = clip != null && !string.IsNullOrWhiteSpace(clip.name)
                ? clip.name
                : "UnnamedClip";
            return $"{DirectClipKeyPrefix}{clipName}";
        }

        private XAnimationCompiledClip ResolveSingleStateClip(XAnimationCompiledSingleState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.HasDirectClip)
            {
                return state.DirectClip;
            }

            return (XAnimationCompiledClip)CompiledAsset.Clips[state.ClipIndex];
        }

        private XAnimationStatePlaybackInstance CreateStatePlayback(int playbackId, string channelName, XAnimationCompiledState state, XAnimationPlaybackRuntimeOptions options)
        {
            bool isTemporaryState = IsTemporaryClipState(state.Key);
            return state switch
            {
                XAnimationCompiledSingleState singleState => CreateSinglePlayback(playbackId, channelName, singleState, ResolveSingleStateClip(singleState), isTemporaryState, options),
                XAnimationCompiledBlend1DState blendState => CreateBlend1DPlayback(playbackId, channelName, blendState, isTemporaryState, options),
                XAnimationCompiledBlend2DSimpleDirectionalState directionalState => CreateBlend2DSimpleDirectionalPlayback(playbackId, channelName, directionalState, isTemporaryState, options),
                XAnimationCompiledBlend2DFreeformDirectionalState freeformState => CreateBlend2DFreeformDirectionalPlayback(playbackId, channelName, freeformState, isTemporaryState, options),
                _ => throw new XAnimationException($"XAnimation state '{state.Key}' has unsupported stateType '{state.StateType}'."),
            };
        }

        private XAnimationSingleStatePlaybackInstance CreateSinglePlayback(int playbackId, string channelName, XAnimationCompiledSingleState state, XAnimationCompiledClip clip, bool isTemporaryState, XAnimationPlaybackRuntimeOptions options)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(Graph, clip.PlaybackClip);
            playable.SetApplyFootIK(false);
            playable.SetTime(Mathf.Clamp01(options.NormalizedTime) * Mathf.Max(clip.PlaybackClip.length, 0.0001f));
            playable.SetSpeed(ResolvePlaybackSpeed(state));
            return new XAnimationSingleStatePlaybackInstance(playbackId, channelName, state, Animator, clip, playable, isTemporaryState, options);
        }

        private XAnimationBlend1DStatePlaybackInstance CreateBlend1DPlayback(int playbackId, string channelName, XAnimationCompiledBlend1DState state, bool isTemporaryState, XAnimationPlaybackRuntimeOptions options)
        {
            XAnimationCompiledClip[] clips = new XAnimationCompiledClip[state.Samples.Count];
            for (int i = 0; i < state.Samples.Count; i++)
            {
                clips[i] = (XAnimationCompiledClip)CompiledAsset.Clips[state.Samples[i].ClipIndex];
            }

            return new XAnimationBlend1DStatePlaybackInstance(Graph, playbackId, channelName, Animator, state, clips, isTemporaryState, options);
        }

        private XAnimationBlend2DSimpleDirectionalStatePlaybackInstance CreateBlend2DSimpleDirectionalPlayback(int playbackId, string channelName, XAnimationCompiledBlend2DSimpleDirectionalState state, bool isTemporaryState, XAnimationPlaybackRuntimeOptions options)
        {
            XAnimationCompiledClip[] clips = new XAnimationCompiledClip[state.Samples.Count];
            for (int i = 0; i < state.Samples.Count; i++)
            {
                clips[i] = (XAnimationCompiledClip)CompiledAsset.Clips[state.Samples[i].ClipIndex];
            }

            return new XAnimationBlend2DSimpleDirectionalStatePlaybackInstance(Graph, playbackId, channelName, Animator, state, clips, isTemporaryState, options);
        }

        private XAnimationBlend2DFreeformDirectionalStatePlaybackInstance CreateBlend2DFreeformDirectionalPlayback(int playbackId, string channelName, XAnimationCompiledBlend2DFreeformDirectionalState state, bool isTemporaryState, XAnimationPlaybackRuntimeOptions options)
        {
            XAnimationCompiledClip[] clips = new XAnimationCompiledClip[state.Samples.Count];
            for (int i = 0; i < state.Samples.Count; i++)
            {
                clips[i] = (XAnimationCompiledClip)CompiledAsset.Clips[state.Samples[i].ClipIndex];
            }

            return new XAnimationBlend2DFreeformDirectionalStatePlaybackInstance(Graph, playbackId, channelName, Animator, state, clips, isTemporaryState, options);
        }

        private static bool IsTemporaryClipState(string stateKey)
        {
            return !string.IsNullOrEmpty(stateKey) &&
                   stateKey.StartsWith(TemporaryClipStateKeyPrefix, StringComparison.Ordinal);
        }
    }
}
