#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    internal interface IXAnimationPlaybackHudHost
    {
        XAnimationPlaybackSettings Settings { get; }
        bool PlaybackExpanded { get; set; }
        bool TransitionExpanded { get; set; }
        bool ShowRootMotion { get; }
        bool RootMotionEnabled { get; set; }
        string StatusText { get; }
        bool StatusIsError { get; }
        bool CanPlayOrPause { get; }
        bool HasPlayback { get; }
        bool IsPaused { get; }
        bool CanStep { get; }
        bool CanStop { get; }
        bool CanSeek { get; }
        float NormalizedTime { get; }
        IReadOnlyList<string> ChannelChoices { get; }

        void SaveSettings();
        void SetSpeed(float speed);
        void SetChannel(string channelName);
        void SetRootMotionEnabled(bool enabled);
        void SetApplyTransition(bool enabled);
        void SetFadeIn(float value);
        void SetFadeOut(float value);
        void SetEnterTime(float value);
        void SetPriority(int value);
        void TogglePlayPause();
        void Step();
        void StopAll();
        void Seek(float normalizedTime);
    }

    internal interface IXAnimationActionDebugHudHost
    {
        bool ActionDebugExpanded { get; set; }
        IReadOnlyList<string> ActionStateChoices { get; }
        string ActionStateKey { get; set; }
        XAnimationActionReturnMode ActionReturnMode { get; set; }
        IReadOnlyList<string> ActionReturnStateChoices { get; }
        string ActionReturnStateKey { get; set; }
        float ActionCancelableAfter { get; set; }
        float ActionCancelFadeOut { get; set; }
        bool ActionForce { get; set; }
        bool CanPlayAction { get; }
        bool CanCancelAction { get; }
        string ActionStatusText { get; }
        bool ActionStatusIsError { get; }

        void PlayAction();
        void CancelAction();
    }

    internal sealed class XAnimationPlaybackHudView
    {
        public const float SpeedMin = 0.1f;
        public const float SpeedMax = 2f;
        public const float ScrubberWidth = 132f;
        public const float SpeedControlWidth = 96f;
        public const float ToolbarButtonSize = 20f;
        public const float MainFieldLabelWidth = 68f;
        public const float MainFieldValueWidth = 112f;
        public const float TransitionFieldLabelWidth = 58f;
        public const float TransitionFieldValueWidth = 64f;

        private readonly IXAnimationPlaybackHudHost m_Host;
        private readonly IXAnimationActionDebugHudHost m_ActionHost;
        private readonly bool m_IncludeStatus;
        private bool m_IsScrubbing;

        private Label m_StatusLabel;
        private VisualElement m_Scrubber;
        private VisualElement m_ScrubberFill;
        private Slider m_SpeedSlider;
        private Label m_SpeedLabel;
        private Button m_PlayPauseButton;
        private Button m_StepButton;
        private Button m_StopButton;
        private DropdownField m_ChannelField;
        private Toggle m_RootMotionToggle;
        private Toggle m_ApplyTransitionToggle;
        private FloatField m_FadeInField;
        private FloatField m_FadeOutField;
        private FloatField m_EnterTimeField;
        private IntegerField m_PriorityField;
        private FoldoutCard m_PlaybackCard;
        private FoldoutCard m_TransitionCard;
        private DropdownField m_ActionStateField;
        private DropdownField m_ActionReturnModeField;
        private DropdownField m_ActionReturnStateField;
        private FloatField m_ActionCancelableAfterField;
        private FloatField m_ActionCancelFadeOutField;
        private Toggle m_ActionForceToggle;
        private Button m_ActionPlayButton;
        private Button m_ActionCancelButton;
        private Label m_ActionStatusLabel;
        private FoldoutCard m_ActionDebugCard;

        public XAnimationPlaybackHudView(IXAnimationPlaybackHudHost host, bool includeStatus = true, string titleText = "")
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
            m_ActionHost = host as IXAnimationActionDebugHudHost;
            m_IncludeStatus = includeStatus;
            Root = Build(titleText);
            Refresh();
        }

        public VisualElement Root { get; }
        public VisualElement Content => m_PlaybackCard?.Content;

        public void TogglePlaybackExpanded()
        {
            m_PlaybackCard?.SetExpanded?.Invoke(!m_Host.PlaybackExpanded);
        }

        public void Refresh()
        {
            XAnimationPlaybackSettings settings = m_Host.Settings;
            float speed = ClampSpeed(settings.Speed);
            m_SpeedSlider?.SetValueWithoutNotify(speed);
            if (m_SpeedLabel != null)
            {
                m_SpeedLabel.text = $"{speed:0.0}x";
            }

            RefreshChannelChoices(settings.ChannelName);
            m_RootMotionToggle?.SetValueWithoutNotify(m_Host.RootMotionEnabled);
            m_ApplyTransitionToggle?.SetValueWithoutNotify(settings.ApplyTransition);
            m_FadeInField?.SetValueWithoutNotify(Mathf.Max(0f, settings.FadeIn));
            m_FadeOutField?.SetValueWithoutNotify(Mathf.Max(0f, settings.FadeOut));
            m_EnterTimeField?.SetValueWithoutNotify(Mathf.Clamp01(settings.EnterTime));
            m_PriorityField?.SetValueWithoutNotify(settings.Priority);

            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = m_Host.StatusText;
                m_StatusLabel.style.color = m_Host.StatusIsError ? DangerColor : TextMuted;
            }

            if (m_PlayPauseButton != null)
            {
                m_PlayPauseButton.SetEnabled(m_Host.CanPlayOrPause);
                m_PlayPauseButton.style.opacity = m_Host.CanPlayOrPause ? 1f : 0.45f;
                m_PlayPauseButton.text = m_Host.HasPlayback && !m_Host.IsPaused ? "Ⅱ" : "▶";
            }

            SetButtonEnabled(m_StepButton, m_Host.CanStep);
            SetButtonEnabled(m_StopButton, m_Host.CanStop);
            UpdateScrubber(m_Host.NormalizedTime, m_Host.CanSeek);
            m_TransitionCard?.RefreshState?.Invoke();
            RefreshActionDebug();
        }

        private VisualElement Build(string titleText)
        {
            VisualElement playbackActions = Row();
            playbackActions.style.flexWrap = Wrap.NoWrap;
            playbackActions.style.minWidth = 0;

            m_Scrubber = CreateScrubber();
            playbackActions.Add(m_Scrubber);

            VisualElement speedControls = Row();
            speedControls.style.width = SpeedControlWidth;
            speedControls.style.flexShrink = 0;
            speedControls.style.marginLeft = 6;
            speedControls.tooltip = "本次播放使用的时间缩放倍率。";

            m_SpeedSlider = new Slider(SpeedMin, SpeedMax)
            {
                value = ClampSpeed(m_Host.Settings.Speed),
            };
            m_SpeedSlider.style.flexGrow = 1;
            m_SpeedSlider.style.flexShrink = 1;
            m_SpeedSlider.style.minWidth = 56;
            m_SpeedSlider.tooltip = "拖动调整播放速度。";
            m_SpeedSlider.RegisterValueChangedCallback(evt =>
            {
                m_Host.SetSpeed(evt.newValue);
                Refresh();
            });
            speedControls.Add(m_SpeedSlider);

            m_SpeedLabel = new Label();
            m_SpeedLabel.style.width = 34;
            m_SpeedLabel.style.minWidth = 34;
            m_SpeedLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_SpeedLabel.style.color = TextNormal;
            m_SpeedLabel.style.fontSize = BodyFontSize;
            m_SpeedLabel.style.marginLeft = 4;
            speedControls.Add(m_SpeedLabel);
            playbackActions.Add(speedControls);

            m_PlayPauseButton = CreateHudButton("▶", m_Host.TogglePlayPause, AccentColor, 6f);
            playbackActions.Add(m_PlayPauseButton);
            m_StepButton = CreateHudButton("▸|", m_Host.Step, AccentColor, 4f);
            playbackActions.Add(m_StepButton);
            m_StopButton = CreateHudButton("■", m_Host.StopAll, DangerColor, 4f);
            playbackActions.Add(m_StopButton);

            m_PlaybackCard = CreateFoldoutCard(titleText, m_Host.PlaybackExpanded, value =>
            {
                m_Host.PlaybackExpanded = value;
                m_Host.SaveSettings();
            }, playbackActions);

            if (m_IncludeStatus)
            {
                m_StatusLabel = new Label();
                m_StatusLabel.style.color = TextMuted;
                m_StatusLabel.style.fontSize = BodyFontSize;
                m_StatusLabel.style.whiteSpace = WhiteSpace.Normal;
                m_StatusLabel.style.marginBottom = 4;
                m_PlaybackCard.Content.Add(m_StatusLabel);
            }

            VisualElement mainFields = CreateSubBox();
            m_ChannelField = new DropdownField();
            m_ChannelField.tooltip = "clip 调试播放使用的 channelName；state 播放始终使用 state 自己配置的 channel。";
            m_ChannelField.style.flexGrow = 1;
            m_ChannelField.style.minWidth = 0;
            m_ChannelField.RegisterValueChangedCallback(evt =>
            {
                m_Host.SetChannel(evt.newValue ?? string.Empty);
                Refresh();
            });

            if (m_Host.ShowRootMotion)
            {
                m_RootMotionToggle = new Toggle();
                m_RootMotionToggle.tooltip = "临时覆盖当前预览 session 是否应用 Root Motion。";
                m_RootMotionToggle.RegisterValueChangedCallback(evt =>
                {
                    m_Host.SetRootMotionEnabled(evt.newValue);
                    Refresh();
                });
                mainFields.Add(CreatePlaybackFieldPairRow(
                    "channelName",
                    m_ChannelField,
                    "rootMotion",
                    m_RootMotionToggle,
                    MainFieldLabelWidth,
                    MainFieldValueWidth));
            }
            else
            {
                mainFields.Add(CreatePlaybackFieldContainer("channelName", m_ChannelField, 92f));
            }

            m_PlaybackCard.Content.Add(mainFields);

            m_ApplyTransitionToggle = CreateHeaderApplyToggle(m_Host.Settings.ApplyTransition, "是否应用 Transition 覆盖。关闭时本分区会自动收起。");
            m_TransitionCard = CreateSectionFoldoutCard("Transition", m_Host.TransitionExpanded, value =>
            {
                m_Host.TransitionExpanded = value;
                m_Host.SaveSettings();
            }, m_ApplyTransitionToggle, () => m_Host.Settings.ApplyTransition);
            m_TransitionCard.Root.style.marginTop = 4;

            m_ApplyTransitionToggle.RegisterValueChangedCallback(evt =>
            {
                m_Host.SetApplyTransition(evt.newValue);
                if (!evt.newValue)
                {
                    m_TransitionCard.SetExpanded?.Invoke(false);
                }
                Refresh();
            });

            m_FadeInField = CreateFloatField(Mathf.Max(0f, m_Host.Settings.FadeIn), value => m_Host.SetFadeIn(Mathf.Max(0f, value)));
            m_FadeOutField = CreateFloatField(Mathf.Max(0f, m_Host.Settings.FadeOut), value => m_Host.SetFadeOut(Mathf.Max(0f, value)));
            m_TransitionCard.Content.Add(CreatePlaybackFieldPairRow(
                "fadeIn",
                m_FadeInField,
                "fadeOut",
                m_FadeOutField,
                TransitionFieldLabelWidth,
                TransitionFieldValueWidth));

            m_EnterTimeField = CreateFloatField(Mathf.Clamp01(m_Host.Settings.EnterTime), value => m_Host.SetEnterTime(Mathf.Clamp01(value)));
            m_PriorityField = new IntegerField { value = m_Host.Settings.Priority };
            m_PriorityField.tooltip = "request.priority。";
            ConfigureCompactPlaybackElement(m_PriorityField, TransitionFieldValueWidth);
            m_PriorityField.RegisterValueChangedCallback(evt =>
            {
                m_Host.SetPriority(evt.newValue);
                Refresh();
            });
            m_TransitionCard.Content.Add(CreatePlaybackFieldPairRow(
                "enterTime",
                m_EnterTimeField,
                "priority",
                m_PriorityField,
                TransitionFieldLabelWidth,
                TransitionFieldValueWidth));

            m_PlaybackCard.Content.Add(m_TransitionCard.Root);
            if (m_ActionHost != null)
            {
                m_PlaybackCard.Content.Add(CreateActionDebugSection().Root);
            }

            return m_PlaybackCard.Root;
        }

        private FoldoutCard CreateActionDebugSection()
        {
            m_ActionPlayButton = CreateTextButton("Play Action", () =>
            {
                m_ActionHost.PlayAction();
                Refresh();
            }, AccentColor);
            m_ActionPlayButton.tooltip = "用当前 Action Debug 配置调用 PlayAction。";
            m_ActionCancelButton = CreateTextButton("Cancel", () =>
            {
                m_ActionHost.CancelAction();
                Refresh();
            }, DangerColor, 4f);
            m_ActionCancelButton.tooltip = "调用当前 action handle 的 Cancel()。";

            VisualElement actions = Row();
            actions.style.flexWrap = Wrap.NoWrap;
            actions.Add(m_ActionPlayButton);
            actions.Add(m_ActionCancelButton);

            m_ActionDebugCard = CreateSectionFoldoutCard("Action Debug", m_ActionHost.ActionDebugExpanded, value =>
            {
                m_ActionHost.ActionDebugExpanded = value;
                m_Host.SaveSettings();
            }, actions);
            m_ActionDebugCard.Root.style.marginTop = 4;

            VisualElement mainFields = m_ActionDebugCard.Content;
            m_ActionStateField = new DropdownField();
            m_ActionStateField.tooltip = "PlayAction 目标 state。Action 只接受已有 state key。";
            ApplyDropdownFieldStyle(m_ActionStateField);
            m_ActionStateField.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionStateKey = evt.newValue ?? string.Empty;
                Refresh();
            });
            mainFields.Add(CreatePlaybackFieldContainer("state", m_ActionStateField, 78f));

            m_ActionReturnModeField = new DropdownField(
                new List<string>
                {
                    nameof(XAnimationActionReturnMode.PreviousState),
                    nameof(XAnimationActionReturnMode.None),
                    nameof(XAnimationActionReturnMode.State),
                },
                ActionReturnModeToString(m_ActionHost.ActionReturnMode));
            m_ActionReturnModeField.tooltip = "Action 完成或取消后的返回规则。";
            ApplyDropdownFieldStyle(m_ActionReturnModeField);
            m_ActionReturnModeField.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionReturnMode = ParseActionReturnMode(evt.newValue);
                Refresh();
            });
            mainFields.Add(CreatePlaybackFieldContainer("returnMode", m_ActionReturnModeField, 78f));

            m_ActionReturnStateField = new DropdownField();
            m_ActionReturnStateField.tooltip = "returnMode = State 时返回的目标 state。";
            ApplyDropdownFieldStyle(m_ActionReturnStateField);
            m_ActionReturnStateField.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionReturnStateKey = evt.newValue ?? string.Empty;
                Refresh();
            });
            mainFields.Add(CreatePlaybackFieldContainer("returnState", m_ActionReturnStateField, 78f));

            m_ActionCancelableAfterField = new FloatField { value = Mathf.Max(0f, m_ActionHost.ActionCancelableAfter) };
            m_ActionCancelableAfterField.tooltip = "当前 action normalizedTime 达到该值后才允许 Cancel。";
            ConfigureCompactPlaybackField(m_ActionCancelableAfterField, TransitionFieldValueWidth);
            m_ActionCancelableAfterField.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionCancelableAfter = Mathf.Max(0f, evt.newValue);
                Refresh();
            });

            m_ActionCancelFadeOutField = new FloatField { value = Mathf.Max(0f, m_ActionHost.ActionCancelFadeOut) };
            m_ActionCancelFadeOutField.tooltip = "Cancel 成功后 Stop 当前 action channel 使用的 fadeOut。0 表示使用 channel 默认 fadeOut。";
            ConfigureCompactPlaybackField(m_ActionCancelFadeOutField, TransitionFieldValueWidth);
            m_ActionCancelFadeOutField.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionCancelFadeOut = Mathf.Max(0f, evt.newValue);
                Refresh();
            });
            mainFields.Add(CreatePlaybackFieldPairRow(
                "cancelAt",
                m_ActionCancelableAfterField,
                "cancelOut",
                m_ActionCancelFadeOutField,
                TransitionFieldLabelWidth,
                TransitionFieldValueWidth));

            m_ActionForceToggle = new Toggle();
            m_ActionForceToggle.tooltip = "启用后 PlayAction 底层 PlayState 使用 force=true。";
            m_ActionForceToggle.RegisterValueChangedCallback(evt =>
            {
                m_ActionHost.ActionForce = evt.newValue;
                Refresh();
            });
            mainFields.Add(CreatePlaybackToggleRow("force", m_ActionForceToggle, 78f));

            m_ActionStatusLabel = new Label();
            m_ActionStatusLabel.style.marginTop = 4;
            m_ActionStatusLabel.style.fontSize = BodyFontSize;
            m_ActionStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            m_ActionStatusLabel.style.color = TextMuted;
            mainFields.Add(m_ActionStatusLabel);

            return m_ActionDebugCard;
        }

        private VisualElement CreateScrubber()
        {
            VisualElement scrubber = new();
            scrubber.style.width = ScrubberWidth;
            scrubber.style.height = 18;
            scrubber.style.flexShrink = 0;
            scrubber.style.position = Position.Relative;
            scrubber.style.backgroundColor = new Color(0.08f, 0.08f, 0.085f, 1f);
            SetBorder(scrubber, SectionDivider, 1, 0);
            scrubber.tooltip = "拖动当前播放 channel 的归一化进度。";

            m_ScrubberFill = new VisualElement();
            m_ScrubberFill.pickingMode = PickingMode.Ignore;
            m_ScrubberFill.style.position = Position.Absolute;
            m_ScrubberFill.style.left = 0f;
            m_ScrubberFill.style.top = 0f;
            m_ScrubberFill.style.bottom = 0f;
            m_ScrubberFill.style.width = Length.Percent(0f);
            m_ScrubberFill.style.backgroundColor = ProgressFillBg;
            scrubber.Add(m_ScrubberFill);

            scrubber.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !m_Host.CanSeek)
                {
                    return;
                }

                m_IsScrubbing = true;
                scrubber.CapturePointer(evt.pointerId);
                SeekFromPointer(evt.localPosition.x);
                evt.StopPropagation();
            });
            scrubber.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!m_IsScrubbing || !scrubber.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                SeekFromPointer(evt.localPosition.x);
                evt.StopPropagation();
            });
            scrubber.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!m_IsScrubbing)
                {
                    return;
                }

                SeekFromPointer(evt.localPosition.x);
                m_IsScrubbing = false;
                if (scrubber.HasPointerCapture(evt.pointerId))
                {
                    scrubber.ReleasePointer(evt.pointerId);
                }

                evt.StopPropagation();
            });
            scrubber.RegisterCallback<PointerCancelEvent>(evt =>
            {
                m_IsScrubbing = false;
                if (scrubber.HasPointerCapture(evt.pointerId))
                {
                    scrubber.ReleasePointer(evt.pointerId);
                }
            });

            return scrubber;
        }

        private Button CreateHudButton(string label, Action action, Color bgColor, float marginLeft)
        {
            Button button = new(() =>
            {
                action?.Invoke();
                Refresh();
            })
            {
                text = label
            };
            button.tooltip = label switch
            {
                "■" => "停止所有正在播放的 channel。",
                "Ⅱ" => "暂停或继续当前播放。",
                "▶" => "播放默认 state，或继续当前播放。",
                "▸|" => "暂停状态下向后推进固定一帧（1/60s）。",
                _ => label,
            };
            button.style.backgroundColor = bgColor;
            button.style.color = Color.white;
            SetBorder(button, Color.clear, 0, 3);
            SetPadding(button, 0);
            button.style.fontSize = label == "▶" ? BodyFontSize - 1f : BodyFontSize;
            button.style.width = ToolbarButtonSize;
            button.style.minWidth = ToolbarButtonSize;
            button.style.maxWidth = ToolbarButtonSize;
            button.style.height = ToolbarButtonSize;
            button.style.minHeight = ToolbarButtonSize;
            button.style.maxHeight = ToolbarButtonSize;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.marginLeft = marginLeft;
            button.style.flexShrink = 0;
            return button;
        }

        private static Button CreateTextButton(string label, Action action, Color bgColor, float marginLeft = 0f)
        {
            Button button = new(action)
            {
                text = label
            };
            button.style.backgroundColor = bgColor;
            button.style.color = Color.white;
            button.style.fontSize = BodyFontSize;
            button.style.height = ToolbarButtonSize;
            button.style.minHeight = ToolbarButtonSize;
            button.style.marginLeft = marginLeft;
            button.style.flexShrink = 0;
            SetBorder(button, Color.clear, 0, 3);
            SetPadding(button, 0, 4);
            return button;
        }

        private FloatField CreateFloatField(float value, Action<float> onChanged)
        {
            FloatField field = new() { value = value };
            ConfigureCompactPlaybackField(field, TransitionFieldValueWidth);
            field.RegisterValueChangedCallback(evt =>
            {
                onChanged?.Invoke(evt.newValue);
                Refresh();
            });
            return field;
        }

        private void RefreshChannelChoices(string selectedValue)
        {
            if (m_ChannelField == null)
            {
                return;
            }

            List<string> choices = new();
            IReadOnlyList<string> source = m_Host.ChannelChoices;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(source[i]) && !choices.Contains(source[i]))
                    {
                        choices.Add(source[i]);
                    }
                }
            }

            string selected = !string.IsNullOrWhiteSpace(selectedValue) && choices.Contains(selectedValue)
                ? selectedValue
                : choices.Count > 0 ? choices[0] : string.Empty;
            m_ChannelField.choices = choices;
            m_ChannelField.SetValueWithoutNotify(selected);
            m_ChannelField.SetEnabled(choices.Count > 0);
        }

        private void RefreshActionDebug()
        {
            if (m_ActionHost == null)
            {
                return;
            }

            RefreshDropdown(m_ActionStateField, m_ActionHost.ActionStateChoices, m_ActionHost.ActionStateKey, value => m_ActionHost.ActionStateKey = value);
            RefreshDropdown(m_ActionReturnStateField, m_ActionHost.ActionReturnStateChoices, m_ActionHost.ActionReturnStateKey, value => m_ActionHost.ActionReturnStateKey = value);
            m_ActionReturnModeField?.SetValueWithoutNotify(ActionReturnModeToString(m_ActionHost.ActionReturnMode));
            m_ActionReturnStateField?.SetEnabled(m_ActionHost.ActionReturnMode == XAnimationActionReturnMode.State &&
                                                 m_ActionHost.ActionReturnStateChoices != null &&
                                                 m_ActionHost.ActionReturnStateChoices.Count > 0);
            m_ActionCancelableAfterField?.SetValueWithoutNotify(Mathf.Max(0f, m_ActionHost.ActionCancelableAfter));
            m_ActionCancelFadeOutField?.SetValueWithoutNotify(Mathf.Max(0f, m_ActionHost.ActionCancelFadeOut));
            m_ActionForceToggle?.SetValueWithoutNotify(m_ActionHost.ActionForce);
            SetButtonEnabled(m_ActionPlayButton, m_ActionHost.CanPlayAction);
            SetButtonEnabled(m_ActionCancelButton, m_ActionHost.CanCancelAction);

            if (m_ActionStatusLabel != null)
            {
                m_ActionStatusLabel.text = m_ActionHost.ActionStatusText;
                m_ActionStatusLabel.style.color = m_ActionHost.ActionStatusIsError ? DangerColor : TextMuted;
            }

            m_ActionDebugCard?.RefreshState?.Invoke();
        }

        private static void RefreshDropdown(DropdownField field, IReadOnlyList<string> source, string selectedValue, Action<string> setSelected)
        {
            if (field == null)
            {
                return;
            }

            List<string> choices = new();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(source[i]) && !choices.Contains(source[i]))
                    {
                        choices.Add(source[i]);
                    }
                }
            }

            string selected = !string.IsNullOrWhiteSpace(selectedValue) && choices.Contains(selectedValue)
                ? selectedValue
                : choices.Count > 0 ? choices[0] : string.Empty;
            if (!string.Equals(selectedValue, selected, StringComparison.Ordinal))
            {
                setSelected?.Invoke(selected);
            }

            field.choices = choices;
            field.SetValueWithoutNotify(selected);
            field.SetEnabled(choices.Count > 0);
        }

        private static string ActionReturnModeToString(XAnimationActionReturnMode mode)
        {
            return mode switch
            {
                XAnimationActionReturnMode.None => nameof(XAnimationActionReturnMode.None),
                XAnimationActionReturnMode.State => nameof(XAnimationActionReturnMode.State),
                _ => nameof(XAnimationActionReturnMode.PreviousState),
            };
        }

        private static XAnimationActionReturnMode ParseActionReturnMode(string value)
        {
            return value switch
            {
                nameof(XAnimationActionReturnMode.None) => XAnimationActionReturnMode.None,
                nameof(XAnimationActionReturnMode.State) => XAnimationActionReturnMode.State,
                _ => XAnimationActionReturnMode.PreviousState,
            };
        }

        private void SeekFromPointer(float localX)
        {
            float width = Mathf.Max(1f, m_Scrubber?.resolvedStyle.width ?? ScrubberWidth);
            float progress = Mathf.Clamp01(localX / width);
            UpdateScrubber(progress, true);
            m_Host.Seek(progress);
            Refresh();
        }

        private void UpdateScrubber(float progress, bool enabled)
        {
            if (m_Scrubber != null)
            {
                m_Scrubber.style.opacity = enabled ? 1f : 0.35f;
                m_Scrubber.SetEnabled(enabled);
            }

            if (m_ScrubberFill != null)
            {
                m_ScrubberFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            }
        }

        private static void SetButtonEnabled(Button button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.SetEnabled(enabled);
            button.style.opacity = enabled ? 1f : 0.45f;
        }

        internal static float ClampSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || Mathf.Approximately(speed, 0f))
            {
                return 1f;
            }

            return Mathf.Clamp(speed, SpeedMin, SpeedMax);
        }
    }
}
#endif
