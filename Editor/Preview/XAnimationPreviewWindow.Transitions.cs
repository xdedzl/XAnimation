#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UEvent = UnityEngine.Event;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorParameterUtility;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    public sealed partial class XAnimationPreviewWindow
    {
        private VisualElement CreateDefaultTransitionEditor(int transitionIndex, XAnimationDefaultTransitionConfig config)
        {
            bool editable = m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset;
            string channelName = GetDefaultTransitionChannelName(config);

            XAnimationEditorSelectionField preStateField = CreateStateSelectionField(
                string.Empty,
                config.preStateKey,
                channelFilterName: channelName);
            preStateField.tooltip = "Default Transition 的 preState。";
            preStateField.SetEnabled(editable);
            preStateField.ApplyInlineSeparatorStyle();

            Label arrowLabel = new("->");
            arrowLabel.style.marginLeft = 4;
            arrowLabel.style.marginRight = 4;
            arrowLabel.style.color = TextMuted;
            arrowLabel.style.fontSize = BodyFontSize;
            arrowLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            XAnimationEditorSelectionField nextStateField = CreateStateSelectionField(
                string.Empty,
                config.nextStateKey,
                config.preStateKey,
                channelFilterName: channelName);
            nextStateField.tooltip = "Default Transition 的 nextState。";
            nextStateField.SetEnabled(editable);
            nextStateField.ApplyInlineSeparatorStyle();

            Button playButton = new() { text = "▶" };
            playButton.tooltip = "播放 preState，进入待切换状态。";
            ApplyClipIconButtonStyle(playButton);
            playButton.style.flexShrink = 0;
            playButton.SetEnabled(m_Session != null && m_Session.IsLoaded);

            Button deleteButton = new(() => DeleteDefaultTransition(transitionIndex)) { text = "⌫" };
            deleteButton.tooltip = editable ? "删除这个 Default Transition。" : "Override 资源不能删除 Default Transition。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;

            VisualElement headerActions = new();
            headerActions.style.flexDirection = FlexDirection.Row;
            headerActions.style.alignItems = Align.Center;
            headerActions.style.flexGrow = 1;
            headerActions.style.flexShrink = 1;
            headerActions.style.minWidth = 0;
            headerActions.style.maxWidth = Length.Percent(100f);

            VisualElement summaryRow = new();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.flexGrow = 1;
            summaryRow.style.flexShrink = 1;
            summaryRow.style.flexBasis = 0;
            summaryRow.style.minWidth = 0;
            headerActions.Add(summaryRow);

            preStateField.style.width = 180f;
            preStateField.style.minWidth = 120f;
            preStateField.style.flexGrow = 1;
            preStateField.style.flexShrink = 1;
            nextStateField.style.width = 180f;
            nextStateField.style.minWidth = 120f;
            nextStateField.style.flexGrow = 1;
            nextStateField.style.flexShrink = 1;
            summaryRow.Add(preStateField);
            summaryRow.Add(arrowLabel);
            summaryRow.Add(nextStateField);

            VisualElement actionsRow = new();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.alignItems = Align.Center;
            actionsRow.style.justifyContent = Justify.FlexEnd;
            actionsRow.style.flexShrink = 0;
            actionsRow.style.flexGrow = 0;
            actionsRow.style.marginLeft = 6;
            headerActions.Add(actionsRow);
            actionsRow.Add(playButton);
            actionsRow.Add(deleteButton);

            FoldoutCard card = CreateSectionFoldoutCard(
                string.Empty,
                IsDefaultTransitionExpanded(transitionIndex),
                value =>
                {
                    SetDefaultTransitionExpanded(transitionIndex, value);
                    ScheduleDefaultTransitionsEditorRebuild();
                },
                headerActions,
                headerTooltip: string.IsNullOrWhiteSpace(channelName)
                    ? "点击空白区域可展开或收起这个 Default Transition。"
                    : $"{channelName} Default Transition。点击空白区域可展开或收起这一项。",
                allowActionAreaBackgroundToggle: true);

            bool pairIsWaitingSwitch = false;
            playButton.clicked += () =>
            {
                if (m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                if (!pairIsWaitingSwitch)
                {
                    if (PlayDefaultTransitionPairPre(channelName, preStateField.value, nextStateField.value))
                    {
                        pairIsWaitingSwitch = true;
                        playButton.text = "⏭";
                        playButton.tooltip = "切换到 nextState（使用 Default Transition 参数）。";
                        ApplyClipIconButtonStyle(playButton, AccentColor);
                    }
                }
                else
                {
                    PlayDefaultTransitionPairNext(channelName, preStateField.value, nextStateField.value);
                    pairIsWaitingSwitch = false;
                    playButton.text = "▶";
                    playButton.tooltip = "播放 preState，进入待切换状态。";
                    ApplyClipIconButtonStyle(playButton);
                }
            };

            preStateField.ValueChanged += (previousValue, newValue) =>
            {
                string nextStateKey = nextStateField.value;
                if (string.Equals(newValue, nextStateKey, StringComparison.Ordinal))
                {
                    nextStateKey = GetFallbackNextState(channelName, newValue);
                }

                ChangeDefaultTransitionPair(transitionIndex, 0, newValue, nextStateKey, preStateField, previousValue);
            };
            nextStateField.ValueChanged += (previousValue, newValue) =>
                ChangeDefaultTransitionPair(transitionIndex, 0, preStateField.value, newValue, nextStateField, previousValue);

            card.Content.Add(CreateDefaultTransitionOptionsEditor(transitionIndex, config, editable));
            m_DefaultTransitionRowMap[transitionIndex] = card.Root;
            return card.Root;
        }

        private VisualElement CreateDefaultTransitionOptionsEditor(int transitionIndex, XAnimationDefaultTransitionConfig config, bool editable)
        {
            VisualElement container = new();
            container.style.marginTop = 6;
            container.style.paddingBottom = 5;

            string preStateKey = GetDefaultTransitionTimelinePreStateKey(config);
            string nextStateKey = GetDefaultTransitionTimelineNextStateKey(config);
            float currentExitTime = 0.5f;
            float currentTransitionDuration = GetDefaultTransitionDuration(config);
            float currentEnterTime = Mathf.Clamp01(config.enterTime);
            XAnimationTransitionTimelineEditor timelineEditor = new();
            timelineEditor.SetData(
                preStateKey,
                nextStateKey,
                ResolveTimelineStateDuration(preStateKey, 1f),
                ResolveTimelineStateDuration(nextStateKey, 1f),
                currentExitTime,
                currentTransitionDuration,
                currentEnterTime,
                editable,
                false,
                "只读显示。拖拽 timeline 的起点可调整这个编辑器临时值，不会写入 Default Transition 数据。");
            timelineEditor.TimingChanged += (exitTime, transitionDuration, enterTime) =>
            {
                bool optionsChanged =
                    !Mathf.Approximately(currentTransitionDuration, Mathf.Max(0f, transitionDuration)) ||
                    !Mathf.Approximately(currentEnterTime, Mathf.Clamp01(enterTime));
                currentExitTime = Mathf.Clamp01(exitTime);
                currentTransitionDuration = Mathf.Max(0f, transitionDuration);
                currentEnterTime = Mathf.Clamp01(enterTime);
                if (!optionsChanged || m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                m_Session.SetDefaultTransitionOptions(
                    transitionIndex,
                    currentTransitionDuration,
                    currentTransitionDuration,
                    currentEnterTime,
                    config.priority,
                    config.interruptible,
                    save: false);
                ScheduleAssetSave();
                RefreshChannelStates();
            };
            timelineEditor.DragStatusChanged += statusText => SetStatus($"Default Transition {transitionIndex + 1} {statusText}");
            container.Add(timelineEditor);
            return container;
        }

        private XAnimationCompiledState GetDefaultTransitionEditingState()
        {
            return TryGetCompiledStateByUiKey(m_DefaultTransitionEditingStateUiKey, out XAnimationCompiledState state)
                ? state
                : null;
        }

        private bool CanAddDefaultTransitionPairFromTab()
        {
            return m_Session != null &&
                   m_Session.IsLoaded &&
                   !m_Session.IsOverrideAsset &&
                   m_Session.CompiledAsset.States.Count >= 2 &&
                   GetDefaultTransitionEditingState() != null;
        }

        private List<DefaultTransitionPairEntry> CollectDefaultTransitionPairEntries(string channelName, string stateKey, bool inState)
        {
            List<DefaultTransitionPairEntry> entries = new();
            if (m_Session == null ||
                !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(stateKey))
            {
                return entries;
            }

            IReadOnlyList<XAnimationCompiledDefaultTransition> transitions = m_Session.CompiledAsset.DefaultTransitions;
            for (int transitionIndex = 0; transitionIndex < transitions.Count; transitionIndex++)
            {
                XAnimationDefaultTransitionConfig config = transitions[transitionIndex]?.Config;
                if (config == null)
                {
                    continue;
                }

                bool matches = inState
                    ? string.Equals(transitions[transitionIndex].ChannelName, channelName, StringComparison.Ordinal) &&
                      string.Equals(config.nextStateKey, stateKey, StringComparison.Ordinal)
                    : string.Equals(transitions[transitionIndex].ChannelName, channelName, StringComparison.Ordinal) &&
                      string.Equals(config.preStateKey, stateKey, StringComparison.Ordinal);
                if (matches)
                {
                    entries.Add(new DefaultTransitionPairEntry(
                        transitionIndex,
                        0,
                        transitions[transitionIndex].ChannelName,
                        config,
                        inState));
                }
            }

            IReadOnlyList<XAnimationCompiledAutoTransition> autoTransitions = m_Session.CompiledAsset.AutoTransitions;
            for (int transitionIndex = 0; transitionIndex < autoTransitions.Count; transitionIndex++)
            {
                XAnimationCompiledAutoTransition transition = autoTransitions[transitionIndex];
                XAnimationAutoTransitionConfig config = transition.Config;
                bool matches = inState
                    ? string.Equals(transition.ChannelName, channelName, StringComparison.Ordinal) &&
                      string.Equals(config.nextStateKey, stateKey, StringComparison.Ordinal)
                    : string.Equals(transition.ChannelName, channelName, StringComparison.Ordinal) &&
                      string.Equals(config.preStateKey, stateKey, StringComparison.Ordinal);
                if (matches)
                {
                    entries.Add(new DefaultTransitionPairEntry(
                        transitionIndex,
                        transition.ChannelName,
                        config,
                        inState));
                }
            }

            return entries;
        }

        private List<XAnimationStateTransitionGraphElement.PairViewData> BuildDefaultTransitionGraphPairs(
            List<DefaultTransitionPairEntry> inEntries,
            List<DefaultTransitionPairEntry> outEntries)
        {
            int inCount = inEntries?.Count ?? 0;
            int outCount = outEntries?.Count ?? 0;
            List<XAnimationStateTransitionGraphElement.PairViewData> pairs = new(inCount + outCount);
            if (inEntries != null)
            {
                for (int i = 0; i < inEntries.Count; i++)
                {
                    pairs.Add(CreateDefaultTransitionGraphPair(inEntries[i]));
                }
            }

            if (outEntries != null)
            {
                for (int i = 0; i < outEntries.Count; i++)
                {
                    pairs.Add(CreateDefaultTransitionGraphPair(outEntries[i]));
                }
            }

            return pairs;
        }

        private XAnimationStateTransitionGraphElement.PairViewData CreateDefaultTransitionGraphPair(DefaultTransitionPairEntry entry)
        {
            bool selected = entry.TransitionIndex == m_DefaultTransitionTabTransitionIndex &&
                            entry.PairIndex == m_DefaultTransitionTabPairIndex &&
                            entry.IsAuto == m_DefaultTransitionTabPairIsAuto;
            bool editable = m_Session != null &&
                            m_Session.IsLoaded &&
                            !m_Session.IsOverrideAsset;
            float fadeIn = entry.IsAuto ? entry.AutoTransition.transitionDuration : entry.Transition.fadeIn;
            float fadeOut = entry.IsAuto ? entry.AutoTransition.transitionDuration : entry.Transition.fadeOut;
            int priority = entry.IsAuto ? 0 : entry.Transition.priority;
            return new XAnimationStateTransitionGraphElement.PairViewData(
                entry.TransitionIndex,
                entry.PairIndex,
                entry.PreStateKey,
                entry.NextStateKey,
                entry.ChannelName,
                fadeIn,
                fadeOut,
                priority,
                entry.IsAuto,
                entry.IsInState,
                selected,
                selected && m_DefaultTransitionTabPairWaitingSwitch,
                editable);
        }

        private void EnsureDefaultTransitionTabSelection(
            List<DefaultTransitionPairEntry> inEntries,
            List<DefaultTransitionPairEntry> outEntries)
        {
            if (ContainsDefaultTransitionPairEntry(inEntries, m_DefaultTransitionTabTransitionIndex, m_DefaultTransitionTabPairIndex, m_DefaultTransitionTabPairIsAuto) ||
                ContainsDefaultTransitionPairEntry(outEntries, m_DefaultTransitionTabTransitionIndex, m_DefaultTransitionTabPairIndex, m_DefaultTransitionTabPairIsAuto))
            {
                return;
            }

            if (outEntries.Count > 0)
            {
                SelectDefaultTransitionTabPair(outEntries[0], rebuild: false);
                return;
            }

            if (inEntries.Count > 0)
            {
                SelectDefaultTransitionTabPair(inEntries[0], rebuild: false);
                return;
            }

            m_DefaultTransitionTabTransitionIndex = -1;
            m_DefaultTransitionTabPairIndex = -1;
            m_DefaultTransitionTabPairIsAuto = false;
            m_DefaultTransitionTabPairWaitingSwitch = false;
        }

        private static bool ContainsDefaultTransitionPairEntry(
            List<DefaultTransitionPairEntry> entries,
            int transitionIndex,
            int pairIndex,
            bool isAuto)
        {
            if (entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].TransitionIndex == transitionIndex &&
                    entries[i].PairIndex == pairIndex &&
                    entries[i].IsAuto == isAuto)
                {
                    return true;
                }
            }

            return false;
        }

        private DefaultTransitionPairEntry? GetSelectedDefaultTransitionPairEntry(
            List<DefaultTransitionPairEntry> inEntries,
            List<DefaultTransitionPairEntry> outEntries)
        {
            if (TryFindDefaultTransitionPairEntry(outEntries, m_DefaultTransitionTabTransitionIndex, m_DefaultTransitionTabPairIndex, m_DefaultTransitionTabPairIsAuto, out DefaultTransitionPairEntry entry) ||
                TryFindDefaultTransitionPairEntry(inEntries, m_DefaultTransitionTabTransitionIndex, m_DefaultTransitionTabPairIndex, m_DefaultTransitionTabPairIsAuto, out entry))
            {
                return entry;
            }

            return null;
        }

        private static bool TryFindDefaultTransitionPairEntry(
            List<DefaultTransitionPairEntry> entries,
            int transitionIndex,
            int pairIndex,
            bool isAuto,
            out DefaultTransitionPairEntry entry)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].TransitionIndex == transitionIndex &&
                        entries[i].PairIndex == pairIndex &&
                        entries[i].IsAuto == isAuto)
                    {
                        entry = entries[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        private void SelectDefaultTransitionTabPair(DefaultTransitionPairEntry entry, bool rebuild = true)
        {
            bool changed = entry.TransitionIndex != m_DefaultTransitionTabTransitionIndex ||
                           entry.PairIndex != m_DefaultTransitionTabPairIndex ||
                           entry.IsAuto != m_DefaultTransitionTabPairIsAuto;
            m_DefaultTransitionTabTransitionIndex = entry.TransitionIndex;
            m_DefaultTransitionTabPairIndex = entry.PairIndex;
            m_DefaultTransitionTabPairIsAuto = entry.IsAuto;
            m_SelectedDefaultTransitionIndex = entry.IsAuto ? -1 : entry.TransitionIndex;
            if (changed)
            {
                m_DefaultTransitionTabPairWaitingSwitch = false;
            }

            if (rebuild)
            {
                RebuildStatesGraphTabIfVisible();
            }
        }

        private void SelectDefaultTransitionTabPair(int transitionIndex, int pairIndex, bool isAuto)
        {
            if (TryGetDefaultTransitionTabPairEntry(transitionIndex, pairIndex, isAuto, out DefaultTransitionPairEntry entry))
            {
                SelectDefaultTransitionTabPair(entry);
            }
        }

        private bool TryGetDefaultTransitionTabPairEntry(
            int transitionIndex,
            int pairIndex,
            bool isAuto,
            out DefaultTransitionPairEntry entry)
        {
            XAnimationCompiledState editingState = GetDefaultTransitionEditingState();
            if (editingState == null)
            {
                entry = default;
                return false;
            }

            List<DefaultTransitionPairEntry> inEntries = CollectDefaultTransitionPairEntries(editingState.ChannelName, editingState.Key, true);
            List<DefaultTransitionPairEntry> outEntries = CollectDefaultTransitionPairEntries(editingState.ChannelName, editingState.Key, false);
            return TryFindDefaultTransitionPairEntry(outEntries, transitionIndex, pairIndex, isAuto, out entry) ||
                   TryFindDefaultTransitionPairEntry(inEntries, transitionIndex, pairIndex, isAuto, out entry);
        }

        private void BuildDefaultTransitionDetails(VisualElement detailsView, DefaultTransitionPairEntry? selectedEntry)
        {
            detailsView.Clear();
            Label title = new("Selected Transition");
            title.style.color = TextNormal;
            title.style.fontSize = SectionTitleFontSize;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            detailsView.Add(title);

            if (!selectedEntry.HasValue)
            {
                AddEmptyLabel(detailsView, "No transition selected");
                return;
            }

            DefaultTransitionPairEntry entry = selectedEntry.Value;
            bool editable = m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset;

            VisualElement pairPreview = CreateSubBox();
            pairPreview.style.backgroundColor = entry.IsAuto
                ? new Color(0.30f, 0.20f, 0.08f, 1f)
                : new Color(0.14f, 0.18f, 0.25f, 1f);
            pairPreview.style.marginBottom = 8;
            string nextStateLabel = string.IsNullOrWhiteSpace(entry.NextStateKey) ? "None" : entry.NextStateKey;
            Label pairTitle = CreateBoldLabel($"{entry.PreStateKey} -> {nextStateLabel}");
            pairTitle.style.color = Color.white;
            pairPreview.Add(pairTitle);
            Label pairName = new($"{(entry.IsAuto ? "Auto" : "Default")} · Channel: {entry.ChannelName}");
            pairName.style.color = TextMuted;
            pairName.style.fontSize = BodyFontSize;
            pairName.style.marginTop = 2;
            pairPreview.Add(pairName);
            detailsView.Add(pairPreview);

            Toggle isAutoToggle = new("isAuto") { value = entry.IsAuto };
            isAutoToggle.tooltip = "切换 Transition 的资源类型。Default 转 Auto 时 ExitTime 设为 1；Auto 转 Default 时 Priority 设为 0、Interruptible 设为 true。";
            isAutoToggle.SetEnabled(editable);
            isAutoToggle.style.marginBottom = 8;
            detailsView.Add(isAutoToggle);
            isAutoToggle.RegisterValueChangedCallback(evt =>
            {
                try
                {
                    int transitionIndex = evt.newValue
                        ? m_Session.ConvertDefaultTransitionToAuto(entry.TransitionIndex, save: false)
                        : m_Session.ConvertAutoTransitionToDefault(entry.ChannelName, entry.PreStateKey, save: false);
                    m_DefaultTransitionTabTransitionIndex = transitionIndex;
                    m_DefaultTransitionTabPairIndex = 0;
                    m_DefaultTransitionTabPairIsAuto = evt.newValue;
                    m_DefaultTransitionTabPairWaitingSwitch = false;
                    m_SelectedDefaultTransitionIndex = evt.newValue ? -1 : transitionIndex;
                    ScheduleAssetSave();
                    RebuildAutoTransitionEditor();
                    RebuildDefaultTransitionsEditor();
                    RefreshChannelStates();
                    SetStatus($"Transition 已转换为 {(evt.newValue ? "Auto" : "Default")}。");
                }
                catch (Exception ex)
                {
                    isAutoToggle.SetValueWithoutNotify(entry.IsAuto);
                    SetStatus(ex.Message, true);
                    Debug.LogException(ex);
                }
            });

            float currentExitTime = entry.IsAuto ? entry.AutoTransition.exitTime : 0.5f;
            float currentTransitionDuration = entry.IsAuto
                ? entry.AutoTransition.transitionDuration
                : GetDefaultTransitionDuration(entry.Transition);
            float currentEnterTime = Mathf.Clamp01(entry.IsAuto ? entry.AutoTransition.enterTime : entry.Transition.enterTime);
            XAnimationTransitionTimelineEditor timelineEditor = new();
            timelineEditor.SetData(
                entry.PreStateKey,
                entry.NextStateKey,
                ResolveTimelineStateDuration(entry.ChannelName, entry.PreStateKey, 1f),
                ResolveTimelineStateDuration(entry.ChannelName, entry.NextStateKey, 1f),
                currentExitTime,
                currentTransitionDuration,
                currentEnterTime,
                editable,
                entry.IsAuto,
                "Default Transition 不保存 ExitTime。切换为 Auto 后可编辑。");
            timelineEditor.TimingChanged += (exitTime, transitionDuration, enterTime) =>
            {
                bool timingChanged =
                    (entry.IsAuto && !Mathf.Approximately(currentExitTime, Mathf.Clamp01(exitTime))) ||
                    !Mathf.Approximately(currentTransitionDuration, Mathf.Max(0f, transitionDuration)) ||
                    !Mathf.Approximately(currentEnterTime, Mathf.Clamp01(enterTime));
                currentExitTime = Mathf.Clamp01(exitTime);
                currentTransitionDuration = Mathf.Max(0f, transitionDuration);
                currentEnterTime = Mathf.Clamp01(enterTime);
                if (!timingChanged || m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                try
                {
                    if (entry.IsAuto)
                    {
                        m_Session.SetAutoTransitionTiming(
                            entry.ChannelName,
                            entry.PreStateKey,
                            currentExitTime,
                            currentTransitionDuration,
                            currentEnterTime,
                            save: false);
                    }
                    else
                    {
                        m_Session.SetDefaultTransitionOptions(
                            entry.TransitionIndex,
                            currentTransitionDuration,
                            currentTransitionDuration,
                            currentEnterTime,
                            entry.Transition.priority,
                            entry.Transition.interruptible,
                            save: false);
                    }
                    ScheduleAssetSave();
                    RefreshChannelStates();
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, true);
                    Debug.LogException(ex);
                }
            };
            timelineEditor.DragStatusChanged += statusText => SetStatus($"{(entry.IsAuto ? "Auto" : "Default")} Transition {statusText}");
            detailsView.Add(timelineEditor);
        }
        private void ShowAddDefaultTransitionPairMenu(bool inState, Rect activatorRect)
        {
            if (!CanAddDefaultTransitionPairFromTab())
            {
                return;
            }

            XAnimationCompiledState editingState = GetDefaultTransitionEditingState();
            if (editingState == null)
            {
                return;
            }

            string channelName = editingState.ChannelName;
            string editingStateKey = editingState.Key;
            List<StateSelectionItem> items = CollectSelectableStates(
                editingStateKey,
                channelFilterName: channelName);
            List<SearchableSelectionItem> entries = BuildStateSelectionEntries(items, includeNone: false, includeChannelName: false);
            SearchableSelectionWindow.Show(
                activatorRect,
                inState ? "Select In State" : "Select Out State",
                string.Empty,
                entries,
                selected =>
                {
                    if (inState)
                    {
                        AddDefaultTransitionTabPair(channelName, selected, editingStateKey);
                    }
                    else
                    {
                        AddDefaultTransitionTabPair(channelName, editingStateKey, selected);
                    }
                });
        }

        private void AddDefaultTransitionTabPair(string channelName, string preStateKey, string nextStateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            try
            {
                int transitionIndex = m_DefaultTransitionTabTransitionIndex;
                int pairIndex;
                if (!m_DefaultTransitionTabPairIsAuto &&
                    transitionIndex >= 0 &&
                    transitionIndex < m_Session.CompiledAsset.DefaultTransitions.Count)
                {
                    pairIndex = m_Session.AddDefaultTransitionPair(transitionIndex, preStateKey, nextStateKey, save: false);
                }
                else
                {
                    transitionIndex = m_Session.AddDefaultTransition(channelName, preStateKey, nextStateKey, save: false);
                    pairIndex = 0;
                }

                m_SelectedDefaultTransitionIndex = transitionIndex;
                m_DefaultTransitionTabTransitionIndex = transitionIndex;
                m_DefaultTransitionTabPairIndex = pairIndex;
                m_DefaultTransitionTabPairIsAuto = false;
                m_DefaultTransitionTabPairWaitingSwitch = false;
                ScheduleAssetSave();
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已新增 Default Transition pair: {preStateKey} -> {nextStateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteDefaultTransitionTabPair(DefaultTransitionPairEntry entry)
        {
            if (m_Session == null || !m_Session.IsLoaded || m_Session.IsOverrideAsset)
            {
                return;
            }

            try
            {
                if (entry.IsAuto)
                {
                    m_Session.DeleteAutoTransition(entry.ChannelName, entry.PreStateKey);
                }
                else
                {
                    m_Session.DeleteDefaultTransition(entry.TransitionIndex);
                }
                m_SelectedDefaultTransitionIndex = -1;
                m_DefaultTransitionTabTransitionIndex = -1;
                m_DefaultTransitionTabPairIndex = -1;
                m_DefaultTransitionTabPairIsAuto = false;
                m_DefaultTransitionTabPairWaitingSwitch = false;
                RebuildAutoTransitionEditor();
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已删除 {(entry.IsAuto ? "Auto" : "Default")} Transition。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteDefaultTransitionTabPair(int transitionIndex, int pairIndex, bool isAuto)
        {
            if (TryGetDefaultTransitionTabPairEntry(transitionIndex, pairIndex, isAuto, out DefaultTransitionPairEntry entry))
            {
                DeleteDefaultTransitionTabPair(entry);
            }
        }

        private VisualElement CreateStateEditor(XAnimationCompiledState state)
        {
            XAnimationStateConfig config = state.Config;
            string channelName = state.ChannelName;
            VisualElement editor = CreateFoldoutRowEditor();
            VisualElement configBox = CreateStateConfigSection();
            editor.Add(configBox);

            DropdownField parameterField = null;
            DropdownField parameterXField = null;
            DropdownField parameterYField = null;
            VisualElement deferredTypeSpecificEditor = null;
            if (config.stateType == XAnimationStateType.Blend1D)
            {
                parameterField = CreateFloatParameterDropdown("parameter", config.parameterName);
                parameterField.tooltip = "Blend1D 绑定的 Float 参数。";
                parameterField.RegisterValueChangedCallback(evt => ChangeStateBlendParameter(channelName, state.Key, evt.newValue, parameterField, evt.previousValue));
                deferredTypeSpecificEditor = CreateBlendSampleEditor(channelName, state.Key, config, parameterField);
            }
            else if (IsDirectionalBlendStateType(config.stateType))
            {
                parameterXField = CreateFloatParameterDropdown("parameterX", config.parameterXName);
                parameterXField.tooltip = $"{config.stateType} 的 X 方向 Float 参数。";
                parameterYField = CreateFloatParameterDropdown("parameterY", config.parameterYName);
                parameterYField.tooltip = $"{config.stateType} 的 Y 方向 Float 参数。";
                parameterXField.RegisterValueChangedCallback(evt =>
                    ChangeStateDirectionalBlendParameters(
                        channelName,
                        state.Key,
                        evt.newValue,
                        parameterYField.value,
                        parameterXField,
                        parameterYField,
                        evt.previousValue,
                        parameterYField.value));
                parameterYField.RegisterValueChangedCallback(evt =>
                    ChangeStateDirectionalBlendParameters(
                        channelName,
                        state.Key,
                        parameterXField.value,
                        evt.newValue,
                        parameterXField,
                        parameterYField,
                        parameterXField.value,
                        evt.previousValue));
                deferredTypeSpecificEditor = CreateDirectionalBlendSampleEditor(channelName, state.Key, config, parameterXField, parameterYField);
            }

            Toggle loopField = new("loop") { value = config.loop };
            loopField.tooltip = "State 是否循环。";
            loopField.RegisterValueChangedCallback(evt =>
            {
                if (m_Session == null || !m_Session.IsLoaded) return;

                m_Session.SetStateLoop(channelName, state.Key, evt.newValue);
                RebuildStateList();
                RestartStateIfPlaying(state.Key, channelName);
                SetStatus($"{state.Key} loop = {evt.newValue}。");
            });

            configBox.Add(loopField);

            FloatField speedField = new("speed") { value = config.speed };
            speedField.tooltip = "State 默认速度。0 会按 1 处理。";
            speedField.RegisterValueChangedCallback(evt =>
            {
                if (m_Session == null || !m_Session.IsLoaded) return;

                float speed = Mathf.Approximately(evt.newValue, 0f) ? 1f : evt.newValue;
                if (!Mathf.Approximately(speed, evt.newValue))
                {
                    speedField.SetValueWithoutNotify(speed);
                }

                m_Session.SetStateSpeed(channelName, state.Key, speed, save: false);
                ScheduleAssetSave();
                SetStatus($"{state.Key} speed = {speed:0.###}。");
            });
            configBox.Add(speedField);

            if (config.stateType == XAnimationStateType.Single)
            {
                VisualElement clipBox = CreateSubBox();
                clipBox.style.marginTop = 5;
                XAnimationEditorSelectionField clipField = CreateClipSelectionField("clipKey", config.clipKey);
                clipField.tooltip = "Single state 播放的 clip。";
                clipField.ValueChanged += (previousValue, newValue) => ChangeStateClipKey(channelName, state.Key, newValue, clipField, previousValue);
                AttachClipKeyPingButton(clipField, config.clipKey, enabled: true);
                clipBox.Add(clipField);
                editor.Add(clipBox);
            }
            else if (deferredTypeSpecificEditor != null)
            {
                editor.Add(deferredTypeSpecificEditor);
            }

            editor.Add(CreateStateGateEditor(channelName, state.Key, "Allowed Next States", config.allowedNextStateKeys, addPreviousGate: false));
            editor.Add(CreateStateGateEditor(channelName, state.Key, "Allowed Previous States", config.allowedPreviousStateKeys, addPreviousGate: true));
            editor.Add(CreateStateBehaviorEditor(channelName, state.Key, config));
            return editor;
        }

        private VisualElement CreateStateGateEditor(string channelName, string stateKey, string title, string[] values, bool addPreviousGate)
        {
            string gateUiKey = BuildStateGateUiKey(channelName, stateKey, addPreviousGate);
            bool expanded = m_ExpandedStateGateKeys.Contains(gateUiKey);
            bool editable = m_Session != null && !m_Session.IsOverrideAsset;

            VisualElement box = CreateSubBox();
            box.style.marginTop = 2;
            SetPadding(box, 0);

            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(expanded);
            Label label = CreateSectionTitleLabel(title);
            label.tooltip = addPreviousGate ? "允许哪些 state 切到当前 state。" : "当前 state 允许切到哪些 state。";
            header.Add(foldoutLabel);
            header.Add(label);

            Button addButton = null;
            addButton = new Button(() =>
            {
                if (addPreviousGate)
                {
                    AddStateAllowedPreviousState(channelName, stateKey);
                }
                else
                {
                    AddStateAllowedNextState(channelName, stateKey);
                }
            })
            {
                text = "+"
            };
            addButton.tooltip = editable ? "新增一条 state 门禁配置。" : "Override 资源不能编辑 state 门禁。";
            addButton.SetEnabled(editable);
            ApplyClipIconButtonStyle(addButton, AccentColor);
            header.Add(addButton);
            box.Add(header);

            VisualElement content = new() { style = { display = expanded ? DisplayStyle.Flex : DisplayStyle.None } };
            ApplyPrettyContentStyle(content);
            box.Add(content);

            string[] gateValues = values ?? Array.Empty<string>();
            for (int i = 0; i < gateValues.Length; i++)
            {
                content.Add(CreateStateGateRow(channelName, stateKey, gateValues[i], i, editable, addPreviousGate));
            }

            if (gateValues.Length == 0)
            {
                AddEmptyLabel(content, "Unrestricted");
            }

            header.style.borderBottomWidth = expanded ? PrettyBorderWidth : 0f;
            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || addButton.worldBound.Contains(evt.mousePosition))
                {
                    return;
                }

                bool isExpanded = content.style.display != DisplayStyle.None;
                bool nextExpanded = !isExpanded;
                content.style.display = nextExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                header.style.borderBottomWidth = nextExpanded ? PrettyBorderWidth : 0f;
                SetFoldoutGlyphText(foldoutLabel, nextExpanded);
                SetExpanded(m_ExpandedStateGateKeys, gateUiKey, nextExpanded);
                evt.StopPropagation();
            });

            return box;
        }

        private static string BuildStateGateUiKey(string channelName, string stateKey, bool previousGate)
        {
            string gateType = previousGate ? "previous" : "next";
            return $"{gateType}:{BuildStateUiKey(channelName, stateKey)}";
        }

        private VisualElement CreateStateGateRow(string channelName, string stateKey, string targetStateKey, int index, bool editable, bool previousGate)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 2;

            XAnimationEditorSelectionField stateField = CreateStateSelectionField(
                string.Empty,
                targetStateKey,
                stateKey,
                channelFilterName: channelName,
                includeSelectors: !previousGate);
            stateField.style.flexGrow = 1;
            stateField.tooltip = previousGate ? "允许哪些 state 切到当前 state。" : "当前 state 允许切到哪些 state。";
            stateField.SetEnabled(editable);
            stateField.ValueChanged += (previousValue, newValue) =>
            {
                if (previousGate)
                {
                    ChangeStateAllowedPreviousState(channelName, stateKey, index, newValue, stateField, previousValue);
                }
                else
                {
                    ChangeStateAllowedNextState(channelName, stateKey, index, newValue, stateField, previousValue);
                }
            };
            row.Add(stateField);

            Button deleteButton = new(() =>
            {
                if (previousGate)
                {
                    DeleteStateAllowedPreviousState(channelName, stateKey, index);
                }
                else
                {
                    DeleteStateAllowedNextState(channelName, stateKey, index);
                }
            })
            {
                text = "⌫"
            };
            deleteButton.tooltip = editable ? "删除这条 state 门禁配置。" : "Override 资源不能编辑 state 门禁。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;
            row.Add(deleteButton);
            return row;
        }

        private VisualElement CreateAutoTransitionEditor(XAnimationCompiledAutoTransition transition)
        {
            XAnimationAutoTransitionConfig transitionConfig = transition.Config;
            string channelName = transition.ChannelName;
            string preStateKey = transition.PreStateKey;
            string stateUiKey = BuildAutoTransitionUiKey(transition);
            XAnimationCompiledState preState = m_Session.CompiledAsset.GetState(channelName, preStateKey);
            XAnimationStateConfig config = preState.Config;
            bool loopEnabled = config.loop;
            bool editable = m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset;
            bool timingEditable = !loopEnabled && editable;
            string currentNextStateKey = transitionConfig.nextStateKey ?? string.Empty;
            float currentExitTime = transitionConfig.exitTime;
            float currentTransitionDuration = transitionConfig.transitionDuration;
            float currentEnterTime = transitionConfig.enterTime;

            XAnimationEditorSelectionField preStateField = CreateAutoTransitionPreStateSelectionField(string.Empty, preStateKey, channelName);
            preStateField.tooltip = "当前 Auto Transition 的源状态。";
            preStateField.SetEnabled(editable);
            preStateField.ApplyInlineSeparatorStyle();

            Label arrowLabel = new("->");
            arrowLabel.style.marginLeft = 4;
            arrowLabel.style.marginRight = 4;
            arrowLabel.style.color = TextMuted;
            arrowLabel.style.fontSize = BodyFontSize;
            arrowLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

            XAnimationEditorSelectionField nextStateField = CreateStateSelectionField(
                string.Empty,
                currentNextStateKey,
                preStateKey,
                includeNone: true,
                channelFilterName: channelName,
                includeSelectors: true);
            nextStateField.style.width = 180f;
            nextStateField.style.minWidth = 120f;
            nextStateField.style.flexGrow = 1;
            nextStateField.style.flexShrink = 1;
            nextStateField.tooltip = "非循环 state 播放完成后自动切到的目标 state。None 表示关闭自动切换。";
            nextStateField.SetEnabled(timingEditable);
            nextStateField.ApplyInlineSeparatorStyle();

            Button deleteButton = new(() => DeleteAutoTransition(channelName, preStateKey)) { text = "⌫" };
            deleteButton.tooltip = editable ? "删除这个 Auto Transition。" : "Override 资源不能删除 Auto Transition。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;

            Button playButton = new(() => ToggleStatePlayback(preState)) { text = "▶" };
            playButton.tooltip = "播放或暂停这个 Auto Transition 对应的 preState。";
            ApplyClipButtonStyle(playButton, false);

            VisualElement headerActions = new();
            headerActions.style.flexDirection = FlexDirection.Row;
            headerActions.style.alignItems = Align.Center;
            headerActions.style.flexGrow = 1;
            headerActions.style.flexShrink = 1;
            headerActions.style.minWidth = 0;
            headerActions.style.maxWidth = Length.Percent(100f);

            VisualElement summaryRow = new();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.flexGrow = 1;
            summaryRow.style.flexShrink = 1;
            summaryRow.style.flexBasis = 0;
            summaryRow.style.minWidth = 0;
            headerActions.Add(summaryRow);

            preStateField.style.width = 180f;
            preStateField.style.minWidth = 120f;
            preStateField.style.flexGrow = 1;
            preStateField.style.flexShrink = 1;
            summaryRow.Add(preStateField);
            summaryRow.Add(arrowLabel);
            summaryRow.Add(nextStateField);

            VisualElement actionsRow = new();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.alignItems = Align.Center;
            actionsRow.style.justifyContent = Justify.FlexEnd;
            actionsRow.style.flexShrink = 0;
            actionsRow.style.flexGrow = 0;
            actionsRow.style.marginLeft = 6;
            headerActions.Add(actionsRow);
            playButton.style.flexShrink = 0;
            deleteButton.style.flexShrink = 0;
            actionsRow.Add(playButton);
            actionsRow.Add(deleteButton);

            string autoTransitionHeaderTooltip = loopEnabled
                ? "仅非循环状态可自动切换。点击空白区域可展开或收起这一项。"
                : "在 ExitTime 触发自动切换，TransitionDuration 为共用过渡时长，EnterTime 决定目标状态的起播点。点击空白区域可展开或收起这一项。";

            FoldoutCard card = CreateSectionFoldoutCard(
                string.Empty,
                IsAutoTransitionExpanded(stateUiKey),
                value =>
                {
                    SetAutoTransitionExpanded(stateUiKey, value);
                    ScheduleAutoTransitionEditorRebuild();
                },
                headerActions,
                headerTooltip: autoTransitionHeaderTooltip,
                allowActionAreaBackgroundToggle: true);
            m_AutoTransitionRowMap[stateUiKey] = card.Root;

            XAnimationTransitionTimelineEditor timelineEditor = new();
            void RefreshTimelineEditor()
            {
                timelineEditor.SetData(
                    preStateKey,
                    currentNextStateKey,
                    ResolveTimelineStateDuration(channelName, preStateKey, 0f),
                    ResolveTimelineStateDuration(channelName, currentNextStateKey, 0f),
                    currentExitTime,
                    currentTransitionDuration,
                    currentEnterTime,
                    timingEditable,
                    true,
                    null);
            }

            timelineEditor.TimingChanged += (exitTime, transitionDuration, enterTime) =>
            {
                if (m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                currentExitTime = Mathf.Clamp01(exitTime);
                currentTransitionDuration = Mathf.Max(0f, transitionDuration);
                currentEnterTime = Mathf.Clamp01(enterTime);
                m_Session.SetAutoTransitionTiming(channelName, preStateKey, currentExitTime, currentTransitionDuration, currentEnterTime, save: false);
                ScheduleAssetSave();
                RefreshChannelStates();
            };
            timelineEditor.DragStatusChanged += statusText => SetStatus($"{preStateKey} {statusText}");
            RefreshTimelineEditor();
            card.Content.Add(timelineEditor);

            preStateField.RegisterValueChangedCallback(evt =>
            {
                if (m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                string newPreStateKey = evt.newValue ?? string.Empty;
                bool wasExpanded = IsAutoTransitionExpanded(stateUiKey);
                m_Session.SetAutoTransitionPreState(channelName, preStateKey, newPreStateKey, save: false);
                string newStateUiKey = BuildStateUiKey(channelName, newPreStateKey);
                m_SelectedAutoTransitionStateUiKey = newStateUiKey;
                SetAutoTransitionExpanded(stateUiKey, true);
                if (!string.IsNullOrWhiteSpace(newPreStateKey))
                {
                    SetAutoTransitionExpanded(newStateUiKey, wasExpanded);
                }

                m_CollapsedAutoTransitionKeys.Remove(stateUiKey);
                ScheduleAssetSave();
                RebuildAutoTransitionEditor();
                RefreshChannelStates();
                SetStatus($"{channelName}: {preStateKey} auto transition preState = {newPreStateKey}。");
            });

            nextStateField.ValueChanged += (_, newValue) =>
            {
                if (m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                string newNextStateKey = NormalizeOptionalStateDropdownValue(newValue);
                currentNextStateKey = newNextStateKey;
                m_Session.SetAutoTransitionNextState(channelName, preStateKey, newNextStateKey, save: false);
                ScheduleAssetSave();
                RefreshChannelStates();
                RefreshTimelineEditor();
                SetStatus(string.IsNullOrWhiteSpace(newNextStateKey)
                    ? $"{channelName}: {preStateKey} auto next = None。"
                    : $"{channelName}: {preStateKey} auto next = {newNextStateKey}。");
            };

            return card.Root;
        }
        private static string BuildAutoTransitionUiKey(XAnimationCompiledAutoTransition transition)
        {
            return transition == null ? string.Empty : BuildStateUiKey(transition.ChannelName, transition.PreStateKey);
        }

        private bool IsAutoTransitionExpanded(string stateUiKey)
        {
            return m_CollapsedAutoTransitionKeys.Contains(stateUiKey);
        }

        private void SetAutoTransitionExpanded(string stateUiKey, bool expanded)
        {
            if (string.IsNullOrWhiteSpace(stateUiKey))
            {
                return;
            }

            if (expanded)
            {
                if (m_CollapsedAutoTransitionKeys.Count > 0)
                {
                    m_CollapsedAutoTransitionKeys.Clear();
                }

                m_CollapsedAutoTransitionKeys.Add(stateUiKey);
                return;
            }

            m_CollapsedAutoTransitionKeys.Remove(stateUiKey);
        }

        private bool IsDefaultTransitionExpanded(int transitionIndex)
        {
            return !m_CollapsedDefaultTransitionIndices.Contains(transitionIndex);
        }

        private void SetDefaultTransitionExpanded(int transitionIndex, bool expanded)
        {
            int transitionCount = m_Session?.CompiledAsset?.DefaultTransitions?.Count ?? 0;
            if (transitionIndex < 0 || (transitionCount > 0 && transitionIndex >= transitionCount))
            {
                return;
            }

            if (expanded)
            {
                m_CollapsedDefaultTransitionIndices.Clear();
                for (int i = 0; i < transitionCount; i++)
                {
                    if (i != transitionIndex)
                    {
                        m_CollapsedDefaultTransitionIndices.Add(i);
                    }
                }
                return;
            }

            m_CollapsedDefaultTransitionIndices.Add(transitionIndex);
        }

        private void NormalizeCollapsedDefaultTransitionIndicesAfterDelete(int deletedIndex)
        {
            HashSet<int> normalized = new();
            foreach (int index in m_CollapsedDefaultTransitionIndices)
            {
                if (index == deletedIndex)
                {
                    continue;
                }

                normalized.Add(index > deletedIndex ? index - 1 : index);
            }

            int remainingCount = Math.Max(0, (m_Session?.CompiledAsset?.DefaultTransitions?.Count ?? 0) - 1);
            if (remainingCount > 0 && normalized.Count < remainingCount - 1)
            {
                int expandedIndex = -1;
                for (int index = 0; index < remainingCount; index++)
                {
                    if (!normalized.Contains(index))
                    {
                        expandedIndex = index;
                        break;
                    }
                }

                normalized.Clear();
                for (int index = 0; index < remainingCount; index++)
                {
                    if (index != expandedIndex)
                    {
                        normalized.Add(index);
                    }
                }
            }

            m_CollapsedDefaultTransitionIndices.Clear();
            foreach (int index in normalized)
            {
                m_CollapsedDefaultTransitionIndices.Add(index);
            }
        }

        private string GetDefaultTransitionChannelName(XAnimationDefaultTransitionConfig config)
        {
            return config == null || m_Session == null || !m_Session.IsLoaded
                ? string.Empty
                : XAnimationEditorStateNodeUtility.GetDefaultTransitionChannelName(m_Session.CompiledAsset.Asset, config);
        }

        private string FormatDefaultTransitionPairSummary(XAnimationDefaultTransitionConfig config)
        {
            if (config == null)
            {
                return "Invalid";
            }

            string resolvedChannelName = GetDefaultTransitionChannelName(config);
            string channelName = string.IsNullOrWhiteSpace(resolvedChannelName) ? "?" : resolvedChannelName;
            string preStateKey = string.IsNullOrWhiteSpace(config.preStateKey) ? "?" : config.preStateKey;
            string nextStateKey = string.IsNullOrWhiteSpace(config.nextStateKey) ? "?" : config.nextStateKey;
            return $"{channelName}: {preStateKey} -> {nextStateKey}";
        }

        private string FormatDefaultTransitionDisplayName(XAnimationDefaultTransitionConfig config, int transitionIndex)
        {
            if (config == null)
            {
                return $"Default Transition {transitionIndex + 1}";
            }

            string resolvedChannelName = GetDefaultTransitionChannelName(config);
            string channelName = string.IsNullOrWhiteSpace(resolvedChannelName) ? "?" : resolvedChannelName;
            return $"{channelName} #{transitionIndex + 1}";
        }

        private static void ConfigureAutoTransitionHeaderDropdown(DropdownField field, float minWidth)
        {
            if (field.labelElement != null)
            {
                field.labelElement.style.display = DisplayStyle.None;
            }

            field.style.minWidth = minWidth;
            field.style.flexGrow = 1;
            field.style.marginLeft = 2;
            field.style.marginRight = 2;
        }

        private static float GetDefaultTransitionDuration(XAnimationDefaultTransitionConfig config)
        {
            if (config == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, Mathf.Max(config.fadeIn, config.fadeOut));
        }

        private float ResolveTimelineStateDuration(string stateKey, float fallbackSeconds)
        {
            if (m_Session != null &&
                m_Session.IsLoaded &&
                !string.IsNullOrWhiteSpace(stateKey) &&
                m_Session.CompiledAsset.TryGetStateDuration(stateKey, out float durationSeconds) &&
                durationSeconds > 0f)
            {
                return durationSeconds;
            }

            return fallbackSeconds;
        }

        private float ResolveTimelineStateDuration(string channelName, string stateKey, float fallbackSeconds)
        {
            if (m_Session != null &&
                m_Session.IsLoaded &&
                !string.IsNullOrWhiteSpace(channelName) &&
                !string.IsNullOrWhiteSpace(stateKey) &&
                m_Session.CompiledAsset.TryGetStateDuration(channelName, stateKey, out float durationSeconds) &&
                durationSeconds > 0f)
            {
                return durationSeconds;
            }

            return fallbackSeconds;
        }

        private static string GetDefaultTransitionTimelineNextStateKey(XAnimationDefaultTransitionConfig config)
        {
            return config?.nextStateKey ?? string.Empty;
        }

        private static string GetDefaultTransitionTimelinePreStateKey(XAnimationDefaultTransitionConfig config)
        {
            return config?.preStateKey ?? string.Empty;
        }

        private string GetFallbackNextState(string channelName, string preStateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return string.Empty;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (state == null ||
                    !string.Equals(state.ChannelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                string stateKey = state.Key;
                if (!string.Equals(stateKey, preStateKey, StringComparison.Ordinal))
                {
                    return stateKey;
                }
            }

            return string.Empty;
        }

        private sealed class XAnimationStateTransitionGraphElement : VisualElement
        {
            public readonly struct PairViewData
            {
                public PairViewData(
                    int transitionIndex,
                    int pairIndex,
                    string preStateKey,
                    string nextStateKey,
                    string transitionName,
                    float fadeIn,
                    float fadeOut,
                    int priority,
                    bool isAuto,
                    bool isInState,
                    bool isSelected,
                    bool isWaitingSwitch,
                    bool canDelete)
                {
                    TransitionIndex = transitionIndex;
                    PairIndex = pairIndex;
                    PreStateKey = preStateKey ?? string.Empty;
                    NextStateKey = nextStateKey ?? string.Empty;
                    TransitionName = transitionName ?? string.Empty;
                    FadeIn = fadeIn;
                    FadeOut = fadeOut;
                    Priority = priority;
                    IsAuto = isAuto;
                    IsInState = isInState;
                    IsSelected = isSelected;
                    IsWaitingSwitch = isWaitingSwitch;
                    CanDelete = canDelete;
                }

                public int TransitionIndex { get; }
                public int PairIndex { get; }
                public string PreStateKey { get; }
                public string NextStateKey { get; }
                public string TransitionName { get; }
                public float FadeIn { get; }
                public float FadeOut { get; }
                public int Priority { get; }
                public bool IsAuto { get; }
                public bool IsInState { get; }
                public bool IsSelected { get; }
                public bool IsWaitingSwitch { get; }
                public bool CanDelete { get; }
                public string ConnectedStateKey => IsInState ? PreStateKey : NextStateKey;
                public string StateKey => string.IsNullOrWhiteSpace(ConnectedStateKey) ? "None" : ConnectedStateKey;
                public string StateUiKey => string.IsNullOrWhiteSpace(ConnectedStateKey)
                    ? string.Empty
                    : BuildStateUiKey(TransitionName, ConnectedStateKey);
                public string LayoutKey => $"{StateKey}::{(IsAuto ? "Auto" : "Default")}::{TransitionIndex}:{PairIndex}";
            }

            private readonly struct EdgeLayout
            {
                public EdgeLayout(Rect from, Rect to, PairViewData pair)
                {
                    From = from;
                    To = to;
                    Pair = pair;
                }

                public Rect From { get; }
                public Rect To { get; }
                public PairViewData Pair { get; }
            }

            private const float MinZoom = 0.9f;
            private const float MaxZoom = 1.85f;
            private const float WheelZoomBase = 1.12f;
            private const float CanvasPadding = 46f;
            private const float NodeWidth = 166f;
            private const float NodeHeight = 66f;
            private const float CenterNodeWidth = 178f;
            private const float CenterNodeHeight = 76f;
            private const float ColumnPitch = 248f;
            private const float RowPitch = 86f;
            private const float MinCanvasWidth = 720f;
            private const float MinCanvasHeight = 360f;

            private static readonly Color CanvasBg = new(0.095f, 0.10f, 0.115f, 1f);
            private static readonly Color CanvasGrid = new(0.78f, 0.79f, 0.80f, 0.075f);
            private static readonly Color CanvasGridMajor = new(0.78f, 0.79f, 0.80f, 0.13f);
            private static readonly Color NodeBg = new(0.18f, 0.19f, 0.21f, 0.98f);
            private static readonly Color CurrentNodeBg = new(0.16f, 0.24f, 0.34f, 0.98f);
            private static readonly Color NodeBorder = new(0.34f, 0.35f, 0.38f, 1f);
            private static readonly Color SelectedBorder = new(0.48f, 0.74f, 1f, 1f);
            private static readonly Color EdgeColor = new(0.48f, 0.72f, 1f, 0.64f);
            private static readonly Color EdgeSelected = new(0.58f, 0.86f, 1f, 0.96f);
            private static readonly Color AutoEdgeColor = new(1f, 0.58f, 0.18f, 0.82f);
            private static readonly Color AutoEdgeSelected = new(1f, 0.76f, 0.28f, 1f);

            private readonly List<PairViewData> m_Pairs = new();
            private readonly List<EdgeLayout> m_Edges = new();
            private readonly Dictionary<string, int> m_InStateRows = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> m_OutStateRows = new(StringComparer.Ordinal);

            private ScrollView m_ScrollView;
            private VisualElement m_Canvas;
            private VisualElement m_EdgeCanvas;
            private VisualElement m_NodeLayer;
            private Label m_EmptyLabel;
            private string m_EditingStateUiKey = string.Empty;
            private string m_EditingStateLabel = string.Empty;
            private float m_Zoom = 1f;
            private float m_BaseCanvasWidth = MinCanvasWidth;
            private float m_BaseCanvasHeight = MinCanvasHeight;
            private float m_CanvasWidth = MinCanvasWidth;
            private float m_CanvasHeight = MinCanvasHeight;
            private Vector2 m_CanvasOrigin = Vector2.zero;
            private Vector2 m_PanOffset = Vector2.zero;
            private bool m_IsPanning;
            private bool m_CanAddPair;
            private int m_PanPointerId = PointerId.invalidPointerId;
            private Vector2 m_PanStartPointer;
            private Vector2 m_PanStartOffset;

            public XAnimationStateTransitionGraphElement()
            {
                style.flexGrow = 1;
                style.minHeight = 0;
                style.backgroundColor = CanvasBg;
                BuildUi();
            }

            public event Action<int, int, bool> PairSelected;
            public event Action<int, int, bool> PairDeleteRequested;
            public event Action<string> StateEditRequested;
            public event Action<bool, Rect> AddPairRequested;
            public event Action<float> ZoomChanged;

            public float Zoom => m_Zoom;

            public void SetCanAddPair(bool canAddPair)
            {
                m_CanAddPair = canAddPair;
            }

            public void SetData(string editingStateUiKey, string editingStateLabel, IReadOnlyList<PairViewData> pairs)
            {
                m_EditingStateUiKey = editingStateUiKey ?? string.Empty;
                m_EditingStateLabel = editingStateLabel ?? string.Empty;
                m_Pairs.Clear();
                if (pairs != null)
                {
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        m_Pairs.Add(pairs[i]);
                    }
                }

                RebuildGraph();
                RefreshViewportAfterLayout();
            }

            public void SetEmpty(string message)
            {
                m_EditingStateUiKey = string.Empty;
                m_EditingStateLabel = string.Empty;
                m_Pairs.Clear();
                RebuildGraph(message);
                RefreshViewportAfterLayout();
            }

            public void ResetView()
            {
                m_Zoom = 1f;
                m_PanOffset = Vector2.zero;
                RebuildGraph();
                ZoomChanged?.Invoke(m_Zoom);
            }

            public void RefreshViewportAfterLayout()
            {
                RefreshCanvasViewport();
                schedule.Execute(RefreshCanvasViewport).ExecuteLater(0);
                schedule.Execute(RefreshCanvasViewport).ExecuteLater(16);
            }

            private void BuildUi()
            {
                RegisterCallback<GeometryChangedEvent>(_ => RefreshCanvasViewport());

                m_ScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
                m_ScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                m_ScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                m_ScrollView.style.flexGrow = 1;
                m_ScrollView.style.minHeight = 0;
                m_ScrollView.RegisterCallback<GeometryChangedEvent>(_ => RefreshCanvasViewport());
                m_ScrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RefreshCanvasViewport());
                Add(m_ScrollView);

                m_Canvas = new VisualElement();
                m_Canvas.style.position = Position.Relative;
                m_Canvas.style.backgroundColor = CanvasBg;
                m_Canvas.focusable = true;
                m_Canvas.RegisterCallback<WheelEvent>(OnWheel);
                m_Canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
                m_Canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                m_Canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
                m_Canvas.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                m_Canvas.RegisterCallback<PointerCaptureOutEvent>(_ => EndPan());
                m_Canvas.AddManipulator(new ContextualMenuManipulator(OnCanvasContextMenu));
                m_ScrollView.Add(m_Canvas);

                m_EdgeCanvas = new VisualElement();
                m_EdgeCanvas.style.position = Position.Absolute;
                m_EdgeCanvas.style.left = 0;
                m_EdgeCanvas.style.top = 0;
                m_EdgeCanvas.pickingMode = PickingMode.Ignore;
                m_EdgeCanvas.generateVisualContent += OnGenerateVisualContent;
                m_Canvas.Add(m_EdgeCanvas);

                m_NodeLayer = new VisualElement();
                m_NodeLayer.style.position = Position.Absolute;
                m_NodeLayer.style.left = 0;
                m_NodeLayer.style.top = 0;
                m_Canvas.Add(m_NodeLayer);

                m_EmptyLabel = new Label();
                m_EmptyLabel.style.position = Position.Absolute;
                m_EmptyLabel.style.left = 14;
                m_EmptyLabel.style.top = 14;
                m_EmptyLabel.style.color = TextMuted;
                m_EmptyLabel.style.fontSize = 11;
                m_Canvas.Add(m_EmptyLabel);
            }

            private void OnCanvasContextMenu(ContextualMenuPopulateEvent evt)
            {
                Rect activatorRect = GUIUtility.GUIToScreenRect(new Rect(evt.mousePosition, Vector2.one));
                DropdownMenuAction.Status GetStatus()
                {
                    return m_CanAddPair
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled;
                }

                evt.menu.AppendAction("+ In", _ => AddPairRequested?.Invoke(true, activatorRect), _ => GetStatus());
                evt.menu.AppendAction("+ Out", _ => AddPairRequested?.Invoke(false, activatorRect), _ => GetStatus());
                evt.StopPropagation();
            }

            private void RebuildGraph(string emptyMessage = null)
            {
                m_InStateRows.Clear();
                m_OutStateRows.Clear();
                m_Edges.Clear();
                m_NodeLayer.Clear();

                if (string.IsNullOrWhiteSpace(m_EditingStateUiKey))
                {
                    ApplyCanvasSize(MinCanvasWidth, MinCanvasHeight);
                    m_EmptyLabel.text = string.IsNullOrWhiteSpace(emptyMessage) ? "No editing state" : emptyMessage;
                    m_EmptyLabel.style.display = DisplayStyle.Flex;
                    m_EdgeCanvas?.MarkDirtyRepaint();
                    return;
                }

                int inStateCount = CountUniqueStates(true);
                int outStateCount = CountUniqueStates(false);
                float inHeight = inStateCount == 0 ? NodeHeight : inStateCount * RowPitch - (RowPitch - NodeHeight);
                float outHeight = outStateCount == 0 ? NodeHeight : outStateCount * RowPitch - (RowPitch - NodeHeight);
                float graphHeight = Mathf.Max(CenterNodeHeight, inHeight, outHeight);
                float graphWidth = CenterNodeWidth + ColumnPitch * 2f;
                m_CanvasOrigin = new Vector2(CanvasPadding, CanvasPadding);
                ApplyCanvasSize(Mathf.Max(MinCanvasWidth, graphWidth + CanvasPadding * 2f), Mathf.Max(MinCanvasHeight, graphHeight + CanvasPadding * 2f));
                m_EmptyLabel.style.display = DisplayStyle.None;

                float centerX = CanvasPadding + ColumnPitch;
                float centerY = CanvasPadding + graphHeight * 0.5f - CenterNodeHeight * 0.5f;
                Rect centerRect = new(centerX, centerY, CenterNodeWidth, CenterNodeHeight);
                CreateNode(centerRect, m_EditingStateLabel, "Editing State", true, false, null);

                for (int i = 0; i < m_Pairs.Count; i++)
                {
                    PairViewData pair = m_Pairs[i];
                    if (pair.IsInState)
                    {
                        Rect nodeRect = GetStateNodeRect(pair.LayoutKey, true, graphHeight);
                        CreateNode(nodeRect, pair.StateKey, BuildPairSummary(pair), false, pair.IsSelected, pair);
                        m_Edges.Add(new EdgeLayout(nodeRect, centerRect, pair));
                    }
                    else
                    {
                        Rect nodeRect = GetStateNodeRect(pair.LayoutKey, false, graphHeight);
                        CreateNode(nodeRect, pair.StateKey, BuildPairSummary(pair), false, pair.IsSelected, pair);
                        m_Edges.Add(new EdgeLayout(centerRect, nodeRect, pair));
                    }
                }

                if (m_Pairs.Count == 0)
                {
                    m_EmptyLabel.text = "No transitions for current state";
                    m_EmptyLabel.style.display = DisplayStyle.Flex;
                }

                m_EdgeCanvas?.MarkDirtyRepaint();
            }

            private Rect GetStateNodeRect(string stateKey, bool inState, float graphHeight)
            {
                Dictionary<string, int> rows = inState ? m_InStateRows : m_OutStateRows;
                if (!rows.TryGetValue(stateKey, out int row))
                {
                    row = rows.Count;
                    rows[stateKey] = row;
                }

                int rowCount = Mathf.Max(1, inState ? CountUniqueStates(true) : CountUniqueStates(false));
                float columnHeight = rowCount * RowPitch - (RowPitch - NodeHeight);
                float startY = CanvasPadding + graphHeight * 0.5f - columnHeight * 0.5f;
                float x = inState ? CanvasPadding : CanvasPadding + ColumnPitch * 2f;
                return new Rect(x, startY + row * RowPitch, NodeWidth, NodeHeight);
            }

            private void CreateNode(Rect graphRect, string title, string detail, bool isCurrent, bool selected, PairViewData? pair)
            {
                string stateKey = title ?? string.Empty;
                Rect rect = ScaleRect(graphRect);
                VisualElement node = new();
                node.style.position = Position.Absolute;
                node.style.left = rect.x;
                node.style.top = rect.y;
                node.style.width = graphRect.width;
                node.style.height = graphRect.height;
                node.style.transformOrigin = new TransformOrigin(0f, 0f);
                node.style.scale = new Vector2(m_Zoom, m_Zoom);
                node.style.paddingLeft = 8;
                node.style.paddingRight = 8;
                node.style.paddingTop = 6;
                node.style.paddingBottom = 6;
                node.style.backgroundColor = isCurrent ? CurrentNodeBg : NodeBg;
                node.style.borderTopWidth = selected ? 2 : 1;
                node.style.borderBottomWidth = selected ? 2 : 1;
                node.style.borderLeftWidth = selected ? 2 : 1;
                node.style.borderRightWidth = selected ? 2 : 1;
                Color border = selected ? SelectedBorder : NodeBorder;
                node.style.borderTopColor = border;
                node.style.borderBottomColor = border;
                node.style.borderLeftColor = border;
                node.style.borderRightColor = border;
                node.style.borderTopLeftRadius = 6;
                node.style.borderTopRightRadius = 6;
                node.style.borderBottomLeftRadius = 6;
                node.style.borderBottomRightRadius = 6;

                Label titleLabel = new(title);
                titleLabel.style.color = TextNormal;
                titleLabel.style.fontSize = 12f;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.overflow = Overflow.Hidden;
                titleLabel.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(titleLabel);

                Label detailLabel = new(detail);
                detailLabel.style.color = pair.HasValue && pair.Value.IsAuto ? AutoEdgeColor : TextMuted;
                detailLabel.style.fontSize = 10f;
                detailLabel.style.marginTop = 2;
                detailLabel.style.overflow = Overflow.Hidden;
                detailLabel.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(detailLabel);

                node.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }

                    if (evt.clickCount >= 2)
                    {
                        StateEditRequested?.Invoke(pair.HasValue ? pair.Value.StateUiKey : m_EditingStateUiKey);
                        evt.StopPropagation();
                        return;
                    }

                    if (pair.HasValue)
                    {
                        PairViewData pairValue = pair.Value;
                        PairSelected?.Invoke(pairValue.TransitionIndex, pairValue.PairIndex, pairValue.IsAuto);
                        evt.StopPropagation();
                    }
                });

                if (pair.HasValue)
                {
                    PairViewData pairValue = pair.Value;
                    VisualElement actions = new();
                    actions.style.flexDirection = FlexDirection.Row;
                    actions.style.justifyContent = Justify.FlexEnd;
                    actions.style.marginTop = 3;
                    node.Add(actions);

                    Button delete = CreateIconButton("x", () => PairDeleteRequested?.Invoke(pairValue.TransitionIndex, pairValue.PairIndex, pairValue.IsAuto));
                    delete.tooltip = pairValue.CanDelete ? "删除这条 Transition。" : "Override 资源不可编辑。";
                    delete.SetEnabled(pairValue.CanDelete);
                    actions.Add(delete);
                }

                m_NodeLayer.Add(node);
            }

            private static Button CreateIconButton(string text, Action clicked)
            {
                Button button = new(clicked) { text = text };
                button.style.width = 22;
                button.style.height = 18;
                button.style.marginLeft = 3;
                button.style.paddingLeft = 0;
                button.style.paddingRight = 0;
                button.style.paddingTop = 0;
                button.style.paddingBottom = 0;
                button.style.fontSize = 10;
                return button;
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                DrawGrid(painter, GetCanvasPaintRect(), 32f * m_Zoom, m_CanvasOrigin * m_Zoom + m_PanOffset);
                for (int i = 0; i < m_Edges.Count; i++)
                {
                    DrawEdge(painter, m_Edges[i]);
                }
            }

            private void DrawEdge(Painter2D painter, EdgeLayout edge)
            {
                Rect fromRect = ScaleRect(edge.From);
                Rect toRect = ScaleRect(edge.To);
                Vector2 from = new(fromRect.xMax, fromRect.center.y);
                Vector2 to = new(toRect.xMin, toRect.center.y);
                float tangent = Mathf.Clamp((to.x - from.x) * 0.44f, 42f * m_Zoom, 110f * m_Zoom);
                Vector2 c1 = from + new Vector2(tangent, 0f);
                Vector2 c2 = to - new Vector2(tangent, 0f);

                Color color = edge.Pair.IsAuto
                    ? (edge.Pair.IsSelected ? AutoEdgeSelected : AutoEdgeColor)
                    : (edge.Pair.IsSelected ? EdgeSelected : EdgeColor);
                painter.lineWidth = (edge.Pair.IsSelected ? 3.25f : 1.7f) * Mathf.Clamp(m_Zoom, 0.65f, 1.25f);
                painter.strokeColor = color;
                if (edge.Pair.IsAuto)
                {
                    Vector2 previous = from;
                    for (int i = 1; i <= 24; i++)
                    {
                        Vector2 current = EvaluateCubic(from, c1, c2, to, i / 24f);
                        if (i % 2 != 0)
                        {
                            painter.BeginPath();
                            painter.MoveTo(previous);
                            painter.LineTo(current);
                            painter.Stroke();
                        }
                        previous = current;
                    }
                    DrawFilledCircle(painter, EvaluateCubic(from, c1, c2, to, 0.5f), 4f * Mathf.Clamp(m_Zoom, 0.72f, 1.2f), color);
                }
                else
                {
                    painter.BeginPath();
                    painter.MoveTo(from);
                    for (int i = 1; i <= 18; i++)
                    {
                        float t = i / 18f;
                        painter.LineTo(EvaluateCubic(from, c1, c2, to, t));
                    }
                    painter.Stroke();
                }

                DrawFilledCircle(painter, from, 3f * Mathf.Clamp(m_Zoom, 0.72f, 1.2f), color);
                DrawFilledCircle(painter, to, 3f * Mathf.Clamp(m_Zoom, 0.72f, 1.2f), color);
            }

            private static void DrawGrid(Painter2D painter, Rect rect, float gridSize, Vector2 origin)
            {
                if (rect.width <= 0f || rect.height <= 0f || gridSize <= 0.01f)
                {
                    return;
                }

                DrawGridLines(painter, rect, gridSize, origin, CanvasGrid, 1f);
                DrawGridLines(painter, rect, gridSize * 5f, origin, CanvasGridMajor, 1.15f);
            }

            private static void DrawGridLines(Painter2D painter, Rect rect, float gridSize, Vector2 origin, Color color, float lineWidth)
            {
                float startX = origin.x + Mathf.Floor((rect.xMin - origin.x) / gridSize) * gridSize;
                float startY = origin.y + Mathf.Floor((rect.yMin - origin.y) / gridSize) * gridSize;
                painter.strokeColor = color;
                painter.lineWidth = lineWidth;
                for (float x = startX; x <= rect.xMax; x += gridSize)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, rect.yMin));
                    painter.LineTo(new Vector2(x, rect.yMax));
                    painter.Stroke();
                }

                for (float y = startY; y <= rect.yMax; y += gridSize)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(rect.xMin, y));
                    painter.LineTo(new Vector2(rect.xMax, y));
                    painter.Stroke();
                }
            }

            private void OnWheel(WheelEvent evt)
            {
                float previousZoom = m_Zoom;
                float nextZoom = Mathf.Clamp(previousZoom * Mathf.Pow(WheelZoomBase, -evt.delta.y), MinZoom, MaxZoom);
                if (Mathf.Approximately(previousZoom, nextZoom))
                {
                    evt.StopPropagation();
                    return;
                }

                Vector2 viewportPoint = m_Canvas.WorldToLocal(evt.mousePosition);
                Vector2 graphPoint = (viewportPoint - m_PanOffset) / previousZoom;
                m_Zoom = nextZoom;
                m_PanOffset = viewportPoint - graphPoint * nextZoom;
                RebuildGraph();
                ZoomChanged?.Invoke(m_Zoom);
                evt.StopPropagation();
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 && evt.button != 2)
                {
                    return;
                }

                m_IsPanning = true;
                m_PanPointerId = evt.pointerId;
                m_PanStartPointer = new Vector2(evt.position.x, evt.position.y);
                m_PanStartOffset = m_PanOffset;
                m_Canvas.CapturePointer(evt.pointerId);
                m_Canvas.Focus();
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (!m_IsPanning || m_PanPointerId != evt.pointerId || !m_Canvas.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 pointerPosition = new(evt.position.x, evt.position.y);
                Vector2 delta = pointerPosition - m_PanStartPointer;
                m_PanOffset = m_PanStartOffset + delta;
                RebuildGraph();
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (!m_IsPanning || m_PanPointerId != evt.pointerId)
                {
                    return;
                }

                if (m_Canvas.HasPointerCapture(evt.pointerId))
                {
                    m_Canvas.ReleasePointer(evt.pointerId);
                }

                EndPan();
                evt.StopPropagation();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                if (m_Canvas.HasPointerCapture(evt.pointerId))
                {
                    m_Canvas.ReleasePointer(evt.pointerId);
                }

                EndPan();
            }

            private void EndPan()
            {
                m_IsPanning = false;
                m_PanPointerId = PointerId.invalidPointerId;
                m_PanStartPointer = Vector2.zero;
                m_PanStartOffset = Vector2.zero;
            }

            private void SetZoom(float value)
            {
                float nextZoom = Mathf.Clamp(value, MinZoom, MaxZoom);
                if (Mathf.Approximately(m_Zoom, nextZoom))
                {
                    return;
                }

                m_Zoom = nextZoom;
                RebuildGraph();
                ZoomChanged?.Invoke(m_Zoom);
            }

            private void ApplyCanvasSize(float width, float height)
            {
                m_BaseCanvasWidth = width;
                m_BaseCanvasHeight = height;
                Vector2 viewportSize = GetViewportSize();
                m_CanvasWidth = Mathf.Max(width * m_Zoom, viewportSize.x);
                m_CanvasHeight = Mathf.Max(height * m_Zoom, viewportSize.y);
                m_Canvas.style.width = m_CanvasWidth;
                m_Canvas.style.height = m_CanvasHeight;
                m_Canvas.style.minWidth = m_CanvasWidth;
                m_Canvas.style.minHeight = m_CanvasHeight;
                m_EdgeCanvas.style.width = m_CanvasWidth;
                m_EdgeCanvas.style.height = m_CanvasHeight;
                m_NodeLayer.style.width = m_CanvasWidth;
                m_NodeLayer.style.height = m_CanvasHeight;
                m_EdgeCanvas?.MarkDirtyRepaint();
                if (m_ScrollView != null)
                {
                    m_ScrollView.scrollOffset = Vector2.zero;
                }
            }

            private void RefreshCanvasViewport()
            {
                if (m_Canvas == null ||
                    m_EdgeCanvas == null ||
                    m_NodeLayer == null)
                {
                    return;
                }

                ApplyCanvasSize(m_BaseCanvasWidth, m_BaseCanvasHeight);
                m_EdgeCanvas?.MarkDirtyRepaint();
            }

            private Rect GetCanvasPaintRect()
            {
                Rect canvasLayout = m_EdgeCanvas?.layout ?? Rect.zero;
                Vector2 viewportSize = GetViewportSize();
                float width = Mathf.Max(m_CanvasWidth, canvasLayout.width, viewportSize.x);
                float height = Mathf.Max(m_CanvasHeight, canvasLayout.height, viewportSize.y);
                return new Rect(0f, 0f, width, height);
            }

            private Vector2 GetViewportSize()
            {
                if (m_ScrollView == null)
                {
                    return Vector2.zero;
                }

                Rect viewport = m_ScrollView.contentViewport.layout;
                Rect layout = m_ScrollView.layout;
                Vector2 size = new(Mathf.Max(0f, layout.width), Mathf.Max(0f, layout.height));
                Rect selfLayout = this.layout;
                size.x = Mathf.Max(size.x, viewport.width);
                size.y = Mathf.Max(size.y, viewport.height);
                if (selfLayout.width > 0f || selfLayout.height > 0f)
                {
                    size.x = Mathf.Max(size.x, selfLayout.width);
                    size.y = Mathf.Max(size.y, selfLayout.height);
                }

                return size;
            }

            private Rect ScaleRect(Rect rect)
            {
                return new Rect(rect.x * m_Zoom + m_PanOffset.x, rect.y * m_Zoom + m_PanOffset.y, rect.width * m_Zoom, rect.height * m_Zoom);
            }

            private int CountUniqueStates(bool inState)
            {
                HashSet<string> states = new(StringComparer.Ordinal);
                for (int i = 0; i < m_Pairs.Count; i++)
                {
                    PairViewData pair = m_Pairs[i];
                    if (pair.IsInState == inState)
                    {
                        states.Add(pair.LayoutKey);
                    }
                }

                return states.Count;
            }

            private static string BuildPairSummary(PairViewData pair)
            {
                if (pair.IsAuto)
                {
                    return $"AUTO  duration {pair.FadeIn:0.###}";
                }

                string name = string.IsNullOrWhiteSpace(pair.TransitionName) ? "Default Transition" : pair.TransitionName;
                return $"{name}  fade {pair.FadeIn:0.###}/{pair.FadeOut:0.###}  p{pair.Priority}";
            }

            private static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
            {
                float u = 1f - t;
                return u * u * u * p0 +
                       3f * u * u * t * p1 +
                       3f * u * t * t * p2 +
                       t * t * t * p3;
            }

            private static void DrawFilledCircle(Painter2D painter, Vector2 center, float radius, Color color)
            {
                painter.fillColor = color;
                painter.BeginPath();
                painter.Arc(center, radius, 0f, 360f);
                painter.Fill();
            }
        }

    }
}
#endif
