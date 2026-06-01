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
        public void CreateGUI()
        {
            BuildUI();
            ApplyDefaultSelections();
            SetStatus("拖入 prefab 和 .xanimation/.xanimationoverride，或打开已配置默认 prefab 的 XAnimationAsset。");
            ScheduleAutoReloadPreview();
            ApplyPendingOpenRequest();
        }

        private void BuildUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.paddingLeft = 2;
            root.style.paddingRight = 2;
            root.style.paddingTop = 2;
            root.style.paddingBottom = 2;
            root.style.flexDirection = FlexDirection.Column;

            TwoPaneSplitView splitView = new(0, DebugPaneInitialWidth, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1;
            root.Add(splitView);

            splitView.Add(BuildDebugPane());
            splitView.Add(BuildPreviewPane());
        }

        private void LoadPlaybackPrefs()
        {
            XAnimationPlaybackSettings settings = XAnimationPlaybackSettingsPrefs.Load();
            m_PlaybackSectionExpanded = settings.PlaybackSectionExpanded;
            m_PlayTransitionSectionExpanded = settings.TransitionSectionExpanded;
            m_PlayTargetChannelName = settings.ChannelName;
            m_PlaySpeed = Mathf.Approximately(settings.Speed, 0f) ? 1f : settings.Speed;
            m_ApplyTransitionRequestOverrides = settings.ApplyTransition;
            m_PlayFadeInOverride = Mathf.Max(0f, settings.FadeIn);
            m_PlayFadeOutOverride = Mathf.Max(0f, settings.FadeOut);
            m_PlayPriorityOverride = settings.Priority;
            m_PlayInterruptibleOverride = settings.Interruptible;
            m_PlayEnterTimeOverride = Mathf.Clamp01(settings.EnterTime);
            m_PlaybackPrefsLoaded = true;
        }

        private float GetPlaybackSpeed()
        {
            return Mathf.Approximately(m_PlaySpeed, 0f) ? 1f : m_PlaySpeed;
        }

        private float ClampPlaybackSpeed(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                return 1f;
            }

            return Mathf.Clamp(speed, PlaybackSpeedMin, PlaybackSpeedMax);
        }

        private void SetPlaybackSpeed(float speed, bool savePrefs = true, bool updateSession = true)
        {
            m_PlaySpeed = ClampPlaybackSpeed(speed);

            if (updateSession && m_Session != null && m_Session.IsLoaded)
            {
                m_Session.SetGlobalSpeed(m_PlaySpeed);
            }

            if (savePrefs)
            {
                SavePlaybackPrefs();
            }
        }

        private void SavePlaybackPrefs()
        {
            if (!m_PlaybackPrefsLoaded)
            {
                return;
            }

            XAnimationPlaybackSettingsPrefs.Save(new XAnimationPlaybackSettings
            {
                PlaybackSectionExpanded = m_PlaybackSectionExpanded,
                TransitionSectionExpanded = m_PlayTransitionSectionExpanded,
                ChannelName = m_PlayTargetChannelName,
                Speed = m_PlaySpeed,
                ApplyTransition = m_ApplyTransitionRequestOverrides,
                FadeIn = m_PlayFadeInOverride,
                FadeOut = m_PlayFadeOutOverride,
                Priority = m_PlayPriorityOverride,
                Interruptible = m_PlayInterruptibleOverride,
                EnterTime = m_PlayEnterTimeOverride,
            });
        }

        private static Button CreateStyledButton(string label, Action onClick, Color bgColor, float marginLeft = 0f)
        {
            Button btn = new(onClick) { text = label };
            bool isPlayGlyphButton = label == "▶";
            btn.tooltip = label switch
            {
                "重载" => "重新读取 Prefab 和 XAnimation 资源并刷新预览。",
                "重置位置" => "将预览对象位置和旋转恢复到初始状态。",
                "重置视角" => "将预览相机恢复到默认视角。",
                "■" => "停止所有正在播放的 channel。",
                "Ⅱ" => "暂停或继续当前预览播放。",
                "▶" => "暂停或继续当前预览播放。",
                "▸|" => "暂停状态下向后推进固定一帧（1/60s）。",
                "设为默认" => "用当前 Prefab 覆盖 XAnimationAsset 的 DefaultPrefabPath。",
                _ => label
            };
            btn.style.backgroundColor = bgColor;
            btn.style.color = Color.white;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.fontSize = isPlayGlyphButton ? BodyFontSize - 1f : BodyFontSize;
            btn.style.paddingLeft = 7;
            btn.style.paddingRight = 7;
            btn.style.paddingTop = 2;
            btn.style.paddingBottom = 2;
            if (marginLeft > 0f) btn.style.marginLeft = marginLeft;
            return btn;
        }

        private VisualElement BuildStatusRow()
        {
            VisualElement statusRow = new VisualElement();
            statusRow.style.flexDirection = FlexDirection.Row;
            statusRow.style.alignItems = Align.Center;
            statusRow.style.marginTop = 4;

            VisualElement statusBar = new VisualElement();
            statusBar.style.width = 2;
            statusBar.style.height = 12;
            statusBar.style.backgroundColor = AccentColor;
            statusBar.style.borderTopLeftRadius = 2;
            statusBar.style.borderTopRightRadius = 2;
            statusBar.style.borderBottomLeftRadius = 2;
            statusBar.style.borderBottomRightRadius = 2;
            statusBar.style.marginRight = 4;
            statusRow.Add(statusBar);

            m_StatusLabel = new Label();
            m_StatusLabel.style.color = TextNormal;
            m_StatusLabel.style.fontSize = BodyFontSize;
            m_StatusLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            statusRow.Add(m_StatusLabel);

            return statusRow;
        }

        private VisualElement BuildPreviewPane()
        {
            VisualElement pane = CreatePane();
            pane.style.minWidth = PreviewPaneMinWidth;

            VisualElement previewSurface = new VisualElement();
            previewSurface.style.position = Position.Relative;
            previewSurface.style.flexGrow = 1;
            previewSurface.style.minHeight = 0;
            pane.Add(previewSurface);

            m_PreviewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit
            };
            m_PreviewImage.style.position = Position.Absolute;
            m_PreviewImage.style.left = 0;
            m_PreviewImage.style.right = 0;
            m_PreviewImage.style.top = 0;
            m_PreviewImage.style.bottom = 0;
            m_PreviewImage.style.backgroundColor = new Color(0.11f, 0.11f, 0.12f, 1f);
            m_PreviewImage.style.borderTopWidth = 1;
            m_PreviewImage.style.borderBottomWidth = 1;
            m_PreviewImage.style.borderLeftWidth = 1;
            m_PreviewImage.style.borderRightWidth = 1;
            m_PreviewImage.style.borderTopColor = PaneBorder;
            m_PreviewImage.style.borderBottomColor = PaneBorder;
            m_PreviewImage.style.borderLeftColor = PaneBorder;
            m_PreviewImage.style.borderRightColor = PaneBorder;
            m_PreviewImage.style.borderTopLeftRadius = 4;
            m_PreviewImage.style.borderTopRightRadius = 4;
            m_PreviewImage.style.borderBottomLeftRadius = 4;
            m_PreviewImage.style.borderBottomRightRadius = 4;
            RegisterPreviewEvents();
            previewSurface.Add(m_PreviewImage);

            m_PlaybackOverlayCard = BuildPlaybackSettingsCard();
            m_PlaybackOverlayCard.style.position = Position.Absolute;
            m_PlaybackOverlayCard.style.left = m_PlaybackOverlayPosition.x;
            m_PlaybackOverlayCard.style.top = m_PlaybackOverlayPosition.y;
            m_PlaybackOverlayCard.style.minWidth = PlaybackOverlayMinWidth;
            m_PlaybackOverlayCard.style.backgroundColor = new Color(0.12f, 0.12f, 0.13f, 0.94f);
            m_PlaybackOverlayCard.style.marginBottom = 0;
            m_PlaybackOverlayCard.style.alignSelf = Align.FlexStart;
            previewSurface.Add(m_PlaybackOverlayCard);
            m_PlaybackOverlayCard.BringToFront();

            m_FreeformBlendGraphOverlay = BuildFreeformBlendGraphOverlay();
            previewSurface.Add(m_FreeformBlendGraphOverlay);
            m_FreeformBlendGraphOverlay.BringToFront();
            previewSurface.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                ClampPlaybackOverlayPosition();
                ClampFreeformBlendGraphOverlayPosition();
            });

            VisualElement controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.marginTop = 3;
            controls.style.alignItems = Align.Center;
            pane.Add(controls);

            controls.Add(CreateStyledButton("重置位置", ResetPreviewTransform, AccentColor));
            controls.Add(CreateStyledButton("重置视角", ResetPreviewCamera, AccentColor, 6));

            Label hint = new("右键拖拽旋转，WASD 移动，QE 升降，滚轮缩放。");
            hint.style.marginLeft = 4;
            hint.style.color = TextMuted;
            hint.style.fontSize = BodyFontSize;
            controls.Add(hint);

            m_GridToggle = new Toggle("网格") { value = true };
            m_GridToggle.tooltip = "显示或隐藏预览地面网格，只影响当前预览。";
            m_GridToggle.style.marginLeft = 12;
            m_GridToggle.RegisterValueChangedCallback(evt =>
            {
                if (m_Session == null || !m_Session.IsLoaded) return;
                m_Session.SetGridVisible(evt.newValue);
                RenderPreview();
            });
            controls.Add(m_GridToggle);

            return pane;
        }

        private VisualElement BuildDebugPane()
        {
            VisualElement pane = BuildDebugPaneShell();
            VisualElement inspectorPane = CreateDebugInspectorPane();
            BuildDebugTabContainers();
            ComposeSettingTab();
            ComposeMainTab();
            ComposeClipTab();
            ComposeChannelsTab();
            ComposeParametersTab();

            Button clearCueLogButton = CreateStyledButton("Clear", ClearCueLog, DangerColor);
            clearCueLogButton.tooltip = "清空当前 Preview Session 的 Log。";
            VisualElement cueCard = CreateCard("Log", clearCueLogButton);
            m_CueLogContainer = new ScrollView();
            m_CueLogContainer.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_CueLogContainer.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_CueLogContainer.style.flexGrow = 1;
            m_CueLogContainer.style.minHeight = 0;
            cueCard.Add(m_CueLogContainer);
            ConfigureConsoleSection(cueCard);

            ApplyDebugToolbarGroup();

            pane.Add(BuildInspectorConsoleSplit(inspectorPane, cueCard));

            VisualElement statusSpacer = new VisualElement();
            statusSpacer.style.height = 4;
            statusSpacer.style.flexShrink = 0;
            pane.Add(statusSpacer);
            pane.Add(BuildStatusRow());

            return pane;
        }

        private VisualElement BuildDebugPaneShell()
        {
            VisualElement pane = new VisualElement();
            pane.style.minWidth = DebugPaneMinWidth;
            pane.style.flexGrow = 1;
            pane.style.minHeight = 0;
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.paddingLeft = 3;
            pane.style.paddingRight = 3;
            pane.style.paddingTop = 3;
            pane.style.paddingBottom = 3;
            pane.style.backgroundColor = PaneBg;
            pane.style.borderTopLeftRadius = 6;
            pane.style.borderTopRightRadius = 6;
            pane.style.borderBottomLeftRadius = 6;
            pane.style.borderBottomRightRadius = 6;
            pane.style.borderTopWidth = 1;
            pane.style.borderBottomWidth = 1;
            pane.style.borderLeftWidth = 1;
            pane.style.borderRightWidth = 1;
            pane.style.borderTopColor = PaneBorder;
            pane.style.borderBottomColor = PaneBorder;
            pane.style.borderLeftColor = PaneBorder;
            pane.style.borderRightColor = PaneBorder;
            return pane;
        }

        private VisualElement CreateDebugInspectorPane()
        {
            VisualElement inspectorPane = new VisualElement();
            inspectorPane.style.position = Position.Relative;
            inspectorPane.style.flexDirection = FlexDirection.Column;
            inspectorPane.style.flexGrow = 1;
            inspectorPane.style.minHeight = 0;

            VisualElement toolbar = BuildDebugToolbar();
            toolbar.style.flexShrink = 0;
            inspectorPane.Add(toolbar);

            m_InspectorScrollView = new ScrollView();
            m_InspectorScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_InspectorScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_InspectorScrollView.style.flexGrow = 1;
            m_InspectorScrollView.style.minHeight = 0;
            inspectorPane.Add(m_InspectorScrollView);

            m_InspectorOverlayLayer = new VisualElement();
            m_InspectorOverlayLayer.style.position = Position.Absolute;
            m_InspectorOverlayLayer.style.left = 0;
            m_InspectorOverlayLayer.style.right = 0;
            m_InspectorOverlayLayer.style.top = 0;
            m_InspectorOverlayLayer.style.bottom = 0;
            m_InspectorOverlayLayer.pickingMode = PickingMode.Ignore;
            inspectorPane.Add(m_InspectorOverlayLayer);
            if (m_SearchResultsPopup != null)
            {
                m_InspectorOverlayLayer.Add(m_SearchResultsPopup);
                m_SearchResultsPopup.BringToFront();
            }

            return inspectorPane;
        }

        private void BuildDebugTabContainers()
        {
            m_SettingGroupContainer = CreateDebugTabContainer();
            m_InspectorScrollView.Add(m_SettingGroupContainer);

            m_MainGroupContainer = CreateDebugTabContainer();
            m_InspectorScrollView.Add(m_MainGroupContainer);

            m_ClipGroupContainer = CreateDebugTabContainer();
            m_InspectorScrollView.Add(m_ClipGroupContainer);

            m_ChannelsGroupContainer = CreateDebugTabContainer();
            m_InspectorScrollView.Add(m_ChannelsGroupContainer);

            m_ParametersGroupContainer = CreateDebugTabContainer();
            m_InspectorScrollView.Add(m_ParametersGroupContainer);
        }

        private static VisualElement CreateDebugTabContainer()
        {
            VisualElement container = new VisualElement();
            container.style.minHeight = 0;
            return container;
        }

        private void ComposeSettingTab()
        {
            m_SettingGroupContainer.Add(CreateAssetsSection());
            m_SettingGroupContainer.Add(CreateSettingActionsSection());
            m_SettingGroupContainer.Add(CreateAssetOptionsSection());
        }

        private void ComposeMainTab()
        {
            m_MainGroupContainer.Add(CreateStatesSection().Root);
            m_MainGroupContainer.Add(CreateAutoTransitionsSection().Root);
            m_MainGroupContainer.Add(CreateDefaultTransitionsSection().Root);
        }

        private void ComposeClipTab()
        {
            m_ClipGroupContainer.Add(CreateClipsSection().Root);
        }

        private void ComposeChannelsTab()
        {
            m_ChannelsGroupContainer.Add(CreateChannelsSection().Root);
        }

        private void ComposeParametersTab()
        {
            m_ParametersGroupContainer.Add(CreateParametersSection().Root);
        }

        private static void ApplyToolbarButtonIcon(Button button, params string[] iconNames)
        {
            if (button == null || iconNames == null || iconNames.Length == 0)
            {
                return;
            }

            Texture icon = null;
            for (int i = 0; i < iconNames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(iconNames[i]))
                {
                    continue;
                }

                icon = EditorGUIUtility.IconContent(iconNames[i]).image;
                if (icon != null)
                {
                    break;
                }
            }

            if (icon == null)
            {
                return;
            }

            button.text = string.Empty;
            button.Clear();
            Image image = new() { image = icon };
            image.tintColor = TextNormal;
            image.style.width = 13;
            image.style.height = 13;
            image.style.alignSelf = Align.Center;
            image.style.flexShrink = 0;
            image.style.marginTop = 0;
            image.style.marginBottom = 0;
            image.style.marginLeft = 0;
            image.style.marginRight = 0;
            button.Add(image);
        }

        private static Button CreateAssetToolbarIconButton(string text, string tooltip, Action onClick, Color? bgColor = null, params string[] iconNames)
        {
            Button button = new(onClick)
            {
                text = text
            };
            button.tooltip = tooltip;
            ApplyClipIconButtonStyle(button, bgColor ?? AccentColor);
            button.style.flexShrink = 0;
            ApplyToolbarButtonIcon(button, iconNames);
            return button;
        }

        private VisualElement CreateAssetsSection()
        {
            VisualElement assetsBar = CreateSubBox();
            assetsBar.style.marginBottom = 4;
            assetsBar.style.marginTop = 0;

            VisualElement prefabRow = new();
            prefabRow.style.flexDirection = FlexDirection.Row;
            prefabRow.style.alignItems = Align.Center;
            assetsBar.Add(prefabRow);

            m_PrefabField = new ObjectField()
            {
                label = "Prefab",
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            m_PrefabField.tooltip = "用于预览动画的角色 Prefab，必须包含 Animator。";
            m_PrefabField.RegisterValueChangedCallback(evt =>
            {
                m_SelectedPrefab = evt.newValue as GameObject;
                RefreshAssetsToolbarButtons();
                if (evt.newValue != null && m_AssetField?.value != null)
                {
                    LoadPreview();
                }
            });
            m_PrefabField.style.flexGrow = 1;
            m_PrefabField.style.flexBasis = 0;
            m_PrefabField.style.minWidth = 0;
            m_PrefabField.style.marginBottom = 0;
            prefabRow.Add(m_PrefabField);

            m_SaveCurrentPrefabAsDefaultButton = CreateAssetToolbarIconButton(
                "✓",
                "把当前 Prefab 设为这个 XAnimation 的默认模型。",
                SaveCurrentPrefabAsDefault);
            m_SaveCurrentPrefabAsDefaultButton.style.marginLeft = 6;
            prefabRow.Add(m_SaveCurrentPrefabAsDefaultButton);

            m_ResetPrefabToDefaultButton = CreateAssetToolbarIconButton(
                "↺",
                "把当前模型恢复成默认 Prefab，并重新加载预览。",
                ResetPrefabToDefault,
                ListHeaderBg);
            m_ResetPrefabToDefaultButton.style.marginLeft = 4;
            prefabRow.Add(m_ResetPrefabToDefaultButton);

            VisualElement assetRow = new();
            assetRow.style.flexDirection = FlexDirection.Row;
            assetRow.style.alignItems = Align.Center;
            assetRow.style.marginTop = 4;
            assetsBar.Add(assetRow);

            m_AssetField = new ObjectField()
            {
                label = "XAnimation",
                objectType = typeof(TextAsset),
                allowSceneObjects = false
            };
            m_AssetField.tooltip = "要加载和编辑的 XAnimation .xanimation 或 .xanimationoverride。";
            m_AssetField.RegisterValueChangedCallback(evt =>
            {
                m_SelectedAsset = evt.newValue as TextAsset;
                RefreshAssetsToolbarButtons();
            });

            m_AssetField.style.flexGrow = 1;
            m_AssetField.style.flexBasis = 0;
            m_AssetField.style.minWidth = 0;
            m_AssetField.style.marginBottom = 0;
            m_AssetField.style.marginLeft = 0;
            assetRow.Add(m_AssetField);

            m_ReloadPreviewButton = CreateAssetToolbarIconButton(
                "⟳",
                "重新读取 Prefab 和 XAnimation 资源并刷新预览。",
                LoadPreview,
                iconNames: new[] { "d_Refresh", "Refresh", "d_TreeEditor.Refresh", "TreeEditor.Refresh" });
            m_ReloadPreviewButton.style.marginLeft = 6;
            assetRow.Add(m_ReloadPreviewButton);

            RefreshAssetsToolbarButtons();
            return assetsBar;
        }

        private VisualElement CreateAssetOptionsSection()
        {
            VisualElement optionsBox = CreateSubBox();
            optionsBox.style.marginTop = 4;

            m_PreloadToggle = new Toggle("Preload");
            m_PreloadToggle.tooltip = "开启后，XAnimationDriver 初始化当前资源时会同步 PreloadAll，适合小型或确定常驻的动作集。";
            m_PreloadToggle.style.marginBottom = 0;
            m_PreloadToggle.RegisterValueChangedCallback(evt => SetSelectedAssetPreload(evt.newValue));
            optionsBox.Add(m_PreloadToggle);

            m_AssetRootMotionToggle = new Toggle("Root Motion");
            m_AssetRootMotionToggle.tooltip = "开启后，XAnimation 初始化时会设置 Animator.applyRootMotion，并由 OnAnimatorMove 消费位移。";
            m_AssetRootMotionToggle.style.marginBottom = 0;
            m_AssetRootMotionToggle.RegisterValueChangedCallback(evt => SetSelectedAssetRootMotion(evt.newValue));
            optionsBox.Add(m_AssetRootMotionToggle);

            Label hint = new("Preload 关闭时保持懒加载；Root Motion 是资产级总开关，Override 资源继承 base 设置。");
            hint.style.color = TextMuted;
            hint.style.fontSize = BodyFontSize;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginTop = 2;
            optionsBox.Add(hint);

            RefreshAssetsToolbarButtons();
            return optionsBox;
        }

        private VisualElement CreateSettingActionsSection()
        {
            VisualElement actionsBox = CreateSubBox();
            actionsBox.style.marginTop = 4;

            m_OpenGraphButton = CreateToolbarActionButton("Graph", OpenGraphDebuggerForPreview);
            m_OpenGraphButton.tooltip = "打开独立的 PlayableGraph 调试窗口。";
            actionsBox.Add(m_OpenGraphButton);

            return actionsBox;
        }

        private FoldoutCard CreateStatesSection()
        {
            m_StatesCard = CreateFoldoutCard("States", m_StatesSectionExpanded, value => m_StatesSectionExpanded = value);
            m_StateListView = new VisualElement();
            m_StatesCard.Content.Add(m_StateListView);
            return m_StatesCard;
        }

        private FoldoutCard CreateAutoTransitionsSection()
        {
            m_AddAutoTransitionButton = CreateStyledButton("+", AddAutoTransition, AccentColor);
            m_AddAutoTransitionButton.tooltip = "新增一个 Auto Transition。";
            SetAutoTransitionButtonsEnabled(false);

            VisualElement autoTransitionActions = new VisualElement();
            autoTransitionActions.style.flexDirection = FlexDirection.Row;
            autoTransitionActions.style.alignItems = Align.Center;
            autoTransitionActions.Add(m_AddAutoTransitionButton);

            m_AutoTransitionCard = CreateFoldoutCard("Auto Transition", m_AutoTransitionSectionExpanded, value => m_AutoTransitionSectionExpanded = value, autoTransitionActions);
            m_AutoTransitionEditorView = new VisualElement();
            m_AutoTransitionCard.Content.Add(m_AutoTransitionEditorView);
            return m_AutoTransitionCard;
        }

        private FoldoutCard CreateDefaultTransitionsSection()
        {
            m_AddDefaultTransitionButton = CreateStyledButton("+", AddDefaultTransition, AccentColor);
            m_AddDefaultTransitionButton.tooltip = "新增一个 Default Transition 分组。";
            SetDefaultTransitionButtonsEnabled(false);

            VisualElement defaultTransitionActions = new VisualElement();
            defaultTransitionActions.style.flexDirection = FlexDirection.Row;
            defaultTransitionActions.style.alignItems = Align.Center;
            defaultTransitionActions.Add(m_AddDefaultTransitionButton);

            m_DefaultTransitionsCard = CreateFoldoutCard("Default Transitions", m_DefaultTransitionsSectionExpanded, value => m_DefaultTransitionsSectionExpanded = value, defaultTransitionActions);
            m_DefaultTransitionsEditorView = new VisualElement();
            m_DefaultTransitionsCard.Content.Add(m_DefaultTransitionsEditorView);
            return m_DefaultTransitionsCard;
        }

        private FoldoutCard CreateClipsSection()
        {
            m_AddClipButton = CreateStyledButton("+", AddClip, AccentColor);
            m_AddClipButton.tooltip = "新增一个全局 clip 资源叶子。";
            m_AddClipGroupButton = CreateStyledButton("+ Group", AddClipGroup, AccentColor);
            m_AddClipGroupButton.tooltip = "新建一个 clip group。";
            SetAddClipButtonEnabled(false);

            VisualElement clipActions = new VisualElement();
            clipActions.style.flexDirection = FlexDirection.Row;
            clipActions.style.alignItems = Align.Center;
            clipActions.Add(m_AddClipButton);
            m_AddClipGroupButton.style.marginLeft = 4;
            clipActions.Add(m_AddClipGroupButton);

            m_ClipsCard = CreateFoldoutCard("Clips", m_ClipsSectionExpanded, value => m_ClipsSectionExpanded = value, clipActions);
            m_ClipListView = new VisualElement();
            m_ClipsCard.Content.Add(m_ClipListView);
            return m_ClipsCard;
        }

        private FoldoutCard CreateChannelsSection()
        {
            m_AddChannelButton = CreateStyledButton("+", AddChannel, AccentColor);
            m_AddChannelButton.tooltip = "新增一个 channel。";
            SetAddChannelButtonEnabled(false);

            m_ChannelsCard = CreateFoldoutCard("Channels", m_ChannelsSectionExpanded, value => m_ChannelsSectionExpanded = value, m_AddChannelButton);
            m_ChannelControlsContainer = new VisualElement();
            m_ChannelsCard.Content.Add(m_ChannelControlsContainer);
            return m_ChannelsCard;
        }

        private FoldoutCard CreateParametersSection()
        {
            m_AddParameterButton = CreateStyledButton("+", AddParameter, AccentColor);
            m_AddParameterButton.tooltip = "新增一个 XAnimation 参数。";
            SetAddParameterButtonEnabled(false);

            m_ParametersCard = CreateFoldoutCard("Parameters", m_ParametersSectionExpanded, value => m_ParametersSectionExpanded = value, m_AddParameterButton);
            m_ParameterListView = new VisualElement();
            m_ParametersCard.Content.Add(m_ParameterListView);
            return m_ParametersCard;
        }

        private VisualElement BuildFreeformBlendGraphOverlay()
        {
            XAnimationBlendGraphHudFrame frame = new();
            VisualElement overlay = frame.Root;
            overlay.style.position = Position.Absolute;
            overlay.style.left = m_FreeformBlendGraphOverlayPosition.x;
            overlay.style.bottom = m_FreeformBlendGraphOverlayPosition.y;
            overlay.style.width = FreeformBlendGraphOverlayWidth;
            overlay.style.display = DisplayStyle.None;

            frame.Header.style.marginBottom = BlendGraphOverlayHeaderMarginBottomExpanded;
            frame.Header.tooltip = "拖拽标题栏可以移动这个 Blend Graph HUD，点击标题栏可展开或收起。";
            m_FreeformBlendGraphOverlayHeader = frame.Header;
            m_FreeformBlendGraphTitleLabel = frame.TitleLabel;
            m_FreeformBlendGraphOverlayContent = frame.Content;
            m_FreeformBlendGraphElement = frame.DirectionalGraph;
            m_FreeformBlendGraphElement.tooltip = "蓝点是 sample，红点是当前 2D 参数值，圆圈大小表示实时 weight。拖动红点可预览 freeform directional blend。";
            m_Blend1DGraphElement = frame.Blend1DGraph;
            m_Blend1DGraphElement.tooltip = "蓝色包络表示 Blend1D sample weight，红线与红点表示当前参数值。拖动红点可预览 Blend1D。";
            m_FreeformBlendGraphHintLabel = frame.HintLabel;

            RegisterFreeformBlendGraphOverlayDrag(overlay, frame.Header, () => SetBlendGraphOverlayExpanded(!m_FreeformBlendGraphOverlayExpanded));
            SetBlendGraphOverlayExpanded(m_FreeformBlendGraphOverlayExpanded);

            return overlay;
        }

        private VisualElement BuildPlaybackSettingsCard()
        {
            m_PlaybackHudView = new XAnimationPlaybackHudView(new PreviewPlaybackHudHost(this), includeStatus: false);
            RegisterPlaybackOverlayDrag(m_PlaybackHudView.Root, () => m_PlaybackHudView.TogglePlaybackExpanded());

            FoldoutCard previewParametersCard = CreateSectionFoldoutCard("Preview Parameters", m_PreviewParametersSectionExpanded, value =>
            {
                m_PreviewParametersSectionExpanded = value;
            });
            previewParametersCard.Root.style.marginTop = 4;
            m_MainParameterPreviewView = new VisualElement();
            previewParametersCard.Content.Add(m_MainParameterPreviewView);
            m_PlaybackHudView.Content?.Add(previewParametersCard.Root);

            return m_PlaybackHudView.Root;
        }

        private void RegisterPlaybackOverlayDrag(VisualElement card, Action toggleExpanded)
        {
            if (card == null)
            {
                return;
            }

            VisualElement titleRow = card.childCount > 0 ? card[0] : null;
            Label toggleLabel = titleRow?.Q<Label>();
            if (titleRow == null || toggleLabel == null)
            {
                return;
            }

            toggleLabel.AddToClassList("xanim-playback-overlay-drag-handle");
            toggleLabel.tooltip = "拖拽左侧三角可以移动这个悬浮面板，鼠标抬起时如果位置没变化则展开/收起。";

            toggleLabel.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                m_IsDraggingPlaybackOverlay = true;
                m_PlaybackOverlayDragMoved = false;
                m_PlaybackOverlayDragStartPointer = new Vector2(evt.position.x, evt.position.y);
                m_PlaybackOverlayDragStartPosition = m_PlaybackOverlayPosition;
                toggleLabel.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            toggleLabel.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!m_IsDraggingPlaybackOverlay || !toggleLabel.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 pointerPosition = new Vector2(evt.position.x, evt.position.y);
                Vector2 delta = pointerPosition - m_PlaybackOverlayDragStartPointer;
                if (!m_PlaybackOverlayDragMoved && delta.sqrMagnitude > PlaybackOverlayClickThreshold * PlaybackOverlayClickThreshold)
                {
                    m_PlaybackOverlayDragMoved = true;
                }
                m_PlaybackOverlayPosition = m_PlaybackOverlayDragStartPosition + delta;
                ClampPlaybackOverlayPosition();
                evt.StopPropagation();
            });

            void EndDrag(IPointerEvent evt, bool canToggle)
            {
                if (!m_IsDraggingPlaybackOverlay)
                {
                    return;
                }

                bool shouldToggle = canToggle && !m_PlaybackOverlayDragMoved;
                m_IsDraggingPlaybackOverlay = false;
                m_PlaybackOverlayDragMoved = false;
                if (toggleLabel.HasPointerCapture(evt.pointerId))
                {
                    toggleLabel.ReleasePointer(evt.pointerId);
                }

                ClampPlaybackOverlayPosition();
                if (shouldToggle)
                {
                    toggleExpanded?.Invoke();
                }
            }

            toggleLabel.RegisterCallback<PointerUpEvent>(evt =>
            {
                EndDrag(evt, canToggle: true);
                evt.StopPropagation();
            });

            toggleLabel.RegisterCallback<PointerCancelEvent>(evt =>
            {
                EndDrag(evt, canToggle: false);
                evt.StopPropagation();
            });
        }

        private void ClampPlaybackOverlayPosition()
        {
            if (m_PlaybackOverlayCard == null || m_PlaybackOverlayCard.parent == null)
            {
                return;
            }

            Rect parentBounds = m_PlaybackOverlayCard.parent.contentRect;
            float cardWidth = Mathf.Max(PlaybackOverlayMinWidth, m_PlaybackOverlayCard.resolvedStyle.width);
            float cardHeight = Mathf.Max(0f, m_PlaybackOverlayCard.resolvedStyle.height);
            float maxX = Mathf.Max(0f, parentBounds.width - cardWidth);
            float maxY = Mathf.Max(0f, parentBounds.height - cardHeight);
            m_PlaybackOverlayPosition = new Vector2(
                Mathf.Clamp(m_PlaybackOverlayPosition.x, 0f, maxX),
                Mathf.Clamp(m_PlaybackOverlayPosition.y, 0f, maxY));

            m_PlaybackOverlayCard.style.left = m_PlaybackOverlayPosition.x;
            m_PlaybackOverlayCard.style.top = m_PlaybackOverlayPosition.y;
        }

        private void RegisterFreeformBlendGraphOverlayDrag(VisualElement card, VisualElement dragHandle, Action toggleExpanded)
        {
            if (card == null || dragHandle == null)
            {
                return;
            }

            dragHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                m_IsDraggingFreeformBlendGraphOverlay = true;
                m_FreeformBlendGraphOverlayDragMoved = false;
                m_FreeformBlendGraphOverlayDragStartPointer = new Vector2(evt.position.x, evt.position.y);
                m_FreeformBlendGraphOverlayDragStartPosition = m_FreeformBlendGraphOverlayPosition;
                dragHandle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            dragHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!m_IsDraggingFreeformBlendGraphOverlay || !dragHandle.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 pointerPosition = new Vector2(evt.position.x, evt.position.y);
                Vector2 delta = pointerPosition - m_FreeformBlendGraphOverlayDragStartPointer;
                if (!m_FreeformBlendGraphOverlayDragMoved && delta.sqrMagnitude > PlaybackOverlayClickThreshold * PlaybackOverlayClickThreshold)
                {
                    m_FreeformBlendGraphOverlayDragMoved = true;
                }

                m_FreeformBlendGraphOverlayPosition = new Vector2(
                    m_FreeformBlendGraphOverlayDragStartPosition.x + delta.x,
                    m_FreeformBlendGraphOverlayDragStartPosition.y - delta.y);
                ClampFreeformBlendGraphOverlayPosition();
                evt.StopPropagation();
            });

            void EndDrag(IPointerEvent evt, bool canToggle)
            {
                if (!m_IsDraggingFreeformBlendGraphOverlay)
                {
                    return;
                }

                bool shouldToggle = canToggle && !m_FreeformBlendGraphOverlayDragMoved;
                m_IsDraggingFreeformBlendGraphOverlay = false;
                m_FreeformBlendGraphOverlayDragMoved = false;
                if (dragHandle.HasPointerCapture(evt.pointerId))
                {
                    dragHandle.ReleasePointer(evt.pointerId);
                }

                ClampFreeformBlendGraphOverlayPosition();
                if (shouldToggle)
                {
                    toggleExpanded?.Invoke();
                }
            }

            dragHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                EndDrag(evt, canToggle: true);
                evt.StopPropagation();
            });

            dragHandle.RegisterCallback<PointerCancelEvent>(evt =>
            {
                EndDrag(evt, canToggle: false);
                evt.StopPropagation();
            });
        }

        private void SetBlendGraphOverlayExpanded(bool expanded)
        {
            float visibleContentHeight = GetResolvedElementHeight(m_FreeformBlendGraphOverlayContent);
            if (visibleContentHeight > 0f)
            {
                m_FreeformBlendGraphLastExpandedContentHeight = visibleContentHeight;
            }

            float heightDelta = 0f;
            if (expanded == m_FreeformBlendGraphOverlayExpanded)
            {
                if (m_FreeformBlendGraphOverlayHeader != null)
                {
                    m_FreeformBlendGraphOverlayHeader.style.marginBottom = expanded ? BlendGraphOverlayHeaderMarginBottomExpanded : 0f;
                }
            }
            else if (expanded)
            {
                heightDelta = -(Mathf.Max(m_FreeformBlendGraphLastExpandedContentHeight, visibleContentHeight) + BlendGraphOverlayHeaderMarginBottomExpanded);
            }
            else
            {
                heightDelta = visibleContentHeight + BlendGraphOverlayHeaderMarginBottomExpanded;
            }

            m_FreeformBlendGraphOverlayExpanded = expanded;
            if (m_FreeformBlendGraphOverlayHeader != null)
            {
                m_FreeformBlendGraphOverlayHeader.style.marginBottom = expanded ? BlendGraphOverlayHeaderMarginBottomExpanded : 0f;
            }

            if (m_FreeformBlendGraphOverlayContent != null)
            {
                m_FreeformBlendGraphOverlayContent.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!Mathf.Approximately(heightDelta, 0f))
            {
                m_FreeformBlendGraphOverlayPosition = new Vector2(
                    m_FreeformBlendGraphOverlayPosition.x,
                    m_FreeformBlendGraphOverlayPosition.y + heightDelta);
            }

            RefreshBlendGraphOverlayTitle();
            ClampFreeformBlendGraphOverlayPosition();
        }

        private static float GetResolvedElementHeight(VisualElement element)
        {
            if (element == null)
            {
                return 0f;
            }

            float resolvedHeight = element.resolvedStyle.height;
            if (!float.IsNaN(resolvedHeight) && resolvedHeight > 0f)
            {
                return resolvedHeight;
            }

            Rect layout = element.layout;
            return layout.height > 0f ? layout.height : 0f;
        }

        private void SetBlendGraphOverlayTitle(string title)
        {
            m_FreeformBlendGraphTitleText = string.IsNullOrWhiteSpace(title) ? "Blend Graph" : title;
            RefreshBlendGraphOverlayTitle();
        }

        private void RefreshBlendGraphOverlayTitle()
        {
            if (m_FreeformBlendGraphTitleLabel == null)
            {
                return;
            }

            m_FreeformBlendGraphTitleLabel.text = m_FreeformBlendGraphOverlayExpanded
                ? $"▾ {m_FreeformBlendGraphTitleText}"
                : $"▸ {m_FreeformBlendGraphTitleText}";
        }

        private void ClampFreeformBlendGraphOverlayPosition()
        {
            if (m_FreeformBlendGraphOverlay == null || m_FreeformBlendGraphOverlay.parent == null)
            {
                return;
            }

            Rect parentBounds = m_FreeformBlendGraphOverlay.parent.contentRect;
            float cardWidth = Mathf.Max(FreeformBlendGraphOverlayWidth, m_FreeformBlendGraphOverlay.resolvedStyle.width);
            float cardHeight = Mathf.Max(0f, m_FreeformBlendGraphOverlay.resolvedStyle.height);
            float maxX = Mathf.Max(0f, parentBounds.width - cardWidth);
            float maxBottom = Mathf.Max(0f, parentBounds.height - cardHeight);
            m_FreeformBlendGraphOverlayPosition = new Vector2(
                Mathf.Clamp(m_FreeformBlendGraphOverlayPosition.x, 0f, maxX),
                Mathf.Clamp(m_FreeformBlendGraphOverlayPosition.y, 0f, maxBottom));

            m_FreeformBlendGraphOverlay.style.left = m_FreeformBlendGraphOverlayPosition.x;
            m_FreeformBlendGraphOverlay.style.bottom = m_FreeformBlendGraphOverlayPosition.y;
        }

        private static void ConfigureConsoleSection(VisualElement section)
        {
            section.style.minHeight = CueLogSectionMinHeight;
            section.style.flexGrow = 1;
            section.style.flexShrink = 1;
            section.style.overflow = Overflow.Hidden;
        }

        private static VisualElement BuildInspectorConsoleSplit(VisualElement inspectorPane, VisualElement cueCard)
        {
            inspectorPane.style.minHeight = InspectorMinHeight;
            inspectorPane.style.flexGrow = 1;
            inspectorPane.style.flexShrink = 1;

            TwoPaneSplitView splitView = new(1, CueLogInitialHeight, TwoPaneSplitViewOrientation.Vertical);
            splitView.style.flexGrow = 1;
            splitView.style.minHeight = 0;
            splitView.Add(inspectorPane);
            splitView.Add(cueCard);
            return splitView;
        }

    }
}
#endif
