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
        private VisualElement BuildDebugToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginBottom = 4;
            toolbar.style.paddingLeft = 2;
            toolbar.style.paddingRight = 2;
            toolbar.style.paddingTop = 2;
            toolbar.style.paddingBottom = 0;
            toolbar.style.backgroundColor = ToolbarBg;
            toolbar.style.borderTopLeftRadius = 3;
            toolbar.style.borderTopRightRadius = 3;
            toolbar.style.borderBottomLeftRadius = 0;
            toolbar.style.borderBottomRightRadius = 0;
            toolbar.style.borderTopWidth = 0;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderLeftWidth = 0;
            toolbar.style.borderRightWidth = 0;
            toolbar.style.borderBottomColor = SectionDivider;
            toolbar.style.justifyContent = Justify.FlexStart;
            toolbar.style.height = 30;

            VisualElement tabContainer = new();
            tabContainer.style.flexDirection = FlexDirection.Row;
            tabContainer.style.alignItems = Align.Center;
            tabContainer.style.flexGrow = 1;
            tabContainer.style.flexShrink = 1;
            tabContainer.style.minWidth = 0;
            tabContainer.style.overflow = Overflow.Hidden;
            toolbar.Add(tabContainer);

            m_SettingGroupButton = CreateToolbarTabButton("Setting", () => SetDebugToolbarGroup(DebugToolbarGroup.Setting));
            m_MainGroupButton = CreateToolbarTabButton("Channel", HandleStateTabClicked);
            m_MainGroupButton.style.width = 80;
            m_MainGroupButton.style.minWidth = 80;
            m_MainGroupButton.style.maxWidth = 80;
            m_MainGroupButton.style.paddingRight = 24;
            m_MainChannelArrow = new Label("▾");
            m_MainChannelArrow.pickingMode = PickingMode.Ignore;
            m_MainChannelArrow.style.position = Position.Absolute;
            m_MainChannelArrow.style.right = 6;
            m_MainChannelArrow.style.top = 0;
            m_MainChannelArrow.style.bottom = 0;
            m_MainChannelArrow.style.width = 12;
            m_MainChannelArrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            m_MainChannelArrow.style.visibility = Visibility.Hidden;
            VisualElement mainTab = new();
            mainTab.style.position = Position.Relative;
            mainTab.style.width = 80;
            mainTab.style.minWidth = 80;
            mainTab.style.maxWidth = 80;
            mainTab.style.flexShrink = 0;
            mainTab.Add(m_MainGroupButton);
            mainTab.Add(m_MainChannelArrow);
            m_ClipTabButton = CreateToolbarTabButton("Clips", () => SetDebugToolbarGroup(DebugToolbarGroup.Clip));
            m_ParametersGroupButton = CreateToolbarTabButton("Parameters", () => SetDebugToolbarGroup(DebugToolbarGroup.Parameters));

            tabContainer.Add(m_SettingGroupButton);
            tabContainer.Add(CreateToolbarDivider());
            tabContainer.Add(mainTab);
            tabContainer.Add(CreateToolbarDivider());
            tabContainer.Add(m_ClipTabButton);
            tabContainer.Add(CreateToolbarDivider());
            tabContainer.Add(m_ParametersGroupButton);

            VisualElement searchDivider = CreateToolbarDivider();
            searchDivider.style.marginLeft = 2;
            searchDivider.style.marginRight = 2;
            searchDivider.style.flexShrink = 0;
            toolbar.Add(searchDivider);

            m_ReloadPreviewButton = CreateToolbarActionButton(string.Empty, LoadPreview);
            m_ReloadPreviewButton.tooltip = "重新读取 Prefab 和 XAnimation 资源并刷新预览。";
            ApplyToolbarButtonIcon(m_ReloadPreviewButton, "d_Refresh", "Refresh", "d_TreeEditor.Refresh", "TreeEditor.Refresh");
            ApplyToolbarIconButtonSize(m_ReloadPreviewButton);
            toolbar.Add(m_ReloadPreviewButton);

            m_SearchButton = CreateToolbarActionButton(string.Empty, ToggleSearchPopup);
            m_SearchButton.tooltip = "打开搜索面板，搜索 state、clip、transition、cue、parameter、channel。";
            ApplyToolbarButtonIcon(m_SearchButton, "d_Search Icon", "Search Icon", "d_ViewToolZoom", "ViewToolZoom");
            ApplyToolbarIconButtonSize(m_SearchButton);
            toolbar.Add(m_SearchButton);

            m_SearchField = new TextField();
            m_SearchField.label = string.Empty;
            m_SearchField.tooltip = "搜索 state、clip、transition、cue、parameter、channel。";
            m_SearchField.style.width = Length.Percent(100);
            m_SearchField.style.minWidth = 0;
            m_SearchField.style.height = 24;
            m_SearchField.style.marginTop = 0;
            m_SearchField.style.marginBottom = 0;
            m_SearchField.style.backgroundColor = PaneBg;
            m_SearchField.style.borderTopWidth = 1;
            m_SearchField.style.borderBottomWidth = 1;
            m_SearchField.style.borderLeftWidth = 1;
            m_SearchField.style.borderRightWidth = 1;
            m_SearchField.style.borderTopColor = SectionDivider;
            m_SearchField.style.borderBottomColor = SectionDivider;
            m_SearchField.style.borderLeftColor = SectionDivider;
            m_SearchField.style.borderRightColor = SectionDivider;
            m_SearchField.style.borderTopLeftRadius = 3;
            m_SearchField.style.borderTopRightRadius = 3;
            m_SearchField.style.borderBottomLeftRadius = 3;
            m_SearchField.style.borderBottomRightRadius = 3;
            m_SearchField.RegisterValueChangedCallback(evt => RefreshSearchResults(evt.newValue));
            m_SearchField.RegisterCallback<FocusOutEvent>(_ =>
            {
                m_SearchField?.schedule.Execute(() =>
                {
                    if (m_SearchField == null || m_SearchResultsPopup == null)
                    {
                        return;
                    }

                    VisualElement focusedElement = m_SearchField.panel?.focusController?.focusedElement as VisualElement;
                    if (focusedElement == null || !m_SearchResultsPopup.Contains(focusedElement))
                    {
                        HideSearchResults();
                    }
                }).ExecuteLater(80);
            });
            m_SearchField.RegisterCallback<FocusInEvent>(_ =>
            {
                if (!string.IsNullOrWhiteSpace(m_SearchField?.value))
                {
                    RefreshSearchResults(m_SearchField.value);
                }
            });
            m_SearchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    HideSearchResults();
                    evt.StopPropagation();
                }
            });
            m_SearchButton.RegisterCallback<GeometryChangedEvent>(_ => UpdateSearchResultsPopupPosition());

            m_SearchResultsPopup = new VisualElement();
            m_SearchResultsPopup.style.position = Position.Absolute;
            m_SearchResultsPopup.style.left = 0;
            m_SearchResultsPopup.style.top = 0;
            m_SearchResultsPopup.style.width = 340;
            m_SearchResultsPopup.style.maxHeight = 320;
            m_SearchResultsPopup.style.paddingLeft = 8;
            m_SearchResultsPopup.style.paddingRight = 8;
            m_SearchResultsPopup.style.paddingTop = 8;
            m_SearchResultsPopup.style.paddingBottom = 8;
            m_SearchResultsPopup.style.backgroundColor = PaneBg;
            m_SearchResultsPopup.style.borderTopWidth = 1;
            m_SearchResultsPopup.style.borderBottomWidth = 1;
            m_SearchResultsPopup.style.borderLeftWidth = 1;
            m_SearchResultsPopup.style.borderRightWidth = 1;
            m_SearchResultsPopup.style.borderTopColor = SectionDivider;
            m_SearchResultsPopup.style.borderBottomColor = SectionDivider;
            m_SearchResultsPopup.style.borderLeftColor = SectionDivider;
            m_SearchResultsPopup.style.borderRightColor = SectionDivider;
            m_SearchResultsPopup.style.borderTopLeftRadius = 4;
            m_SearchResultsPopup.style.borderTopRightRadius = 4;
            m_SearchResultsPopup.style.borderBottomLeftRadius = 4;
            m_SearchResultsPopup.style.borderBottomRightRadius = 4;
            m_SearchResultsPopup.style.display = DisplayStyle.None;
            m_SearchResultsPopup.style.unityOverflowClipBox = OverflowClipBox.PaddingBox;
            m_SearchResultsPopup.pickingMode = PickingMode.Position;

            Label searchPopupTitle = new("Search");
            searchPopupTitle.style.color = TextNormal;
            searchPopupTitle.style.fontSize = BodyFontSize;
            searchPopupTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            searchPopupTitle.style.marginBottom = 6;
            m_SearchResultsPopup.Add(searchPopupTitle);

            m_SearchResultsPopup.Add(m_SearchField);

            ScrollView searchScroll = new();
            searchScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            searchScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            searchScroll.style.maxHeight = 320;
            searchScroll.style.flexGrow = 1;
            searchScroll.style.marginTop = 6;
            m_SearchResultsPopup.Add(searchScroll);

            m_SearchResultsList = new VisualElement();
            searchScroll.Add(m_SearchResultsList);

            return toolbar;
        }

        private static VisualElement CreateToolbarDivider()
        {
            VisualElement divider = new();
            divider.style.width = 1;
            divider.style.minWidth = 1;
            divider.style.height = 16;
            divider.style.alignSelf = Align.Center;
            divider.style.backgroundColor = SectionDivider;
            return divider;
        }

        private static void ApplyToolbarIconButtonSize(Button button)
        {
            button.style.width = 26;
            button.style.minWidth = 26;
            button.style.maxWidth = 26;
            button.style.height = 26;
            button.style.minHeight = 26;
            button.style.maxHeight = 26;
            button.style.paddingLeft = 0;
            button.style.paddingRight = 0;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.alignSelf = Align.Center;
            button.style.flexShrink = 0;
        }

        private Button CreateToolbarActionButton(string label, Action onClick)
        {
            Button button = new(onClick)
            {
                text = label
            };
            button.style.marginRight = 1;
            button.style.marginBottom = 0;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.paddingTop = 3;
            button.style.paddingBottom = 4;
            button.style.borderTopLeftRadius = 4;
            button.style.borderTopRightRadius = 4;
            button.style.borderBottomLeftRadius = 4;
            button.style.borderBottomRightRadius = 4;
            button.style.borderTopWidth = 1;
            button.style.borderBottomWidth = 1;
            button.style.borderLeftWidth = 1;
            button.style.borderRightWidth = 1;
            button.style.borderTopColor = SectionDivider;
            button.style.borderBottomColor = SectionDivider;
            button.style.borderLeftColor = SectionDivider;
            button.style.borderRightColor = SectionDivider;
            button.style.color = TextNormal;
            button.style.backgroundColor = new Color(0.18f, 0.24f, 0.34f, 1f);
            button.style.fontSize = BodyFontSize;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.flexShrink = 0;
            button.style.justifyContent = Justify.Center;
            button.style.alignItems = Align.Center;
            return button;
        }

        private Button CreateToolbarTabButton(string label, Action onClick)
        {
            Button button = new(onClick)
            {
                text = label
            };
            button.style.marginRight = 0;
            button.style.marginBottom = 0;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.paddingTop = 3;
            button.style.paddingBottom = 4;
            button.style.borderTopLeftRadius = 0;
            button.style.borderTopRightRadius = 0;
            button.style.borderBottomLeftRadius = 0;
            button.style.borderBottomRightRadius = 0;
            button.style.borderTopWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.color = TextMuted;
            button.style.backgroundColor = ToolbarBg;
            button.style.fontSize = BodyFontSize;
            button.style.unityFontStyleAndWeight = FontStyle.Normal;
            button.style.flexShrink = 0;
            return button;
        }

        private void SetDebugToolbarGroup(DebugToolbarGroup group)
        {
            if (group == DebugToolbarGroup.Main)
            {
                EnsureStateTabChannelSelection();
            }

            if (m_SelectedDebugToolbarGroup == group &&
                m_SettingGroupContainer != null &&
                m_MainGroupContainer != null &&
                m_ClipTabContainer != null &&
                m_ParametersGroupContainer != null)
            {
                ApplyDebugToolbarGroup();
                return;
            }

            m_SelectedDebugToolbarGroup = group;
            ApplyDebugToolbarGroup();
        }

        private void ApplyDebugToolbarGroup()
        {
            if (m_SettingGroupContainer == null || m_MainGroupContainer == null || m_ClipTabContainer == null || m_ParametersGroupContainer == null)
            {
                return;
            }

            if (m_SelectedDebugToolbarGroup != DebugToolbarGroup.Setting &&
                m_SelectedDebugToolbarGroup != DebugToolbarGroup.Main &&
                m_SelectedDebugToolbarGroup != DebugToolbarGroup.Clip &&
                m_SelectedDebugToolbarGroup != DebugToolbarGroup.Parameters)
            {
                m_SelectedDebugToolbarGroup = DebugToolbarGroup.Setting;
            }

            m_SettingGroupContainer.style.display = m_SelectedDebugToolbarGroup == DebugToolbarGroup.Setting ? DisplayStyle.Flex : DisplayStyle.None;
            m_MainGroupContainer.style.display = m_SelectedDebugToolbarGroup == DebugToolbarGroup.Main ? DisplayStyle.Flex : DisplayStyle.None;
            m_ClipTabContainer.style.display = m_SelectedDebugToolbarGroup == DebugToolbarGroup.Clip ? DisplayStyle.Flex : DisplayStyle.None;
            m_ParametersGroupContainer.style.display = m_SelectedDebugToolbarGroup == DebugToolbarGroup.Parameters ? DisplayStyle.Flex : DisplayStyle.None;

            ApplyToolbarTabVisual(m_SettingGroupButton, m_SelectedDebugToolbarGroup == DebugToolbarGroup.Setting);
            ApplyToolbarTabVisual(m_MainGroupButton, m_SelectedDebugToolbarGroup == DebugToolbarGroup.Main);
            m_MainChannelArrow.style.visibility = m_SelectedDebugToolbarGroup == DebugToolbarGroup.Main
                ? Visibility.Visible
                : Visibility.Hidden;
            ApplyToolbarTabVisual(m_ClipTabButton, m_SelectedDebugToolbarGroup == DebugToolbarGroup.Clip);
            ApplyToolbarTabVisual(m_ParametersGroupButton, m_SelectedDebugToolbarGroup == DebugToolbarGroup.Parameters);
        }

        private void HandleStateTabClicked()
        {
            if (m_SelectedDebugToolbarGroup != DebugToolbarGroup.Main)
            {
                SetDebugToolbarGroup(DebugToolbarGroup.Main);
                return;
            }

            ShowStateTabChannelMenu();
        }

        private bool EnsureStateTabChannelSelection()
        {
            if (m_Session == null || !m_Session.IsLoaded || m_Session.CompiledAsset.Channels.Count == 0)
            {
                m_StateTabChannelName = string.Empty;
                UpdateStateTabChannelButton();
                return false;
            }

            if (!HasChannel(m_StateTabChannelName))
            {
                m_StateTabChannelName = m_Session.CompiledAsset.Channels[0].Name;
            }

            UpdateStateTabChannelButton();
            return true;
        }

        private void UpdateStateTabChannelButton()
        {
            if (m_MainGroupButton == null)
            {
                return;
            }

            m_MainGroupButton.tooltip = string.IsNullOrWhiteSpace(m_StateTabChannelName)
                ? "点击切换到 Channel。当前没有可选择的 Channel。"
                : $"当前 Channel：{m_StateTabChannelName}。选中后点击可切换 Channel。";
        }

        private void ShowStateTabChannelMenu()
        {
            if (!EnsureStateTabChannelSelection())
            {
                return;
            }

            GenericMenu menu = new();
            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                string channelName = channels[i].Name;
                menu.AddItem(
                    new GUIContent(channelName),
                    string.Equals(channelName, m_StateTabChannelName, StringComparison.Ordinal),
                    () => SetStateTabChannel(channelName));
            }
            menu.DropDown(m_MainGroupButton.worldBound);
        }

        private void SetStateTabChannel(string channelName, bool rebuild = true)
        {
            m_StateTabChannelName = channelName;
            UpdateStateTabChannelButton();
            if (rebuild)
            {
                RebuildStateList();
            }
        }

        private void ShowStateTabAddNodeMenu()
        {
            if (EnsureStateTabChannelSelection())
            {
                ShowAddStateNodeMenu(m_AddStateNodeButton, m_StateTabChannelName, string.Empty);
            }
        }

        private void RefreshSearchIndex()
        {
            m_SearchEntries.Clear();

            if (m_Session == null || !m_Session.IsLoaded)
            {
                RefreshSearchResults(m_SearchField?.value);
                return;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (state == null)
                {
                    continue;
                }

                string stateKey = state.Key;
                string channelName = state.ChannelName;
                string parentPath = GetStatePathParent(stateKey);
                string pathSearchText = string.IsNullOrWhiteSpace(parentPath) ? string.Empty : $" path={parentPath}";
                AddSearchEntry(
                    SearchEntryType.State,
                    stateKey,
                    $"{state.StateType} | channel={state.ChannelName}{pathSearchText}",
                    $"{stateKey} {state.ChannelName} {parentPath} {state.Config.clipKey} {state.Config.parameterName}",
                    () => FocusStateInInspector(channelName, stateKey));
            }

            IReadOnlyList<XAnimationCompiledClip> clips = m_Session.CompiledAsset.Clips;
            XAnimationCueConfig[] cues = m_Session.CompiledAsset.Asset.cues ?? Array.Empty<XAnimationCueConfig>();
            for (int i = 0; i < clips.Count; i++)
            {
                XAnimationCompiledClip clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                string clipKey = clip.Key;
                string clipPath = clip.Config.clipPath ?? string.Empty;
                ClipPathInfo clipPathInfo = BuildClipPathInfo(clip);
                string displayPath = string.IsNullOrWhiteSpace(clipPathInfo.DisplayPath) ? clipKey : clipPathInfo.DisplayPath;
                AddSearchEntry(
                    SearchEntryType.Clip,
                    displayPath,
                    string.IsNullOrWhiteSpace(clipPath) ? "clip" : clipPath,
                    $"{clipKey} {displayPath} {clipPath} {clip.Clip?.name}",
                    () => FocusClipInInspector(clipKey));

                for (int cueIndex = 0; cueIndex < cues.Length; cueIndex++)
                {
                    XAnimationCueConfig cue = cues[cueIndex];
                    if (cue == null || !string.Equals(cue.clipKey, clipKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string cueKey = BuildCueSearchKey(clipKey, cueIndex);
                    string cueTitle = string.IsNullOrWhiteSpace(cue.eventKey)
                        ? $"{clipKey} @ {cue.time:0.###}"
                        : $"{cue.eventKey} @ {cue.time:0.###}";
                    string cueDetail = $"{displayPath} | clipKey={clipKey} | payload={cue.payload}";
                    AddSearchEntry(
                        SearchEntryType.Cue,
                        cueTitle,
                        cueDetail,
                        $"{clipKey} {displayPath} {cue.eventKey} {cue.payload} {cue.time:0.###}",
                        () => FocusCueInInspector(cueKey, clipKey));
                }

                List<DisplayedCueEntry> derivedCues = CollectDerivedClipCues(clip);
                for (int derivedIndex = 0; derivedIndex < derivedCues.Count; derivedIndex++)
                {
                    DisplayedCueEntry cue = derivedCues[derivedIndex];
                    string cueKey = BuildDerivedCueSearchKey(clipKey, derivedIndex);
                    string cueTitle = string.IsNullOrWhiteSpace(cue.EventKey)
                        ? $"{clipKey} evt @ {cue.Time:0.###}"
                        : $"{cue.EventKey} @ {cue.Time:0.###}";
                    string cueDetail = $"{displayPath} | clipKey={clipKey} | Animation Event | payload={cue.Payload}";
                    AddSearchEntry(
                        SearchEntryType.Cue,
                        cueTitle,
                        cueDetail,
                        $"{clipKey} {displayPath} {cue.EventKey} {cue.Payload} {cue.Time:0.###} animation event",
                        () => FocusCueInInspector(cueKey, clipKey));
                }
            }

            IReadOnlyList<XAnimationCompiledAutoTransition> transitions = m_Session.CompiledAsset.AutoTransitions;
            for (int i = 0; i < transitions.Count; i++)
            {
                XAnimationCompiledAutoTransition transition = transitions[i];
                if (transition == null)
                {
                    continue;
                }

                string channelName = transition.ChannelName;
                string preStateKey = transition.PreStateKey;
                string nextStateKey = string.IsNullOrWhiteSpace(transition.Config.nextStateKey) ? "None" : transition.Config.nextStateKey;
                string title = $"{channelName}: {preStateKey}";
                string detail = $"{preStateKey} -> {nextStateKey} | exit={transition.Config.exitTime:0.###} | duration={transition.Config.transitionDuration:0.###}";
                AddSearchEntry(
                    SearchEntryType.Transition,
                    title,
                    detail,
                    $"{channelName} {preStateKey} {transition.Config.nextStateKey} transition auto exit enter duration",
                    () => FocusAutoTransitionInInspector(channelName, preStateKey));
            }

            IReadOnlyList<XAnimationCompiledDefaultTransition> defaultTransitions = m_Session.CompiledAsset.DefaultTransitions;
            for (int i = 0; i < defaultTransitions.Count; i++)
            {
                XAnimationCompiledDefaultTransition transition = defaultTransitions[i];
                if (transition == null)
                {
                    continue;
                }

                string title = string.IsNullOrWhiteSpace(transition.ChannelName)
                    ? $"Default Transition {i + 1}"
                    : $"{transition.ChannelName} #{i + 1}";
                string pairs = FormatDefaultTransitionPairSummary(transition.Config);
                string detail = $"{pairs} | fadeIn={transition.Config.fadeIn:0.###} | fadeOut={transition.Config.fadeOut:0.###}";
                int transitionIndex = i;
                AddSearchEntry(
                    SearchEntryType.Transition,
                    title,
                    detail,
                    $"{title} {pairs} transition default fade enter priority interruptible",
                    () => FocusDefaultTransitionInInspector(transitionIndex));
            }

            IReadOnlyList<XAnimationCompiledParameter> parameters = m_Session.CompiledAsset.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                XAnimationCompiledParameter parameter = parameters[i];
                if (parameter == null)
                {
                    continue;
                }

                string parameterName = parameter.Name;
                AddSearchEntry(
                    SearchEntryType.Parameter,
                    parameterName,
                    $"{parameter.Type} | default={parameter.Config.defaultValue}",
                    $"{parameterName} {parameter.Type} {parameter.Config.defaultValue}",
                    () => FocusParameterInInspector(parameterName));
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationCompiledChannel channel = channels[i];
                if (channel == null)
                {
                    continue;
                }

                string channelName = channel.Name;
                AddSearchEntry(
                    SearchEntryType.Channel,
                    channelName,
                    $"{channel.Config.layerType} | weight={channel.Config.defaultWeight:0.###}",
                    $"{channelName} {channel.Config.layerType} {channel.Config.maskPath}",
                    () => FocusChannelInInspector(channelName));
            }

            RefreshSearchResults(m_SearchField?.value);
        }

        private void AddSearchEntry(SearchEntryType type, string title, string detail, string searchText, Action navigate)
        {
            m_SearchEntries.Add(new SearchEntry(type, title, detail, searchText, navigate));
        }

        private void RefreshSearchResults(string query)
        {
            if (m_SearchResultsList == null || m_SearchResultsPopup == null)
            {
                return;
            }

            m_SearchResultsList.Clear();
            m_VisibleSearchEntries.Clear();

            string normalizedQuery = query?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                HideSearchResults();
                return;
            }

            List<(SearchEntry Entry, int Score)> matches = new();
            for (int i = 0; i < m_SearchEntries.Count; i++)
            {
                SearchEntry entry = m_SearchEntries[i];
                int titleIndex = entry.Title.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                int detailIndex = entry.Detail.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                int searchIndex = entry.SearchText.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase);
                int score = titleIndex >= 0
                    ? titleIndex
                    : detailIndex >= 0
                        ? detailIndex + 100
                        : searchIndex >= 0
                            ? searchIndex + 200
                            : -1;
                if (score < 0)
                {
                    continue;
                }

                matches.Add((entry, score));
            }

            matches.Sort((left, right) =>
            {
                int scoreCompare = left.Score.CompareTo(right.Score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                int typeCompare = left.Entry.Type.CompareTo(right.Entry.Type);
                if (typeCompare != 0)
                {
                    return typeCompare;
                }

                return string.Compare(left.Entry.Title, right.Entry.Title, StringComparison.OrdinalIgnoreCase);
            });

            int maxCount = Mathf.Min(18, matches.Count);
            for (int i = 0; i < maxCount; i++)
            {
                SearchEntry entry = matches[i].Entry;
                m_VisibleSearchEntries.Add(entry);
                m_SearchResultsList.Add(CreateSearchResultRow(entry, i));
            }

            if (m_VisibleSearchEntries.Count == 0)
            {
                Label emptyLabel = new("No results");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.paddingLeft = 8;
                emptyLabel.style.paddingRight = 8;
                emptyLabel.style.paddingTop = 6;
                emptyLabel.style.paddingBottom = 6;
                m_SearchResultsList.Add(emptyLabel);
            }

            UpdateSearchResultsPopupPosition();
            m_SearchResultsPopup.style.display = DisplayStyle.Flex;
            m_SearchResultsPopup.BringToFront();
        }

        private VisualElement CreateSearchResultRow(SearchEntry entry, int rowIndex)
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Column;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.backgroundColor = rowIndex % 2 == 0 ? ListRowEvenBg : ListRowOddBg;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = SectionDivider;

            Label title = new($"[{GetSearchEntryTypeLabel(entry.Type)}] {entry.Title}");
            title.style.color = TextNormal;
            title.style.fontSize = BodyFontSize;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(title);

            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                Label detail = new(entry.Detail);
                detail.style.color = TextMuted;
                detail.style.fontSize = 10;
                detail.style.whiteSpace = WhiteSpace.Normal;
                row.Add(detail);
            }

            row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = HoverBg);
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = rowIndex % 2 == 0 ? ListRowEvenBg : ListRowOddBg);
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                HideSearchResults();
                entry.Navigate?.Invoke();
                evt.StopPropagation();
            });
            return row;
        }

        private void HideSearchResults()
        {
            if (m_SearchResultsPopup != null)
            {
                m_SearchResultsPopup.style.display = DisplayStyle.None;
            }

            m_SearchField?.Blur();
        }

        private void ToggleSearchPopup()
        {
            if (m_SearchResultsPopup == null)
            {
                return;
            }

            bool isVisible = m_SearchResultsPopup.style.display == DisplayStyle.Flex;
            if (isVisible)
            {
                HideSearchResults();
                return;
            }

            UpdateSearchResultsPopupPosition();
            m_SearchResultsPopup.style.display = DisplayStyle.Flex;
            m_SearchResultsPopup.BringToFront();
            m_SearchField?.Focus();
            if (!string.IsNullOrWhiteSpace(m_SearchField?.value))
            {
                RefreshSearchResults(m_SearchField.value);
            }
        }

        private void UpdateSearchResultsPopupPosition()
        {
            if (m_SearchButton == null || m_SearchResultsPopup == null || m_InspectorOverlayLayer == null)
            {
                return;
            }

            Rect buttonWorld = m_SearchButton.worldBound;
            Rect overlayWorld = m_InspectorOverlayLayer.worldBound;
            if (buttonWorld.width <= 0f || overlayWorld.width <= 0f)
            {
                return;
            }

            float left = Mathf.Max(0f, buttonWorld.xMin - overlayWorld.xMin - 220f);
            float top = Mathf.Max(0f, buttonWorld.yMax - overlayWorld.yMin + 2f);
            float width = Mathf.Min(320f, Mathf.Max(220f, overlayWorld.width - left));
            m_SearchResultsPopup.style.left = left;
            m_SearchResultsPopup.style.top = top;
            m_SearchResultsPopup.style.width = width;
        }

        private static string GetSearchEntryTypeLabel(SearchEntryType type)
        {
            return type switch
            {
                SearchEntryType.State => "State",
                SearchEntryType.Clip => "Clip",
                SearchEntryType.Transition => "Transition",
                SearchEntryType.Cue => "Cue",
                SearchEntryType.Parameter => "Parameter",
                SearchEntryType.Channel => "Channel",
                _ => "Item",
            };
        }

        private static void ApplyToolbarTabVisual(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.style.backgroundColor = selected ? PaneBg : ToolbarBg;
            button.style.color = selected ? TextNormal : TextMuted;
            button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            button.style.borderTopColor = selected ? PaneBorder : SectionDivider;
            button.style.borderLeftColor = selected ? PaneBorder : SectionDivider;
            button.style.borderRightColor = selected ? PaneBorder : SectionDivider;
        }
    }
}
#endif
