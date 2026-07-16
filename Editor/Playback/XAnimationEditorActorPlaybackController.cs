#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    internal sealed class XAnimationEditorActorPlaybackController : IXAnimationPlaybackHudHost, IXAnimationActionDebugHudHost, IDisposable
    {
        private const float StepDeltaTime = 1f / 60f;

        private readonly XAnimationActorInspectorPlaybackSession m_EditModeSession = new();
        private XAnimationPlaybackSettings m_Settings;
        private readonly List<string> m_ActionStateChoices = new();
        private readonly List<string> m_ActionReturnStateChoices = new();
        private XAnimationActor m_Actor;
        private XAnimationAsset m_Asset;
        private int m_AssetInstanceId;
        private readonly List<string> m_ChannelChoices = new();
        private string m_StatusText = "请选择一个 XAnimationActor。";
        private bool m_StatusIsError;
        private bool m_RootMotionEnabled;
        private string m_ActionStateKey;
        private string m_ActionReturnStateKey;
        private XAnimationActionReturnMode m_ActionReturnMode;
        private float m_ActionCancelableAfter;
        private float m_ActionCancelFadeOut;
        private bool m_ActionForce;
        private XAnimationActionHandle m_ActionHandle;
        private XAnimationActionExitResult m_LastActionExitResult;

        public XAnimationEditorActorPlaybackController()
        {
            m_Settings = XAnimationPlaybackSettingsPrefs.Load();
            m_Settings.Speed = XAnimationPlaybackHudView.ClampSpeed(m_Settings.Speed);
        }

        public XAnimationPlaybackSettings Settings => m_Settings;
        public bool PlaybackExpanded
        {
            get => m_Settings.PlaybackSectionExpanded;
            set => m_Settings.PlaybackSectionExpanded = value;
        }

        public bool TransitionExpanded
        {
            get => m_Settings.TransitionSectionExpanded;
            set => m_Settings.TransitionSectionExpanded = value;
        }

        public bool ActionDebugExpanded
        {
            get => m_Settings.ActionDebugSectionExpanded;
            set => m_Settings.ActionDebugSectionExpanded = value;
        }

        public bool ShowRootMotion => true;
        public bool RootMotionEnabled
        {
            get => !Application.isPlaying && m_EditModeSession.IsLoaded
                ? m_EditModeSession.GetRootMotionEnabled()
                : m_RootMotionEnabled;
            set => SetRootMotionEnabled(value);
        }
        public string StatusText => m_StatusText;
        public bool StatusIsError => m_StatusIsError;
        public bool CanPlayOrPause => ResolveDefaultState() != null || TryGetDominantPlaybackState(out _);
        public bool HasPlayback => TryGetDominantPlaybackState(out _);
        public bool IsPaused => Application.isPlaying
            ? m_Actor != null && m_Actor.IsPaused
            : m_EditModeSession.IsLoaded && m_EditModeSession.Matches(m_Actor) && m_EditModeSession.IsPaused;
        public bool CanStep => TryGetDominantPlaybackState(out _);
        public bool CanStop => TryGetDominantPlaybackState(out _) || (!Application.isPlaying && m_EditModeSession.IsLoaded);
        public bool CanSeek => TryGetDominantPlaybackState(out _);
        public float NormalizedTime => TryGetDominantPlaybackState(out XAnimationChannelState state) ? Mathf.Clamp01(state.normalizedTime) : 0f;
        public IReadOnlyList<string> ChannelChoices => m_ChannelChoices;
        public IReadOnlyList<string> ActionStateChoices => GetActionStateChoices(m_ActionStateChoices);
        public string ActionStateKey
        {
            get => m_ActionStateKey;
            set => m_ActionStateKey = value ?? string.Empty;
        }
        public XAnimationActionReturnMode ActionReturnMode
        {
            get => m_ActionReturnMode;
            set => m_ActionReturnMode = value;
        }
        public IReadOnlyList<string> ActionReturnStateChoices => GetActionStateChoices(m_ActionReturnStateChoices);
        public string ActionReturnStateKey
        {
            get => m_ActionReturnStateKey;
            set => m_ActionReturnStateKey = value ?? string.Empty;
        }
        public float ActionCancelableAfter
        {
            get => m_ActionCancelableAfter;
            set => m_ActionCancelableAfter = Mathf.Max(0f, value);
        }
        public float ActionCancelFadeOut
        {
            get => m_ActionCancelFadeOut;
            set => m_ActionCancelFadeOut = Mathf.Max(0f, value);
        }
        public bool ActionForce
        {
            get => m_ActionForce;
            set => m_ActionForce = value;
        }
        public bool CanPlayAction => m_Actor != null && m_Asset != null && !string.IsNullOrWhiteSpace(m_ActionStateKey);
        public bool CanCancelAction => m_ActionHandle != null && m_ActionHandle.CanCancel;
        public string ActionStatusText => BuildActionStatusText();
        public bool ActionStatusIsError => m_ActionHandle != null && m_ActionHandle.Status == XAnimationActionStatus.Rejected;
        public XAnimationActor Actor => m_Actor;
        public XAnimationAsset Asset => m_Asset;
        public bool EditModeSessionLoaded => !Application.isPlaying && m_EditModeSession.IsLoaded;

        public void RefreshSelection()
        {
            XAnimationActor actor = ResolveSelectedActor();
            if (actor != m_Actor)
            {
                ClearActionDebugRuntimeState();
                m_Actor = actor;
                m_Asset = null;
                m_AssetInstanceId = 0;
                m_ChannelChoices.Clear();
            }

            if (Application.isPlaying)
            {
                ReleaseEditModeSession();
            }

            RefreshAssetCache();
            RefreshStatus();
        }

        public void SaveSettings()
        {
            XAnimationPlaybackSettingsPrefs.Save(m_Settings);
        }

        public void SetSpeed(float speed)
        {
            m_Settings.Speed = XAnimationPlaybackHudView.ClampSpeed(speed);
            SaveSettings();
            if (Application.isPlaying && m_Actor != null)
            {
                m_Actor.GlobalSpeed = m_Settings.Speed;
            }
            else if (m_EditModeSession.IsLoaded)
            {
                m_EditModeSession.SetGlobalSpeed(m_Settings.Speed);
            }
        }

        public void SetChannel(string channelName)
        {
            m_Settings.ChannelName = NormalizeChannelName(channelName);
            SaveSettings();
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            m_RootMotionEnabled = enabled;
            if (Application.isPlaying)
            {
                m_Actor?.SetRootMotionEnabled(enabled);
            }
            else if (m_EditModeSession.IsLoaded)
            {
                m_EditModeSession.SetRootMotionEnabled(enabled);
            }

            SetStatus(enabled ? "已开启当前 Actor Root Motion。" : "已关闭当前 Actor Root Motion。");
        }

        public void SetApplyTransition(bool enabled)
        {
            m_Settings.ApplyTransition = enabled;
            SaveSettings();
        }

        public void SetFadeIn(float value)
        {
            m_Settings.FadeIn = Mathf.Max(0f, value);
            SaveSettings();
        }

        public void SetFadeOut(float value)
        {
            m_Settings.FadeOut = Mathf.Max(0f, value);
            SaveSettings();
        }

        public void SetEnterTime(float value)
        {
            m_Settings.EnterTime = Mathf.Clamp01(value);
            SaveSettings();
        }

        public void SetPriority(int value)
        {
            m_Settings.Priority = value;
            SaveSettings();
        }

        public void TogglePlayPause()
        {
            RefreshSelection();
            TryGetDominantPlaybackState(out XAnimationChannelState state);
            if (state == null)
            {
                if (!TryPlayDefaultState())
                {
                    SetStatus("当前没有可播放的 state。", true);
                }
                return;
            }

            try
            {
                if (Application.isPlaying)
                {
                    if (m_Actor == null)
                    {
                        return;
                    }

                    if (m_Actor.IsPaused)
                    {
                        m_Actor.Resume();
                        SetStatus("已继续播放。");
                    }
                    else
                    {
                        m_Actor.Pause();
                        SetStatus("已暂停播放。");
                    }
                }
                else if (m_EditModeSession.IsPaused)
                {
                    m_EditModeSession.Resume();
                    SetStatus("已继续编辑态预览。");
                }
                else
                {
                    m_EditModeSession.Pause();
                    SetStatus("已暂停编辑态预览。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
            }
        }

        public void Step()
        {
            RefreshSelection();
            TryGetDominantPlaybackState(out XAnimationChannelState state);
            if (state == null)
            {
                SetStatus("当前没有可步进的播放项。", true);
                return;
            }

            try
            {
                if (Application.isPlaying)
                {
                    m_Actor.Pause();
                    m_Actor.Step(StepDeltaTime);
                }
                else
                {
                    m_EditModeSession.Step(StepDeltaTime);
                }

                SetStatus("已向后推进一帧。");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
            }
        }

        public void StopAll()
        {
            RefreshSelection();
            try
            {
                if (Application.isPlaying)
                {
                    m_Actor?.StopAll(0f);
                }
                else
                {
                    m_EditModeSession.StopAll(restorePose: true);
                }

                SetStatus("已停止全部 channel。");
                XAnimationSceneOverlaySelection.RequestRepaint();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
            }
        }

        public void Seek(float normalizedTime)
        {
            RefreshSelection();
            TryGetDominantPlaybackState(out XAnimationChannelState state);
            if (state == null || string.IsNullOrWhiteSpace(state.channelName))
            {
                return;
            }

            try
            {
                float time = Mathf.Clamp01(normalizedTime);
                if (Application.isPlaying)
                {
                    m_Actor.Pause();
                    if (m_Actor.SeekChannel(state.channelName, time) &&
                        m_Actor.UpdateMode == XAnimationUpdateMode.Manual)
                    {
                        m_Actor.SyncFrame();
                    }
                }
                else
                {
                    m_EditModeSession.SetPaused(true);
                    m_EditModeSession.SeekChannel(state.channelName, time);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
            }
        }

        public void PlayAction()
        {
            RefreshSelection();
            EnsureActionStateSelection();
            if (string.IsNullOrWhiteSpace(m_ActionStateKey))
            {
                SetStatus("请选择 Action state。", true);
                return;
            }

            if (m_ActionReturnMode == XAnimationActionReturnMode.State)
            {
                EnsureActionReturnStateSelection();
                if (string.IsNullOrWhiteSpace(m_ActionReturnStateKey))
                {
                    SetStatus("returnMode = State 时需要选择 returnState。", true);
                    return;
                }
            }

            XAnimationActionOptions options = new()
            {
                transition = BuildTransitionOptions(),
                force = m_ActionForce,
                cancelableAfter = Mathf.Max(0f, m_ActionCancelableAfter),
                cancelFadeOut = Mathf.Max(0f, m_ActionCancelFadeOut),
                returnMode = m_ActionReturnMode,
                returnStateKey = m_ActionReturnMode == XAnimationActionReturnMode.State ? m_ActionReturnStateKey : null,
                returnTransition = null,
            };

            try
            {
                m_LastActionExitResult = null;
                if (Application.isPlaying)
                {
                    if (m_Actor == null)
                    {
                        SetStatus("请选择一个 XAnimationActor。", true);
                        return;
                    }

                    m_Actor.GlobalSpeed = m_Settings.Speed;
                    m_ActionHandle = m_Actor.PlayAction(m_ActionStateKey, options);
                }
                else
                {
                    m_EditModeSession.EnsureLoaded(m_Actor);
                    m_EditModeSession.SetGlobalSpeed(m_Settings.Speed);
                    m_EditModeSession.SetRootMotionEnabled(m_RootMotionEnabled);
                    m_ActionHandle = m_EditModeSession.PlayAction(m_Actor, m_ActionStateKey, options);
                }

                m_ActionHandle.OnExit(result =>
                {
                    m_LastActionExitResult = result;
                    XAnimationSceneOverlaySelection.RequestRepaint();
                });

                SetStatus(m_ActionHandle.IsValid
                    ? $"正在 PlayAction {m_ActionStateKey}。"
                    : $"PlayAction {m_ActionStateKey} 被拒绝。", !m_ActionHandle.IsValid);
                XAnimationSceneOverlaySelection.RequestRepaint();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
            }
        }

        public void CancelAction()
        {
            if (m_ActionHandle == null || !m_ActionHandle.IsValid)
            {
                SetStatus("当前没有有效的 Action handle。", true);
                return;
            }

            bool canceled = m_ActionHandle.Cancel();
            SetStatus(canceled ? "已请求取消 Action。" : "当前 Action 尚不可取消。", !canceled);
            XAnimationSceneOverlaySelection.RequestRepaint();
        }

        public bool TrySetParameter(string key, float value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    m_Actor?.SetParameter(key, value);
                }
                else
                {
                    m_EditModeSession.SetParameter(key, value);
                }

                SetStatus($"{key} = {value:0.###}");
                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool TrySetParameter(string key, int value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    m_Actor?.SetParameter(key, value);
                }
                else
                {
                    m_EditModeSession.SetParameter(key, value);
                }

                SetStatus($"{key} = {value}");
                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool TrySetParameter(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    m_Actor?.SetParameter(key, value);
                }
                else
                {
                    m_EditModeSession.SetParameter(key, value);
                }

                SetStatus($"{key} = {value}");
                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool TrySetTrigger(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    m_Actor?.SetTrigger(key);
                }
                else if (m_EditModeSession.IsLoaded)
                {
                    m_EditModeSession.SetTrigger(key);
                }

                SetStatus($"Trigger {key} 已触发。");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool TryGetParameter(string key, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (Application.isPlaying)
            {
                return m_Actor != null && m_Actor.TryGetParameter(key, out value);
            }

            return m_EditModeSession.TryGetParameter(key, out value);
        }

        public bool TryGetParameter(string key, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (Application.isPlaying)
            {
                return m_Actor != null && m_Actor.TryGetParameter(key, out value);
            }

            return m_EditModeSession.TryGetParameter(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (Application.isPlaying)
            {
                return m_Actor != null && m_Actor.TryGetParameter(key, out value);
            }

            return m_EditModeSession.TryGetParameter(key, out value);
        }

        public bool TryGetDominantPlaybackState(out XAnimationChannelState dominantState)
        {
            dominantState = null;
            if (m_Asset?.channels == null)
            {
                return false;
            }

            float bestWeight = -1f;
            for (int i = 0; i < m_Asset.channels.Length; i++)
            {
                string channelName = m_Asset.channels[i]?.name;
                XAnimationChannelState state = GetChannelState(channelName);
                if (state == null)
                {
                    continue;
                }

                float weight = Mathf.Max(state.weight, state.channelWeight);
                XAnimationBlendClipState[] blendClips = state.blendClips;
                if (blendClips != null)
                {
                    for (int j = 0; j < blendClips.Length; j++)
                    {
                        if (blendClips[j] != null)
                        {
                            weight = Mathf.Max(weight, blendClips[j].weight);
                        }
                    }
                }

                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    dominantState = state;
                }
            }

            return dominantState != null;
        }

        public XAnimationStateConfig FindStateConfig(string stateKey)
        {
            return FindStateConfig(null, stateKey);
        }

        public XAnimationStateConfig FindStateConfig(string channelName, string stateKey)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return null;
            }

            IReadOnlyList<XAnimationStateNodeLocation> nodes = XAnimationStateNodeUtility.GetLocations(m_Asset);
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationStateNodeLocation node = nodes[i];
                if (node.Node.kind == XAnimationStateNodeKind.State &&
                    (string.IsNullOrWhiteSpace(channelName) || string.Equals(node.Channel.name, channelName, StringComparison.Ordinal)) &&
                    string.Equals(node.Key, stateKey, StringComparison.Ordinal))
                {
                    return node.Node.state;
                }
            }

            return null;
        }

        public bool TryGetPlayingBlendState(out XAnimationStateConfig stateConfig)
        {
            stateConfig = null;
            if (!TryGetDominantPlaybackState(out XAnimationChannelState channelState) ||
                string.IsNullOrWhiteSpace(channelState.stateKey))
            {
                return false;
            }

            stateConfig = FindStateConfig(channelState.channelName, channelState.stateKey);
            return IsBlendState(stateConfig);
        }

        public bool ToggleStatePlayback(XAnimationActor actor, XAnimationStateConfig state, float speed, XAnimationTransitionOptions transition)
        {
            if (actor == null || state == null)
            {
                return false;
            }

            RefreshSelection();
            if (m_Actor != actor)
            {
                SetStatus("Scene Overlay 只控制当前选中的 XAnimationActor。", true);
                return false;
            }

            try
            {
                XAnimationStateNodeLocation location = XAnimationEditorStateNodeUtility.GetStateLocation(m_Asset, state);
                return ToggleStatePlayback(actor, location.Channel.name, location.Key, speed, transition);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool ToggleStatePlayback(XAnimationActor actor, string channelName, string stateKey, float speed, XAnimationTransitionOptions transition)
        {
            if (actor == null || string.IsNullOrWhiteSpace(channelName) || string.IsNullOrWhiteSpace(stateKey))
            {
                return false;
            }

            RefreshSelection();
            if (m_Actor != actor)
            {
                SetStatus("Scene Overlay 只控制当前选中的 XAnimationActor。", true);
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    XAnimationChannelState channelState = TryGetActorChannelState(actor, channelName, out XAnimationChannelState runtimeState) ? runtimeState : null;
                    bool isPlaying = channelState != null && string.Equals(channelState.stateKey, stateKey, StringComparison.Ordinal);
                    if (isPlaying)
                    {
                        actor.Stop(channelName, 0f);
                        SetStatus($"已停止 state {stateKey}。");
                    }
                    else
                    {
                        actor.GlobalSpeed = XAnimationPlaybackHudView.ClampSpeed(speed);
                        actor.PlayState(channelName, stateKey, transition);
                        SetStatus($"正在播放 state {stateKey}。");
                    }
                }
                else
                {
                    m_EditModeSession.EnsureLoaded(actor);
                    m_EditModeSession.SetRootMotionEnabled(m_RootMotionEnabled);
                    XAnimationChannelState channelState = m_EditModeSession.GetChannelState(channelName);
                    bool isPlaying = channelState != null && string.Equals(channelState.stateKey, stateKey, StringComparison.Ordinal);
                    if (isPlaying)
                    {
                        m_EditModeSession.StopAll(restorePose: true);
                        SetStatus($"已停止 state {stateKey}，并恢复编辑态姿势。");
                    }
                    else
                    {
                        m_EditModeSession.SetGlobalSpeed(XAnimationPlaybackHudView.ClampSpeed(speed));
                        m_EditModeSession.PlayState(actor, channelName, stateKey, transition);
                        SetStatus($"正在当前 Actor 上预览 state {stateKey}。");
                    }
                }

                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public bool ToggleClipPlayback(XAnimationActor actor, XAnimationClipConfig clip, string channelName, float speed, XAnimationTransitionOptions transition)
        {
            if (actor == null || clip == null || string.IsNullOrWhiteSpace(channelName))
            {
                return false;
            }

            RefreshSelection();
            if (m_Actor != actor)
            {
                SetStatus("Scene Overlay 只控制当前选中的 XAnimationActor。", true);
                return false;
            }

            try
            {
                if (Application.isPlaying)
                {
                    XAnimationChannelState channelState = TryGetActorChannelState(actor, channelName, out XAnimationChannelState runtimeState) ? runtimeState : null;
                    bool isPlaying = channelState != null && string.Equals(channelState.clipKey, clip.key, StringComparison.Ordinal);
                    if (isPlaying)
                    {
                        actor.Stop(channelName, 0f);
                        SetStatus($"已停止 clip {clip.key}。");
                    }
                    else
                    {
                        actor.GlobalSpeed = XAnimationPlaybackHudView.ClampSpeed(speed);
                        actor.PlayClip(clip.key, channelName, transition);
                        SetStatus($"正在 {channelName} 播放 clip {clip.key}。");
                    }
                }
                else
                {
                    m_EditModeSession.EnsureLoaded(actor);
                    m_EditModeSession.SetRootMotionEnabled(m_RootMotionEnabled);
                    XAnimationChannelState channelState = m_EditModeSession.GetChannelState(channelName);
                    bool isPlaying = channelState != null && string.Equals(channelState.clipKey, clip.key, StringComparison.Ordinal);
                    if (isPlaying)
                    {
                        m_EditModeSession.StopAll(restorePose: true);
                        SetStatus($"已停止 clip {clip.key}，并恢复编辑态姿势。");
                    }
                    else
                    {
                        m_EditModeSession.SetGlobalSpeed(XAnimationPlaybackHudView.ClampSpeed(speed));
                        m_EditModeSession.PlayClip(actor, clip.key, channelName, transition);
                        SetStatus($"正在当前 Actor 的 {channelName} 预览 clip {clip.key}。");
                    }
                }

                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        public void Dispose()
        {
            ReleaseEditModeSession();
        }

        private IReadOnlyList<string> GetActionStateChoices(List<string> choices)
        {
            choices.Clear();
            IReadOnlyList<XAnimationStateNodeLocation> nodes = XAnimationStateNodeUtility.GetLocations(m_Asset);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node.kind != XAnimationStateNodeKind.Normal)
                {
                    choices.Add(nodes[i].Key);
                }
            }

            return choices;
        }

        private void EnsureActionStateSelection()
        {
            IReadOnlyList<XAnimationStateNodeLocation> nodes = XAnimationStateNodeUtility.GetLocations(m_Asset);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node.kind != XAnimationStateNodeKind.Normal &&
                    string.Equals(nodes[i].Key, m_ActionStateKey, StringComparison.Ordinal))
                {
                    return;
                }
            }

            m_ActionStateKey = FindFirstActionStateKey(nodes);
        }

        private void EnsureActionReturnStateSelection()
        {
            IReadOnlyList<XAnimationStateNodeLocation> nodes = XAnimationStateNodeUtility.GetLocations(m_Asset);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node.kind != XAnimationStateNodeKind.Normal &&
                    string.Equals(nodes[i].Key, m_ActionReturnStateKey, StringComparison.Ordinal))
                {
                    return;
                }
            }

            m_ActionReturnStateKey = FindFirstActionStateKey(nodes);
        }

        private static string FindFirstActionStateKey(IReadOnlyList<XAnimationStateNodeLocation> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Node.kind != XAnimationStateNodeKind.Normal)
                {
                    return nodes[i].Key;
                }
            }

            return string.Empty;
        }

        private void ClearActionDebugRuntimeState()
        {
            m_ActionHandle = null;
            m_LastActionExitResult = null;
        }

        private string BuildActionStatusText()
        {
            if (m_ActionHandle == null)
            {
                return "Status: no action";
            }

            string channelName = string.IsNullOrWhiteSpace(m_ActionHandle.ChannelName)
                ? "-"
                : m_ActionHandle.ChannelName;
            string stateKey = string.IsNullOrWhiteSpace(m_ActionHandle.StateKey)
                ? "-"
                : m_ActionHandle.StateKey;
            string statusText = $"Status: {m_ActionHandle.Status} | State: {stateKey} | Channel: {channelName} | CanCancel: {m_ActionHandle.CanCancel}";
            if (m_LastActionExitResult != null)
            {
                statusText += $" | Return: {m_LastActionExitResult.ReturnStarted}";
            }

            return statusText;
        }

        private bool TryPlayDefaultState()
        {
            XAnimationStateConfig state = ResolveDefaultState();
            if (m_Actor == null || state == null)
            {
                return false;
            }

            try
            {
                XAnimationStateNodeLocation location = XAnimationEditorStateNodeUtility.GetStateLocation(m_Asset, state);
                if (Application.isPlaying)
                {
                    m_Actor.GlobalSpeed = m_Settings.Speed;
                    m_Actor.PlayState(location.Channel.name, location.Key, BuildTransitionOptions());
                    SetStatus($"正在播放 state {location.Key}。");
                    XAnimationSceneOverlaySelection.RequestRepaint();
                    return true;
                }

                m_EditModeSession.EnsureLoaded(m_Actor);
                m_EditModeSession.SetGlobalSpeed(m_Settings.Speed);
                m_EditModeSession.SetRootMotionEnabled(m_RootMotionEnabled);
                m_EditModeSession.PlayState(m_Actor, location.Channel.name, location.Key, BuildTransitionOptions());
                SetStatus($"正在当前 Actor 上预览 state {location.Key}。");
                XAnimationSceneOverlaySelection.RequestRepaint();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, m_Actor);
                SetStatus(ex.Message, true);
                return false;
            }
        }

        private XAnimationTransitionOptions BuildTransitionOptions()
        {
            XAnimationTransitionOptions transition = new()
            {
                interruptible = true,
            };

            if (m_Settings.ApplyTransition)
            {
                transition.fadeIn = Mathf.Max(0f, m_Settings.FadeIn);
                transition.fadeOut = Mathf.Max(0f, m_Settings.FadeOut);
                transition.priority = m_Settings.Priority;
                transition.interruptible = m_Settings.Interruptible;
                transition.enterTime = Mathf.Clamp01(m_Settings.EnterTime);
            }

            return transition;
        }

        private XAnimationStateConfig ResolveDefaultState()
        {
            XAnimationStateConfig[] states = m_Asset == null
                ? Array.Empty<XAnimationStateConfig>()
                : XAnimationEditorStateNodeUtility.GetStates(m_Asset);
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] != null)
                {
                    return states[i];
                }
            }

            return null;
        }

        public XAnimationChannelState GetChannelState(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return null;
            }

            return Application.isPlaying
                ? TryGetActorChannelState(m_Actor, channelName, out XAnimationChannelState state) ? state : null
                : m_EditModeSession.IsLoaded && m_EditModeSession.Matches(m_Actor)
                    ? m_EditModeSession.GetChannelState(channelName)
                    : null;
        }

        private void RefreshAssetCache()
        {
            int instanceId = m_Actor != null && m_Actor.AnimationAsset != null ? m_Actor.AnimationAsset.GetInstanceID() : 0;
            if (m_Asset != null && m_AssetInstanceId == instanceId)
            {
                return;
            }

            m_AssetInstanceId = instanceId;
            m_Asset = null;
            m_EditModeSession.ClearParameterOverrides();
            m_ChannelChoices.Clear();
            if (m_Actor?.AnimationAsset == null)
            {
                return;
            }

            XAnimationOverrideAsset overrideAsset = m_Actor.AnimationAsset.ToXAnimationAsset<XAnimationOverrideAsset>();
            if (overrideAsset != null && !string.IsNullOrWhiteSpace(overrideAsset.baseAssetPath))
            {
                TextAsset baseTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(overrideAsset.baseAssetPath);
                m_Asset = baseTextAsset == null ? null : baseTextAsset.ToXAnimationAsset<XAnimationAsset>();
            }
            else
            {
                m_Asset = m_Actor.AnimationAsset.ToXAnimationAsset<XAnimationAsset>();
            }

            m_RootMotionEnabled = m_Asset != null && m_Asset.rootMotion;
            XAnimationChannelConfig[] channels = m_Asset?.channels ?? Array.Empty<XAnimationChannelConfig>();
            for (int i = 0; i < channels.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(channels[i]?.name))
                {
                    m_ChannelChoices.Add(channels[i].name);
                }
            }

            m_Settings.ChannelName = NormalizeChannelName(m_Settings.ChannelName);
        }

        private string NormalizeChannelName(string channelName)
        {
            if (!string.IsNullOrWhiteSpace(channelName) && m_ChannelChoices.Contains(channelName))
            {
                return channelName;
            }

            return m_ChannelChoices.Count > 0 ? m_ChannelChoices[0] : string.Empty;
        }

        private void RefreshStatus()
        {
            if (m_Actor == null)
            {
                return;
            }

            if (m_Actor.AnimationAsset == null)
            {
                SetStatus("当前 XAnimationActor 没有绑定 animation asset。", true);
                return;
            }

            if (!Application.isPlaying && !m_EditModeSession.CanPreviewActor(m_Actor, out string message))
            {
                SetStatus(message, true);
                return;
            }

            TryGetDominantPlaybackState(out XAnimationChannelState state);
            if (state != null)
            {
                string item = !string.IsNullOrWhiteSpace(state.stateKey) ? state.stateKey : state.clipKey;
                SetStatus($"{state.channelName} | {item} | {state.normalizedTime:0.000}");
                return;
            }

            SetStatus(Application.isPlaying
                ? "Play Mode 下控制真实 XAnimationActor。"
                : "Edit Mode 下控制当前场景 Actor 的临时预览。");
        }

        private void SetStatus(string text, bool isError = false)
        {
            if (isError)
            {
                Debug.LogError(text);
            }

            m_StatusText = text ?? string.Empty;
            m_StatusIsError = false;
        }

        private void ReleaseEditModeSession()
        {
            if (m_EditModeSession.IsLoaded)
            {
                m_EditModeSession.Dispose();
            }
        }

        private static XAnimationActor ResolveSelectedActor()
        {
            return XAnimationSceneOverlaySelection.TryGetSelectedSceneActor(out XAnimationActor actor) ? actor : null;
        }

        private static bool TryGetActorChannelState(XAnimationActor actor, string channelName, out XAnimationChannelState state)
        {
            state = null;
            return actor != null &&
                   !string.IsNullOrWhiteSpace(channelName) &&
                   actor.TryGetCurrentState(channelName, out state);
        }

        private static bool IsBlendState(XAnimationStateConfig state)
        {
            return state != null &&
                   (state.stateType == XAnimationStateType.Blend1D ||
                    state.stateType == XAnimationStateType.Blend2DSimpleDirectional ||
                    state.stateType == XAnimationStateType.Blend2DFreeformDirectional);
        }
    }
}
#endif
