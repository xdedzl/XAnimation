#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    [CustomEditor(typeof(XAnimationActor))]
    public sealed class XAnimationActorEditor : UnityEditor.Editor
    {
        private const long RuntimeRefreshIntervalMs = 33;
        private const float PlaybackSpeedMin = 0.1f;
        private const float PlaybackSpeedMax = 2f;
        private const string NullStateKeyDisplayName = "[NULL]";

        private sealed class StateGroupBucket
        {
            public StateGroupBucket(string channelName, string groupName)
            {
                ChannelName = channelName ?? string.Empty;
                GroupName = groupName ?? string.Empty;
                States = new List<XAnimationStateConfig>(); 
            }

            public string ChannelName { get; }
            public string GroupName { get; }
            public List<XAnimationStateConfig> States { get; }
            public bool IsUngrouped => string.IsNullOrWhiteSpace(GroupName);
        }

        private sealed class ClipGroupBucket
        {
            public ClipGroupBucket(string groupName)
            {
                GroupName = groupName ?? string.Empty;
                Clips = new List<XAnimationClipConfig>();
            }

            public string GroupName { get; }
            public List<XAnimationClipConfig> Clips { get; }
            public bool IsUngrouped => string.IsNullOrWhiteSpace(GroupName);
        }

        private sealed class ChannelNameOption
        {
            public string Name;
            public string DisplayName;
            public int ChannelOrder;

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private readonly Dictionary<string, VisualElement> m_StateRowMap = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> m_StateButtonMap = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RowVisualState> m_StateVisualStateMap = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_CollapsedStateGroupKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VisualElement> m_ClipRowMap = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> m_ClipButtonMap = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ClipRowVisualState> m_ClipVisualStateMap = new(StringComparer.Ordinal);

        private VisualElement m_ClipsListView;
        private VisualElement m_ClipsTabPane;
        private VisualElement m_StatesTabPane;
        private Button m_ClipsTabButton;
        private Button m_StatesTabButton;
        private VisualElement m_StatesListView;
        private Label m_StatusLabel;
        private IVisualElementScheduledItem m_RefreshItem;
        private VisualElement m_Root;
        private bool m_RuntimeViewsDirty = true;
        private bool m_LastPlayingState;
        private int m_LastAnimationAssetInstanceId;
        private int m_CachedSelectedAssetInstanceId = int.MinValue;
        private XAnimationAsset m_CachedAnimationAsset;
        private List<ChannelNameOption> m_CachedChannelOptions;
        private readonly Dictionary<string, AnimationClip> m_CachedClipObjectMap = new(StringComparer.Ordinal);

        private string m_CurrentPlaybackChannelName;
        private string m_CurrentPlaybackStateKey;
        private string m_CurrentPlaybackClipKey;
        private string m_PlayTargetChannelName;
        private float m_PlayFadeInOverride;
        private float m_PlayFadeOutOverride;
        private int m_PlayPriorityOverride;
        private bool m_PlayInterruptibleOverride = true;
        private bool m_ApplyTransitionOverrides;
        private float m_PlayEnterTimeOverride;
        private float m_PlaySpeed = 1f;
        private RuntimeInspectorTab m_SelectedRuntimeTab = RuntimeInspectorTab.States;

        private enum RuntimeInspectorTab
        {
            Clips,
            States,
        }

        public override VisualElement CreateInspectorGUI()
        {
            LoadPlaybackPrefs();

            VisualElement root = new()
            {
                style =
                {
                    paddingTop = 4,
                    paddingBottom = 4,
                }
            };
            m_Root = root;
            m_RuntimeViewsDirty = true;

            PropertyField animationAssetField = AddProperty(root, "m_AnimationAsset");
            AddProperty(root, "m_Animator");
            AddProperty(root, "m_InitializeOnAwake");
            AddProperty(root, "m_GlobalSpeed");
            AddProperty(root, "m_UpdateMode");
            AddProperty(root, "m_UnityAnimationEventsEnabled");

            PropertyField playOnStartField = AddProperty(root, "m_PlayOnStart");
            VisualElement startStateKeyContainer = new();
            root.Add(startStateKeyContainer);
            RebuildStateKeyPopup(startStateKeyContainer, "m_StartStateKey", "Start State Key");

            root.Add(BuildRuntimeInspector());

            animationAssetField?.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                ClearCurrentPlayback();
                RebuildStateKeyPopup(startStateKeyContainer, "m_StartStateKey", "Start State Key");
                MarkRuntimeViewsDirty();
                RefreshRuntimeViews();
            });
            playOnStartField?.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                RebuildStateKeyPopup(startStateKeyContainer, "m_StartStateKey", "Start State Key");
            });

            root.RegisterCallback<AttachToPanelEvent>(_ => StartRefreshLoop());
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                StopRefreshLoop();
                ClearCurrentPlayback();
            });
            root.schedule.Execute(RefreshRuntimeViews).ExecuteLater(0);
            return root;
        }

        private void OnDisable()
        {
            StopRefreshLoop();
            ClearCurrentPlayback();
        }

        private VisualElement BuildRuntimeInspector()
        {
            VisualElement root = new();
            root.style.marginTop = 8;

            root.Add(BuildRuntimeInspectorTabs());

            m_StatusLabel = new("播放和参数调试请使用 SceneView XAnimation Overlay。")
            {
                style =
                {
                    marginTop = 6,
                    color = TextMuted,
                    fontSize = BodyFontSize,
                    whiteSpace = WhiteSpace.Normal,
                }
            };
            root.Add(m_StatusLabel);
            return root;
        }

        private VisualElement BuildRuntimeInspectorTabs()
        {
            VisualElement card = CreateSubBox();

            VisualElement toolbar = new();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginBottom = 4;
            card.Add(toolbar);

            m_StatesTabButton = CreateRuntimeTabButton("States", RuntimeInspectorTab.States);
            m_ClipsTabButton = CreateRuntimeTabButton("Clips", RuntimeInspectorTab.Clips);
            toolbar.Add(m_StatesTabButton);
            toolbar.Add(m_ClipsTabButton);

            m_ClipsTabPane = new VisualElement();
            m_StatesTabPane = new VisualElement();

            m_ClipsListView = new VisualElement();
            m_StatesListView = new VisualElement();

            m_ClipsTabPane.Add(m_ClipsListView);
            m_StatesTabPane.Add(m_StatesListView);

            card.Add(m_StatesTabPane);
            card.Add(m_ClipsTabPane);
            RefreshRuntimeTabSelection();
            return card;
        }

        private Button CreateRuntimeTabButton(string text, RuntimeInspectorTab tab)
        {
            Button button = new(() =>
            {
                m_SelectedRuntimeTab = tab;
                RefreshRuntimeTabSelection();
            })
            {
                text = text
            };
            button.tooltip = $"切换到 {text}。";
            button.style.flexGrow = 1;
            button.style.flexBasis = 0;
            button.style.height = 24;
            button.style.marginLeft = 1;
            button.style.marginRight = 1;
            return button;
        }

        private void RefreshRuntimeTabSelection()
        {
            SetRuntimeTabVisible(RuntimeInspectorTab.Clips, m_ClipsTabPane, m_ClipsTabButton);
            SetRuntimeTabVisible(RuntimeInspectorTab.States, m_StatesTabPane, m_StatesTabButton);
        }

        private void SetRuntimeTabVisible(RuntimeInspectorTab tab, VisualElement pane, Button button)
        {
            bool selected = m_SelectedRuntimeTab == tab;
            if (pane != null)
            {
                pane.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (button != null)
            {
                button.style.backgroundColor = selected ? AccentColor : ListHeaderBg;
                button.style.color = selected ? Color.white : TextNormal;
            }
        }

        private void LoadPlaybackPrefs()
        {
            XAnimationPlaybackSettings settings = XAnimationPlaybackSettingsPrefs.Load();
            m_PlayTargetChannelName = settings.ChannelName;
            m_PlaySpeed = ClampPlaybackSpeed(settings.Speed);
            m_ApplyTransitionOverrides = settings.ApplyTransition;
            m_PlayFadeInOverride = Mathf.Max(0f, settings.FadeIn);
            m_PlayFadeOutOverride = Mathf.Max(0f, settings.FadeOut);
            m_PlayPriorityOverride = settings.Priority;
            m_PlayInterruptibleOverride = settings.Interruptible;
            m_PlayEnterTimeOverride = Mathf.Clamp01(settings.EnterTime);
        }

        private float GetPlaybackSpeed()
        {
            return ClampPlaybackSpeed(m_PlaySpeed);
        }

        private static float ClampPlaybackSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                return 1f;
            }

            return Mathf.Clamp(speed, PlaybackSpeedMin, PlaybackSpeedMax);
        }

        private void StartRefreshLoop()
        {
            StopRefreshLoop();
            if (m_Root == null)
            {
                return;
            }

            m_RefreshItem = m_Root.schedule.Execute(RefreshRuntimeLoop).Every(RuntimeRefreshIntervalMs);
        }

        private void StopRefreshLoop()
        {
            m_RefreshItem?.Pause();
            m_RefreshItem = null;
        }

        private void RefreshRuntimeViews()
        {
            RefreshRuntimeViewState();
            if (m_RuntimeViewsDirty)
            {
                RefreshChannelChoices();
                RebuildClipList();
                RebuildStateList();
                m_RuntimeViewsDirty = false;
            }

            RefreshStatePlayingStates();
            RefreshClipPlayingStates();
        }

        private void RefreshRuntimeLoop()
        {
            RefreshRuntimeViews();
        }

        private void RefreshRuntimeViewState()
        {
            int currentAssetInstanceId = GetCurrentAnimationAssetInstanceId();
            bool isPlaying = Application.isPlaying;
            if (currentAssetInstanceId != m_LastAnimationAssetInstanceId || isPlaying != m_LastPlayingState)
            {
                ClearCurrentPlayback();
                m_LastAnimationAssetInstanceId = currentAssetInstanceId;
                m_LastPlayingState = isPlaying;
                m_RuntimeViewsDirty = true;
            }
        }

        private void MarkRuntimeViewsDirty()
        {
            m_RuntimeViewsDirty = true;
            InvalidateAnimationAssetCache();
        }

        private int GetCurrentAnimationAssetInstanceId()
        {
            SerializedProperty assetProperty = serializedObject.FindProperty("m_AnimationAsset");
            return assetProperty?.objectReferenceValue != null ? assetProperty.objectReferenceValue.GetInstanceID() : 0;
        }

        private void RefreshChannelChoices()
        {
            List<ChannelNameOption> options = GetChannelOptions();
            if (!string.IsNullOrWhiteSpace(m_PlayTargetChannelName) && HasChannel(options, m_PlayTargetChannelName))
            {
                return;
            }

            m_PlayTargetChannelName = FindFirstChannelName(options);
        }

        private void RebuildClipList()
        {
            m_ClipsListView?.Clear();
            m_ClipRowMap.Clear();
            m_ClipButtonMap.Clear();
            m_ClipVisualStateMap.Clear();

            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (m_ClipsListView == null || asset?.clips == null || asset.clips.Length == 0)
            {
                AddEmptyLabel(m_ClipsListView, "No clips");
                return;
            }

            List<ClipGroupBucket> buckets = new();
            for (int i = 0; i < asset.clips.Length; i++)
            {
                XAnimationClipConfig clip = asset.clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key))
                {
                    continue;
                }

                string groupName = NormalizeClipEditorGroupName(clip.editorGroupName);
                ClipGroupBucket bucket = FindClipGroupBucket(buckets, groupName);
                if (bucket == null)
                {
                    bucket = new ClipGroupBucket(groupName);
                    buckets.Add(bucket);
                }

                bucket.Clips.Add(clip);
            }

            int rowIndex = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                ClipGroupBucket bucket = buckets[i];
                if (bucket == null)
                {
                    continue;
                }

                if (bucket.IsUngrouped)
                {
                    for (int clipIndex = 0; clipIndex < bucket.Clips.Count; clipIndex++)
                    {
                        m_ClipsListView.Add(CreateClipRow(bucket.Clips[clipIndex], rowIndex++));
                    }

                    continue;
                }

                m_ClipsListView.Add(CreateClipEditorGroup(bucket, ref rowIndex));
            }
        }

        private VisualElement CreateClipEditorGroup(ClipGroupBucket bucket, ref int rowIndex)
        {
            VisualElement group = CreateNestedListGroup();
            string groupKey = BuildClipGroupKey(bucket.GroupName);

            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(!IsClipGroupCollapsed(groupKey));
            header.Add(foldoutLabel);

            Label title = CreateBoldLabel(bucket.GroupName);
            title.style.flexGrow = 1;
            title.style.minWidth = 0;
            header.Add(title);

            Label info = CreateSmallInfoLabel($"{bucket.Clips.Count} clips");
            header.Add(info);
            group.Add(header);

            VisualElement content = new VisualElement();
            content.style.display = IsClipGroupCollapsed(groupKey) ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < bucket.Clips.Count; i++)
            {
                content.Add(CreateClipRow(bucket.Clips[i], rowIndex++));
            }

            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                bool expanded = content.style.display != DisplayStyle.None;
                content.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                foldoutLabel.text = expanded ? "▸" : "▾";
                SetClipGroupCollapsed(groupKey, expanded);
                evt.StopPropagation();
            });

            group.Add(content);
            return group;
        }

        private VisualElement CreateClipRow(XAnimationClipConfig clip, int rowIndex)
        {
            VisualElement container = CreateRowContainer(rowIndex);
            VisualElement progressFill = CreateRowProgressFill();
            container.Add(progressFill);
            ClipRowVisualState visualState = new()
            {
                BaseColor = RowBaseColor(rowIndex),
                ProgressFill = progressFill,
            };
            m_ClipVisualStateMap[clip.key] = visualState;
            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                visualState.Hovered = true;
                ApplyClipRowVisualState(clip.key);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                visualState.Hovered = false;
                ApplyClipRowVisualState(clip.key);
            });

            VisualElement row = CreateRowContent();
            container.Add(row);

            Label nameLabel = new(clip.key);
            nameLabel.style.width = 140;
            nameLabel.style.flexShrink = 0;
            nameLabel.style.color = TextNormal;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.position = Position.Relative;
            row.Add(nameLabel);

            Label pathLabel = new(clip.clipPath);
            pathLabel.style.flexGrow = 1;
            pathLabel.style.flexShrink = 1;
            pathLabel.style.minWidth = 0;
            pathLabel.style.marginLeft = 6;
            pathLabel.style.color = TextMuted;
            pathLabel.style.fontSize = BodyFontSize;
            pathLabel.style.position = Position.Relative;
            row.Add(pathLabel);

            Button locateButton = new(() => PingClipAsset(clip))
            {
                text = "◎"
            };
            locateButton.tooltip = "定位当前 clip 对应的 AnimationClip 资源。";
            locateButton.SetEnabled(TryGetClipAsset(clip, out _));
            ApplyClipIconButtonStyle(locateButton);
            locateButton.style.marginLeft = 6;
            locateButton.style.position = Position.Relative;
            row.Add(locateButton);

            Button playButton = new(() => ToggleClipPlayback(clip))
            {
                text = "▶"
            };
            playButton.tooltip = "使用当前 channelName 播放或暂停这个 clip。";
            ApplyClipIconButtonStyle(playButton);
            playButton.style.marginLeft = 6;
            playButton.style.position = Position.Relative;
            row.Add(playButton);

            m_ClipRowMap[clip.key] = container;
            m_ClipButtonMap[clip.key] = playButton;
            return container;
        }

        private void PingClipAsset(XAnimationClipConfig clip)
        {
            if (!TryGetClipAsset(clip, out AnimationClip clipAsset))
            {
                SetStatus(string.IsNullOrWhiteSpace(clip?.key)
                    ? "当前没有可定位的 clip。"
                    : $"没有找到 clip '{clip.key}' 对应的 AnimationClip 资源。", true);
                return;
            }

            EditorGUIUtility.PingObject(clipAsset);
            SetStatus($"已定位动画资源: {clipAsset.name}。");
        }

        private bool TryGetClipAsset(XAnimationClipConfig clip, out AnimationClip clipAsset)
        {
            clipAsset = null;
            if (clip == null || string.IsNullOrWhiteSpace(clip.clipPath))
            {
                return false;
            }

            string cacheKey = !string.IsNullOrWhiteSpace(clip.key) ? clip.key : clip.clipPath;
            if (m_CachedClipObjectMap.TryGetValue(cacheKey, out clipAsset))
            {
                return clipAsset != null;
            }

            clipAsset = XAnimationEditorAssetResolver.ResolveAnimationClip(clip.clipPath);
            if (clipAsset != null)
            {
                m_CachedClipObjectMap[cacheKey] = clipAsset;
            }

            return clipAsset != null;
        }

        private void RebuildStateList()
        {
            m_StatesListView?.Clear();
            m_StateRowMap.Clear();
            m_StateButtonMap.Clear();
            m_StateVisualStateMap.Clear();

            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (m_StatesListView == null || asset?.states == null || asset.states.Length == 0 || asset.channels == null)
            {
                AddEmptyLabel(m_StatesListView, "No states");
                return;
            }

            Dictionary<string, List<StateGroupBucket>> statesByChannel = new(StringComparer.Ordinal);
            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channel = asset.channels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                {
                    continue;
                }

                statesByChannel[channel.name] = new List<StateGroupBucket>();
            }

            for (int i = 0; i < asset.states.Length; i++)
            {
                XAnimationStateConfig state = asset.states[i];
                if (state == null || string.IsNullOrWhiteSpace(state.key))
                {
                    continue;
                }

                string channelName = state.channelName ?? string.Empty;
                if (!statesByChannel.TryGetValue(channelName, out List<StateGroupBucket> channelStates))
                {
                    channelStates = new List<StateGroupBucket>();
                    statesByChannel[channelName] = channelStates;
                }

                string groupName = NormalizeStateEditorGroupName(state.editorGroupName);
                StateGroupBucket bucket = FindStateGroupBucket(channelStates, groupName);
                if (bucket == null)
                {
                    bucket = new StateGroupBucket(channelName, groupName);
                    channelStates.Add(bucket);
                }

                bucket.States.Add(state);
            }

            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channel = asset.channels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                {
                    continue;
                }

                statesByChannel.TryGetValue(channel.name, out List<StateGroupBucket> channelStates);
                m_StatesListView.Add(CreateStateChannelGroup(channel, channelStates ?? new List<StateGroupBucket>()));
            }
        }

        private VisualElement CreateStateChannelGroup(XAnimationChannelConfig channel, List<StateGroupBucket> channelStates)
        {
            VisualElement group = CreateListGroup();
            VisualElement header = CreateListHeader();
            group.Add(header);

            Label title = CreateBoldLabel(channel.name);
            title.style.flexGrow = 1;
            header.Add(title);

            int stateCount = CountStatesInBuckets(channelStates);
            int groupedCount = CountGroupedBuckets(channelStates);
            Label info = CreateSmallInfoLabel(groupedCount > 0
                ? $"{channel.layerType} | {stateCount} states | {groupedCount} groups"
                : $"{channel.layerType} | {stateCount} states");
            header.Add(info);

            int rowIndex = 0;
            for (int i = 0; i < channelStates.Count; i++)
            {
                StateGroupBucket bucket = channelStates[i];
                if (bucket == null)
                {
                    continue;
                }

                if (bucket.IsUngrouped)
                {
                    for (int stateIndex = 0; stateIndex < bucket.States.Count; stateIndex++)
                    {
                        group.Add(CreateStateRow(bucket.States[stateIndex], rowIndex++));
                    }

                    continue;
                }

                group.Add(CreateStateEditorGroup(channel.name, bucket, ref rowIndex));
            }

            if (stateCount == 0)
            {
                AddEmptyLabel(group, "No states");
            }

            return group;
        }

        private VisualElement CreateStateEditorGroup(string channelName, StateGroupBucket bucket, ref int rowIndex)
        {
            VisualElement group = CreateNestedListGroup();
            string groupKey = BuildStateGroupKey(channelName, bucket.GroupName);

            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(!IsStateGroupCollapsed(groupKey));
            header.Add(foldoutLabel);

            Label title = CreateBoldLabel(bucket.GroupName);
            title.style.flexGrow = 1;
            title.style.minWidth = 0;
            header.Add(title);

            Label info = CreateSmallInfoLabel($"{bucket.States.Count} states");
            header.Add(info);
            group.Add(header);

            VisualElement content = new VisualElement();
            content.style.display = IsStateGroupCollapsed(groupKey) ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < bucket.States.Count; i++)
            {
                content.Add(CreateStateRow(bucket.States[i], rowIndex++));
            }

            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                bool expanded = content.style.display != DisplayStyle.None;
                content.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                foldoutLabel.text = expanded ? "▸" : "▾";
                SetStateGroupCollapsed(groupKey, expanded);
                evt.StopPropagation();
            });

            group.Add(content);
            return group;
        }

        private VisualElement CreateStateRow(XAnimationStateConfig state, int rowIndex)
        {
            VisualElement container = CreateRowContainer(rowIndex);
            VisualElement progressFill = CreateRowProgressFill();
            container.Add(progressFill);
            RowVisualState visualState = new()
            {
                BaseColor = RowBaseColor(rowIndex),
                ProgressFill = progressFill,
            };
            m_StateVisualStateMap[state.key] = visualState;
            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                visualState.Hovered = true;
                ApplyStateRowVisualState(state.key);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                visualState.Hovered = false;
                ApplyStateRowVisualState(state.key);
            });
            VisualElement row = CreateRowContent();
            container.Add(row);

            Label nameLabel = new(state.key);
            nameLabel.style.width = 140;
            nameLabel.style.flexShrink = 0;
            nameLabel.style.color = TextNormal;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.position = Position.Relative;
            row.Add(nameLabel);

            Label stateTypeLabel = new(GetStateTypeDisplayText(state));
            stateTypeLabel.style.flexGrow = 1;
            stateTypeLabel.style.flexShrink = 1;
            stateTypeLabel.style.minWidth = 0;
            stateTypeLabel.style.marginLeft = 6;
            stateTypeLabel.style.color = TextMuted;
            stateTypeLabel.style.fontSize = BodyFontSize;
            stateTypeLabel.style.position = Position.Relative;
            row.Add(stateTypeLabel);

            Button locateButton = new(() => OpenPreviewAndFocusState(target as XAnimationActor, state.key))
            {
                text = "↗"
            };
            locateButton.tooltip = "在预览窗口中定位到这个 state。";
            ApplyClipIconButtonStyle(locateButton);
            locateButton.style.marginLeft = 6;
            locateButton.style.position = Position.Relative;
            locateButton.SetEnabled(true);
            row.Add(locateButton);

            Button playButton = new(() => ToggleStatePlayback(state))
            {
                text = "▶"
            };
            playButton.tooltip = "播放或停止这个 state。";
            ApplyClipIconButtonStyle(playButton);
            playButton.style.marginLeft = 6;
            playButton.style.position = Position.Relative;
            playButton.SetEnabled(true);
            row.Add(playButton);

            m_StateRowMap[state.key] = container;
            m_StateButtonMap[state.key] = playButton;
            return container;
        }

        private void ToggleStatePlayback(XAnimationStateConfig state)
        {
            XAnimationActor actor = target as XAnimationActor;
            if (actor == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                ToggleEditModeStatePlayback(actor, state);
                return;
            }

            try
            {
                string channelName = FindPlayingChannelForState(actor, state.key) ?? state.channelName;
                XAnimationChannelState channelState = TryGetActorChannelState(actor, channelName, out XAnimationChannelState runtimeState) ? runtimeState : null;
                bool isPlaying = channelState != null && string.Equals(channelState.stateKey, state.key, StringComparison.Ordinal);
                if (isPlaying)
                {
                    actor.Stop(channelName, 0f);
                    ClearCurrentPlaybackIfMatches(channelName, state.key, null);
                    SetStatus($"已停止 state {state.key}。");
                }
                else
                {
                    actor.GlobalSpeed = GetPlaybackSpeed();
                    actor.PlayState(state.key, BuildTransitionOptions());
                    SetCurrentPlayback(channelName, state.key, null);
                    SetStatus($"正在播放 state {state.key}。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
            }

            XAnimationSceneOverlaySelection.RequestRepaint();
            RefreshRuntimeViews();
        }

        private void ToggleEditModeStatePlayback(XAnimationActor actor, XAnimationStateConfig state)
        {
            if (actor == null || state == null)
            {
                return;
            }

            ClearCurrentPlayback();
            XAnimationEditorActorPlaybackController controller = XAnimationSceneOverlaySelection.Controller;
            controller.ToggleStatePlayback(actor, state, GetPlaybackSpeed(), BuildTransitionOptions());
            SetStatus(controller.StatusText, controller.StatusIsError);

            RefreshRuntimeViews();
        }

        private void ToggleClipPlayback(XAnimationClipConfig clip)
        {
            XAnimationActor actor = target as XAnimationActor;
            if (actor == null || clip == null)
            {
                return;
            }

            string channelName = GetSelectedChannelName();
            if (string.IsNullOrWhiteSpace(channelName))
            {
                SetStatus("请先选择 clip 调试播放使用的 channelName。", true);
                return;
            }

            if (!Application.isPlaying)
            {
                ToggleEditModeClipPlayback(actor, clip, channelName);
                return;
            }

            try
            {
                XAnimationChannelState channelState = TryGetActorChannelState(actor, channelName, out XAnimationChannelState runtimeState) ? runtimeState : null;
                bool isPlaying = channelState != null && string.Equals(channelState.clipKey, clip.key, StringComparison.Ordinal);
                if (isPlaying)
                {
                    actor.Stop(channelName, 0f);
                    ClearCurrentPlaybackIfMatches(channelName, null, clip.key);
                    SetStatus($"已停止 clip {clip.key}。");
                }
                else
                {
                    actor.GlobalSpeed = GetPlaybackSpeed();
                    actor.PlayClip(clip.key, channelName, BuildTransitionOptions());
                    SetCurrentPlayback(channelName, null, clip.key);
                    SetStatus($"正在 {channelName} 播放 clip {clip.key}。");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
            }

            XAnimationSceneOverlaySelection.RequestRepaint();
            RefreshRuntimeViews();
        }

        private void ToggleEditModeClipPlayback(XAnimationActor actor, XAnimationClipConfig clip, string channelName)
        {
            ClearCurrentPlayback();
            XAnimationEditorActorPlaybackController controller = XAnimationSceneOverlaySelection.Controller;
            controller.ToggleClipPlayback(actor, clip, channelName, GetPlaybackSpeed(), BuildTransitionOptions());
            SetStatus(controller.StatusText, controller.StatusIsError);

            RefreshRuntimeViews();
        }

        private void RefreshStatePlayingStates()
        {
            XAnimationActor actor = target as XAnimationActor;
            HashSet<string> playingStateKeys = null;
            Dictionary<string, float> stateProgressByKey = null;
            if (actor != null)
            {
                XAnimationAsset asset = LoadCurrentAnimationAsset();
                if (asset?.channels != null)
                {
                    for (int i = 0; i < asset.channels.Length; i++)
                    {
                        XAnimationChannelConfig channel = asset.channels[i];
                        if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                        {
                            continue;
                        }

                        XAnimationChannelState state = GetChannelState(actor, channel.name);
                        if (state != null && !string.IsNullOrWhiteSpace(state.stateKey))
                        {
                            playingStateKeys ??= new HashSet<string>(StringComparer.Ordinal);
                            playingStateKeys.Add(state.stateKey);
                            stateProgressByKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                            stateProgressByKey[state.stateKey] = Mathf.Clamp01(state.normalizedTime);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, VisualElement> kvp in m_StateRowMap)
            {
                bool isPlaying = playingStateKeys != null && playingStateKeys.Contains(kvp.Key);
                if (m_StateVisualStateMap.TryGetValue(kvp.Key, out RowVisualState visualState))
                {
                    visualState.Playing = isPlaying;
                    visualState.Progress = isPlaying && stateProgressByKey != null && stateProgressByKey.TryGetValue(kvp.Key, out float progress)
                        ? progress
                        : 0f;
                    ApplyStateRowVisualState(kvp.Key);
                }
                if (m_StateButtonMap.TryGetValue(kvp.Key, out Button button))
                {
                    ApplyClipIconButtonStyle(button, isPlaying ? AccentColor : null);
                    button.text = isPlaying ? "■" : "▶";
                }
            }
        }

        private void RefreshClipPlayingStates()
        {
            XAnimationActor actor = target as XAnimationActor;
            HashSet<string> playingClipKeys = null;
            Dictionary<string, float> clipProgressByKey = null;
            if (actor != null)
            {
                XAnimationAsset asset = LoadCurrentAnimationAsset();
                if (asset?.channels != null)
                {
                    for (int i = 0; i < asset.channels.Length; i++)
                    {
                        XAnimationChannelConfig channel = asset.channels[i];
                        if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                        {
                            continue;
                        }

                        XAnimationChannelState state = GetChannelState(actor, channel.name);
                        if (state != null && !string.IsNullOrWhiteSpace(state.clipKey))
                        {
                            playingClipKeys ??= new HashSet<string>(StringComparer.Ordinal);
                            playingClipKeys.Add(state.clipKey);
                            clipProgressByKey ??= new Dictionary<string, float>(StringComparer.Ordinal);
                            clipProgressByKey[state.clipKey] = Mathf.Clamp01(state.normalizedTime);
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, VisualElement> kvp in m_ClipRowMap)
            {
                bool isPlaying = playingClipKeys != null && playingClipKeys.Contains(kvp.Key);
                if (m_ClipVisualStateMap.TryGetValue(kvp.Key, out ClipRowVisualState visualState))
                {
                    visualState.Playing = isPlaying;
                    visualState.Progress = isPlaying && clipProgressByKey != null && clipProgressByKey.TryGetValue(kvp.Key, out float progress)
                        ? progress
                        : 0f;
                    ApplyClipRowVisualState(kvp.Key);
                }

                if (m_ClipButtonMap.TryGetValue(kvp.Key, out Button button))
                {
                    ApplyClipIconButtonStyle(button, isPlaying ? AccentColor : null);
                    button.text = isPlaying ? "■" : "▶";
                }
            }
        }

        private XAnimationTransitionOptions BuildTransitionOptions()
        {
            XAnimationTransitionOptions transition = new()
            {
                interruptible = true,
            };

            if (m_ApplyTransitionOverrides)
            {
                transition.fadeIn = Mathf.Max(0f, m_PlayFadeInOverride);
                transition.fadeOut = Mathf.Max(0f, m_PlayFadeOutOverride);
                transition.priority = m_PlayPriorityOverride;
                transition.interruptible = m_PlayInterruptibleOverride;
                transition.enterTime = Mathf.Clamp01(m_PlayEnterTimeOverride);
            }

            return transition;
        }

        private XAnimationStateConfig FindStateConfig(string stateKey)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return null;
            }

            XAnimationAsset asset = LoadCurrentAnimationAsset();
            XAnimationStateConfig[] states = asset?.states ?? Array.Empty<XAnimationStateConfig>();
            for (int i = 0; i < states.Length; i++)
            {
                XAnimationStateConfig state = states[i];
                if (state != null && string.Equals(state.key, stateKey, StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }

        private void OpenPreviewAndPlayState(XAnimationActor actor, string stateKey)
        {
            if (!TryGetPreviewSelection(actor, out TextAsset animationAsset, out GameObject prefab))
            {
                return;
            }

            try
            {
                XAnimationPreviewWindow.ShowWindowAndPlayState(
                    animationAsset,
                    prefab,
                    stateKey,
                    GetPlaybackSpeed(),
                    BuildTransitionOptions());
                SetStatus($"已在预览窗口打开并播放 state {stateKey}。");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
            }
        }

        private void OpenPreviewAndFocusState(XAnimationActor actor, string stateKey)
        {
            if (!TryGetPreviewSelection(actor, out TextAsset animationAsset, out GameObject prefab))
            {
                return;
            }

            try
            {
                XAnimationPreviewWindow.ShowWindowAndFocusState(
                    animationAsset,
                    prefab,
                    stateKey);
                SetStatus($"已在预览窗口定位 state {stateKey}。");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, actor);
                SetStatus(ex.Message, true);
            }
        }

        private bool TryGetPreviewSelection(XAnimationActor actor, out TextAsset animationAsset, out GameObject prefab)
        {
            animationAsset = actor?.AnimationAsset;
            prefab = null;

            if (animationAsset == null)
            {
                SetStatus("当前 XAnimationActor 没有绑定 animation asset。", true);
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(actor.gameObject))
            {
                prefab = actor.gameObject;
            }
            else
            {
                prefab = PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject);
                if (prefab == null)
                {
                    prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(actor.gameObject);
                }
            }

            if (prefab == null)
            {
                SetStatus("非运行时播放需要当前对象关联到一个 prefab asset，才能在预览窗口中打开。", true);
                return false;
            }

            return true;
        }

        private string FindPlayingChannelForState(XAnimationActor actor, string stateKey)
        {
            if (actor == null || string.IsNullOrWhiteSpace(stateKey))
            {
                return null;
            }

            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (asset?.channels == null)
            {
                return null;
            }

            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channel = asset.channels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                {
                    continue;
                }

                XAnimationChannelState state = GetChannelState(actor, channel.name);
                if (state != null && string.Equals(state.stateKey, stateKey, StringComparison.Ordinal))
                {
                    return channel.name;
                }
            }

            return null;
        }

        private XAnimationChannelState GetChannelState(XAnimationActor actor, string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return null;
            }

            if (!Application.isPlaying)
            {
                XAnimationEditorActorPlaybackController controller = XAnimationSceneOverlaySelection.Controller;
                controller.RefreshSelection();
                return controller.GetChannelState(channelName);
            }

            return TryGetActorChannelState(actor, channelName, out XAnimationChannelState state) ? state : null;
        }

        private static bool TryGetActorChannelState(XAnimationActor actor, string channelName, out XAnimationChannelState state)
        {
            state = null;
            return actor != null &&
                   !string.IsNullOrWhiteSpace(channelName) &&
                   actor.TryGetCurrentState(channelName, out state);
        }

        private string GetSelectedChannelName()
        {
            string channelName = m_PlayTargetChannelName;
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                return channelName;
            }

            List<ChannelNameOption> options = GetChannelOptions();
            return FindFirstChannelName(options);
        }

        private void SetCurrentPlayback(string channelName, string stateKey, string clipKey)
        {
            m_CurrentPlaybackChannelName = channelName ?? string.Empty;
            m_CurrentPlaybackStateKey = stateKey ?? string.Empty;
            m_CurrentPlaybackClipKey = clipKey ?? string.Empty;
        }

        private void ClearCurrentPlaybackIfMatches(string channelName, string stateKey, string clipKey)
        {
            bool channelMatches = string.IsNullOrWhiteSpace(channelName) ||
                                  string.Equals(m_CurrentPlaybackChannelName, channelName, StringComparison.Ordinal);
            bool stateMatches = string.IsNullOrWhiteSpace(stateKey) ||
                                string.Equals(m_CurrentPlaybackStateKey, stateKey, StringComparison.Ordinal);
            bool clipMatches = string.IsNullOrWhiteSpace(clipKey) ||
                               string.Equals(m_CurrentPlaybackClipKey, clipKey, StringComparison.Ordinal);
            if (channelMatches && stateMatches && clipMatches)
            {
                ClearCurrentPlayback();
            }
        }

        private void ClearCurrentPlayback()
        {
            m_CurrentPlaybackChannelName = string.Empty;
            m_CurrentPlaybackStateKey = string.Empty;
            m_CurrentPlaybackClipKey = string.Empty;
        }

        private PropertyField AddProperty(VisualElement root, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return null;
            }

            PropertyField field = new(property);
            root.Add(field);
            return field;
        }

        private void RebuildStateKeyPopup(VisualElement container, string propertyPath, string label)
        {
            container.Clear();
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return;
            }

            SerializedProperty playOnStartProperty = serializedObject.FindProperty("m_PlayOnStart");
            if (playOnStartProperty != null && !playOnStartProperty.boolValue)
            {
                return;
            }

            List<string> stateKeys = new();
            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (asset?.states != null)
            {
                for (int i = 0; i < asset.states.Length; i++)
                {
                    XAnimationStateConfig state = asset.states[i];
                    if (state != null && !string.IsNullOrWhiteSpace(state.key))
                    {
                        stateKeys.Add(state.key);
                    }
                }
            }

            List<string> stateKeyOptions = new() { NullStateKeyDisplayName };
            for (int i = 0; i < stateKeys.Count; i++)
            {
                if (!stateKeyOptions.Contains(stateKeys[i]))
                {
                    stateKeyOptions.Add(stateKeys[i]);
                }
            }

            string currentStateKey = property.stringValue ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentStateKey) && !stateKeyOptions.Contains(currentStateKey))
            {
                stateKeyOptions.Insert(1, currentStateKey);
            }

            string selectedStateKey = string.IsNullOrWhiteSpace(currentStateKey) ? NullStateKeyDisplayName : currentStateKey;
            PopupField<string> popup = new(label, stateKeyOptions, Mathf.Max(0, stateKeyOptions.IndexOf(selectedStateKey)));
            ConfigureInspectorPopupField(popup);
            popup.RegisterValueChangedCallback(evt =>
            {
                SerializedProperty targetProperty = serializedObject.FindProperty(propertyPath);
                if (targetProperty == null)
                {
                    return;
                }

                targetProperty.stringValue = evt.newValue == NullStateKeyDisplayName ? string.Empty : evt.newValue ?? string.Empty;
                serializedObject.ApplyModifiedProperties();
            });
            container.Add(popup);
        }

        private static void ConfigureInspectorPopupField(PopupField<string> popup)
        {
            popup.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            popup.style.minWidth = 0;
            popup.style.flexShrink = 1;

            void ApplyInnerStyle()
            {
                VisualElement input = popup.Q<VisualElement>(className: "unity-base-field__input");
                if (input != null)
                {
                    input.style.minWidth = 0;
                }
            }

            ApplyInnerStyle();
            popup.RegisterCallback<AttachToPanelEvent>(_ => ApplyInnerStyle());
        }

        private List<ChannelNameOption> GetChannelOptions()
        {
            if (m_CachedChannelOptions != null)
            {
                return m_CachedChannelOptions;
            }

            List<ChannelNameOption> options = new();
            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (asset?.channels == null)
            {
                m_CachedChannelOptions = options;
                return m_CachedChannelOptions;
            }

            for (int i = 0; i < asset.channels.Length; i++)
            {
                XAnimationChannelConfig channel = asset.channels[i];
                if (channel == null || string.IsNullOrWhiteSpace(channel.name))
                {
                    continue;
                }

                options.Add(new ChannelNameOption
                {
                    Name = channel.name,
                    DisplayName = $"{channel.name}    [{channel.layerType}]",
                    ChannelOrder = i,
                });
            }

            m_CachedChannelOptions = options;
            return m_CachedChannelOptions;
        }

        private XAnimationAsset LoadCurrentAnimationAsset()
        {
            TextAsset textAsset = GetSelectedAnimationTextAsset();
            int instanceId = textAsset != null ? textAsset.GetInstanceID() : 0;
            if (m_CachedAnimationAsset != null && m_CachedSelectedAssetInstanceId == instanceId)
            {
                return m_CachedAnimationAsset;
            }

            m_CachedSelectedAssetInstanceId = instanceId;
            m_CachedAnimationAsset = null;
            m_CachedClipObjectMap.Clear();
            m_CachedChannelOptions = null;
            if (textAsset == null)
            {
                return null;
            }

            XAnimationOverrideAsset overrideAsset = textAsset.ToXAnimationAsset<XAnimationOverrideAsset>();
            if (overrideAsset != null && !string.IsNullOrWhiteSpace(overrideAsset.baseAssetPath))
            {
                TextAsset baseTextAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(overrideAsset.baseAssetPath);
                m_CachedAnimationAsset = baseTextAsset == null ? null : baseTextAsset.ToXAnimationAsset<XAnimationAsset>();
                return m_CachedAnimationAsset;
            }

            m_CachedAnimationAsset = textAsset.ToXAnimationAsset<XAnimationAsset>();
            return m_CachedAnimationAsset;
        }

        private TextAsset GetSelectedAnimationTextAsset()
        {
            SerializedProperty assetProperty = serializedObject.FindProperty("m_AnimationAsset");
            return assetProperty?.objectReferenceValue as TextAsset;
        }

        private void SaveCurrentAnimationAsset()
        {
            XAnimationAsset asset = LoadCurrentAnimationAsset();
            if (asset == null)
            {
                throw new XAnimationException("当前没有选中的 XAnimationAsset。");
            }

            asset.SaveAsset();
        }

        private void ApplyStateType(XAnimationStateConfig state, XAnimationStateType stateType)
        {
            if (state == null)
            {
                throw new XAnimationException("State 配置不能为空。");
            }

            if (state.stateType == stateType)
            {
                return;
            }

            ApplyMigratedStateType(state, stateType);
        }

        private void ApplyMigratedStateType(XAnimationStateConfig state, XAnimationStateType stateType)
        {
            XAnimationStateType sourceType = state.stateType;
            string nextClipKey;
            string nextParameterName;
            string nextParameterXName;
            string nextParameterYName;
            XAnimationBlend1DSampleConfig[] nextSamples;
            XAnimationBlend2DSimpleDirectionalSampleConfig[] nextDirectionalSamples;

            if (stateType == XAnimationStateType.Single)
            {
                nextClipKey = ResolvePreferredSingleClipKey(state);
                nextParameterName = string.Empty;
                nextParameterXName = string.Empty;
                nextParameterYName = string.Empty;
                nextSamples = Array.Empty<XAnimationBlend1DSampleConfig>();
                nextDirectionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }
            else if (stateType == XAnimationStateType.Blend1D)
            {
                nextClipKey = string.Empty;
                nextParameterName = sourceType switch
                {
                    XAnimationStateType.Blend1D when !string.IsNullOrWhiteSpace(state.parameterName) => state.parameterName,
                    XAnimationStateType.Blend2DSimpleDirectional or XAnimationStateType.Blend2DFreeformDirectional
                        when !string.IsNullOrWhiteSpace(state.parameterXName) => state.parameterXName,
                    _ => EnsureFloatParameter(),
                };
                nextParameterXName = string.Empty;
                nextParameterYName = string.Empty;
                nextSamples = BuildMigratedBlendSamples(state);
                nextDirectionalSamples = Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }
            else if (IsDirectionalBlendStateType(stateType))
            {
                nextClipKey = string.Empty;
                nextParameterName = string.Empty;
                bool sourceDirectional = IsDirectionalBlendStateType(sourceType);
                bool sourceBlend1D = sourceType == XAnimationStateType.Blend1D;
                nextParameterXName = sourceDirectional && !string.IsNullOrWhiteSpace(state.parameterXName)
                    ? state.parameterXName
                    : sourceBlend1D && !string.IsNullOrWhiteSpace(state.parameterName)
                        ? state.parameterName
                        : EnsureFloatParameter("blendX");
                nextParameterYName = sourceDirectional && !string.IsNullOrWhiteSpace(state.parameterYName)
                    ? state.parameterYName
                    : sourceBlend1D && !string.IsNullOrWhiteSpace(state.parameterName)
                        ? state.parameterName
                        : EnsureFloatParameter("blendY");
                nextSamples = Array.Empty<XAnimationBlend1DSampleConfig>();
                nextDirectionalSamples = BuildMigratedDirectionalSamples(state);
            }
            else
            {
                throw new XAnimationException($"XAnimation stateType '{stateType}' is not supported.");
            }

            state.stateType = stateType;
            state.clipKey = nextClipKey;
            state.parameterName = nextParameterName;
            state.parameterXName = nextParameterXName;
            state.parameterYName = nextParameterYName;
            state.samples = nextSamples;
            state.directionalSamples = nextDirectionalSamples;
        }

        private string FindTemplateClipKey()
        {
            XAnimationClipConfig[] clips = LoadCurrentAnimationAsset()?.clips ?? Array.Empty<XAnimationClipConfig>();
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.key))
                {
                    return clip.key;
                }
            }

            return string.Empty;
        }

        private string EnsureFloatParameter(string prefix = "blend")
        {
            XAnimationAsset asset = LoadCurrentAnimationAsset();
            XAnimationParameterConfig[] parameters = asset?.parameters ?? Array.Empty<XAnimationParameterConfig>();
            for (int i = 0; i < parameters.Length; i++)
            {
                XAnimationParameterConfig parameter = parameters[i];
                if (parameter != null && parameter.type == XAnimationParameterType.Float && !string.IsNullOrWhiteSpace(parameter.name))
                {
                    if (string.IsNullOrWhiteSpace(prefix) || parameter.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return parameter.name;
                    }
                }
            }

            string parameterName = CreateUniqueParameterName(prefix);
            List<XAnimationParameterConfig> orderedParameters = new(parameters)
            {
                new()
                {
                    name = parameterName,
                    type = XAnimationParameterType.Float,
                    defaultValue = 0f,
                }
            };
            if (asset != null)
            {
                asset.parameters = orderedParameters.ToArray();
            }

            return parameterName;
        }

        private string CreateUniqueParameterName(string prefix)
        {
            return CreateUniqueName(prefix, name =>
            {
                XAnimationParameterConfig[] parameters = LoadCurrentAnimationAsset()?.parameters ?? Array.Empty<XAnimationParameterConfig>();
                for (int i = 0; i < parameters.Length; i++)
                {
                    XAnimationParameterConfig parameter = parameters[i];
                    if (parameter != null && string.Equals(parameter.name, name, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private static string CreateUniqueName(string prefix, Func<string, bool> exists)
        {
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "New" : prefix.Trim();
            if (!exists(safePrefix))
            {
                return safePrefix;
            }

            for (int i = 1; i < 1000; i++)
            {
                string candidate = $"{safePrefix}{i}";
                if (!exists(candidate))
                {
                    return candidate;
                }
            }

            throw new XAnimationException($"Unable to create unique name with prefix '{safePrefix}'.");
        }

        private XAnimationBlend1DSampleConfig[] CreateDefaultBlendSamples()
        {
            XAnimationClipConfig[] clips = LoadCurrentAnimationAsset()?.clips ?? Array.Empty<XAnimationClipConfig>();
            List<string> clipKeys = new(2);
            for (int i = 0; i < clips.Length && clipKeys.Count < 2; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip != null && !string.IsNullOrWhiteSpace(clip.key) && !clipKeys.Contains(clip.key))
                {
                    clipKeys.Add(clip.key);
                }
            }

            if (clipKeys.Count < 2)
            {
                throw new XAnimationException("Cannot create Blend1D state because at least two clips are required.");
            }

            return new[]
            {
                new XAnimationBlend1DSampleConfig
                {
                    clipKey = clipKeys[0],
                    threshold = 0f,
                },
                new XAnimationBlend1DSampleConfig
                {
                    clipKey = clipKeys[1],
                    threshold = 1f,
                }
            };
        }

        private XAnimationBlend2DSimpleDirectionalSampleConfig[] CreateDefaultDirectionalBlendSamples()
        {
            XAnimationClipConfig[] clips = LoadCurrentAnimationAsset()?.clips ?? Array.Empty<XAnimationClipConfig>();
            if (clips.Length < 2)
            {
                throw new XAnimationException("Cannot create Blend2DSimpleDirectional state because at least two clips are required.");
            }

            string idleClipKey = FindTemplateClipKey();
            string directionalClipKey = idleClipKey;
            for (int i = 0; i < clips.Length; i++)
            {
                XAnimationClipConfig clip = clips[i];
                if (clip == null || string.IsNullOrWhiteSpace(clip.key) || string.Equals(clip.key, idleClipKey, StringComparison.Ordinal))
                {
                    continue;
                }

                directionalClipKey = clip.key;
                break;
            }

            return new[]
            {
                new XAnimationBlend2DSimpleDirectionalSampleConfig
                {
                    clipKey = idleClipKey,
                    positionX = 0f,
                    positionY = 0f,
                },
                new XAnimationBlend2DSimpleDirectionalSampleConfig
                {
                    clipKey = directionalClipKey,
                    positionX = 0f,
                    positionY = 1f,
                }
            };
        }

        private string ResolvePreferredSingleClipKey(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return FindTemplateClipKey();
            }

            if (!string.IsNullOrWhiteSpace(state.clipKey))
            {
                return state.clipKey;
            }

            if (state.stateType == XAnimationStateType.Blend1D)
            {
                return GetFirstBlendSampleClipKey(state) ?? FindTemplateClipKey();
            }

            if (IsDirectionalBlendStateType(state.stateType))
            {
                return GetIdleDirectionalClipKey(state) ??
                       GetFirstDirectionalClipKey(state) ??
                       FindTemplateClipKey();
            }

            return FindTemplateClipKey();
        }

        private XAnimationBlend1DSampleConfig[] BuildMigratedBlendSamples(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return CreateDefaultBlendSamples();
            }

            if (state.stateType == XAnimationStateType.Blend1D && (state.samples?.Length ?? 0) >= 2)
            {
                return CloneBlendSamples(state.samples);
            }

            XAnimationBlend1DSampleConfig[] samples = CreateDefaultBlendSamples();
            List<string> seedClipKeys = GetBlendSeedClipKeys(state);
            for (int i = 0; i < samples.Length && i < seedClipKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(seedClipKeys[i]))
                {
                    samples[i].clipKey = seedClipKeys[i];
                }
            }

            return samples;
        }

        private XAnimationBlend2DSimpleDirectionalSampleConfig[] BuildMigratedDirectionalSamples(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return CreateDefaultDirectionalBlendSamples();
            }

            if (IsDirectionalBlendStateType(state.stateType) && (state.directionalSamples?.Length ?? 0) >= 2)
            {
                return CloneDirectionalSamples(state.directionalSamples);
            }

            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = CreateDefaultDirectionalBlendSamples();
            List<string> seedClipKeys = GetDirectionalSeedClipKeys(state);
            for (int i = 0; i < samples.Length && i < seedClipKeys.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(seedClipKeys[i]))
                {
                    samples[i].clipKey = seedClipKeys[i];
                }
            }

            return samples;
        }

        private static XAnimationBlend1DSampleConfig[] CloneBlendSamples(XAnimationBlend1DSampleConfig[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return Array.Empty<XAnimationBlend1DSampleConfig>();
            }

            XAnimationBlend1DSampleConfig[] cloned = new XAnimationBlend1DSampleConfig[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend1DSampleConfig sample = samples[i];
                cloned[i] = sample == null
                    ? null
                    : new XAnimationBlend1DSampleConfig
                    {
                        clipKey = sample.clipKey,
                        threshold = sample.threshold,
                    };
            }

            return cloned;
        }

        private static XAnimationBlend2DSimpleDirectionalSampleConfig[] CloneDirectionalSamples(XAnimationBlend2DSimpleDirectionalSampleConfig[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            }

            XAnimationBlend2DSimpleDirectionalSampleConfig[] cloned = new XAnimationBlend2DSimpleDirectionalSampleConfig[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                cloned[i] = sample == null
                    ? null
                    : new XAnimationBlend2DSimpleDirectionalSampleConfig
                    {
                        clipKey = sample.clipKey,
                        positionX = sample.positionX,
                        positionY = sample.positionY,
                    };
            }

            return cloned;
        }

        private List<string> GetBlendSeedClipKeys(XAnimationStateConfig state)
        {
            List<string> seedClipKeys = new(2);
            if (state == null)
            {
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Single)
            {
                AddOrderedClipKey(seedClipKeys, state.clipKey);
                return seedClipKeys;
            }

            if (IsDirectionalBlendStateType(state.stateType))
            {
                string idleClipKey = GetIdleDirectionalClipKey(state);
                AddOrderedClipKey(seedClipKeys, idleClipKey);
                XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                for (int i = 0; i < samples.Length && seedClipKeys.Count < 2; i++)
                {
                    XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                    if (sample == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(idleClipKey) &&
                        Mathf.Approximately(sample.positionX, 0f) &&
                        Mathf.Approximately(sample.positionY, 0f))
                    {
                        continue;
                    }

                    AddOrderedClipKey(seedClipKeys, sample.clipKey);
                }
            }

            return seedClipKeys;
        }

        private List<string> GetDirectionalSeedClipKeys(XAnimationStateConfig state)
        {
            List<string> seedClipKeys = new(2);
            if (state == null)
            {
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Single)
            {
                AddOrderedClipKey(seedClipKeys, state.clipKey);
                return seedClipKeys;
            }

            if (state.stateType == XAnimationStateType.Blend1D)
            {
                XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
                if (samples.Length > 0)
                {
                    AddOrderedClipKey(seedClipKeys, samples[0]?.clipKey);
                }

                if (samples.Length > 1)
                {
                    AddOrderedClipKey(seedClipKeys, samples[1]?.clipKey);
                }
            }

            return seedClipKeys;
        }

        private static string GetFirstBlendSampleClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend1DSampleConfig[] samples = state?.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(samples[i]?.clipKey))
                {
                    return samples[i].clipKey;
                }
            }

            return null;
        }

        private static string GetIdleDirectionalClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state?.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                if (sample != null &&
                    Mathf.Approximately(sample.positionX, 0f) &&
                    Mathf.Approximately(sample.positionY, 0f) &&
                    !string.IsNullOrWhiteSpace(sample.clipKey))
                {
                    return sample.clipKey;
                }
            }

            return null;
        }

        private static string GetFirstDirectionalClipKey(XAnimationStateConfig state)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state?.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(samples[i]?.clipKey))
                {
                    return samples[i].clipKey;
                }
            }

            return null;
        }

        private static void AddOrderedClipKey(List<string> clipKeys, string clipKey)
        {
            if (!string.IsNullOrWhiteSpace(clipKey))
            {
                clipKeys.Add(clipKey);
            }
        }

        private static bool IsDirectionalBlendStateType(XAnimationStateType stateType)
        {
            return stateType == XAnimationStateType.Blend2DSimpleDirectional ||
                   stateType == XAnimationStateType.Blend2DFreeformDirectional;
        }

        private void InvalidateAnimationAssetCache()
        {
            m_CachedSelectedAssetInstanceId = int.MinValue;
            m_CachedAnimationAsset = null;
            m_CachedChannelOptions = null;
            m_CachedClipObjectMap.Clear();
        }

        private static string FindChannelDisplayName(List<ChannelNameOption> options, string channelName)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Name, channelName, StringComparison.Ordinal))
                {
                    return options[i].DisplayName;
                }
            }

            return null;
        }

        private static string NormalizeChannelOptionValue(string displayValue)
        {
            if (string.IsNullOrWhiteSpace(displayValue))
            {
                return null;
            }

            int markerIndex = displayValue.IndexOf("    [", StringComparison.Ordinal);
            return markerIndex >= 0 ? displayValue[..markerIndex] : displayValue;
        }

        private static string FindFirstChannelName(List<ChannelNameOption> options)
        {
            return options != null && options.Count > 0 ? options[0].Name : null;
        }

        private static bool HasChannel(List<ChannelNameOption> options, string channelName)
        {
            if (options == null || string.IsNullOrWhiteSpace(channelName))
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].Name, channelName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetStatus(string message, bool isError = false)
        {
            if (m_StatusLabel == null)
            {
                return;
            }

            m_StatusLabel.text = message;
            m_StatusLabel.style.color = isError ? DangerColor : TextMuted;
        }

        private void ApplyStateRowVisualState(string stateKey)
        {
            if (!m_StateRowMap.TryGetValue(stateKey, out VisualElement row) ||
                !m_StateVisualStateMap.TryGetValue(stateKey, out RowVisualState visualState))
            {
                return;
            }

            ApplyRowVisualState(row, visualState);
        }

        private void ApplyClipRowVisualState(string clipKey)
        {
            if (!m_ClipRowMap.TryGetValue(clipKey, out VisualElement row) ||
                !m_ClipVisualStateMap.TryGetValue(clipKey, out ClipRowVisualState visualState))
            {
                return;
            }

            ApplyRowVisualState(row, visualState);
        }

        private static string NormalizeStateEditorGroupName(string groupName)
        {
            groupName = groupName?.Trim();
            return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName;
        }

        private static string NormalizeClipEditorGroupName(string groupName)
        {
            groupName = groupName?.Trim();
            return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName;
        }

        private static string BuildStateGroupKey(string channelName, string groupName)
        {
            return $"{channelName ?? string.Empty}::{NormalizeStateEditorGroupName(groupName)}";
        }

        private static string BuildClipGroupKey(string groupName)
        {
            return NormalizeClipEditorGroupName(groupName);
        }

        private bool IsStateGroupCollapsed(string groupKey)
        {
            return !string.IsNullOrWhiteSpace(groupKey) && !m_CollapsedStateGroupKeys.Contains(groupKey);
        }

        private bool IsClipGroupCollapsed(string groupKey)
        {
            return !string.IsNullOrWhiteSpace(groupKey) && !m_CollapsedStateGroupKeys.Contains($"clip::{groupKey}");
        }

        private void SetStateGroupCollapsed(string groupKey, bool collapsed)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            if (collapsed)
            {
                m_CollapsedStateGroupKeys.Remove(groupKey);
            }
            else
            {
                m_CollapsedStateGroupKeys.Add(groupKey);
            }
        }

        private void SetClipGroupCollapsed(string groupKey, bool collapsed)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            string key = $"clip::{groupKey}";
            if (collapsed)
            {
                m_CollapsedStateGroupKeys.Remove(key);
            }
            else
            {
                m_CollapsedStateGroupKeys.Add(key);
            }
        }

        private static StateGroupBucket FindStateGroupBucket(List<StateGroupBucket> buckets, string groupName)
        {
            if (buckets == null)
            {
                return null;
            }

            groupName = NormalizeStateEditorGroupName(groupName);
            for (int i = 0; i < buckets.Count; i++)
            {
                StateGroupBucket bucket = buckets[i];
                if (bucket != null &&
                    string.Equals(NormalizeStateEditorGroupName(bucket.GroupName), groupName, StringComparison.Ordinal))
                {
                    return bucket;
                }
            }

            return null;
        }

        private static ClipGroupBucket FindClipGroupBucket(List<ClipGroupBucket> buckets, string groupName)
        {
            if (buckets == null)
            {
                return null;
            }

            groupName = NormalizeClipEditorGroupName(groupName);
            for (int i = 0; i < buckets.Count; i++)
            {
                ClipGroupBucket bucket = buckets[i];
                if (bucket != null &&
                    string.Equals(NormalizeClipEditorGroupName(bucket.GroupName), groupName, StringComparison.Ordinal))
                {
                    return bucket;
                }
            }

            return null;
        }

        private static int CountStatesInBuckets(List<StateGroupBucket> buckets)
        {
            int count = 0;
            if (buckets == null)
            {
                return count;
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                count += buckets[i]?.States?.Count ?? 0;
            }

            return count;
        }

        private static int CountGroupedBuckets(List<StateGroupBucket> buckets)
        {
            int count = 0;
            if (buckets == null)
            {
                return count;
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i] != null && !buckets[i].IsUngrouped)
                {
                    count++;
                }
            }

            return count;
        }

        private static string GetStateTypeDisplayText(XAnimationStateConfig state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            return state.stateType switch
            {
                XAnimationStateType.Blend1D => "Blend1D",
                XAnimationStateType.Blend2DSimpleDirectional => "Blend2DSimpleDirectional",
                XAnimationStateType.Blend2DFreeformDirectional => "Blend2DFreeformDirectional",
                _ => "Single",
            };
        }
    }
}
#endif
