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
        private int m_NextTemporaryStateId = 1;

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
            XAnimationTransitionRequest request = BuildTransitionRequest(temporaryState, channel, transition, XAnimationTransitionRequestSource.ExplicitPlay);
            return TryPlayCompiledState(temporaryState, channel, request);
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
                editorGroupName = string.Empty,
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
            XAnimationTransitionRequest request = BuildTransitionRequest(temporaryState, channel, transition, XAnimationTransitionRequestSource.ExplicitPlay);
            return TryPlayCompiledState(temporaryState, channel, request);
        }

        internal XAnimationPlaybackStartInfo StartStatePlayback(string stateKey, XAnimationTransitionOptions transition, bool force)
        {
            XAnimationCompiledState state = CompiledAsset.GetState(stateKey);
            XAnimationCompiledChannel channel = GetStateChannel(state);
            if (!force &&
                !CanTransitionFromCurrentPlayback(m_Runtime.GetChannel(channel.Name), state, out XAnimationTransitionRejectReason gateRejectReason))
            {
                string clipKey = state is XAnimationCompiledSingleState singleState
                    ? ResolveSingleStateClip(singleState).Key
                    : string.Empty;
                return XAnimationPlaybackStartInfo.CreateFailed(channel.Name, state.Key, clipKey, IsTemporaryClipState(state.Key), gateRejectReason);
            }

            XAnimationTransitionRequest request = BuildTransitionRequest(state, channel, transition, transition != null ? XAnimationTransitionRequestSource.ExplicitPlay : ResolveRequestSource(state, channel), force);
            return TryPlayCompiledState(state, channel, request);
        }

        internal void ProcessCompletedNonLoopPlayback()
        {
            IReadOnlyList<XAnimationChannel> channels = m_Runtime.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannel channel = channels[i];
                if (!channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance playback) ||
                    playback == null ||
                    playback.IsLooping ||
                    playback.IsTemporaryState ||
                    playback.HasCompletedExitOrTransition ||
                    !CompiledAsset.TryGetStateIndex(playback.StateKey, out int stateIndex))
                {
                    continue;
                }

                XAnimationCompiledState state = (XAnimationCompiledState)CompiledAsset.States[stateIndex];
                bool hasAutoTransition = CompiledAsset.TryGetAutoTransition(state.Key, out XAnimationCompiledAutoTransition autoTransition);
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

                XAnimationCompiledState nextState = CompiledAsset.GetState(autoTransition.NextStateKey);
                float fadeIn = autoTransition.TransitionDuration > 0f ? autoTransition.TransitionDuration : channel.CompiledChannel.Config.defaultFadeIn;
                float fadeOut = autoTransition.TransitionDuration > 0f ? autoTransition.TransitionDuration : channel.CompiledChannel.Config.defaultFadeOut;
                XAnimationTransitionRequest request = new(channel.Name, nextState.Key, nextState is XAnimationCompiledSingleState singleState ? ResolveSingleStateClip(singleState).Key : string.Empty,
                    XAnimationTransitionRequestSource.AutoTransition, fadeIn, fadeOut, autoTransition.EnterTime, playback.Priority, true, false);

                if (!CanTransitionFromCurrentPlayback(channel, nextState, out _))
                {
                    continue;
                }

                XAnimationPlaybackStartInfo startInfo = TryPlayCompiledState(nextState, channel.CompiledChannel, request);
                if (startInfo.Started)
                {
                    channel.TryMarkCompletedExit(out _);
                }
            }
        }

        private XAnimationPlaybackStartInfo TryPlayCompiledState(XAnimationCompiledState state, XAnimationCompiledChannel channel, XAnimationTransitionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!m_Runtime.TryPlay(m_Runtime.GetChannel(channel.Name), (playbackId, actualOptions) => CreateStatePlayback(playbackId, channel.Name, state, actualOptions),
                    request, out XAnimationStatePlaybackInstance playback, out XAnimationTransitionRejectReason rejectReason))
            {
                string clipKey = state switch
                {
                    XAnimationCompiledSingleState singleState => ResolveSingleStateClip(singleState).Key,
                    _ => string.Empty,
                };
                return XAnimationPlaybackStartInfo.CreateFailed(channel.Name, state.Key, clipKey, IsTemporaryClipState(state.Key), rejectReason);
            }

            return XAnimationPlaybackStartInfo.CreateStarted(playback);
        }

        private XAnimationTransitionRequest BuildTransitionRequest(XAnimationCompiledState state, XAnimationCompiledChannel channel, XAnimationTransitionOptions transition, XAnimationTransitionRequestSource explicitSource, bool force = false)
        {
            XAnimationTransitionOptions resolvedTransition = ResolveTransitionOptions(state, channel, transition);
            float fadeIn = resolvedTransition.fadeIn > 0f ? resolvedTransition.fadeIn : channel.Config.defaultFadeIn;
            float fadeOut = resolvedTransition.fadeOut > 0f ? resolvedTransition.fadeOut : channel.Config.defaultFadeOut;
            string clipKey = state is XAnimationCompiledSingleState singleState
                ? ResolveSingleStateClip(singleState).Key
                : string.Empty;
            XAnimationTransitionRequestSource source = transition != null ? explicitSource : ResolveRequestSource(state, channel);

            return new XAnimationTransitionRequest(channel.Name, state.Key, clipKey, source, fadeIn, fadeOut, resolvedTransition.enterTime, resolvedTransition.priority,
                resolvedTransition.interruptible, force);
        }

        private XAnimationTransitionOptions ResolveTransitionOptions(XAnimationCompiledState state, XAnimationCompiledChannel channel, XAnimationTransitionOptions transition)
        {
            if (transition != null)
            {
                return transition;
            }

            if (!IsTemporaryClipState(state.Key) &&
                m_Runtime.TryGetChannel(channel.Name, out XAnimationChannel runtimeChannel) &&
                runtimeChannel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) &&
                currentPlayback != null &&
                !currentPlayback.IsTemporaryState &&
                CompiledAsset.TryGetDefaultTransition(currentPlayback.StateKey, state.Key, out XAnimationCompiledDefaultTransition defaultTransition))
            {
                return defaultTransition.CreateTransitionOptions();
            }

            return new XAnimationTransitionOptions();
        }

        private XAnimationTransitionRequestSource ResolveRequestSource(XAnimationCompiledState state, XAnimationCompiledChannel channel)
        {
            if (IsTemporaryClipState(state.Key))
            {
                return XAnimationTransitionRequestSource.ExplicitPlay;
            }

            if (m_Runtime.TryGetChannel(channel.Name, out XAnimationChannel runtimeChannel) &&
                runtimeChannel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) &&
                currentPlayback != null &&
                !currentPlayback.IsTemporaryState &&
                CompiledAsset.TryGetDefaultTransition(currentPlayback.StateKey, state.Key, out _))
            {
                return XAnimationTransitionRequestSource.DefaultTransition;
            }

            return XAnimationTransitionRequestSource.ExplicitPlay;
        }

        private bool CanTransitionFromCurrentPlayback(XAnimationChannel channel, XAnimationCompiledState targetState, out XAnimationTransitionRejectReason rejectReason)
        {
            rejectReason = XAnimationTransitionRejectReason.None;
            if (channel == null ||
                !channel.TryGetCurrentPlayback(out XAnimationStatePlaybackInstance currentPlayback) ||
                currentPlayback == null)
            {
                return true;
            }

            string[] allowedNext = GetAllowedNextStateKeys(currentPlayback);
            if (allowedNext.Length > 0 && Array.IndexOf(allowedNext, targetState.Key) < 0)
            {
                rejectReason = XAnimationTransitionRejectReason.SourceStateDisallowTarget;
                return false;
            }

            string[] allowedPrevious = targetState.Config.allowedPreviousStateKeys ?? Array.Empty<string>();
            if (allowedPrevious.Length > 0 && Array.IndexOf(allowedPrevious, currentPlayback.StateKey) < 0)
            {
                rejectReason = XAnimationTransitionRejectReason.TargetStateDisallowSource;
                return false;
            }

            return true;
        }

        private string[] GetAllowedNextStateKeys(XAnimationStatePlaybackInstance playback)
        {
            if (playback == null)
            {
                return Array.Empty<string>();
            }

            if (CompiledAsset.TryGetStateIndex(playback.StateKey, out int stateIndex))
            {
                return ((XAnimationCompiledState)CompiledAsset.States[stateIndex]).Config.allowedNextStateKeys ?? Array.Empty<string>();
            }

            return Array.Empty<string>();
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
            XAnimationStateConfig config = CreateTemporaryClipStateConfig(clip, channel);
            config.key = CreateTemporaryClipStateKey(clip.Key);
            return new XAnimationCompiledSingleState(config, channelIndex, clipIndex);
        }

        private XAnimationCompiledSingleState CreateTemporaryDirectClipState(XAnimationCompiledClip clip, XAnimationCompiledChannel channel, int channelIndex)
        {
            XAnimationStateConfig config = CreateTemporaryClipStateConfig(clip, channel);
            config.key = CreateTemporaryClipStateKey(clip.Key);
            return new XAnimationCompiledSingleState(config, channelIndex, clip);
        }

        private static XAnimationStateConfig CreateTemporaryClipStateConfig(XAnimationCompiledClip clip, XAnimationCompiledChannel channel)
        {
            return new XAnimationStateConfig
            {
                key = string.Empty,
                stateType = XAnimationStateType.Single,
                clipKey = clip.Key,
                channelName = channel.Name,
                speed = 1f,
                loop = clip.PlaybackClip.isLooping,
                parameterName = string.Empty,
                parameterXName = string.Empty,
                parameterYName = string.Empty,
                samples = Array.Empty<XAnimationBlend1DSampleConfig>(),
                directionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>(),
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
