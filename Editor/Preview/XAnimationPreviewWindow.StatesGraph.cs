#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    public sealed partial class XAnimationPreviewWindow
    {
        private VisualElement BuildStatesGraphTab()
        {
            VisualElement root = new();
            root.style.flexGrow = 1;
            root.style.minHeight = 0;
            root.style.display = DisplayStyle.None;
            root.style.backgroundColor = new Color(0.13f, 0.14f, 0.16f, 1f);
            SetBorder(root, SectionDivider, 1, 4);

            TwoPaneSplitView body = new(1, DefaultTransitionDetailsWidth, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            root.Add(body);

            VisualElement leftPane = new();
            leftPane.style.flexGrow = 1;
            leftPane.style.flexShrink = 1;
            leftPane.style.minWidth = 0;
            leftPane.style.minHeight = 0;
            leftPane.style.flexDirection = FlexDirection.Column;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = SectionDivider;
            body.Add(leftPane);

            leftPane.Add(BuildStatesGraphPane());

            m_StatesGraphDetailsView = new();
            m_StatesGraphDetailsView.style.minWidth = 220;
            m_StatesGraphDetailsView.style.minHeight = 0;
            m_StatesGraphDetailsView.style.flexShrink = 0;
            m_StatesGraphDetailsView.style.paddingLeft = 8;
            m_StatesGraphDetailsView.style.paddingRight = 8;
            m_StatesGraphDetailsView.style.paddingTop = 8;
            m_StatesGraphDetailsView.style.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            body.Add(m_StatesGraphDetailsView);
            body.RegisterCallback<GeometryChangedEvent>(_ => m_StatesGraphView?.RefreshViewportAfterLayout());

            Label status = new("滚轮缩放，拖动空白处平移；右键空白处新增 state/folder；双击 folder 下钻，拖动节点调整位置。");
            status.style.height = 23;
            status.style.flexShrink = 0;
            status.style.paddingLeft = 8;
            status.style.paddingTop = 3;
            status.style.borderTopWidth = 1;
            status.style.borderTopColor = SectionDivider;
            status.style.backgroundColor = ListHeaderBg;
            status.style.color = TextMuted;
            status.style.fontSize = BodyFontSize;
            root.Add(status);

            return root;
        }

        private VisualElement BuildStatesGraphPane()
        {
            VisualElement pane = new();
            pane.style.flexGrow = 1;
            pane.style.minHeight = 0;
            pane.style.flexDirection = FlexDirection.Column;

            VisualElement toolbar = Row();
            toolbar.style.flexShrink = 0;
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.paddingTop = 6;
            toolbar.style.paddingBottom = 6;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = SectionDivider;
            toolbar.style.backgroundColor = new Color(0.12f, 0.13f, 0.145f, 1f);
            pane.Add(toolbar);

            m_StatesGraphChannelButton = new Button(ShowStatesGraphChannelMenu);
            m_StatesGraphChannelButton.style.flexGrow = 1;
            m_StatesGraphChannelButton.style.flexShrink = 1;
            m_StatesGraphChannelButton.style.minWidth = 160;
            m_StatesGraphChannelButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            m_StatesGraphChannelButton.style.paddingLeft = 6;
            m_StatesGraphChannelButton.style.paddingRight = 6;
            toolbar.Add(m_StatesGraphChannelButton);

            Button resetViewButton = CreateStyledButton("Reset View", () =>
            {
                m_StatesGraphView?.ResetView();
            }, AccentColor);
            resetViewButton.tooltip = "把 States Graph 的缩放和平移还原。";
            toolbar.Add(resetViewButton);

            m_StatesGraphView = new XAnimationStatesGraphElement();
            m_StatesGraphView.BreadcrumbRow.style.marginLeft = 8;
            toolbar.Add(m_StatesGraphView.BreadcrumbRow);
            m_StatesGraphView.FolderDoubleClicked += SetStatesGraphPath;
            m_StatesGraphView.StateSelected += SelectStatesGraphState;
            m_StatesGraphView.NodePositionChanged += SetStatesGraphNodePosition;
            m_StatesGraphView.PanOffsetChanged += SetStatesGraphPanOffset;
            m_StatesGraphView.AddStateRequested += AddStatesGraphState;
            m_StatesGraphView.AddFolderRequested += AddStatesGraphFolder;
            pane.Add(m_StatesGraphView);
            return pane;
        }

        private void RebuildStatesGraphTab()
        {
            if (m_StatesGraphTabView == null ||
                m_StatesGraphView == null ||
                m_StatesGraphDetailsView == null)
            {
                return;
            }

            EnsureStatesGraphChannel();
            UpdateStatesGraphChannelButton();

            if (m_Session == null || !m_Session.IsLoaded)
            {
                m_StatesGraphView.SetEditEnabled(false);
                m_StatesGraphView.SetEmpty("No asset loaded");
                BuildStatesGraphDetails(null);
                return;
            }

            if (string.IsNullOrWhiteSpace(m_StatesGraphChannelName))
            {
                m_StatesGraphView.SetEditEnabled(false);
                m_StatesGraphView.SetEmpty("No channel");
                BuildStatesGraphDetails(null);
                return;
            }

            StatePathNode rootNode = BuildStatesGraphRootNode(m_StatesGraphChannelName);
            StatePathNode currentNode = ResolveStatesGraphCurrentNode(rootNode);
            if (currentNode == null)
            {
                m_StatesGraphCurrentPath = string.Empty;
                currentNode = rootNode;
            }

            if (!TryGetCompiledStateByUiKey(m_StatesGraphSelectedStateUiKey, out XAnimationCompiledState selectedState) ||
                !string.Equals(selectedState.Config.channelName, m_StatesGraphChannelName, StringComparison.Ordinal))
            {
                selectedState = null;
                m_StatesGraphSelectedStateUiKey = string.Empty;
            }

            m_Session.TryGetStatesGraphViewPanOffset(m_StatesGraphChannelName, currentNode.FullPath, out Vector2 panOffset);
            m_StatesGraphView.SetData(
                m_StatesGraphChannelName,
                currentNode.FullPath,
                BuildStatesGraphBreadcrumbs(currentNode.FullPath),
                BuildStatesGraphNodes(currentNode),
                m_StatesGraphSelectedStateUiKey,
                panOffset);
            m_StatesGraphView.SetEditEnabled(!m_Session.IsOverrideAsset);
            BuildStatesGraphDetails(selectedState);
        }

        private void RebuildStatesGraphTabIfVisible()
        {
            if (m_SelectedPreviewPaneTab != PreviewPaneTab.StatesGraph)
            {
                return;
            }

            RebuildStatesGraphTab();
            m_StatesGraphView?.RefreshViewportAfterLayout();
        }

        private void EnsureStatesGraphChannel()
        {
            if (m_Session == null || !m_Session.IsLoaded || m_Session.CompiledAsset.Channels.Count == 0)
            {
                m_StatesGraphChannelName = string.Empty;
                m_StatesGraphCurrentPath = string.Empty;
                m_StatesGraphSelectedStateUiKey = string.Empty;
                return;
            }

            if (HasStatesGraphChannel(m_StatesGraphChannelName))
            {
                return;
            }

            m_StatesGraphChannelName = m_Session.CompiledAsset.Channels[0].Name;
            m_StatesGraphCurrentPath = string.Empty;
            m_StatesGraphSelectedStateUiKey = string.Empty;
        }

        private bool HasStatesGraphChannel(string channelName)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(channelName))
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                if (string.Equals(channels[i]?.Name, channelName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateStatesGraphChannelButton()
        {
            if (m_StatesGraphChannelButton == null)
            {
                return;
            }

            string text = string.IsNullOrWhiteSpace(m_StatesGraphChannelName) ? "None" : m_StatesGraphChannelName;
            m_StatesGraphChannelButton.text = text;
            m_StatesGraphChannelButton.tooltip = $"Channel: {text}";
        }

        private void ShowStatesGraphChannelMenu()
        {
            if (m_StatesGraphChannelButton == null || m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            GenericMenu menu = new();
            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            for (int i = 0; i < channels.Count; i++)
            {
                string channelName = channels[i]?.Name;
                if (string.IsNullOrWhiteSpace(channelName))
                {
                    continue;
                }

                menu.AddItem(
                    new GUIContent(channelName),
                    string.Equals(channelName, m_StatesGraphChannelName, StringComparison.Ordinal),
                    () => SetStatesGraphChannel(channelName));
            }

            if (menu.GetItemCount() == 0)
            {
                menu.AddDisabledItem(new GUIContent("No Channels"));
            }

            menu.DropDown(GetSelectionActivatorRect(m_StatesGraphChannelButton));
        }

        private void SetStatesGraphChannel(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName) ||
                string.Equals(m_StatesGraphChannelName, channelName, StringComparison.Ordinal))
            {
                return;
            }

            m_StatesGraphChannelName = channelName;
            m_StatesGraphCurrentPath = string.Empty;
            m_StatesGraphSelectedStateUiKey = string.Empty;
            RebuildStatesGraphTab();
        }

        private void SetStatesGraphPath(string path)
        {
            string normalizedPath = NormalizeStatePath(path);
            if (string.Equals(m_StatesGraphCurrentPath, normalizedPath, StringComparison.Ordinal))
            {
                return;
            }

            m_StatesGraphCurrentPath = normalizedPath;
            RebuildStatesGraphTab();
        }

        private void SelectStatesGraphState(string stateUiKey)
        {
            if (string.IsNullOrWhiteSpace(stateUiKey) ||
                !TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState state) ||
                !string.Equals(state.Config.channelName, m_StatesGraphChannelName, StringComparison.Ordinal))
            {
                return;
            }

            m_StatesGraphSelectedStateUiKey = stateUiKey;
            RebuildStatesGraphTab();
        }

        private StatePathNode BuildStatesGraphRootNode(string channelName)
        {
            StatePathNode rootNode = new(string.Empty, string.Empty);
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (state == null ||
                    !string.Equals(state.Config.channelName, channelName, StringComparison.Ordinal))
                {
                    continue;
                }

                string parentPath = GetStatePathParent(state.Key);
                if (string.IsNullOrWhiteSpace(parentPath))
                {
                    rootNode.States.Add(state);
                    continue;
                }

                FindOrCreateStatePathNode(rootNode, parentPath).States.Add(state);
            }

            return rootNode;
        }

        private StatePathNode ResolveStatesGraphCurrentNode(StatePathNode rootNode)
        {
            if (rootNode == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(m_StatesGraphCurrentPath))
            {
                return rootNode;
            }

            StatePathNode current = rootNode;
            List<string> segments = SplitStatePathSegments(m_StatesGraphCurrentPath);
            for (int i = 0; i < segments.Count; i++)
            {
                current = FindStatePathChild(current, segments[i]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private List<XAnimationStatesGraphElement.BreadcrumbViewData> BuildStatesGraphBreadcrumbs(string path)
        {
            List<XAnimationStatesGraphElement.BreadcrumbViewData> breadcrumbs = new()
            {
                new XAnimationStatesGraphElement.BreadcrumbViewData("Root", string.Empty)
            };
            List<string> segments = SplitStatePathSegments(path);
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = BuildStatePathKey(currentPath, segments[i]);
                breadcrumbs.Add(new XAnimationStatesGraphElement.BreadcrumbViewData(segments[i], currentPath));
            }

            return breadcrumbs;
        }

        private List<XAnimationStatesGraphElement.NodeViewData> BuildStatesGraphNodes(StatePathNode node)
        {
            List<XAnimationStatesGraphElement.NodeViewData> nodes = new(node.Children.Count + node.States.Count);
            for (int i = 0; i < node.Children.Count; i++)
            {
                StatePathNode child = node.Children[i];
                bool hasPosition = m_Session.TryGetStatesGraphNodePosition(
                    m_StatesGraphChannelName,
                    child.FullPath,
                    isFolder: true,
                    out Vector2 position);
                nodes.Add(XAnimationStatesGraphElement.NodeViewData.Folder(
                    child.Name,
                    child.FullPath,
                    CountStatePathNodeStates(child),
                    child.Children.Count,
                    hasPosition,
                    position));
            }

            for (int i = 0; i < node.States.Count; i++)
            {
                XAnimationCompiledState state = node.States[i];
                bool hasPosition = m_Session.TryGetStatesGraphNodePosition(
                    m_StatesGraphChannelName,
                    state.Key,
                    isFolder: false,
                    out Vector2 position);
                nodes.Add(XAnimationStatesGraphElement.NodeViewData.State(
                    GetStatePathLeafName(state.Key),
                    state.Key,
                    BuildStateUiKey(state),
                    state.Config.stateType.ToString(),
                    state.Config.loop,
                    state.Config.speed,
                    hasPosition,
                    position));
            }

            return nodes;
        }

        private void SetStatesGraphNodePosition(string path, bool isFolder, Vector2 position)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            try
            {
                m_Session.SetStatesGraphNodePosition(m_StatesGraphChannelName, path, isFolder, position);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void SetStatesGraphPanOffset(Vector2 panOffset)
        {
            if (m_Session == null || !m_Session.IsLoaded || m_Session.IsOverrideAsset)
            {
                return;
            }

            try
            {
                m_Session.SetStatesGraphViewPanOffset(m_StatesGraphChannelName, m_StatesGraphCurrentPath, panOffset);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddStatesGraphState(Vector2 graphPosition)
        {
            try
            {
                string stateKey = m_Session.AddState(m_StatesGraphChannelName, m_StatesGraphCurrentPath);
                m_Session.SetStatesGraphNodePosition(m_StatesGraphChannelName, stateKey, isFolder: false, graphPosition);
                m_PendingStateRenameKey = stateKey;
                m_StatesGraphSelectedStateUiKey = BuildStateUiKey(m_StatesGraphChannelName, stateKey);
                RebuildStatePresentation(includeChannelPresentation: true);
                RebuildStatesGraphTab();
                SetStatus($"已在 {m_StatesGraphChannelName} / {m_StatesGraphCurrentPath} 新增 State {stateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddStatesGraphFolder(Vector2 graphPosition)
        {
            string channelName = m_StatesGraphChannelName;
            string parentPath = m_StatesGraphCurrentPath;
            string[] stateOptions = BuildChannelStateGroupCandidateOptions(channelName);
            if (!TryPromptForStateGroupSetup("新建 State Folder", channelName, parentPath, out string groupName, out string selectedStateKey, stateOptions))
            {
                return;
            }

            string groupPath = BuildStatePathKey(parentPath, groupName);
            string visibleFolderPath = GetStatesGraphVisibleFolderPath(parentPath, groupPath);
            try
            {
                if (!string.IsNullOrWhiteSpace(selectedStateKey))
                {
                    m_Session.MoveState(channelName, selectedStateKey, channelName, insertBeforeStateKey: null, groupPath);
                    SetStateGroupCollapsed(BuildStateGroupKey(channelName, groupPath), false);
                }
                else
                {
                    string stateKey = m_Session.AddState(channelName, groupPath);
                    m_PendingStateRenameKey = stateKey;
                }

                m_Session.SetStatesGraphNodePosition(channelName, visibleFolderPath, isFolder: true, graphPosition);
                RebuildStatePresentation(includeChannelPresentation: true);
                RebuildStatesGraphTab();
                SetStatus($"已创建 State Folder {channelName} / {groupPath}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private static string GetStatesGraphVisibleFolderPath(string parentPath, string groupPath)
        {
            parentPath = NormalizeStatePath(parentPath);
            groupPath = NormalizeStatePath(groupPath);
            string suffix = groupPath;
            if (!string.IsNullOrWhiteSpace(parentPath) &&
                groupPath.StartsWith($"{parentPath}/", StringComparison.Ordinal))
            {
                suffix = groupPath[(parentPath.Length + 1)..];
            }

            List<string> segments = SplitStatePathSegments(suffix);
            return segments.Count == 0 ? groupPath : BuildStatePathKey(parentPath, segments[0]);
        }

        private void BuildStatesGraphDetails(XAnimationCompiledState selectedState)
        {
            m_StatesGraphDetailsView.Clear();

            Label title = CreateBoldLabel("State");
            title.style.fontSize = SectionTitleFontSize;
            m_StatesGraphDetailsView.Add(title);

            if (selectedState == null)
            {
                AddEmptyLabel(m_StatesGraphDetailsView, "No state selected");
                return;
            }

            XAnimationStateConfig config = selectedState.Config;
            Label channelLabel = CreateSmallInfoLabel($"Channel: {config.channelName}");
            channelLabel.style.marginTop = 8;
            m_StatesGraphDetailsView.Add(channelLabel);

            Label pathLabel = CreateSmallInfoLabel($"Path: {FormatStateDisplayPath(selectedState.Key)}");
            pathLabel.style.marginTop = 4;
            m_StatesGraphDetailsView.Add(pathLabel);

            List<string> stateTypeNames = new(Enum.GetNames(typeof(XAnimationStateType)));
            DropdownField stateTypeField = new(
                "stateType",
                stateTypeNames,
                Mathf.Max(0, stateTypeNames.IndexOf(config.stateType.ToString())));
            stateTypeField.style.marginTop = 8;
            ApplyDropdownFieldStyle(stateTypeField);
            stateTypeField.RegisterValueChangedCallback(evt =>
            {
                if (!Enum.TryParse(evt.newValue, out XAnimationStateType stateType))
                {
                    return;
                }

                ChangeStateType(config.channelName, selectedState.Key, stateType, evt.previousValue, stateTypeField);
                m_StatesGraphSelectedStateUiKey = BuildStateUiKey(config.channelName, selectedState.Key);
                RebuildStatesGraphTab();
            });
            m_StatesGraphDetailsView.Add(stateTypeField);

            VisualElement editor = CreateStateEditor(selectedState);
            editor.style.marginTop = 8;
            m_StatesGraphDetailsView.Add(editor);
        }

        private sealed class XAnimationStatesGraphElement : VisualElement
        {
            public readonly struct BreadcrumbViewData
            {
                public BreadcrumbViewData(string label, string path)
                {
                    Label = label ?? string.Empty;
                    Path = path ?? string.Empty;
                }

                public string Label { get; }
                public string Path { get; }
            }

            public readonly struct NodeViewData
            {
                private NodeViewData(
                    bool isFolder,
                    string title,
                    string path,
                    string stateUiKey,
                    string detail,
                    bool loop,
                    float speed,
                    int stateCount,
                    int folderCount,
                    bool hasPosition,
                    Vector2 position)
                {
                    IsFolder = isFolder;
                    Title = title ?? string.Empty;
                    Path = path ?? string.Empty;
                    StateUiKey = stateUiKey ?? string.Empty;
                    Detail = detail ?? string.Empty;
                    Loop = loop;
                    Speed = speed;
                    StateCount = stateCount;
                    FolderCount = folderCount;
                    HasPosition = hasPosition;
                    Position = position;
                }

                public bool IsFolder { get; }
                public string Title { get; }
                public string Path { get; }
                public string StateUiKey { get; }
                public string Detail { get; }
                public bool Loop { get; }
                public float Speed { get; }
                public int StateCount { get; }
                public int FolderCount { get; }
                public bool HasPosition { get; }
                public Vector2 Position { get; }

                public static NodeViewData Folder(
                    string title,
                    string path,
                    int stateCount,
                    int folderCount,
                    bool hasPosition,
                    Vector2 position)
                {
                    return new NodeViewData(true, title, path, string.Empty, "Folder", false, 0f, stateCount, folderCount, hasPosition, position);
                }

                public static NodeViewData State(
                    string title,
                    string path,
                    string stateUiKey,
                    string stateType,
                    bool loop,
                    float speed,
                    bool hasPosition,
                    Vector2 position)
                {
                    return new NodeViewData(false, title, path, stateUiKey, stateType, loop, speed, 0, 0, hasPosition, position);
                }

                public NodeViewData WithPosition(Vector2 position)
                {
                    return new NodeViewData(
                        IsFolder,
                        Title,
                        Path,
                        StateUiKey,
                        Detail,
                        Loop,
                        Speed,
                        StateCount,
                        FolderCount,
                        true,
                        position);
                }
            }

            private const float MinZoom = 0.9f;
            private const float MaxZoom = 1.85f;
            private const float WheelZoomBase = 1.12f;
            private const float MinCanvasWidth = 720f;
            private const float MinCanvasHeight = 360f;
            private const float CanvasPadding = 42f;
            private const float NodeWidth = 168f;
            private const float NodeHeight = 64f;
            private const float NodeGapX = 34f;
            private const float NodeGapY = 28f;
            private const string NodeClassName = "xanimation-states-graph-node";

            private static readonly Color CanvasBg = new(0.095f, 0.10f, 0.115f, 1f);
            private static readonly Color CanvasGrid = new(0.78f, 0.79f, 0.80f, 0.075f);
            private static readonly Color CanvasGridMajor = new(0.78f, 0.79f, 0.80f, 0.13f);
            private static readonly Color FolderBg = new(0.20f, 0.18f, 0.25f, 0.98f);
            private static readonly Color StateBg = new(0.18f, 0.19f, 0.21f, 0.98f);
            private static readonly Color SelectedBg = new(0.16f, 0.24f, 0.34f, 0.98f);
            private static readonly Color NodeBorder = new(0.34f, 0.35f, 0.38f, 1f);
            private static readonly Color SelectedBorder = new(0.48f, 0.74f, 1f, 1f);

            private readonly List<NodeViewData> m_Nodes = new();
            private ScrollView m_ScrollView;
            private VisualElement m_BreadcrumbRow;
            private VisualElement m_Canvas;
            private VisualElement m_GridCanvas;
            private VisualElement m_NodeLayer;
            private Label m_EmptyLabel;
            private string m_SelectedStateUiKey = string.Empty;
            private string m_EmptyMessage = string.Empty;
            private bool m_CanEdit;
            private float m_Zoom = 1f;
            private float m_BaseCanvasWidth = MinCanvasWidth;
            private float m_BaseCanvasHeight = MinCanvasHeight;
            private float m_CanvasWidth = MinCanvasWidth;
            private float m_CanvasHeight = MinCanvasHeight;
            private Vector2 m_PanOffset = Vector2.zero;
            private bool m_IsPanning;
            private int m_PanPointerId = PointerId.invalidPointerId;
            private Vector2 m_PanStartPointer;
            private Vector2 m_PanStartOffset;
            private bool m_IsDraggingNode;
            private int m_NodeDragPointerId = PointerId.invalidPointerId;
            private int m_NodeDragIndex = -1;
            private int m_NodeDragClickCount;
            private VisualElement m_DraggingNode;
            private NodeViewData m_DraggingNodeData;
            private Vector2 m_NodeDragStartPointer;
            private Vector2 m_NodeDragStartPosition;
            private Vector2 m_NodeDragPointerOffset;
            private Vector2 m_NodeDragCurrentCanvasPosition;
            private Vector2 m_NodeDragCurrentPosition;
            private bool m_NodeDragMoved;

            public XAnimationStatesGraphElement()
            {
                style.flexGrow = 1;
                style.minHeight = 0;
                style.backgroundColor = CanvasBg;
                BuildUi();
            }

            public event Action<string> FolderDoubleClicked;
            public event Action<string> StateSelected;
            public event Action<string, bool, Vector2> NodePositionChanged;
            public event Action<Vector2> PanOffsetChanged;
            public event Action<Vector2> AddStateRequested;
            public event Action<Vector2> AddFolderRequested;
            public event Action<float> ZoomChanged;

            public float Zoom => m_Zoom;
            public VisualElement BreadcrumbRow => m_BreadcrumbRow;

            public void SetData(
                string channelName,
                string currentPath,
                IReadOnlyList<BreadcrumbViewData> breadcrumbs,
                IReadOnlyList<NodeViewData> nodes,
                string selectedStateUiKey,
                Vector2 panOffset)
            {
                m_SelectedStateUiKey = selectedStateUiKey ?? string.Empty;
                m_EmptyMessage = string.Empty;
                m_PanOffset = panOffset;
                m_Nodes.Clear();
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        m_Nodes.Add(nodes[i]);
                    }
                }

                RebuildBreadcrumbs(breadcrumbs);
                RebuildGraph(channelName, currentPath);
                RefreshViewportAfterLayout();
            }

            public void SetEmpty(string message)
            {
                m_SelectedStateUiKey = string.Empty;
                m_EmptyMessage = message ?? string.Empty;
                m_Nodes.Clear();
                RebuildBreadcrumbs(Array.Empty<BreadcrumbViewData>());
                RebuildGraph(message: message);
                RefreshViewportAfterLayout();
            }

            public void ResetView()
            {
                m_Zoom = 1f;
                m_PanOffset = Vector2.zero;
                RebuildGraph();
                PanOffsetChanged?.Invoke(m_PanOffset);
                ZoomChanged?.Invoke(m_Zoom);
            }

            public void SetEditEnabled(bool canEdit)
            {
                m_CanEdit = canEdit;
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

                m_BreadcrumbRow = Row();
                m_BreadcrumbRow.style.flexShrink = 0;
                m_BreadcrumbRow.style.minWidth = 0;
                m_BreadcrumbRow.style.alignItems = Align.Center;

                m_ScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
                m_ScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                m_ScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                m_ScrollView.style.flexGrow = 1;
                m_ScrollView.style.minHeight = 0;
                m_ScrollView.RegisterCallback<GeometryChangedEvent>(_ => RefreshCanvasViewport());
                m_ScrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => RefreshCanvasViewport());
                m_ScrollView.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                Add(m_ScrollView);

                m_Canvas = new VisualElement();
                m_Canvas.style.position = Position.Relative;
                m_Canvas.style.backgroundColor = CanvasBg;
                m_Canvas.focusable = true;
                m_Canvas.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                m_Canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
                m_Canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                m_Canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
                m_Canvas.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
                m_Canvas.RegisterCallback<PointerCaptureOutEvent>(_ => EndPan(savePanOffset: false));
                m_Canvas.AddManipulator(new ContextualMenuManipulator(OnCanvasContextMenu));
                m_ScrollView.Add(m_Canvas);

                m_GridCanvas = new VisualElement();
                m_GridCanvas.style.position = Position.Absolute;
                m_GridCanvas.style.left = 0;
                m_GridCanvas.style.top = 0;
                m_GridCanvas.pickingMode = PickingMode.Ignore;
                m_GridCanvas.generateVisualContent += OnGenerateVisualContent;
                m_Canvas.Add(m_GridCanvas);

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

            private void RebuildBreadcrumbs(IReadOnlyList<BreadcrumbViewData> breadcrumbs)
            {
                m_BreadcrumbRow.Clear();
                if (breadcrumbs == null || breadcrumbs.Count == 0)
                {
                    Label empty = CreateSmallInfoLabel("No Path");
                    m_BreadcrumbRow.Add(empty);
                    return;
                }

                for (int i = 0; i < breadcrumbs.Count; i++)
                {
                    BreadcrumbViewData breadcrumb = breadcrumbs[i];
                    Button button = CreateStyledButton(breadcrumb.Label, () => FolderDoubleClicked?.Invoke(breadcrumb.Path), AccentColor, i == 0 ? 0 : 4);
                    button.tooltip = string.IsNullOrWhiteSpace(breadcrumb.Path) ? "回到根路径。" : $"回到 {breadcrumb.Path}。";
                    m_BreadcrumbRow.Add(button);

                    if (i < breadcrumbs.Count - 1)
                    {
                        Label separator = CreateSmallInfoLabel("/");
                        separator.style.marginLeft = 4;
                        separator.style.marginRight = 0;
                        m_BreadcrumbRow.Add(separator);
                    }
                }
            }

            private void RebuildGraph(string channelName = null, string currentPath = null, string message = null)
            {
                m_NodeLayer.Clear();
                if (m_Nodes.Count == 0)
                {
                    ApplyCanvasSize(MinCanvasWidth, MinCanvasHeight);
                    string emptyMessage = string.IsNullOrWhiteSpace(message) ? m_EmptyMessage : message;
                    m_EmptyLabel.text = string.IsNullOrWhiteSpace(emptyMessage) ? "No states in current path" : emptyMessage;
                    m_EmptyLabel.style.display = DisplayStyle.Flex;
                    m_GridCanvas.MarkDirtyRepaint();
                    return;
                }

                m_EmptyLabel.style.display = DisplayStyle.None;
                int columns = Mathf.Max(1, Mathf.FloorToInt((GetViewportSize().x - CanvasPadding * 2f) / (NodeWidth + NodeGapX)));
                columns = Mathf.Clamp(columns, 1, 4);
                int rows = Mathf.CeilToInt(m_Nodes.Count / (float)columns);
                List<Vector2> positions = new(m_Nodes.Count);
                float maxX = 0f;
                float maxY = 0f;
                for (int i = 0; i < m_Nodes.Count; i++)
                {
                    int column = i % columns;
                    int row = i / columns;
                    Vector2 position = m_Nodes[i].HasPosition
                        ? m_Nodes[i].Position
                        : new Vector2(
                            CanvasPadding + column * (NodeWidth + NodeGapX),
                            CanvasPadding + row * (NodeHeight + NodeGapY));
                    positions.Add(position);
                    maxX = Mathf.Max(maxX, position.x + NodeWidth + CanvasPadding);
                    maxY = Mathf.Max(maxY, position.y + NodeHeight + CanvasPadding);
                }

                float contentWidth = Mathf.Max(CanvasPadding * 2f + columns * NodeWidth + (columns - 1) * NodeGapX, maxX);
                float contentHeight = Mathf.Max(CanvasPadding * 2f + rows * NodeHeight + Mathf.Max(0, rows - 1) * NodeGapY, maxY);
                ApplyCanvasSize(Mathf.Max(MinCanvasWidth, contentWidth), Mathf.Max(MinCanvasHeight, contentHeight));

                for (int i = 0; i < m_Nodes.Count; i++)
                {
                    CreateNode(i, positions[i], m_Nodes[i]);
                }

                m_GridCanvas.MarkDirtyRepaint();
            }

            private void CreateNode(int nodeIndex, Vector2 graphPosition, NodeViewData nodeData)
            {
                bool selected = !nodeData.IsFolder &&
                                string.Equals(nodeData.StateUiKey, m_SelectedStateUiKey, StringComparison.Ordinal);
                Rect rect = ScaleRect(new Rect(graphPosition.x, graphPosition.y, NodeWidth, NodeHeight));
                VisualElement node = new();
                node.AddToClassList(NodeClassName);
                node.style.position = Position.Absolute;
                node.style.left = rect.x;
                node.style.top = rect.y;
                node.style.width = rect.width;
                node.style.height = rect.height;
                node.style.paddingLeft = 8;
                node.style.paddingRight = 8;
                node.style.paddingTop = 7;
                node.style.paddingBottom = 6;
                node.style.backgroundColor = selected ? SelectedBg : nodeData.IsFolder ? FolderBg : StateBg;
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

                Label title = new(nodeData.Title);
                title.style.color = TextNormal;
                title.style.fontSize = Mathf.Max(10f, 12f * Mathf.Clamp(m_Zoom, 0.72f, 1f));
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.overflow = Overflow.Hidden;
                title.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(title);

                string detail = nodeData.IsFolder
                    ? $"{nodeData.StateCount} states | {nodeData.FolderCount} folders"
                    : $"{nodeData.Detail} | loop {nodeData.Loop} | speed {nodeData.Speed:0.###}";
                Label detailLabel = new(detail);
                detailLabel.style.color = TextMuted;
                detailLabel.style.fontSize = Mathf.Max(9f, 10f * Mathf.Clamp(m_Zoom, 0.72f, 1f));
                detailLabel.style.marginTop = 4;
                detailLabel.style.overflow = Overflow.Hidden;
                detailLabel.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(detailLabel);

                node.RegisterCallback<PointerDownEvent>(evt => OnNodePointerDown(evt, node, nodeIndex, nodeData, graphPosition));
                node.RegisterCallback<PointerMoveEvent>(OnNodePointerMove);
                node.RegisterCallback<PointerUpEvent>(OnNodePointerUp);
                node.RegisterCallback<PointerCancelEvent>(OnNodePointerCancel);

                m_NodeLayer.Add(node);
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
                m_GridCanvas.style.width = m_CanvasWidth;
                m_GridCanvas.style.height = m_CanvasHeight;
                m_NodeLayer.style.width = m_CanvasWidth;
                m_NodeLayer.style.height = m_CanvasHeight;
                m_GridCanvas?.MarkDirtyRepaint();
                if (m_ScrollView != null && !m_IsDraggingNode && !m_IsPanning)
                {
                    m_ScrollView.scrollOffset = Vector2.zero;
                }
            }

            private void RefreshCanvasViewport()
            {
                if (m_Canvas == null || m_GridCanvas == null || m_NodeLayer == null)
                {
                    return;
                }

                ApplyCanvasSize(m_BaseCanvasWidth, m_BaseCanvasHeight);
                m_GridCanvas.MarkDirtyRepaint();
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

            private Rect GetCanvasPaintRect()
            {
                Rect canvasLayout = m_GridCanvas?.layout ?? Rect.zero;
                Vector2 viewportSize = GetViewportSize();
                float width = Mathf.Max(m_CanvasWidth, canvasLayout.width, viewportSize.x);
                float height = Mathf.Max(m_CanvasHeight, canvasLayout.height, viewportSize.y);
                return new Rect(0f, 0f, width, height);
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                DrawGrid(context.painter2D, GetCanvasPaintRect(), 32f * m_Zoom, new Vector2(CanvasPadding, CanvasPadding) * m_Zoom + m_PanOffset);
            }

            private void OnCanvasContextMenu(ContextualMenuPopulateEvent evt)
            {
                if (IsStatesGraphNode(evt.target as VisualElement))
                {
                    return;
                }

                Vector2 canvasPoint = m_Canvas.WorldToLocal(evt.mousePosition);
                Vector2 graphPoint = CanvasToGraphPosition(canvasPoint);
                evt.menu.AppendAction(
                    "新加状态",
                    _ => AddStateRequested?.Invoke(graphPoint),
                    _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction(
                    "新建文件夹",
                    _ => AddFolderRequested?.Invoke(graphPoint),
                    _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.StopPropagation();
            }

            private static bool IsStatesGraphNode(VisualElement element)
            {
                while (element != null)
                {
                    if (element.ClassListContains(NodeClassName))
                    {
                        return true;
                    }

                    element = element.parent;
                }

                return false;
            }

            private void OnWheel(WheelEvent evt)
            {
                float previousZoom = m_Zoom;
                float nextZoom = Mathf.Clamp(previousZoom * Mathf.Pow(WheelZoomBase, -evt.delta.y), MinZoom, MaxZoom);
                if (Mathf.Approximately(previousZoom, nextZoom))
                {
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }

                Vector2 viewportPoint = m_Canvas.WorldToLocal(evt.mousePosition);
                Vector2 graphPoint = (viewportPoint - m_PanOffset) / previousZoom;
                m_Zoom = nextZoom;
                m_PanOffset = viewportPoint - graphPoint * nextZoom;
                RebuildGraph();
                ZoomChanged?.Invoke(m_Zoom);
                evt.PreventDefault();
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
                m_PanStartPointer = GetCanvasPointerPosition(evt.position);
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

                Vector2 pointerPosition = GetCanvasPointerPosition(evt.position);
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

                bool hasPointerCapture = m_Canvas.HasPointerCapture(evt.pointerId);
                EndPan(savePanOffset: true);
                if (hasPointerCapture)
                {
                    m_Canvas.ReleasePointer(evt.pointerId);
                }

                evt.StopPropagation();
            }

            private void OnPointerCancel(PointerCancelEvent evt)
            {
                if (m_Canvas.HasPointerCapture(evt.pointerId))
                {
                    m_Canvas.ReleasePointer(evt.pointerId);
                }

                EndPan(savePanOffset: false);
            }

            private void EndPan(bool savePanOffset)
            {
                if (savePanOffset && m_IsPanning)
                {
                    PanOffsetChanged?.Invoke(m_PanOffset);
                }

                m_IsPanning = false;
                m_PanPointerId = PointerId.invalidPointerId;
                m_PanStartPointer = Vector2.zero;
                m_PanStartOffset = Vector2.zero;
            }

            private void OnNodePointerDown(
                PointerDownEvent evt,
                VisualElement node,
                int nodeIndex,
                NodeViewData nodeData,
                Vector2 graphPosition)
            {
                if (evt.button != 0)
                {
                    return;
                }

                m_IsDraggingNode = true;
                m_NodeDragPointerId = evt.pointerId;
                m_NodeDragIndex = nodeIndex;
                m_NodeDragClickCount = evt.clickCount;
                m_DraggingNode = node;
                m_DraggingNodeData = nodeData;
                m_NodeDragCurrentCanvasPosition = ScaleRect(new Rect(graphPosition.x, graphPosition.y, NodeWidth, NodeHeight)).position;
                Vector2 localPointerPosition = GetLocalPointerPosition(evt.localPosition);
                m_NodeDragStartPointer = m_NodeDragCurrentCanvasPosition + localPointerPosition;
                m_NodeDragStartPosition = graphPosition;
                m_NodeDragPointerOffset = localPointerPosition / m_Zoom;
                m_NodeDragCurrentPosition = graphPosition;
                m_NodeDragMoved = false;
                node.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            }

            private void OnNodePointerMove(PointerMoveEvent evt)
            {
                if (!m_IsDraggingNode ||
                    m_NodeDragPointerId != evt.pointerId ||
                    m_DraggingNode == null ||
                    !m_DraggingNode.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 pointerPosition = m_NodeDragCurrentCanvasPosition + GetLocalPointerPosition(evt.localPosition);
                Vector2 screenDelta = pointerPosition - m_NodeDragStartPointer;
                Vector2 nextPosition = CanvasToGraphPosition(pointerPosition) - m_NodeDragPointerOffset;
                m_NodeDragCurrentPosition = nextPosition;
                if (screenDelta.sqrMagnitude > 9f)
                {
                    m_NodeDragMoved = true;
                }

                ApplyNodePosition(m_DraggingNode, nextPosition);
                evt.StopPropagation();
            }

            private void OnNodePointerUp(PointerUpEvent evt)
            {
                if (!m_IsDraggingNode || m_NodeDragPointerId != evt.pointerId)
                {
                    return;
                }

                bool savePosition = m_NodeDragMoved;
                if (m_DraggingNode != null && m_DraggingNode.HasPointerCapture(evt.pointerId))
                {
                    m_DraggingNode.ReleasePointer(evt.pointerId);
                }

                EndNodeDrag(savePosition, invokeClick: !savePosition);
                evt.StopPropagation();
            }

            private void OnNodePointerCancel(PointerCancelEvent evt)
            {
                if (m_DraggingNode != null && m_DraggingNode.HasPointerCapture(evt.pointerId))
                {
                    m_DraggingNode.ReleasePointer(evt.pointerId);
                }

                EndNodeDrag(savePosition: false, invokeClick: false);
            }

            private void EndNodeDrag(bool savePosition, bool invokeClick)
            {
                if (savePosition)
                {
                    if (m_NodeDragIndex >= 0 && m_NodeDragIndex < m_Nodes.Count)
                    {
                        m_Nodes[m_NodeDragIndex] = m_Nodes[m_NodeDragIndex].WithPosition(m_NodeDragCurrentPosition);
                    }

                    NodePositionChanged?.Invoke(m_DraggingNodeData.Path, m_DraggingNodeData.IsFolder, m_NodeDragCurrentPosition);
                }
                else if (invokeClick && m_IsDraggingNode)
                {
                    InvokeNodeClick(m_DraggingNodeData, m_NodeDragClickCount);
                }

                m_IsDraggingNode = false;
                m_NodeDragPointerId = PointerId.invalidPointerId;
                m_NodeDragIndex = -1;
                m_NodeDragClickCount = 0;
                m_DraggingNode = null;
                m_DraggingNodeData = default;
                m_NodeDragStartPointer = Vector2.zero;
                m_NodeDragStartPosition = Vector2.zero;
                m_NodeDragPointerOffset = Vector2.zero;
                m_NodeDragCurrentCanvasPosition = Vector2.zero;
                m_NodeDragCurrentPosition = Vector2.zero;
                m_NodeDragMoved = false;
            }

            private void InvokeNodeClick(NodeViewData nodeData, int clickCount)
            {
                if (nodeData.IsFolder)
                {
                    if (clickCount >= 2)
                    {
                        FolderDoubleClicked?.Invoke(nodeData.Path);
                    }

                    return;
                }

                StateSelected?.Invoke(nodeData.StateUiKey);
            }

            private void ApplyNodePosition(VisualElement node, Vector2 graphPosition)
            {
                Rect rect = ScaleRect(new Rect(graphPosition.x, graphPosition.y, NodeWidth, NodeHeight));
                node.style.left = rect.x;
                node.style.top = rect.y;
                node.style.width = rect.width;
                node.style.height = rect.height;
                if (node == m_DraggingNode)
                {
                    m_NodeDragCurrentCanvasPosition = rect.position;
                }

                m_GridCanvas?.MarkDirtyRepaint();
            }

            private Vector2 CanvasToGraphPosition(Vector2 canvasPosition)
            {
                return (canvasPosition - m_PanOffset) / m_Zoom;
            }

            private Vector2 GetCanvasPointerPosition(Vector3 panelPosition)
            {
                return m_Canvas.WorldToLocal(new Vector2(panelPosition.x, panelPosition.y));
            }

            private static Vector2 GetLocalPointerPosition(Vector3 localPosition)
            {
                return new Vector2(localPosition.x, localPosition.y);
            }

            private Rect ScaleRect(Rect rect)
            {
                return new Rect(rect.x * m_Zoom + m_PanOffset.x, rect.y * m_Zoom + m_PanOffset.y, rect.width * m_Zoom, rect.height * m_Zoom);
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
        }
    }
}
#endif
