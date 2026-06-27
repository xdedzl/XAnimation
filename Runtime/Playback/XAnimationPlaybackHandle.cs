using System;
using System.Collections.Generic;
using UnityEngine;

namespace XAnimationEngine
{
    public sealed class XAnimationPlaybackExitResult
    {
        public bool WasStarted { get; internal set; }
        public int PlaybackId { get; internal set; }
        public string ChannelName { get; internal set; }
        public string RequestedStateKey { get; internal set; }
        public string RequestedClipKey { get; internal set; }
        public bool IsTemporaryState { get; internal set; }
        public XAnimationStateExitReason? ExitReason { get; internal set; }
    }

    public sealed class XAnimationPlaybackHandle
    {
        private readonly XAnimationDriver m_Driver;
        private readonly List<Action<XAnimationPlaybackExitResult>> m_ExitCallbacks = new();
        private XAnimationPlaybackExitResult m_ExitResult;

        internal XAnimationPlaybackHandle(
            XAnimationDriver driver,
            bool isValid,
            int playbackId,
            string channelName,
            string requestedStateKey,
            string requestedClipKey,
            bool isTemporaryState)
        {
            m_Driver = driver;
            IsValid = isValid;
            PlaybackId = playbackId;
            ChannelName = channelName ?? string.Empty;
            RequestedStateKey = requestedStateKey ?? string.Empty;
            RequestedClipKey = requestedClipKey ?? string.Empty;
            IsTemporaryState = isTemporaryState;
        }

        public bool IsValid { get; }
        public int PlaybackId { get; }
        public string ChannelName { get; }
        public string RequestedStateKey { get; }
        public string RequestedClipKey { get; }
        public bool IsTemporaryState { get; }

        public bool IsPlaying
        {
            get
            {
                if (!IsValid || m_Driver == null)
                {
                    return false;
                }

                return m_Driver.TryGetPlaybackState(PlaybackId, ChannelName, out _);
            }
        }

        public bool TryGetState(out XAnimationChannelState state)
        {
            state = null;
            if (!IsValid || m_Driver == null)
            {
                return false;
            }

            return m_Driver.TryGetPlaybackState(PlaybackId, ChannelName, out state);
        }

        public XAnimationPlaybackHandle OnExit(Action<XAnimationPlaybackExitResult> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (m_ExitResult != null)
            {
                InvokeExitCallback(callback, m_ExitResult);
                return this;
            }

            m_ExitCallbacks.Add(callback);
            return this;
        }

        internal void CompleteExit(XAnimationPlaybackExitResult result)
        {
            if (result == null || m_ExitResult != null)
            {
                return;
            }

            m_ExitResult = result;
            if (m_ExitCallbacks.Count == 0)
            {
                return;
            }

            var callbacks = m_ExitCallbacks.ToArray();
            m_ExitCallbacks.Clear();
            foreach (var callback in callbacks)
            {
                InvokeExitCallback(callback, result);
            }
        }

        private static void InvokeExitCallback(
            Action<XAnimationPlaybackExitResult> callback,
            XAnimationPlaybackExitResult result)
        {
            try
            {
                callback(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    internal readonly struct XAnimationPlaybackStartInfo
    {
        public XAnimationPlaybackStartInfo(
            bool started,
            int playbackId,
            string channelName,
            string stateKey,
            string clipKey,
            bool isTemporaryState,
            XAnimationTransitionRejectReason rejectReason)
        {
            Started = started;
            PlaybackId = playbackId;
            ChannelName = channelName ?? string.Empty;
            StateKey = stateKey ?? string.Empty;
            ClipKey = clipKey ?? string.Empty;
            IsTemporaryState = isTemporaryState;
            RejectReason = rejectReason;
        }

        public bool Started { get; }
        public int PlaybackId { get; }
        public string ChannelName { get; }
        public string StateKey { get; }
        public string ClipKey { get; }
        public bool IsTemporaryState { get; }
        public XAnimationTransitionRejectReason RejectReason { get; }

        public static XAnimationPlaybackStartInfo CreateFailed(
            string channelName,
            string stateKey,
            string clipKey,
            bool isTemporaryState,
            XAnimationTransitionRejectReason rejectReason)
        {
            return new XAnimationPlaybackStartInfo(false, 0, channelName, stateKey, clipKey, isTemporaryState, rejectReason);
        }

        public static XAnimationPlaybackStartInfo CreateStarted(XAnimationStatePlaybackInstance playback)
        {
            if (playback == null)
            {
                throw new ArgumentNullException(nameof(playback));
            }

            return new XAnimationPlaybackStartInfo(
                true,
                playback.PlaybackId,
                playback.ChannelName,
                playback.StateKey,
                playback.PrimaryClipKey,
                playback.IsTemporaryState,
                XAnimationTransitionRejectReason.None);
        }
    }

    public struct XAnimationActionOptions
    {
        public XAnimationTransitionOptions transition;
        public bool force;
        public float cancelableAfter;
        public float cancelFadeOut;
        public XAnimationActionReturnMode returnMode;
        public string returnStateKey;
        public XAnimationTransitionOptions returnTransition;
    }

    public enum XAnimationActionReturnMode
    {
        PreviousState = 0,
        None = 1,
        State = 2,
    }

    public enum XAnimationActionStatus
    {
        Rejected,
        Running,
        Completed,
        Canceled,
        Interrupted,
        Stopped,
        Disposed,
    }

    public sealed class XAnimationActionExitResult
    {
        public bool WasStarted { get; internal set; }
        public XAnimationActionStatus Status { get; internal set; }
        public string StateKey { get; internal set; }
        public string ChannelName { get; internal set; }
        public int PlaybackId { get; internal set; }
        public XAnimationStateExitReason? PlaybackExitReason { get; internal set; }
        public bool ReturnStarted { get; internal set; }
    }

    public sealed class XAnimationActionHandle
    {
        private readonly XAnimationActionManager m_Manager;
        private readonly List<Action<XAnimationActionExitResult>> m_ExitCallbacks = new();
        private XAnimationActionExitResult m_ExitResult;
        private bool m_CancelRequested;

        internal XAnimationActionHandle(
            XAnimationActionManager manager,
            int actionId,
            string stateKey,
            string channelName,
            string previousStateKey,
            XAnimationActionOptions options,
            XAnimationPlaybackHandle playbackHandle)
        {
            m_Manager = manager;
            ActionId = actionId;
            StateKey = stateKey ?? string.Empty;
            ChannelName = channelName ?? string.Empty;
            PreviousStateKey = previousStateKey ?? string.Empty;
            Options = options;
            PlaybackHandle = playbackHandle;
            IsValid = playbackHandle != null && playbackHandle.IsValid;
            Status = IsValid ? XAnimationActionStatus.Running : XAnimationActionStatus.Rejected;
        }

        public bool IsValid { get; }
        public XAnimationActionStatus Status { get; internal set; }
        public string StateKey { get; }
        public string ChannelName { get; }
        public XAnimationPlaybackHandle PlaybackHandle { get; }
        public bool CanCancel => m_Manager != null && m_Manager.CanCancel(this);

        internal int ActionId { get; }
        internal string PreviousStateKey { get; }
        internal XAnimationActionOptions Options { get; }
        internal bool CancelRequested => m_CancelRequested;

        public bool Cancel()
        {
            return m_Manager != null && m_Manager.Cancel(this);
        }

        public XAnimationActionHandle OnExit(Action<XAnimationActionExitResult> callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (m_ExitResult != null)
            {
                InvokeExitCallback(callback, m_ExitResult);
                return this;
            }

            m_ExitCallbacks.Add(callback);
            return this;
        }

        internal void MarkCancelRequested()
        {
            m_CancelRequested = true;
        }

        internal void CompleteExit(XAnimationActionExitResult result)
        {
            if (result == null || m_ExitResult != null)
            {
                return;
            }

            m_ExitResult = result;
            Status = result.Status;
            if (m_ExitCallbacks.Count == 0)
            {
                return;
            }

            Action<XAnimationActionExitResult>[] callbacks = m_ExitCallbacks.ToArray();
            m_ExitCallbacks.Clear();
            foreach (Action<XAnimationActionExitResult> callback in callbacks)
            {
                InvokeExitCallback(callback, result);
            }
        }

        private static void InvokeExitCallback(
            Action<XAnimationActionExitResult> callback,
            XAnimationActionExitResult result)
        {
            try
            {
                callback(result);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    internal sealed class XAnimationActionManager
    {
        private readonly XAnimationDriver m_Driver;
        private readonly Dictionary<string, XAnimationActionHandle> m_ActiveActions = new(StringComparer.Ordinal);
        private readonly List<XAnimationActionHandle> m_PendingReturns = new();
        private int m_NextActionId = 1;

        internal XAnimationActionManager(XAnimationDriver driver)
        {
            m_Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        internal XAnimationActionHandle PlayAction(string stateKey, XAnimationActionOptions options)
        {
            XAnimationCompiledState state = m_Driver.CompiledAsset.GetState(stateKey);
            return PlayAction(state, options);
        }

        internal XAnimationActionHandle PlayAction(string channelName, string stateKey, XAnimationActionOptions options)
        {
            XAnimationCompiledState state = m_Driver.CompiledAsset.GetState(channelName, stateKey);
            return PlayAction(state, options);
        }

        private XAnimationActionHandle PlayAction(XAnimationCompiledState state, XAnimationActionOptions options)
        {
            XAnimationCompiledChannel channel = (XAnimationCompiledChannel)m_Driver.CompiledAsset.Channels[state.DefaultChannelIndex];
            string channelName = channel.Name;
            string previousStateKey = ResolvePreviousStateKey(channelName);

            XAnimationPlaybackHandle playbackHandle = m_Driver.PlayState(channelName, state.Key, options.transition, options.force);
            XAnimationActionHandle actionHandle = new(
                this,
                m_NextActionId++,
                state.Key,
                channelName,
                previousStateKey,
                options,
                playbackHandle);

            if (!actionHandle.IsValid)
            {
                actionHandle.CompleteExit(CreateExitResult(actionHandle, XAnimationActionStatus.Rejected, null, false));
                return actionHandle;
            }

            m_ActiveActions[channelName] = actionHandle;
            playbackHandle.OnExit(result => OnPlaybackExit(actionHandle, result));
            return actionHandle;
        }

        internal bool CanCancel(XAnimationActionHandle action)
        {
            if (!IsRunningAction(action) || action.PlaybackHandle == null)
            {
                return false;
            }

            if (!action.PlaybackHandle.TryGetState(out XAnimationChannelState state) || state == null)
            {
                return false;
            }

            return state.normalizedTime >= Mathf.Max(0f, action.Options.cancelableAfter);
        }

        internal bool Cancel(XAnimationActionHandle action)
        {
            if (!CanCancel(action))
            {
                return false;
            }

            action.MarkCancelRequested();
            m_Driver.Stop(action.ChannelName, action.Options.cancelFadeOut);
            ProcessPendingReturns();
            return action.Status == XAnimationActionStatus.Canceled;
        }

        internal void ProcessPendingReturns()
        {
            if (m_PendingReturns.Count == 0)
            {
                return;
            }

            XAnimationActionHandle[] pending = m_PendingReturns.ToArray();
            m_PendingReturns.Clear();
            foreach (XAnimationActionHandle action in pending)
            {
                if (action == null || action.Status == XAnimationActionStatus.Running)
                {
                    continue;
                }

                bool returnStarted = TryStartReturn(action);
                CompleteExitedAction(action, returnStarted);
            }
        }

        internal void Dispose()
        {
            foreach (XAnimationActionHandle action in m_ActiveActions.Values)
            {
                if (action != null && action.Status == XAnimationActionStatus.Running)
                {
                    action.Status = XAnimationActionStatus.Disposed;
                    action.CompleteExit(CreateExitResult(action, XAnimationActionStatus.Disposed, XAnimationStateExitReason.Disposed, false));
                }
            }

            foreach (XAnimationActionHandle action in m_PendingReturns)
            {
                if (action == null)
                {
                    continue;
                }

                action.Status = XAnimationActionStatus.Disposed;
                action.CompleteExit(CreateExitResult(action, XAnimationActionStatus.Disposed, XAnimationStateExitReason.Disposed, false));
            }

            m_ActiveActions.Clear();
            m_PendingReturns.Clear();
        }

        private void OnPlaybackExit(XAnimationActionHandle action, XAnimationPlaybackExitResult playbackResult)
        {
            if (action == null || action.Status != XAnimationActionStatus.Running)
            {
                return;
            }

            XAnimationActionStatus status = ResolveStatus(action, playbackResult);
            action.Status = status;
            if (m_ActiveActions.TryGetValue(action.ChannelName, out XAnimationActionHandle activeAction) &&
                activeAction == action)
            {
                m_ActiveActions.Remove(action.ChannelName);
            }

            if (status == XAnimationActionStatus.Completed || status == XAnimationActionStatus.Canceled)
            {
                m_PendingReturns.Add(action);
                return;
            }

            CompleteExitedAction(action, returnStarted: false);
        }

        private string ResolvePreviousStateKey(string channelName)
        {
            if (m_Driver.TryGetCurrentState(channelName, out XAnimationChannelState state) &&
                state != null &&
                !state.isTemporaryState &&
                !string.IsNullOrWhiteSpace(state.stateKey))
            {
                return state.stateKey;
            }

            return string.Empty;
        }

        private static XAnimationActionStatus ResolveStatus(
            XAnimationActionHandle action,
            XAnimationPlaybackExitResult playbackResult)
        {
            if (action.CancelRequested)
            {
                return XAnimationActionStatus.Canceled;
            }

            return playbackResult?.ExitReason switch
            {
                XAnimationStateExitReason.Completed => XAnimationActionStatus.Completed,
                XAnimationStateExitReason.Stopped => XAnimationActionStatus.Stopped,
                XAnimationStateExitReason.Disposed => XAnimationActionStatus.Disposed,
                _ => XAnimationActionStatus.Interrupted,
            };
        }

        private static XAnimationActionExitResult CreateExitResult(
            XAnimationActionHandle action,
            XAnimationActionStatus status,
            XAnimationStateExitReason? playbackExitReason,
            bool returnStarted)
        {
            return new XAnimationActionExitResult
            {
                WasStarted = action != null && action.IsValid,
                Status = status,
                StateKey = action?.StateKey ?? string.Empty,
                ChannelName = action?.ChannelName ?? string.Empty,
                PlaybackId = action?.PlaybackHandle?.PlaybackId ?? 0,
                PlaybackExitReason = playbackExitReason,
                ReturnStarted = returnStarted,
            };
        }

        private bool TryStartReturn(XAnimationActionHandle action)
        {
            string returnStateKey = ResolveReturnStateKey(action);
            if (string.IsNullOrWhiteSpace(returnStateKey))
            {
                return false;
            }

            XAnimationPlaybackHandle returnHandle = m_Driver.PlayState(action.ChannelName, returnStateKey, action.Options.returnTransition);
            return returnHandle != null && returnHandle.IsValid;
        }

        private static string ResolveReturnStateKey(XAnimationActionHandle action)
        {
            if (action == null)
            {
                return string.Empty;
            }

            return action.Options.returnMode switch
            {
                XAnimationActionReturnMode.PreviousState => action.PreviousStateKey,
                XAnimationActionReturnMode.State => action.Options.returnStateKey ?? string.Empty,
                _ => string.Empty,
            };
        }

        private void CompleteExitedAction(XAnimationActionHandle action, bool returnStarted)
        {
            action.CompleteExit(CreateExitResult(action, action.Status, ResolvePlaybackExitReason(action.Status), returnStarted));
        }

        private static XAnimationStateExitReason? ResolvePlaybackExitReason(XAnimationActionStatus status)
        {
            return status switch
            {
                XAnimationActionStatus.Completed => XAnimationStateExitReason.Completed,
                XAnimationActionStatus.Canceled => XAnimationStateExitReason.Stopped,
                XAnimationActionStatus.Stopped => XAnimationStateExitReason.Stopped,
                XAnimationActionStatus.Disposed => XAnimationStateExitReason.Disposed,
                XAnimationActionStatus.Interrupted => XAnimationStateExitReason.Interrupted,
                _ => null,
            };
        }

        private bool IsRunningAction(XAnimationActionHandle action)
        {
            return action != null &&
                   action.Status == XAnimationActionStatus.Running &&
                   m_ActiveActions.TryGetValue(action.ChannelName, out XAnimationActionHandle activeAction) &&
                   activeAction == action;
        }
    }
}
