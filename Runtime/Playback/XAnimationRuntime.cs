using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationRuntime : IDisposable
    {
        #region Fields

        private readonly List<XAnimationChannel> m_Channels = new();
        private readonly Dictionary<string, XAnimationChannel> m_ChannelMap = new(StringComparer.Ordinal);
        private readonly XAnimationCueRuntime m_CueRuntime = new();
        private readonly XAnimationPlaybackController m_PlaybackController;

        private PlayableGraph m_Graph;
        private AnimationLayerMixerPlayable m_LayerMixer;
        private AnimationPlayableOutput m_Output;
        private RuntimeAnimatorController m_OriginalController;
        private bool m_OriginalApplyRootMotion;
        private bool m_OriginalFireEvents;
        private bool m_RootMotionEnabled;
        private int m_NextPlaybackId = 1;
        private float[] m_LastLayerInputWeights;
        private bool m_UseDirectChannelOutput;
        private bool m_RuntimeInitialized;
        private XAnimationCompiledAsset m_CompiledAsset;
        private XAnimationContext m_Context;
        private Animator m_Animator;
        private bool m_IsPaused;
        private float m_GlobalSpeed = 1f;
        private XAnimationUpdateMode m_UpdateMode = XAnimationUpdateMode.Manual;
        private bool m_UnityAnimationEventsEnabled;

        internal event Action<XAnimationCueEvent> CueTriggered;
        internal event Action<XAnimationStateEvent> StateEntered;
        internal event Action<XAnimationStateEvent> StateExited;

        internal Animator Animator => m_Animator;
        internal XAnimationCompiledAsset CompiledAsset => m_CompiledAsset;
        internal XAnimationContext Context => m_Context;
        internal PlayableGraph Graph => m_Graph;
        internal IReadOnlyList<XAnimationChannel> Channels => m_Channels;
        internal bool IsPaused => m_IsPaused;
        internal float GlobalSpeed => m_GlobalSpeed;
        internal XAnimationUpdateMode UpdateMode => m_UpdateMode;
        internal bool UnityAnimationEventsEnabled => m_UnityAnimationEventsEnabled;
        internal bool IsInitialized => m_RuntimeInitialized;

        #endregion

        internal XAnimationRuntime()
        {
            m_PlaybackController = new XAnimationPlaybackController(this);
            m_CueRuntime.CueTriggered += RaiseCueTriggered;
        }

        #region Initialization

        internal void Initialize(XAnimationCompiledAsset compiledAsset, XAnimationContext context, Animator animator)
        {
            if (compiledAsset == null)
            {
                throw new ArgumentNullException(nameof(compiledAsset));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            DisposeRuntime();
            m_CompiledAsset = compiledAsset;
            m_Context = context;
            m_Animator = animator;
            m_IsPaused = false;
            m_GlobalSpeed = 1f;
            BuildGraph();
        }

        #endregion

        #region Public Runtime Controls

        internal void SetPaused(bool paused)
        {
            m_IsPaused = paused;
            SyncGraphPlayState();
        }

        internal void SetGlobalSpeed(float speed)
        {
            m_GlobalSpeed = Mathf.Max(0f, speed);
        }

        internal void SetUpdateMode(XAnimationUpdateMode updateMode)
        {
            if (m_RuntimeInitialized)
            {
                if (m_UpdateMode == updateMode) return;
                throw new XAnimationException("XAnimation update mode cannot be changed after initialization.");
            }

            m_UpdateMode = updateMode;
        }

        internal void SetUnityAnimationEventsEnabled(bool enabled)
        {
            m_UnityAnimationEventsEnabled = enabled;
            SyncAnimatorFireEvents();
        }

        internal bool PrepareFromScheduler(float deltaTime, bool isStepping)
        {
            m_CueRuntime.Flush();
            if (!m_RuntimeInitialized || m_IsPaused || isStepping)
            {
                SyncGraphPlayState();
                return false;
            }

            float scaledDeltaTime = deltaTime * m_GlobalSpeed;
            PrepareRuntimeFrame(scaledDeltaTime);
            if (m_UpdateMode == XAnimationUpdateMode.Manual)
            {
                EvaluateRuntime(scaledDeltaTime);
            }

            return true;
        }

        internal void FinalizeFromScheduler()
        {
            if (!m_RuntimeInitialized)
            {
                return;
            }

            FinalizeRuntimeFrame();
        }

        internal void RunManualFrame(float deltaTime)
        {
            if (deltaTime < 0f || !m_RuntimeInitialized)
            {
                return;
            }

            PrepareRuntimeFrame(deltaTime);
            EvaluateRuntime(deltaTime);
            FinalizeRuntimeFrame();
        }

        public void Dispose()
        {
            DisposeRuntime();
            m_Context = null;
            m_CompiledAsset = null;
            m_Animator = null;
        }

        #endregion

        #region Debug Graph

        internal XAnimationDebugGraphSnapshot BuildDebugGraphSnapshot()
        {
            XAnimationRuntimeDebugContext debugContext = new(m_RuntimeInitialized, m_Graph, m_Output, m_LayerMixer, m_UseDirectChannelOutput, m_Animator, m_Channels, m_GlobalSpeed);
            return new XAnimationDebugGraphBuilder().Build(debugContext);
        }

        #endregion

        #region Playback Startup

        internal XAnimationPlaybackStartInfo StartClipPlayback(string clipKey, string channelName, XAnimationTransitionOptions transition = default)
        {
            ThrowIfDisposed();
            return m_PlaybackController.StartClipPlayback(clipKey, channelName, transition);
        }

        internal XAnimationPlaybackStartInfo StartClipPlayback(AnimationClip animationClip, string channelName, XAnimationTransitionOptions transition)
        {
            ThrowIfDisposed();
            return m_PlaybackController.StartClipPlayback(animationClip, channelName, transition);
        }

        internal XAnimationPlaybackStartInfo StartStatePlayback(string stateKey, XAnimationTransitionOptions transition, bool force)
        {
            ThrowIfDisposed();
            return m_PlaybackController.StartStatePlayback(stateKey, transition, force);
        }

        internal XAnimationPlaybackStartInfo StartStatePlayback(string channelName, string stateKey, XAnimationTransitionOptions transition, bool force)
        {
            ThrowIfDisposed();
            return m_PlaybackController.StartStatePlayback(channelName, stateKey, transition, force);
        }

        #endregion

        #region Runtime Controls

        internal void Stop(string channelName, float fadeOut = 0)
        {
            ThrowIfDisposed();
            XAnimationChannel channel = GetChannel(channelName);
            m_CueRuntime.StopChannel(channel, fadeOut > 0f ? fadeOut : channel.CompiledChannel.Config.defaultFadeOut);
        }

        internal void StopAll(float fadeOut = 0)
        {
            ThrowIfDisposed();
            foreach (XAnimationChannel channel in m_Channels)
            {
                float actualFadeOut = fadeOut > 0f ? fadeOut : channel.CompiledChannel.Config.defaultFadeOut;
                m_CueRuntime.StopChannel(channel, actualFadeOut);
            }
        }

        internal void SetChannelWeight(string channelName, float weight)
        {
            ThrowIfDisposed();
            GetChannel(channelName).SetChannelWeight(weight);
        }

        internal bool SeekChannel(string channelName, float normalizedTime)
        {
            ThrowIfDisposed();
            return GetChannel(channelName).SeekCurrent(Mathf.Clamp01(normalizedTime));
        }

        internal void SetRootMotionEnabled(bool enabled)
        {
            ThrowIfDisposed();
            if (m_RootMotionEnabled == enabled)
            {
                SyncAnimatorRootMotion();
                return;
            }

            m_RootMotionEnabled = enabled;
            SyncAnimatorRootMotion();
        }

        internal bool ShouldApplyNativeRootMotion()
        {
            ThrowIfDisposed();
            return m_RootMotionEnabled;
        }

        #endregion

        #region Runtime Queries

        internal XAnimationChannelState GetChannelState(string channelName)
        {
            ThrowIfDisposed();
            XAnimationChannel channel = GetChannel(channelName);
            XAnimationChannelState state = channel.GetState(m_GlobalSpeed);
            if (state == null)
            {
                return null;
            }

            state.nextStateKey = string.Empty;
            if (CompiledAsset.TryGetAutoTransition(channelName, state.stateKey, out XAnimationCompiledAutoTransition transition))
            {
                state.nextStateKey = transition.NextStateKey ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(state.transitionTargetStateKey))
            {
                state.nextStateKey = state.transitionTargetStateKey;
            }

            return state;
        }

        internal bool TryGetCurrentState(string channelName, out XAnimationChannelState state)
        {
            ThrowIfDisposed();
            state = GetChannelState(channelName);
            return state != null;
        }

        internal bool IsPlaying(string stateKey, string channelName = null)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(channelName))
            {
                XAnimationChannelState state = GetChannelState(channelName);
                return state != null && string.Equals(state.stateKey, stateKey, StringComparison.Ordinal);
            }

            for (int i = 0; i < m_Channels.Count; i++)
            {
                XAnimationChannelState state = m_Channels[i].GetState(m_GlobalSpeed);
                if (state != null && string.Equals(state.stateKey, stateKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal float GetStateDuration(string stateKey)
        {
            ThrowIfDisposed();
            return CompiledAsset.GetStateDuration(stateKey);
        }

        internal float GetStateDuration(string channelName, string stateKey)
        {
            ThrowIfDisposed();
            return CompiledAsset.GetStateDuration(channelName, stateKey);
        }

        internal float GetClipDuration(string clipKey)
        {
            ThrowIfDisposed();
            return CompiledAsset.GetClipDuration(clipKey);
        }

        internal void PreloadAll()
        {
            ThrowIfDisposed();
            CompiledAsset.PreloadAll();
        }

        internal void PreloadState(string stateKey)
        {
            ThrowIfDisposed();
            CompiledAsset.PreloadState(stateKey);
        }

        internal void PreloadState(string channelName, string stateKey)
        {
            ThrowIfDisposed();
            CompiledAsset.PreloadState(channelName, stateKey);
        }

        #endregion

        #region Frame Evaluation

        private void EvaluateRuntime(float deltaTime)
        {
            if (m_UpdateMode == XAnimationUpdateMode.Manual)
            {
                m_Graph.Evaluate(deltaTime);
            }
        }

        private void PrepareRuntimeFrame(float deltaTime)
        {
            ThrowIfDisposed();
            if (deltaTime < 0f)
            {
                throw new XAnimationException("XAnimation deltaTime cannot be negative.");
            }

            for (int i = 0; i < m_Channels.Count; i++)
            {
                XAnimationChannel channel = m_Channels[i];
                channel.PrepareFrame(deltaTime, Context, m_UseDirectChannelOutput, ResolvePlayableSpeedScale());
                if (!m_UseDirectChannelOutput)
                {
                    SetLayerInputWeight(i, channel.HasActivePlayback ? channel.ChannelWeight : 0f);
                }
            }
        }

        private void FinalizeRuntimeFrame()
        {
            ThrowIfDisposed();
            for (int i = 0; i < m_Channels.Count; i++)
            {
                m_CueRuntime.FinalizeChannelFrame(m_Channels[i], false);
            }

            m_CueRuntime.Flush();
            m_PlaybackController.ProcessCompletedNonLoopPlayback();
        }

        #endregion

        #region Runtime Lifecycle

        private void DisposeRuntime()
        {
            XAnimationCompiledAsset compiledAsset = m_CompiledAsset;
            m_CompiledAsset = null;
            m_PlaybackController.Reset();
            if (!m_RuntimeInitialized)
            {
                m_CueRuntime.Clear();
                compiledAsset?.Dispose();
                return;
            }

            foreach (XAnimationChannel channel in m_Channels)
            {
                m_CueRuntime.DisposeChannel(channel);
            }

            m_Channels.Clear();
            m_ChannelMap.Clear();

            if (m_Graph.IsValid())
            {
                m_Graph.Destroy();
            }

            RestoreAnimatorController();
            m_CueRuntime.Clear();
            m_RuntimeInitialized = false;
            compiledAsset?.Dispose();
        }

        private void BuildGraph()
        {
            m_CueRuntime.Register(CompiledAsset.CuesByClipKey);
            m_PlaybackController.Reset();
            DisableAnimatorController();

            m_Graph = PlayableGraph.Create($"XAnimationDriver_{Animator.name}");
            ApplyGraphTimeUpdateMode();

            m_Output = AnimationPlayableOutput.Create(m_Graph, "XAnimationOutput", Animator);
            m_UseDirectChannelOutput = ShouldUseDirectChannelOutput();
            if (!m_UseDirectChannelOutput)
            {
                m_LayerMixer = AnimationLayerMixerPlayable.Create(m_Graph, CompiledAsset.Channels.Count);
                m_Output.SetSourcePlayable(m_LayerMixer);
                m_LastLayerInputWeights = new float[CompiledAsset.Channels.Count];
                for (int i = 0; i < m_LastLayerInputWeights.Length; i++)
                {
                    m_LastLayerInputWeights[i] = float.NaN;
                }
            }
            else
            {
                m_LastLayerInputWeights = null;
            }

            for (int i = 0; i < CompiledAsset.Channels.Count; i++)
            {
                XAnimationCompiledChannel compiledChannel = (XAnimationCompiledChannel)CompiledAsset.Channels[i];
                XAnimationChannel channel = new(m_Graph, compiledChannel, Animator, () => m_GlobalSpeed, NextPlaybackId, OnStateEntered, OnStateExited);
                m_Channels.Add(channel);
                m_ChannelMap.Add(channel.Name, channel);
                if (m_UseDirectChannelOutput)
                {
                    m_Output.SetSourcePlayable(channel.Mixer);
                }
                else
                {
                    m_Graph.Connect(channel.Mixer, 0, m_LayerMixer, i);
                    SetLayerInputWeight(i, compiledChannel.Config.defaultWeight, force: true);
                    m_LayerMixer.SetLayerAdditive((uint)i, compiledChannel.Config.layerType == XAnimationChannelLayerType.Additive);
                    if (compiledChannel.Mask != null)
                    {
                        m_LayerMixer.SetLayerMaskFromAvatarMask((uint)i, compiledChannel.Mask);
                    }
                }
            }

            m_CueRuntime.BindChannels(m_Channels);
            m_CueRuntime.EnsurePlayable(m_Graph);
            m_Graph.Play();
            m_RootMotionEnabled = CompiledAsset.RootMotionEnabled;
            SyncAnimatorRootMotion();
            m_RuntimeInitialized = true;
            SyncGraphPlayState();
        }

        #endregion

        #region Graph State

        private void ApplyGraphTimeUpdateMode()
        {
            if (!m_Graph.IsValid())
            {
                return;
            }

            m_Graph.SetTimeUpdateMode(m_UpdateMode switch
            {
                XAnimationUpdateMode.GameTime => DirectorUpdateMode.GameTime,
                _ => DirectorUpdateMode.Manual,
            });
            SyncGraphPlayState();
        }

        private float ResolvePlayableSpeedScale()
        {
            return m_UpdateMode == XAnimationUpdateMode.GameTime ? m_GlobalSpeed : 1f;
        }

        private void SyncGraphPlayState()
        {
            if (!m_Graph.IsValid())
            {
                return;
            }

            if (m_UpdateMode == XAnimationUpdateMode.GameTime && m_IsPaused)
            {
                if (m_Graph.IsPlaying())
                {
                    m_Graph.Stop();
                }

                return;
            }

            if (!m_Graph.IsPlaying())
            {
                m_Graph.Play();
            }
        }

        #endregion

        #region Graph Utilities

        private bool ShouldUseDirectChannelOutput()
        {
            if (CompiledAsset.Channels.Count != 1)
            {
                return false;
            }

            XAnimationCompiledChannel channel = (XAnimationCompiledChannel)CompiledAsset.Channels[0];
            return channel.Config.layerType == XAnimationChannelLayerType.Base &&
                   channel.Mask == null;
        }

        private void SetLayerInputWeight(int inputIndex, float weight, bool force = false)
        {
            if (!m_LayerMixer.IsValid())
            {
                return;
            }

            if (m_LastLayerInputWeights == null ||
                inputIndex < 0 ||
                inputIndex >= m_LastLayerInputWeights.Length ||
                force ||
                float.IsNaN(m_LastLayerInputWeights[inputIndex]) ||
                Mathf.Abs(m_LastLayerInputWeights[inputIndex] - weight) > 0.00001f)
            {
                m_LayerMixer.SetInputWeight(inputIndex, weight);
                if (m_LastLayerInputWeights != null &&
                    inputIndex >= 0 &&
                    inputIndex < m_LastLayerInputWeights.Length)
                {
                    m_LastLayerInputWeights[inputIndex] = weight;
                }
            }
        }

        #endregion

        #region Animator State

        private void DisableAnimatorController()
        {
            m_OriginalController = Animator.runtimeAnimatorController;
            m_OriginalApplyRootMotion = Animator.applyRootMotion;
            m_OriginalFireEvents = Animator.fireEvents;
            Animator.runtimeAnimatorController = null;
            Animator.applyRootMotion = false;
            Animator.fireEvents = m_UnityAnimationEventsEnabled;
        }

        private void RestoreAnimatorController()
        {
            if (Animator == null)
            {
                return;
            }

            if (m_OriginalController != null && Animator.runtimeAnimatorController == null)
            {
                Animator.runtimeAnimatorController = m_OriginalController;
            }

            Animator.applyRootMotion = m_OriginalApplyRootMotion;
            Animator.fireEvents = m_OriginalFireEvents;
        }

        private void SyncAnimatorFireEvents()
        {
            if (Animator != null && Animator.fireEvents != m_UnityAnimationEventsEnabled)
            {
                Animator.fireEvents = m_UnityAnimationEventsEnabled;
            }
        }

        private void SyncAnimatorRootMotion()
        {
            if (Animator != null && Animator.applyRootMotion != m_RootMotionEnabled)
            {
                Animator.applyRootMotion = m_RootMotionEnabled;
            }
        }

        #endregion

        #region Channel Access

        internal XAnimationChannel GetChannel(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new XAnimationException("XAnimation channelName cannot be empty.");
            }

            if (!m_ChannelMap.TryGetValue(channelName, out XAnimationChannel channel))
            {
                throw new XAnimationException($"XAnimation channel '{channelName}' does not exist.");
            }

            return channel;
        }

        internal bool TryGetChannel(string channelName, out XAnimationChannel channel)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                channel = null;
                return false;
            }

            return m_ChannelMap.TryGetValue(channelName, out channel);
        }

        internal bool TryPlay(XAnimationChannel channel, Func<int, XAnimationPlaybackRuntimeOptions, XAnimationStatePlaybackInstance> playbackFactory, XAnimationTransitionRequest request, out XAnimationStatePlaybackInstance playback, out XAnimationTransitionRejectReason rejectReason)
        {
            return m_CueRuntime.TryPlay(channel, playbackFactory, request, out playback, out rejectReason);
        }

        internal void StopChannel(XAnimationChannel channel, float fadeOut)
        {
            m_CueRuntime.StopChannel(channel, fadeOut);
        }

        internal void RegisterDirectClipCues(string clipKey, IReadOnlyList<XAnimationCompiledCue> cues)
        {
            m_CueRuntime.RegisterClipCues(clipKey, cues);
        }

        internal void EnsureCuePlayable()
        {
            m_CueRuntime.EnsurePlayable(m_Graph);
        }

        private int NextPlaybackId()
        {
            return m_NextPlaybackId++;
        }

        #endregion

        private void OnStateEntered(XAnimationStatePlaybackInstance playback)
        {
            StateEntered?.Invoke(BuildStateEvent(playback, null));
        }

        private void OnStateExited(XAnimationStatePlaybackInstance playback, XAnimationStateExitReason reason)
        {
            StateExited?.Invoke(BuildStateEvent(playback, reason));
        }

        private static XAnimationStateEvent BuildStateEvent(XAnimationStatePlaybackInstance playback, XAnimationStateExitReason? exitReason)
        {
            return new XAnimationStateEvent
            {
                stateKey = playback?.StateKey ?? string.Empty,
                channelName = playback?.ChannelName ?? string.Empty,
                playbackId = playback?.PlaybackId ?? 0,
                isTemporaryState = playback?.IsTemporaryState ?? false,
                normalizedTime = playback?.GetNormalizedTime() ?? 0f,
                totalNormalizedTime = playback?.GetTotalNormalizedTime() ?? 0f,
                exitReason = exitReason,
            };
        }

        #region Guards

        private void ThrowIfDisposed()
        {
            if (!m_RuntimeInitialized)
            {
                throw new XAnimationException("XAnimationDriver is not initialized.");
            }
        }

        private void RaiseCueTriggered(XAnimationCueEvent cueEvent)
        {
            CueTriggered?.Invoke(cueEvent);
        }

        #endregion
    }
}
