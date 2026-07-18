using System;
using System.Collections.Generic;
using UnityEngine;

namespace XAnimationEngine
{
    public sealed partial class XAnimationDriver : IDisposable
    {
        #region Fields

        private readonly XAnimationAssetLoader m_AssetLoader = new();
        private readonly Dictionary<int, PendingPlaybackExit> m_PendingPlaybackExits = new();
        private readonly XAnimationRuntime m_Runtime = new();
        private readonly XAnimationActionManager m_ActionManager;
        private readonly XAnimationDriverScheduler m_Scheduler;

        #endregion

        #region Events And Properties

        public event Action<XAnimationCueEvent> CueTriggered;
        public event Action<XAnimationStateEvent> OnStateEnter;
        public event Action<XAnimationStateEvent> OnStateExit;

        public Animator Animator => m_Runtime.Animator;
        public XAnimationAsset Asset => m_Runtime.CompiledAsset?.Asset;
        public XAnimationCompiledAsset CompiledAsset => m_Runtime.CompiledAsset;
        public bool IsPaused => m_Runtime.IsPaused;
        public float GlobalSpeed => m_Runtime.GlobalSpeed;
        public XAnimationUpdateMode UpdateMode => m_Runtime.UpdateMode;
        public bool UnityAnimationEventsEnabled => m_Runtime.UnityAnimationEventsEnabled;
        public bool SupportsCue => true;
        public bool IsRegisteredForAutomaticUpdate => m_Scheduler.IsRegisteredForAutomaticUpdate;
        public bool IsRunning => m_Runtime.IsInitialized && !m_Runtime.IsPaused;

        #endregion

        public XAnimationDriver()
        {
            m_ActionManager = new XAnimationActionManager(this);
            m_Scheduler = new XAnimationDriverScheduler(m_Runtime, ProcessActionReturns);
            m_Runtime.CueTriggered += RaiseCueTriggered;
            m_Runtime.StateEntered += RaiseStateEntered;
            m_Runtime.StateExited += CompletePlaybackExitAndRaise;
        }

        #region Types

        private sealed class PendingPlaybackExit
        {
            internal PendingPlaybackExit(int playbackId, string channelName, string requestedStateKey, string requestedClipKey, bool isTemporaryState, XAnimationPlaybackHandle handle)
            {
                PlaybackId = playbackId;
                ChannelName = channelName ?? string.Empty;
                RequestedStateKey = requestedStateKey ?? string.Empty;
                RequestedClipKey = requestedClipKey ?? string.Empty;
                IsTemporaryState = isTemporaryState;
                Handle = handle ?? throw new ArgumentNullException(nameof(handle));
            }

            public int PlaybackId { get; }
            public string ChannelName { get; }
            public string RequestedStateKey { get; }
            public string RequestedClipKey { get; }
            public bool IsTemporaryState { get; }
            public XAnimationPlaybackHandle Handle { get; }
        }

        #endregion

        #region Initialization

        public void Initialize(string assetPath, Animator animator)
        {
            ValidateAssetPath(assetPath);
            ValidateAnimator(animator);
            m_Runtime.Dispose();
            InitializeLoadedAsset(m_AssetLoader.Load(assetPath), animator);
        }

        public void Initialize(TextAsset animationAsset, Animator animator)
        {
            ValidateAnimationAsset(animationAsset);
            ValidateAnimator(animator);
            m_Runtime.Dispose();
            InitializeLoadedAsset(m_AssetLoader.Load(animationAsset), animator);
        }

        public void Initialize(XAnimationCompiledAsset compiledAsset, Animator animator)
        {
            ValidateCompiledAsset(compiledAsset);
            ValidateAnimator(animator);
            m_Runtime.Dispose();
            InitializeLoadedAsset(compiledAsset, animator);
        }

        #endregion

        #region Parameters

        public void SetParameter(string key, float value)
        {
            EnsureInitialized();
            m_Runtime.Context.SetFloat(key, value);
        }

        public void SetParameter(string key, bool value)
        {
            EnsureInitialized();
            m_Runtime.Context.SetBool(key, value);
        }

        public void SetParameter(string key, int value)
        {
            EnsureInitialized();
            m_Runtime.Context.SetInt(key, value);
            m_Runtime.ProcessSelectorParameterChange(key);
        }

        public void SetParameter(string key, string value)
        {
            EnsureInitialized();
            m_Runtime.Context.SetString(key, value);
            m_Runtime.ProcessSelectorParameterChange(key);
        }

        public void SetTrigger(string key)
        {
            EnsureInitialized();
            m_Runtime.Context.SetTrigger(key);
        }

        public void ResetTrigger(string key)
        {
            EnsureInitialized();
            m_Runtime.Context.ResetTrigger(key);
        }

        public bool TryGetParameter(string key, out float value)
        {
            EnsureInitialized();
            return m_Runtime.Context.TryGetFloat(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            EnsureInitialized();
            return m_Runtime.Context.TryGetBool(key, out value);
        }

        public bool TryGetParameter(string key, out int value)
        {
            EnsureInitialized();
            return m_Runtime.Context.TryGetInt(key, out value);
        }

        public bool TryGetParameter(string key, out string value)
        {
            EnsureInitialized();
            return m_Runtime.Context.TryGetString(key, out value);
        }

        public bool TryGetTrigger(string key, out bool value)
        {
            EnsureInitialized();
            return m_Runtime.Context.TryGetTrigger(key, out value);
        }

        #endregion

        #region Playback

        public XAnimationPlaybackHandle PlayClip(string clipName, string channelName, XAnimationTransitionOptions transition = null)
        {
            EnsureInitialized();
            XAnimationPlaybackStartInfo startInfo = m_Runtime.StartClipPlayback(clipName, channelName, NormalizeTransitionOptions(transition));
            return CreatePlaybackHandle(startInfo, string.Empty, clipName);
        }

        public XAnimationPlaybackHandle PlayClip(AnimationClip clip, string channelName, XAnimationTransitionOptions transition = null)
        {
            EnsureInitialized();
            XAnimationPlaybackStartInfo startInfo = m_Runtime.StartClipPlayback(clip, channelName, NormalizeTransitionOptions(transition));
            return CreatePlaybackHandle(startInfo, string.Empty, startInfo.ClipKey);
        }

        public XAnimationPlaybackHandle PlayState(string stateName)
        {
            return PlayState(stateName, (XAnimationTransitionOptions)null);
        }

        public XAnimationPlaybackHandle PlayState(string stateName, bool force)
        {
            return PlayState(stateName, (XAnimationTransitionOptions)null, force);
        }

        public XAnimationPlaybackHandle PlayState(string stateName, XAnimationTransitionOptions transition)
        {
            return PlayState(stateName, transition, false);
        }

        public XAnimationPlaybackHandle PlayState(string stateName, XAnimationTransitionOptions transition, bool force)
        {
            EnsureInitialized();
            XAnimationPlaybackStartInfo startInfo = m_Runtime.StartStatePlayback(stateName, NormalizeTransitionOptions(transition), force);
            return CreatePlaybackHandle(startInfo, stateName, string.Empty);
        }

        public XAnimationPlaybackHandle PlayState(string channelName, string stateName)
        {
            return PlayState(channelName, stateName, null);
        }

        public XAnimationPlaybackHandle PlayState(string channelName, string stateName, bool force)
        {
            return PlayState(channelName, stateName, null, force);
        }

        public XAnimationPlaybackHandle PlayState(string channelName, string stateName, XAnimationTransitionOptions transition)
        {
            return PlayState(channelName, stateName, transition, false);
        }

        public XAnimationPlaybackHandle PlayState(string channelName, string stateName, XAnimationTransitionOptions transition, bool force)
        {
            EnsureInitialized();
            XAnimationPlaybackStartInfo startInfo = m_Runtime.StartStatePlayback(channelName, stateName, NormalizeTransitionOptions(transition), force);
            return CreatePlaybackHandle(startInfo, stateName, string.Empty);
        }

        public XAnimationActionHandle PlayAction(string stateKey, XAnimationActionOptions options = default)
        {
            EnsureInitialized();
            return m_ActionManager.PlayAction(stateKey, options);
        }

        public XAnimationActionHandle PlayAction(string channelName, string stateKey, XAnimationActionOptions options = default)
        {
            EnsureInitialized();
            return m_ActionManager.PlayAction(channelName, stateKey, options);
        }

        public void Stop(string channelName, float fadeOut = 0)
        {
            EnsureInitialized();
            m_Runtime.Stop(channelName, fadeOut);
        }

        public void StopAll(float fadeOut = 0)
        {
            EnsureInitialized();
            m_Runtime.StopAll(fadeOut);
        }

        #endregion

        #region Controls

        public void SetChannelWeight(string channelName, float weight)
        {
            EnsureInitialized();
            m_Runtime.SetChannelWeight(channelName, weight);
        }

        public float GetChannelWeight(string channelName)
        {
            EnsureInitialized();
            return m_Runtime.GetChannelWeight(channelName);
        }

        public void PauseChannel(string channelName)
        {
            SetChannelPaused(channelName, true);
        }

        public void ResumeChannel(string channelName)
        {
            SetChannelPaused(channelName, false);
        }

        public void SetChannelPaused(string channelName, bool paused)
        {
            EnsureInitialized();
            m_Runtime.SetChannelPaused(channelName, paused);
        }

        public bool IsChannelPaused(string channelName)
        {
            EnsureInitialized();
            return m_Runtime.IsChannelPaused(channelName);
        }

        public bool SeekChannel(string channelName, float normalizedTime)
        {
            EnsureInitialized();
            return m_Runtime.SeekChannel(channelName, normalizedTime);
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            EnsureInitialized();
            m_Runtime.SetRootMotionEnabled(enabled);
        }

        public void Pause()
        {
            SetPaused(true);
        }

        public void Resume()
        {
            SetPaused(false);
        }

        public void SetPaused(bool paused)
        {
            m_Runtime.SetPaused(paused);
        }

        public void SetGlobalSpeed(float speed)
        {
            m_Runtime.SetGlobalSpeed(speed);
        }

        public void SetUpdateMode(XAnimationUpdateMode updateMode)
        {
            m_Runtime.SetUpdateMode(updateMode);
        }

        public void SetUnityAnimationEventsEnabled(bool enabled)
        {
            m_Runtime.SetUnityAnimationEventsEnabled(enabled);
        }

        public void Step(float deltaTime)
        {
            EnsureInitialized();
            EnsureManualUpdateMode(nameof(Step));
            if (deltaTime <= 0f)
            {
                throw new XAnimationException("XAnimation step deltaTime must be greater than 0.");
            }

            m_Scheduler.RunStep(deltaTime);
        }

        public void SyncFrame()
        {
            EnsureInitialized();
            EnsureManualUpdateMode(nameof(SyncFrame));
            m_Runtime.RunManualFrame(0f);
            ProcessActionReturns();
        }

        #endregion

        #region Queries And Preload

        public XAnimationChannelState GetChannelState(string channelName)
        {
            EnsureInitialized();
            return m_Runtime.GetChannelState(channelName);
        }

        public bool TryGetCurrentState(string channelName, out XAnimationChannelState state)
        {
            state = null;
            if (!m_Runtime.IsInitialized)
            {
                return false;
            }

            return m_Runtime.TryGetCurrentState(channelName, out state);
        }

        public bool IsPlaying(string stateKey, string channelName = null)
        {
            EnsureInitialized();
            return m_Runtime.IsPlaying(stateKey, channelName);
        }

        public bool HasState(string stateKey)
        {
            if (!m_Runtime.IsInitialized ||
                string.IsNullOrWhiteSpace(stateKey) ||
                !m_Runtime.CompiledAsset.TryGetStateNodeIndex(stateKey, out int stateNodeIndex))
            {
                return false;
            }

            return m_Runtime.CompiledAsset.StateNodes[stateNodeIndex].IsPlayable;
        }

        public bool HasState(string channelName, string stateKey)
        {
            if (!m_Runtime.IsInitialized ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(stateKey) ||
                !m_Runtime.CompiledAsset.TryGetStateNodeIndex(channelName, stateKey, out int stateNodeIndex))
            {
                return false;
            }

            return m_Runtime.CompiledAsset.StateNodes[stateNodeIndex].IsPlayable;
        }

        public bool HasStateNode(string stateNodeKey)
        {
            return m_Runtime.IsInitialized &&
                   !string.IsNullOrWhiteSpace(stateNodeKey) &&
                   m_Runtime.CompiledAsset.TryGetStateNodeIndex(stateNodeKey, out _);
        }

        public bool HasStateNode(string channelName, string stateNodeKey)
        {
            return m_Runtime.IsInitialized &&
                   !string.IsNullOrWhiteSpace(channelName) &&
                   !string.IsNullOrWhiteSpace(stateNodeKey) &&
                   m_Runtime.CompiledAsset.TryGetStateNodeIndex(channelName, stateNodeKey, out _);
        }

        public float GetStateDuration(string stateKey)
        {
            EnsureInitialized();
            return m_Runtime.GetStateDuration(stateKey);
        }

        public float GetStateDuration(string channelName, string stateKey)
        {
            EnsureInitialized();
            return m_Runtime.GetStateDuration(channelName, stateKey);
        }

        public float GetClipDuration(string clipKey)
        {
            EnsureInitialized();
            return m_Runtime.GetClipDuration(clipKey);
        }

        public void PreloadAll()
        {
            EnsureInitialized();
            m_Runtime.PreloadAll();
        }

        public void PreloadState(string stateKey)
        {
            EnsureInitialized();
            m_Runtime.PreloadState(stateKey);
        }

        public void PreloadState(string channelName, string stateKey)
        {
            EnsureInitialized();
            m_Runtime.PreloadState(channelName, stateKey);
        }

        public bool ShouldApplyNativeRootMotion()
        {
            return m_Runtime.IsInitialized && m_Runtime.ShouldApplyNativeRootMotion();
        }

        public XAnimationDebugGraphSnapshot GetDebugGraphSnapshot()
        {
            if (!m_Runtime.IsInitialized)
            {
                return XAnimationDebugGraphSnapshot.Invalid("XAnimationDriver is not initialized.");
            }

            return m_Runtime.BuildDebugGraphSnapshot();
        }

        #endregion

        #region Internal Runtime Access

        public void TickFromScheduler(float deltaTime)
        {
            m_Scheduler.TickFromScheduler(deltaTime);
        }

        internal void Update(float deltaTime)
        {
            EnsureInitialized();
            m_Runtime.RunManualFrame(deltaTime);
            ProcessActionReturns();
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            m_Scheduler.UnregisterFromAutomaticUpdate();
            m_ActionManager.Dispose();
            m_Runtime.Dispose();
            m_PendingPlaybackExits.Clear();
        }

        #endregion

        #region Playback State Queries

        internal bool TryGetPlaybackState(int playbackId, string channelName, out XAnimationChannelState state)
        {
            state = null;
            if (!m_Runtime.IsInitialized || playbackId <= 0 || string.IsNullOrWhiteSpace(channelName))
            {
                return false;
            }

            if (!m_Runtime.TryGetCurrentState(channelName, out state) || state == null)
            {
                return false;
            }

            if (state.playbackId != playbackId)
            {
                state = null;
                return false;
            }

            return true;
        }

        #endregion

        #region Validation

        private void EnsureInitialized()
        {
            if (!m_Runtime.IsInitialized)
            {
                throw new XAnimationException("XAnimationDriver is not initialized.");
            }
        }

        private static void ValidateAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new XAnimationException("XAnimationDriver assetPath cannot be empty.");
            }
        }

        private static void ValidateAnimationAsset(TextAsset animationAsset)
        {
            if (animationAsset == null)
            {
                throw new XAnimationException("XAnimationDriver animationAsset cannot be null.");
            }
        }

        private static void ValidateCompiledAsset(XAnimationCompiledAsset compiledAsset)
        {
            if (compiledAsset == null)
            {
                throw new XAnimationException("XAnimationDriver compiledAsset cannot be null.");
            }
        }

        private static void ValidateAnimator(Animator animator)
        {
            if (animator == null)
            {
                throw new XAnimationException("XAnimationDriver animator cannot be null.");
            }
        }

        #endregion

        #region Initialization Helpers

        private void InitializeLoadedAsset(XAnimationCompiledAsset compiledAsset, Animator animator)
        {
            m_PendingPlaybackExits.Clear();
            m_ActionManager.Dispose();
            XAnimationContext context = new(compiledAsset.Parameters);
            m_Runtime.Initialize(compiledAsset, context, animator);
            if (compiledAsset.Asset.preload)
            {
                m_Runtime.PreloadAll();
            }
            m_Scheduler.RegisterForAutomaticUpdate();
        }

        #endregion

        #region Event Helpers

        private void RaiseCueTriggered(XAnimationCueEvent cueEvent)
        {
            CueTriggered?.Invoke(cueEvent);
        }

        private void ProcessActionReturns()
        {
            m_ActionManager.ProcessPendingReturns();
        }

        private void RaiseStateEntered(XAnimationStateEvent stateEvent)
        {
            OnStateEnter?.Invoke(stateEvent);
        }

        private void CompletePlaybackExitAndRaise(XAnimationStateEvent stateEvent)
        {
            if (stateEvent != null &&
                stateEvent.playbackId > 0 &&
                m_PendingPlaybackExits.Remove(stateEvent.playbackId, out PendingPlaybackExit pending))
            {
                XAnimationPlaybackExitResult result = new()
                {
                    WasStarted = true,
                    PlaybackId = pending.PlaybackId,
                    ChannelName = pending.ChannelName,
                    RequestedStateKey = pending.RequestedStateKey,
                    RequestedClipKey = pending.RequestedClipKey,
                    IsTemporaryState = pending.IsTemporaryState,
                    ExitReason = stateEvent.exitReason,
                };
                pending.Handle.CompleteExit(result);
            }

            OnStateExit?.Invoke(stateEvent);
        }

        #endregion

        #region Transition And Handle Helpers

        private static XAnimationTransitionOptions NormalizeTransitionOptions(XAnimationTransitionOptions options)
        {
            if (options == null)
            {
                return null;
            }

            return new XAnimationTransitionOptions
            {
                fadeIn = Mathf.Max(0f, options.fadeIn),
                fadeOut = Mathf.Max(0f, options.fadeOut),
                enterTime = Mathf.Clamp01(options.enterTime),
                priority = options.priority,
                interruptible = options.interruptible,
            };
        }

        private void EnsureManualUpdateMode(string operationName)
        {
            if (m_Runtime.UpdateMode != XAnimationUpdateMode.Manual)
            {
                throw new XAnimationException($"XAnimation {operationName} is only supported in Manual update mode.");
            }
        }

        private XAnimationPlaybackHandle CreatePlaybackHandle(XAnimationPlaybackStartInfo startInfo, string requestedStateKey, string requestedClipKey)
        {
            requestedStateKey ??= string.Empty;
            requestedClipKey ??= string.Empty;

            if (!startInfo.Started)
            {
                XAnimationPlaybackExitResult result = new()
                {
                    WasStarted = false,
                    PlaybackId = 0,
                    ChannelName = startInfo.ChannelName,
                    RequestedStateKey = requestedStateKey,
                    RequestedClipKey = requestedClipKey,
                    IsTemporaryState = startInfo.IsTemporaryState,
                    ExitReason = null,
                };
                XAnimationPlaybackHandle failedHandle = new(this, false, 0, startInfo.ChannelName, requestedStateKey, requestedClipKey, startInfo.IsTemporaryState);
                failedHandle.CompleteExit(result);
                return failedHandle;
            }

            XAnimationPlaybackHandle handle = new(this, true, startInfo.PlaybackId, startInfo.ChannelName, requestedStateKey, requestedClipKey, startInfo.IsTemporaryState);
            PendingPlaybackExit pending = new(startInfo.PlaybackId, startInfo.ChannelName, requestedStateKey, requestedClipKey, startInfo.IsTemporaryState, handle);
            m_PendingPlaybackExits[startInfo.PlaybackId] = pending;
            return handle;
        }

        #endregion

    }
}
