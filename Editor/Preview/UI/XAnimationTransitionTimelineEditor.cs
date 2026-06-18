#if UNITY_EDITOR
using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace XAnimationEditor
{
    internal sealed class XAnimationTransitionTimelineEditor : VisualElement
    {
        private enum DragMode
        {
            ExitTime,
            TransitionDuration,
            EnterTime,
        }

        private const float LabelWidth = 56f;
        private const float LabelMarginRight = 6f;
        private const float DragThreshold = 1.5f;

        private static readonly Color NextStateInactiveColor = new(0.20f, 0.34f, 0.58f, 0.32f);
        private static readonly Color NextStateActiveColor = new(0.24f, 0.50f, 0.92f, 0.72f);
        private static readonly Color TransitionMarkerColor = new(0.95f, 0.82f, 0.24f, 1f);
        private static readonly Color TransitionMarkerHoverColor = new(1f, 0.92f, 0.34f, 1f);

        private readonly VisualElement _timelineBox;
        private readonly VisualElement _rulerTrack;
        private readonly VisualElement _rulerTicksLayer;
        private readonly VisualElement _rulerTransitionStartLine;
        private readonly VisualElement _rulerTransitionEndLine;
        private readonly VisualElement _preStateTrack;
        private readonly VisualElement _preStateFill;
        private readonly VisualElement _preTransitionStartLine;
        private readonly VisualElement _preTransitionEndLine;
        private readonly VisualElement _nextStateTrack;
        private readonly VisualElement _nextStateFill;
        private readonly VisualElement _nextStatePlayedFill;
        private readonly VisualElement _nextTransitionStartLine;
        private readonly VisualElement _nextTransitionEndLine;
        private readonly VisualElement _transitionBand;
        private readonly Label _dragValueLabel;
        private readonly FloatField _exitTimeField;
        private readonly Slider _exitTimeSlider;
        private readonly FloatField _transitionDurationField;
        private readonly Slider _transitionDurationSlider;
        private readonly FloatField _enterTimeField;
        private readonly Slider _enterTimeSlider;

        private bool _suppressCallbacks;
        private bool _editable;
        private bool _exitTimeEditable;
        private float _exitTime;
        private float _transitionDuration;
        private float _enterTime;
        private float _preStateDurationSeconds = 1f;
        private float _nextStateDurationSeconds = 1f;
        private float _lastAxisDurationSeconds = 1f;
        private float _lastPreStateDurationSeconds = 1f;
        private float _lastNextStateDurationSeconds = 1f;
        private float _lastNextStateStartSeconds;
        private float _lastNextStateEndSeconds = 1f;
        private float _lastTransitionStartSeconds;

        public XAnimationTransitionTimelineEditor()
        {
            style.flexDirection = FlexDirection.Column;
            style.minWidth = 0;

            _timelineBox = XAnimationEditorUi.CreateSubBox();
            _timelineBox.style.position = Position.Relative;
            _timelineBox.style.marginBottom = 8;

            VisualElement rulerRow = CreateTimelineRow("Sec", out _rulerTrack);
            _rulerTrack.style.height = 26;
            _rulerTicksLayer = CreateAbsoluteLayer();
            _rulerTrack.Add(_rulerTicksLayer);
            _rulerTransitionStartLine = CreateMarkerLine();
            _rulerTransitionEndLine = CreateMarkerLine();
            _rulerTrack.Add(_rulerTransitionStartLine);
            _rulerTrack.Add(_rulerTransitionEndLine);
            _timelineBox.Add(rulerRow);

            VisualElement preStateRow = CreateTimelineRow("In", out _preStateTrack);
            _preStateFill = CreateFill(XAnimationEditorUi.AccentColor, 0.72f);
            _preTransitionStartLine = CreateMarkerLine();
            _preTransitionEndLine = CreateMarkerLine();
            _preStateTrack.Add(_preStateFill);
            _preStateTrack.Add(_preTransitionStartLine);
            _preStateTrack.Add(_preTransitionEndLine);
            _timelineBox.Add(preStateRow);

            VisualElement nextStateRow = CreateTimelineRow("Out", out _nextStateTrack);
            _nextStateFill = CreateFill(NextStateInactiveColor, 1f);
            _nextStatePlayedFill = CreateFill(NextStateActiveColor, 1f);
            _nextTransitionStartLine = CreateMarkerLine();
            _nextTransitionEndLine = CreateMarkerLine();
            _nextStateTrack.Add(_nextStateFill);
            _nextStateTrack.Add(_nextStatePlayedFill);
            _nextStateTrack.Add(_nextTransitionStartLine);
            _nextStateTrack.Add(_nextTransitionEndLine);
            _timelineBox.Add(nextStateRow);
            _timelineBox.Add(CreateTransitionBandHost(out _transitionBand));

            _dragValueLabel = new Label();
            _dragValueLabel.style.position = Position.Absolute;
            _dragValueLabel.style.top = 2;
            _dragValueLabel.style.left = LabelWidth + LabelMarginRight + 6;
            _dragValueLabel.style.paddingLeft = 5;
            _dragValueLabel.style.paddingRight = 5;
            _dragValueLabel.style.paddingTop = 2;
            _dragValueLabel.style.paddingBottom = 2;
            _dragValueLabel.style.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.92f);
            _dragValueLabel.style.color = Color.white;
            _dragValueLabel.style.fontSize = 10;
            _dragValueLabel.style.display = DisplayStyle.None;
            XAnimationEditorUi.SetRadius(_dragValueLabel, 2);
            _timelineBox.Add(_dragValueLabel);

            Add(_timelineBox);

            _exitTimeField = new FloatField("ExitTime");
            _exitTimeSlider = new Slider(0f, 1f);
            Add(CreateTimingRow(_exitTimeField, _exitTimeSlider));

            _transitionDurationField = new FloatField("TransitionDuration");
            _transitionDurationSlider = new Slider(0f, 1f);
            Add(CreateTimingRow(_transitionDurationField, _transitionDurationSlider));

            _enterTimeField = new FloatField("EnterTime");
            _enterTimeSlider = new Slider(0f, 1f);
            Add(CreateTimingRow(_enterTimeField, _enterTimeSlider));

            RegisterCallbacks();
        }

        public event Action<float, float, float> TimingChanged;
        public event Action<string> DragStatusChanged;

        public void SetData(
            string preStateKey,
            string nextStateKey,
            float preStateDurationSeconds,
            float nextStateDurationSeconds,
            float exitTime,
            float transitionDuration,
            float enterTime,
            bool editable,
            bool exitTimeEditable,
            string exitTimeReadOnlyReason)
        {
            _preStateDurationSeconds = preStateDurationSeconds > 0f ? preStateDurationSeconds : 0f;
            _nextStateDurationSeconds = nextStateDurationSeconds > 0f ? nextStateDurationSeconds : 0f;
            _exitTime = Mathf.Clamp01(exitTime);
            _transitionDuration = Mathf.Max(0f, transitionDuration);
            _enterTime = Mathf.Clamp01(enterTime);
            _editable = editable;
            _exitTimeEditable = exitTimeEditable;

            _exitTimeField.tooltip = exitTimeEditable
                ? "当前状态播到哪个 normalized time 时开始切换。范围 [0, 1]。"
                : exitTimeReadOnlyReason ?? "只读显示。";
            _exitTimeSlider.tooltip = _exitTimeField.tooltip;
            _transitionDurationField.tooltip = "<= 0 表示回退到 channel 默认 fadeIn / fadeOut。";
            _transitionDurationSlider.tooltip = "拖动调节过渡时长。";
            _enterTimeField.tooltip = "目标状态从哪个 normalized time 开始播放。范围 [0, 1]。";
            _enterTimeSlider.tooltip = _enterTimeField.tooltip;
            _preStateTrack.tooltip = string.IsNullOrWhiteSpace(preStateKey) ? "In state" : preStateKey;
            _nextStateTrack.tooltip = string.IsNullOrWhiteSpace(nextStateKey) ? "Out state" : nextStateKey;

            ApplyEditableState();
            SyncControls();
        }

        private void RegisterCallbacks()
        {
            _exitTimeField.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(evt.newValue, _transitionDuration, _enterTime, FormatStatus("ExitTime", Mathf.Clamp01(evt.newValue)));
            });
            _exitTimeSlider.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(evt.newValue, _transitionDuration, _enterTime, FormatStatus("ExitTime", Mathf.Clamp01(evt.newValue)));
            });
            _transitionDurationField.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(_exitTime, evt.newValue, _enterTime, FormatStatus("TransitionDuration", Mathf.Max(0f, evt.newValue)));
            });
            _transitionDurationSlider.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(_exitTime, evt.newValue, _enterTime, FormatStatus("TransitionDuration", Mathf.Max(0f, evt.newValue)));
            });
            _enterTimeField.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(_exitTime, _transitionDuration, evt.newValue, FormatStatus("EnterTime", Mathf.Clamp01(evt.newValue)));
            });
            _enterTimeSlider.RegisterValueChangedCallback(evt =>
            {
                if (_suppressCallbacks) return;
                ApplyTimingChange(_exitTime, _transitionDuration, evt.newValue, FormatStatus("EnterTime", Mathf.Clamp01(evt.newValue)));
            });

            const string exitTooltip = "左右拖拽，调整 ExitTime。";
            const string durationTooltip = "左右拖拽，调整 TransitionDuration。";
            const string enterTooltip = "左右拖拽目标 state 区块，调整 EnterTime。";
            RegisterTransitionBandDragHandle(_transitionBand, _rulerTrack, exitTooltip);
            RegisterAbsoluteDragHandle(_rulerTransitionStartLine, _rulerTrack, DragMode.ExitTime, exitTooltip);
            RegisterAbsoluteDragHandle(_preTransitionStartLine, _preStateTrack, DragMode.ExitTime, exitTooltip);
            RegisterAbsoluteDragHandle(_nextTransitionStartLine, _nextStateTrack, DragMode.ExitTime, exitTooltip);
            RegisterAbsoluteDragHandle(_rulerTransitionEndLine, _rulerTrack, DragMode.TransitionDuration, durationTooltip);
            RegisterAbsoluteDragHandle(_preTransitionEndLine, _preStateTrack, DragMode.TransitionDuration, durationTooltip);
            RegisterAbsoluteDragHandle(_nextTransitionEndLine, _nextStateTrack, DragMode.TransitionDuration, durationTooltip);
            RegisterEnterTimeTrackDragHandle(_nextStateTrack, enterTooltip);
        }

        private void ApplyEditableState()
        {
            _exitTimeField.SetEnabled(_editable && _exitTimeEditable);
            _exitTimeSlider.SetEnabled(_editable && _exitTimeEditable);
            _transitionDurationField.SetEnabled(_editable);
            _transitionDurationSlider.SetEnabled(_editable);
            _enterTimeField.SetEnabled(_editable);
            _enterTimeSlider.SetEnabled(_editable);
        }

        private void SyncControls()
        {
            _suppressCallbacks = true;
            _exitTimeField.SetValueWithoutNotify(_exitTime);
            _exitTimeSlider.SetValueWithoutNotify(_exitTime);
            _transitionDurationField.SetValueWithoutNotify(_transitionDuration);
            _transitionDurationSlider.highValue = GetDurationSliderMax(_lastNextStateDurationSeconds);
            _transitionDurationSlider.SetValueWithoutNotify(Mathf.Min(_transitionDuration, _transitionDurationSlider.highValue));
            _enterTimeField.SetValueWithoutNotify(_enterTime);
            _enterTimeSlider.SetValueWithoutNotify(_enterTime);
            SyncTimelinePreview();
            _suppressCallbacks = false;
        }

        private void SyncTimelinePreview()
        {
            float preStateDurationSeconds = _preStateDurationSeconds > 0f ? _preStateDurationSeconds : 1f;
            float nextStateDurationSeconds = _nextStateDurationSeconds > 0f ? _nextStateDurationSeconds : 0f;
            float transitionStartSeconds = preStateDurationSeconds * _exitTime;
            float transitionEndSeconds = transitionStartSeconds + _transitionDuration;
            float nextStateStartSeconds = nextStateDurationSeconds > 0f
                ? transitionStartSeconds - nextStateDurationSeconds * _enterTime
                : 0f;
            float nextStateEndSeconds = nextStateDurationSeconds > 0f
                ? nextStateStartSeconds + nextStateDurationSeconds
                : 0f;
            float axisDurationSeconds = Mathf.Max(
                Mathf.Max(0.1f, preStateDurationSeconds),
                Mathf.Max(transitionEndSeconds, nextStateEndSeconds));

            _lastAxisDurationSeconds = axisDurationSeconds;
            _lastPreStateDurationSeconds = preStateDurationSeconds;
            _lastNextStateDurationSeconds = nextStateDurationSeconds;
            _lastNextStateStartSeconds = nextStateStartSeconds;
            _lastNextStateEndSeconds = nextStateEndSeconds;
            _lastTransitionStartSeconds = transitionStartSeconds;

            RebuildTimelineRuler(_rulerTicksLayer, axisDurationSeconds);
            UpdateSegment(_preStateFill, 0f, preStateDurationSeconds, axisDurationSeconds);
            UpdateSegment(_nextStateFill, nextStateStartSeconds, nextStateEndSeconds, axisDurationSeconds);
            UpdateSegment(_nextStatePlayedFill, transitionStartSeconds, nextStateEndSeconds, axisDurationSeconds);
            UpdateSegment(_transitionBand, transitionStartSeconds, transitionEndSeconds, axisDurationSeconds);
            UpdateMarker(_rulerTransitionStartLine, transitionStartSeconds, axisDurationSeconds);
            UpdateMarker(_rulerTransitionEndLine, transitionEndSeconds, axisDurationSeconds);
            UpdateMarker(_preTransitionStartLine, transitionStartSeconds, axisDurationSeconds);
            UpdateMarker(_preTransitionEndLine, transitionEndSeconds, axisDurationSeconds);
            UpdateMarker(_nextTransitionStartLine, transitionStartSeconds, axisDurationSeconds);
            UpdateMarker(_nextTransitionEndLine, transitionEndSeconds, axisDurationSeconds);
        }

        private void ApplyTimingChange(float exitTime, float transitionDuration, float enterTime, string statusText)
        {
            _exitTime = Mathf.Clamp01(exitTime);
            _transitionDuration = Mathf.Max(0f, transitionDuration);
            _enterTime = Mathf.Clamp01(enterTime);
            TimingChanged?.Invoke(_exitTime, _transitionDuration, _enterTime);
            SyncControls();
            DragStatusChanged?.Invoke(statusText);
        }

        private bool TryGetTrackTime(VisualElement track, Vector2 pointerPosition, out float targetSeconds)
        {
            targetSeconds = 0f;
            if (track == null) return false;

            Rect trackBounds = track.worldBound;
            if (trackBounds.width <= 0f) return false;

            float pointerX = Mathf.Clamp(pointerPosition.x - trackBounds.xMin, 0f, trackBounds.width);
            float axisSeconds = Mathf.Max(0.0001f, _lastAxisDurationSeconds);
            targetSeconds = pointerX / trackBounds.width * axisSeconds;
            return true;
        }

        private void ApplyAbsoluteTimelineDrag(DragMode mode, VisualElement track, Vector2 pointerPosition)
        {
            if (!_editable || !TryGetTrackTime(track, pointerPosition, out float targetSeconds)) return;

            switch (mode)
            {
                case DragMode.ExitTime:
                {
                    float exitTime = _lastPreStateDurationSeconds > 0f
                        ? Mathf.Clamp01(targetSeconds / _lastPreStateDurationSeconds)
                        : 0f;
                    ApplyTimingChange(exitTime, _transitionDuration, _enterTime, FormatStatus("ExitTime", exitTime));
                    break;
                }
                case DragMode.TransitionDuration:
                {
                    float duration = Mathf.Max(0f, targetSeconds - _lastTransitionStartSeconds);
                    ApplyTimingChange(_exitTime, duration, _enterTime, FormatStatus("TransitionDuration", duration));
                    break;
                }
                case DragMode.EnterTime:
                {
                    float enterTime = _lastNextStateDurationSeconds > 0f
                        ? Mathf.Clamp01((_lastTransitionStartSeconds - targetSeconds) / _lastNextStateDurationSeconds)
                        : 0f;
                    ApplyTimingChange(_exitTime, _transitionDuration, enterTime, FormatStatus("EnterTime", enterTime));
                    break;
                }
            }
        }

        private void RegisterAbsoluteDragHandle(VisualElement element, VisualElement track, DragMode mode, string tooltip)
        {
            element.tooltip = tooltip;
            RegisterHoverFeedback(element, false);
            int activePointerId = PointerId.invalidPointerId;
            bool dragging = false;
            Vector2 startPosition = Vector2.zero;
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_editable || evt.button != 0) return;

                activePointerId = evt.pointerId;
                dragging = false;
                startPosition = evt.position;
                element.CapturePointer(activePointerId);
                SetDragFeedback(element, true, false);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (activePointerId != evt.pointerId || !element.HasPointerCapture(evt.pointerId)) return;

                if (!dragging && Vector2.Distance(startPosition, evt.position) < DragThreshold) return;
                dragging = true;
                ApplyAbsoluteTimelineDrag(mode, track, evt.position);
                ShowDragValueLabel(FormatDragLabel(mode), evt.position);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (activePointerId != evt.pointerId) return;

                if (element.HasPointerCapture(evt.pointerId)) element.ReleasePointer(evt.pointerId);
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(element, false, false);
                HideDragValueLabel();
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(element, false, false);
                HideDragValueLabel();
            });
        }

        private void RegisterTransitionBandDragHandle(VisualElement element, VisualElement track, string tooltip)
        {
            element.tooltip = tooltip;
            RegisterHoverFeedback(element, true);
            int activePointerId = PointerId.invalidPointerId;
            bool dragging = false;
            Vector2 startPosition = Vector2.zero;
            float dragStartSeconds = 0f;
            float dragStartExitTime = 0f;
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_editable || evt.button != 0 || !TryGetTrackTime(track, evt.position, out float targetSeconds)) return;

                activePointerId = evt.pointerId;
                dragging = false;
                startPosition = evt.position;
                dragStartSeconds = targetSeconds;
                dragStartExitTime = _exitTime;
                element.CapturePointer(activePointerId);
                SetDragFeedback(element, true, true);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (activePointerId != evt.pointerId || !element.HasPointerCapture(evt.pointerId)) return;

                if (!dragging && Vector2.Distance(startPosition, evt.position) < DragThreshold) return;
                if (!TryGetTrackTime(track, evt.position, out float targetSeconds) || _lastPreStateDurationSeconds <= 0f) return;

                dragging = true;
                float exitTime = Mathf.Clamp01(dragStartExitTime + (targetSeconds - dragStartSeconds) / _lastPreStateDurationSeconds);
                ApplyTimingChange(exitTime, _transitionDuration, _enterTime, FormatStatus("ExitTime", exitTime));
                ShowDragValueLabel(FormatDragLabel(DragMode.ExitTime), evt.position);
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (activePointerId != evt.pointerId) return;

                if (element.HasPointerCapture(evt.pointerId)) element.ReleasePointer(evt.pointerId);
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(element, false, true);
                HideDragValueLabel();
                evt.StopPropagation();
            });
            element.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(element, false, true);
                HideDragValueLabel();
            });
        }

        private void RegisterEnterTimeTrackDragHandle(VisualElement track, string tooltip)
        {
            track.tooltip = tooltip;
            RegisterHoverFeedback(track, true);
            int activePointerId = PointerId.invalidPointerId;
            bool dragging = false;
            Vector2 startPosition = Vector2.zero;
            float dragStartSeconds = 0f;
            float dragStartEnterTime = 0f;
            track.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!_editable ||
                    evt.button != 0 ||
                    !TryGetTrackTime(track, evt.position, out float targetSeconds) ||
                    _lastNextStateDurationSeconds <= 0f ||
                    targetSeconds < _lastNextStateStartSeconds ||
                    targetSeconds > _lastNextStateEndSeconds)
                {
                    return;
                }

                activePointerId = evt.pointerId;
                dragging = false;
                startPosition = evt.position;
                dragStartSeconds = targetSeconds;
                dragStartEnterTime = _enterTime;
                track.CapturePointer(activePointerId);
                SetDragFeedback(track, true, true);
                evt.StopPropagation();
            });
            track.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (activePointerId != evt.pointerId || !track.HasPointerCapture(evt.pointerId)) return;

                if (!dragging && Vector2.Distance(startPosition, evt.position) < DragThreshold) return;
                if (!TryGetTrackTime(track, evt.position, out float targetSeconds) || _lastNextStateDurationSeconds <= 0f) return;

                dragging = true;
                float enterTime = Mathf.Clamp01(dragStartEnterTime - (targetSeconds - dragStartSeconds) / _lastNextStateDurationSeconds);
                ApplyTimingChange(_exitTime, _transitionDuration, enterTime, FormatStatus("EnterTime", enterTime));
                ShowDragValueLabel(FormatDragLabel(DragMode.EnterTime), evt.position);
                evt.StopPropagation();
            });
            track.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (activePointerId != evt.pointerId) return;

                if (track.HasPointerCapture(evt.pointerId)) track.ReleasePointer(evt.pointerId);
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(track, false, true);
                HideDragValueLabel();
                evt.StopPropagation();
            });
            track.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                activePointerId = PointerId.invalidPointerId;
                dragging = false;
                SetDragFeedback(track, false, true);
                HideDragValueLabel();
            });
        }

        private void RegisterHoverFeedback(VisualElement element, bool bandLike)
        {
            element.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (_editable) SetHoverFeedback(element, true, bandLike);
            });
            element.RegisterCallback<MouseLeaveEvent>(_ => SetHoverFeedback(element, false, bandLike));
        }

        private static void SetHoverFeedback(VisualElement element, bool active, bool bandLike)
        {
            if (bandLike)
            {
                element.style.opacity = active ? 0.28f : 0.16f;
                return;
            }

            element.style.backgroundColor = active ? TransitionMarkerHoverColor : TransitionMarkerColor;
        }

        private static void SetDragFeedback(VisualElement element, bool active, bool bandLike)
        {
            if (bandLike)
            {
                element.style.opacity = active ? 0.36f : 0.16f;
                return;
            }

            element.style.backgroundColor = active ? TransitionMarkerHoverColor : TransitionMarkerColor;
        }

        private void ShowDragValueLabel(string text, Vector2 pointerPosition)
        {
            _dragValueLabel.text = text;
            Rect bounds = _timelineBox.worldBound;
            float left = Mathf.Clamp(pointerPosition.x - bounds.xMin + 8f, LabelWidth + LabelMarginRight, Mathf.Max(LabelWidth + LabelMarginRight, bounds.width - 96f));
            _dragValueLabel.style.left = left;
            _dragValueLabel.style.display = DisplayStyle.Flex;
        }

        private void HideDragValueLabel()
        {
            _dragValueLabel.style.display = DisplayStyle.None;
        }

        private string FormatDragLabel(DragMode mode)
        {
            return mode switch
            {
                DragMode.ExitTime => $"ExitTime {_exitTime:0.###}",
                DragMode.TransitionDuration => $"Duration {_transitionDuration:0.###}",
                DragMode.EnterTime => $"EnterTime {_enterTime:0.###}",
                _ => string.Empty,
            };
        }

        private static string FormatStatus(string label, float value)
        {
            return $"{label} = {value:0.###}。";
        }

        private static VisualElement CreateTimelineRow(string labelText, out VisualElement track)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            row.style.minWidth = 0;

            Label label = new(labelText);
            label.style.width = LabelWidth;
            label.style.minWidth = LabelWidth;
            label.style.maxWidth = LabelWidth;
            label.style.flexShrink = 0;
            label.style.marginRight = LabelMarginRight;
            label.style.color = XAnimationEditorUi.TextMuted;
            label.style.fontSize = 10;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);

            track = new VisualElement();
            track.style.position = Position.Relative;
            track.style.flexGrow = 1;
            track.style.flexShrink = 1;
            track.style.minWidth = 0;
            track.style.height = 16;
            track.style.backgroundColor = new Color(0.10f, 0.10f, 0.11f, 1f);
            XAnimationEditorUi.SetBorder(track, XAnimationEditorUi.SectionDivider, 1, 2);
            track.style.overflow = Overflow.Hidden;
            row.Add(track);
            return row;
        }

        private static VisualElement CreateTimingRow(FloatField field, Slider slider)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 4;
            row.style.minWidth = 0;

            field.style.width = 170;
            field.style.minWidth = 170;
            field.style.maxWidth = 170;
            field.style.flexShrink = 0;
            if (field.labelElement != null)
            {
                field.labelElement.style.color = XAnimationEditorUi.TextMuted;
            }
            row.Add(field);

            slider.style.flexGrow = 1;
            slider.style.flexShrink = 1;
            slider.style.minWidth = 0;
            slider.style.marginLeft = 8;
            row.Add(slider);
            return row;
        }

        private static VisualElement CreateAbsoluteLayer()
        {
            VisualElement element = new();
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.right = 0;
            element.style.top = 0;
            element.style.bottom = 0;
            return element;
        }

        private static VisualElement CreateTransitionBandHost(out VisualElement band)
        {
            VisualElement host = new();
            host.pickingMode = PickingMode.Ignore;
            host.style.position = Position.Absolute;
            host.style.left = LabelWidth + LabelMarginRight;
            host.style.right = 0;
            host.style.top = 4;
            host.style.bottom = 4;

            band = new VisualElement();
            band.pickingMode = PickingMode.Position;
            band.style.position = Position.Absolute;
            band.style.top = 0;
            band.style.bottom = 0;
            band.style.backgroundColor = TransitionMarkerColor;
            band.style.opacity = 0.16f;
            XAnimationEditorUi.SetRadius(band, 2);
            host.Add(band);
            return host;
        }

        private static VisualElement CreateFill(Color color, float opacity)
        {
            VisualElement fill = new();
            fill.style.position = Position.Absolute;
            fill.style.top = 2;
            fill.style.bottom = 2;
            fill.style.backgroundColor = color;
            fill.style.opacity = opacity;
            XAnimationEditorUi.SetRadius(fill, 2);
            return fill;
        }

        private static VisualElement CreateMarkerLine()
        {
            VisualElement handle = new();
            handle.style.position = Position.Absolute;
            handle.style.top = 0;
            handle.style.bottom = 0;
            handle.style.width = 7;
            handle.style.backgroundColor = TransitionMarkerColor;
            handle.style.opacity = 1f;
            XAnimationEditorUi.SetRadius(handle, 2);
            handle.pickingMode = PickingMode.Position;
            return handle;
        }

        private static void UpdateSegment(VisualElement element, float startSeconds, float endSeconds, float axisDurationSeconds)
        {
            float axis = Mathf.Max(0.0001f, axisDurationSeconds);
            float start = Mathf.Clamp01(startSeconds / axis);
            float end = Mathf.Clamp01(endSeconds / axis);
            if (end < start)
            {
                (start, end) = (end, start);
            }

            element.style.left = Length.Percent(start * 100f);
            element.style.width = Length.Percent(Mathf.Max(0f, end - start) * 100f);
        }

        private static void UpdateMarker(VisualElement marker, float seconds, float axisDurationSeconds)
        {
            float percent = Mathf.Clamp01(seconds / Mathf.Max(0.0001f, axisDurationSeconds));
            marker.style.left = Length.Percent(percent * 100f);
        }

        private static void RebuildTimelineRuler(VisualElement ticksLayer, float axisDurationSeconds)
        {
            ticksLayer.Clear();
            float axis = Mathf.Max(0.1f, axisDurationSeconds);
            float step = GetNiceTimelineStep(axis / 5f);
            for (float time = 0f; time <= axis + 0.0001f; time += step)
            {
                float percent = Mathf.Clamp01(time / axis) * 100f;
                VisualElement tick = new();
                tick.style.position = Position.Absolute;
                tick.style.left = Length.Percent(percent);
                tick.style.top = 0;
                tick.style.bottom = 0;
                tick.style.width = 1;
                tick.style.backgroundColor = XAnimationEditorUi.SectionDivider;
                ticksLayer.Add(tick);

                Label label = new($"{time:0.#}s");
                label.style.position = Position.Absolute;
                label.style.left = Length.Percent(percent);
                label.style.top = 5;
                label.style.width = 42;
                label.style.marginLeft = 3;
                label.style.fontSize = 9;
                label.style.color = XAnimationEditorUi.TextMuted;
                label.style.unityTextAlign = TextAnchor.UpperLeft;
                label.pickingMode = PickingMode.Ignore;
                ticksLayer.Add(label);
            }
        }

        private static float GetDurationSliderMax(float duration)
        {
            return Mathf.Max(1f, Mathf.Ceil(Mathf.Max(0f, duration) * 10f) / 10f);
        }

        private static float GetNiceTimelineStep(float roughStep)
        {
            roughStep = Mathf.Max(0.1f, roughStep);
            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(roughStep)));
            float normalized = roughStep / magnitude;
            float stepBase = normalized <= 1f
                ? 1f
                : normalized <= 2f
                    ? 2f
                    : normalized <= 5f
                        ? 5f
                        : 10f;
            return stepBase * magnitude;
        }
    }
}
#endif
