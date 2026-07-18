#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    public sealed partial class XAnimationPreviewWindow
    {
        private sealed class PreviewPlaybackHudHost : IXAnimationPlaybackHudHost, IXAnimationActionDebugHudHost, IXAnimationChannelPlaybackHudHost
        {
            private readonly XAnimationPreviewWindow m_Window;
            private readonly XAnimationPlaybackSettings m_Settings = new();
            private readonly List<string> m_ChannelChoices = new();
            private readonly List<string> m_ActionStateChoices = new();

            public PreviewPlaybackHudHost(XAnimationPreviewWindow window)
            {
                m_Window = window;
            }

            public XAnimationPlaybackSettings Settings
            {
                get
                {
                    m_Settings.PlaybackSectionExpanded = m_Window.m_PlaybackSectionExpanded;
                    m_Settings.PlayingAnimationsSectionExpanded = m_Window.m_PlayingAnimationsSectionExpanded;
                    m_Settings.TransitionSectionExpanded = m_Window.m_PlayTransitionSectionExpanded;
                    m_Settings.ActionDebugSectionExpanded = m_Window.m_ActionDebugSectionExpanded;
                    m_Settings.ChannelName = m_Window.m_PlayTargetChannelName;
                    m_Settings.Speed = m_Window.GetPlaybackSpeed();
                    m_Settings.ApplyTransition = m_Window.m_ApplyTransitionRequestOverrides;
                    m_Settings.FadeIn = m_Window.m_PlayFadeInOverride;
                    m_Settings.FadeOut = m_Window.m_PlayFadeOutOverride;
                    m_Settings.EnterTime = m_Window.m_PlayEnterTimeOverride;
                    m_Settings.Priority = m_Window.m_PlayPriorityOverride;
                    m_Settings.Interruptible = m_Window.m_PlayInterruptibleOverride;
                    return m_Settings;
                }
            }

            public bool PlaybackExpanded
            {
                get => m_Window.m_PlaybackSectionExpanded;
                set => m_Window.m_PlaybackSectionExpanded = value;
            }

            public bool TransitionExpanded
            {
                get => m_Window.m_PlayTransitionSectionExpanded;
                set => m_Window.m_PlayTransitionSectionExpanded = value;
            }

            public bool PlayingAnimationsExpanded
            {
                get => m_Window.m_PlayingAnimationsSectionExpanded;
                set => m_Window.m_PlayingAnimationsSectionExpanded = value;
            }

            public bool ActionDebugExpanded
            {
                get => m_Window.m_ActionDebugSectionExpanded;
                set => m_Window.m_ActionDebugSectionExpanded = value;
            }

            public bool ShowRootMotion => true;
            public bool RootMotionEnabled
            {
                get => m_Window.m_PreviewRootMotionEnabled;
                set => SetRootMotionEnabled(value);
            }

            public string StatusText => string.Empty;
            public bool StatusIsError => false;
            public bool CanPlayOrPause => HasPlayback || CanPlayFirstState;
            public bool HasPlayback => m_Window.HasAnyPlayingChannel();
            public bool IsPaused => m_Window.m_IsPaused;
            public bool CanStep => m_Window.m_Session != null && m_Window.m_Session.IsLoaded && HasPlayback;
            public bool CanStop => HasPlayback;
            public bool CanSeek => m_Window.m_Session != null && m_Window.m_Session.IsLoaded && m_Window.TryGetDominantPlaybackState(out _);
            public float NormalizedTime => m_Window.TryGetDominantPlaybackState(out XAnimationChannelState state)
                ? Mathf.Clamp01(state.normalizedTime)
                : 0f;
            public bool CanControlSelectedChannel => HasSelectedChannel;
            public bool CanPlaySelectedChannel => HasSelectedChannelPlayback
                ? m_Window.m_Session.IsPaused || m_Window.m_Session.IsChannelPaused(m_Window.m_PlayTargetChannelName)
                : HasSelectedChannel && m_Window.FindFirstSelectedChannelState() != null;
            public bool CanPauseSelectedChannel => HasSelectedChannelPlayback &&
                                                   !m_Window.m_Session.IsPaused &&
                                                   !m_Window.m_Session.IsChannelPaused(m_Window.m_PlayTargetChannelName);
            public bool CanStopSelectedChannel => HasSelectedChannelPlayback;
            public float SelectedChannelWeight => HasSelectedChannel
                ? m_Window.m_Session.GetChannelWeight(m_Window.m_PlayTargetChannelName)
                : 0f;

            public IReadOnlyList<string> ChannelChoices
            {
                get
                {
                    m_ChannelChoices.Clear();
                    if (m_Window.m_Session != null && m_Window.m_Session.IsLoaded)
                    {
                        IReadOnlyList<XAnimationCompiledChannel> channels = m_Window.m_Session.CompiledAsset.Channels;
                        for (int i = 0; i < channels.Count; i++)
                        {
                            m_ChannelChoices.Add(channels[i].Name);
                        }
                    }

                    return m_ChannelChoices;
                }
            }
            public XAnimationDebugGraphSnapshot DebugGraphSnapshot => m_Window.m_Session != null
                ? m_Window.m_Session.GetDebugGraphSnapshot()
                : XAnimationDebugGraphSnapshot.Invalid("XAnimation Preview 尚未加载。");

            public IReadOnlyList<string> ActionStateChoices => GetActionStateChoices();
            public string ActionStateKey
            {
                get => m_Window.m_ActionStateKey;
                set => m_Window.m_ActionStateKey = value ?? string.Empty;
            }
            public XAnimationActionReturnMode ActionReturnMode
            {
                get => m_Window.m_ActionReturnMode;
                set => m_Window.m_ActionReturnMode = value;
            }
            public IReadOnlyList<string> ActionReturnStateChoices => GetActionStateChoices();
            public string ActionReturnStateKey
            {
                get => m_Window.m_ActionReturnStateKey;
                set => m_Window.m_ActionReturnStateKey = value ?? string.Empty;
            }
            public float ActionCancelableAfter
            {
                get => m_Window.m_ActionCancelableAfter;
                set => m_Window.m_ActionCancelableAfter = Mathf.Max(0f, value);
            }
            public float ActionCancelFadeOut
            {
                get => m_Window.m_ActionCancelFadeOut;
                set => m_Window.m_ActionCancelFadeOut = Mathf.Max(0f, value);
            }
            public bool ActionForce
            {
                get => m_Window.m_ActionForce;
                set => m_Window.m_ActionForce = value;
            }
            public bool CanPlayAction => m_Window.m_Session != null &&
                                         m_Window.m_Session.IsLoaded &&
                                         !string.IsNullOrWhiteSpace(m_Window.m_ActionStateKey);
            public bool CanCancelAction => m_Window.m_ActionHandle != null && m_Window.m_ActionHandle.CanCancel;
            public string ActionStatusText => m_Window.BuildActionStatusText();
            public bool ActionStatusIsError => m_Window.m_ActionHandle != null && m_Window.m_ActionHandle.Status == XAnimationActionStatus.Rejected;

            private bool CanPlayFirstState => m_Window.m_Session != null &&
                                              m_Window.m_Session.IsLoaded &&
                                              m_Window.m_Session.CompiledAsset?.States != null &&
                                              m_Window.m_Session.CompiledAsset.States.Count > 0;
            private bool HasSelectedChannel => m_Window.m_Session != null &&
                                               m_Window.m_Session.IsLoaded &&
                                               !string.IsNullOrWhiteSpace(m_Window.m_PlayTargetChannelName);
            private bool HasSelectedChannelPlayback => HasSelectedChannel &&
                                                       m_Window.m_Session.GetChannelState(m_Window.m_PlayTargetChannelName) != null;

            public void SaveSettings()
            {
                m_Window.SavePlaybackPrefs();
            }

            public void SetSpeed(float speed)
            {
                m_Window.SetPlaybackSpeed(speed);
            }

            public void SetChannel(string channelName)
            {
                m_Window.SetPlaybackTargetChannel(channelName);
            }

            public void SetSelectedChannelWeight(float weight)
            {
                m_Window.SetSelectedChannelWeight(weight);
            }

            public void PlaySelectedChannel()
            {
                m_Window.PlaySelectedChannel();
            }

            public void PauseSelectedChannel()
            {
                m_Window.PauseSelectedChannel();
            }

            public void StopSelectedChannel()
            {
                m_Window.StopSelectedChannel();
            }

            public void SetRootMotionEnabled(bool enabled)
            {
                m_Window.m_PreviewRootMotionEnabled = enabled;
                if (m_Window.m_Session != null && m_Window.m_Session.IsLoaded)
                {
                    m_Window.m_Session.SetRootMotionEnabled(enabled);
                    m_Window.RenderPreview();
                }
            }

            public void SetApplyTransition(bool enabled)
            {
                m_Window.m_ApplyTransitionRequestOverrides = enabled;
                m_Window.SavePlaybackPrefs();
            }

            public void SetFadeIn(float value)
            {
                m_Window.m_PlayFadeInOverride = Mathf.Max(0f, value);
                m_Window.SavePlaybackPrefs();
            }

            public void SetFadeOut(float value)
            {
                m_Window.m_PlayFadeOutOverride = Mathf.Max(0f, value);
                m_Window.SavePlaybackPrefs();
            }

            public void SetEnterTime(float value)
            {
                m_Window.m_PlayEnterTimeOverride = Mathf.Clamp01(value);
                m_Window.SavePlaybackPrefs();
            }

            public void SetPriority(int value)
            {
                m_Window.m_PlayPriorityOverride = value;
                m_Window.SavePlaybackPrefs();
            }

            public void TogglePlayPause()
            {
                m_Window.TogglePause();
            }

            public void Step()
            {
                m_Window.StepForward();
            }

            public void StopAll()
            {
                m_Window.StopAllClips();
            }

            public void Seek(float normalizedTime)
            {
                m_Window.SeekDominantPlayback(normalizedTime);
            }

            public void PlayAction()
            {
                m_Window.PlayPreviewAction();
            }

            public void CancelAction()
            {
                m_Window.CancelPreviewAction();
            }

            private IReadOnlyList<string> GetActionStateChoices()
            {
                m_ActionStateChoices.Clear();
                m_ActionStateChoices.AddRange(m_Window.CollectStateKeyChoices());
                return m_ActionStateChoices;
            }
        }
    }
}
#endif
