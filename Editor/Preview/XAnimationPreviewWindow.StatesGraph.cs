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

            ScrollView detailsScrollView = new(ScrollViewMode.Vertical);
            detailsScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            detailsScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            detailsScrollView.style.minWidth = 220;
            detailsScrollView.style.minHeight = 0;
            detailsScrollView.style.flexShrink = 0;
            detailsScrollView.style.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);

            m_StatesGraphDetailsView = new();
            m_StatesGraphDetailsView.style.flexShrink = 0;
            m_StatesGraphDetailsView.style.paddingLeft = 8;
            m_StatesGraphDetailsView.style.paddingRight = 8;
            m_StatesGraphDetailsView.style.paddingTop = 8;
            detailsScrollView.Add(m_StatesGraphDetailsView);
            body.Add(detailsScrollView);
            body.RegisterCallback<GeometryChangedEvent>(_ => m_StatesGraphView?.RefreshViewportAfterLayout());

            Label status = new("滚轮缩放，拖动空白处平移；右键新增；双击 Node 进入对应视图。");
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

            m_StatesGraphView = new XAnimationStatesGraphElement();
            m_StatesGraphView.BreadcrumbRow.style.flexGrow = 1;
            m_StatesGraphView.BreadcrumbRow.style.flexShrink = 1;
            toolbar.Add(m_StatesGraphView.BreadcrumbRow);

            Button resetViewButton = CreateStyledButton("Reset View", () =>
            {
                m_StatesGraphView?.ResetView();
            }, AccentColor, 8);
            resetViewButton.tooltip = "把 Channel Graph 的缩放和平移还原。";
            toolbar.Add(resetViewButton);

            m_StatesGraphView.ContainerDoubleClicked += SetStatesGraphPath;
            m_StatesGraphView.NodeSelected += SelectStatesGraphNode;
            m_StatesGraphView.NodeTreeFocusRequested += FocusStatesGraphNodeInNodeTree;
            m_StatesGraphView.NodePositionChanged += SetStatesGraphNodePosition;
            m_StatesGraphView.PanOffsetChanged += SetStatesGraphPanOffset;
            m_StatesGraphView.AddNodeRequested += AddStatesGraphNode;
            m_StatesGraphView.DeleteNodeRequested += DeleteStatesGraphNode;
            m_StatesGraphView.SelectorParameterChanged += SetStatesGraphSelectorParameter;
            m_StatesGraphView.TransitionSelected += SelectDefaultTransitionTabPair;
            m_StatesGraphView.TransitionDeleteRequested += DeleteDefaultTransitionTabPair;
            m_StatesGraphView.TransitionStateEntered += SetStatesGraphTransitionState;
            m_StatesGraphView.TransitionAddRequested += ShowAddDefaultTransitionPairMenu;
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
            UpdateChannelGraphTab();

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

            XAnimationCompiledStateNode currentNode = ResolveStatesGraphCurrentNode();
            if (!string.IsNullOrWhiteSpace(m_StatesGraphCurrentPath) && currentNode == null)
            {
                m_StatesGraphCurrentPath = string.Empty;
            }

            if (!TryGetCompiledStateNodeByUiKey(m_StatesGraphSelectedStateUiKey, out XAnimationCompiledStateNode selectedNode) ||
                !string.Equals(selectedNode.ChannelName, m_StatesGraphChannelName, StringComparison.Ordinal))
            {
                selectedNode = null;
                m_StatesGraphSelectedStateUiKey = string.Empty;
            }

            string currentPath = currentNode?.Key ?? string.Empty;
            if (currentNode is XAnimationCompiledState currentState)
            {
                m_DefaultTransitionEditingStateUiKey = BuildStateUiKey(currentState);
                m_StatesGraphSelectedStateUiKey = m_DefaultTransitionEditingStateUiKey;
                List<DefaultTransitionPairEntry> inEntries = CollectDefaultTransitionPairEntries(currentState.ChannelName, currentState.Key, true);
                List<DefaultTransitionPairEntry> outEntries = CollectDefaultTransitionPairEntries(currentState.ChannelName, currentState.Key, false);
                EnsureDefaultTransitionTabSelection(inEntries, outEntries);
                m_StatesGraphView.SetStateData(
                    currentPath,
                    BuildStatesGraphBreadcrumbs(m_StatesGraphChannelName, currentPath),
                    m_DefaultTransitionEditingStateUiKey,
                    FormatStateDisplayPath(currentState.Key),
                    BuildDefaultTransitionGraphPairs(inEntries, outEntries),
                    CanAddDefaultTransitionPairFromTab());
                m_StatesGraphView.SetEditEnabled(!m_Session.IsOverrideAsset);
                BuildDefaultTransitionDetails(
                    m_StatesGraphDetailsView,
                    GetSelectedDefaultTransitionPairEntry(inEntries, outEntries));
                return;
            }

            bool selectorGraph = currentNode is XAnimationCompiledSelectorStateNode;
            IReadOnlyList<XAnimationCompiledStateNode> children = currentNode?.Children ??
                m_Session.CompiledAsset.GetChannel(m_StatesGraphChannelName).RootStateNodes;
            List<XAnimationStatesGraphElement.NodeViewData> nodes = selectorGraph
                ? BuildSelectorStatesGraphNodes((XAnimationCompiledSelectorStateNode)currentNode)
                : BuildStatesGraphNodes(children);
            m_Session.TryGetStatesGraphViewPanOffset(m_StatesGraphChannelName, currentPath, out Vector2 panOffset);
            m_StatesGraphView.SetData(
                m_StatesGraphChannelName,
                currentPath,
                BuildStatesGraphBreadcrumbs(m_StatesGraphChannelName, currentPath),
                nodes,
                selectorGraph
                    ? XAnimationStatesGraphElement.DisplayMode.Selector
                    : XAnimationStatesGraphElement.DisplayMode.Normal,
                BuildStatesGraphIntParameters(),
                m_StatesGraphSelectedStateUiKey,
                panOffset);
            m_StatesGraphView.SetEditEnabled(!m_Session.IsOverrideAsset);
            BuildStatesGraphDetails(selectedNode);
        }

        private void FocusStatesGraphNodeInNodeTree(string nodePath, XAnimationStateNodeKind nodeKind)
        {
            if (string.IsNullOrWhiteSpace(m_StatesGraphChannelName) || string.IsNullOrWhiteSpace(nodePath))
            {
                return;
            }

            if (nodeKind == XAnimationStateNodeKind.State)
            {
                FocusStateInInspector(m_StatesGraphChannelName, nodePath);
                return;
            }

            SetStateTabChannel(m_StatesGraphChannelName, rebuild: false);
            SetDebugToolbarGroup(DebugToolbarGroup.Main);
            m_StatesSectionExpanded = true;
            m_StatesCard?.SetExpanded?.Invoke(true);

            string parentPath = GetStatePathParent(nodePath);
            List<string> segments = SplitStatePathSegments(parentPath);
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = BuildStatePathKey(currentPath, segments[i]);
                SetStateGroupCollapsed(BuildStateGroupKey(m_StatesGraphChannelName, currentPath), false);
            }

            RebuildStateList();
            string groupKey = BuildStateGroupKey(m_StatesGraphChannelName, nodePath);
            if (m_StateGroupRowMap.TryGetValue(groupKey, out VisualElement groupRow))
            {
                ScheduleInspectorScrollIntoView(groupRow);
                FlashElement(groupRow);
            }
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

        private void UpdateChannelGraphTab()
        {
            if (m_PreviewStatesGraphTabButton == null)
            {
                return;
            }

            string text = string.IsNullOrWhiteSpace(m_StatesGraphChannelName) ? "None" : m_StatesGraphChannelName;
            m_PreviewStatesGraphTabButton.tooltip = $"当前 Channel：{text}。选中后点击可切换 Channel。";
        }

        private void ShowStatesGraphChannelMenu()
        {
            if (m_PreviewStatesGraphTabButton == null || m_Session == null || !m_Session.IsLoaded)
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

            menu.DropDown(m_PreviewStatesGraphTabButton.worldBound);
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

        private void SelectStatesGraphNode(string stateUiKey)
        {
            if (string.IsNullOrWhiteSpace(stateUiKey) ||
                !TryGetCompiledStateNodeByUiKey(stateUiKey, out XAnimationCompiledStateNode node) ||
                !string.Equals(node.ChannelName, m_StatesGraphChannelName, StringComparison.Ordinal))
            {
                return;
            }

            m_StatesGraphSelectedStateUiKey = stateUiKey;
            RebuildStatesGraphTab();
        }

        private bool TryGetCompiledStateNodeByUiKey(string nodeUiKey, out XAnimationCompiledStateNode node)
        {
            IReadOnlyList<XAnimationCompiledStateNode> nodes = m_Session?.CompiledAsset?.StateNodes ?? Array.Empty<XAnimationCompiledStateNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationCompiledStateNode candidate = nodes[i];
                if (candidate != null &&
                    string.Equals(BuildStateUiKey(candidate.ChannelName, candidate.Key), nodeUiKey, StringComparison.Ordinal))
                {
                    node = candidate;
                    return true;
                }
            }
            node = null;
            return false;
        }

        private XAnimationCompiledStateNode ResolveStatesGraphCurrentNode()
        {
            if (string.IsNullOrWhiteSpace(m_StatesGraphCurrentPath))
            {
                return null;
            }
            return m_Session.CompiledAsset.TryGetStateNodeIndex(m_StatesGraphChannelName, m_StatesGraphCurrentPath, out int nodeIndex)
                ? m_Session.CompiledAsset.StateNodes[nodeIndex]
                : null;
        }

        private List<XAnimationStatesGraphElement.BreadcrumbViewData> BuildStatesGraphBreadcrumbs(string channelName, string path)
        {
            List<XAnimationStatesGraphElement.BreadcrumbViewData> breadcrumbs = new()
            {
                new XAnimationStatesGraphElement.BreadcrumbViewData(channelName, string.Empty)
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

        private List<XAnimationStatesGraphElement.NodeViewData> BuildStatesGraphNodes(IReadOnlyList<XAnimationCompiledStateNode> children)
        {
            List<XAnimationStatesGraphElement.NodeViewData> nodes = new(children.Count);
            for (int i = 0; i < children.Count; i++)
            {
                XAnimationCompiledStateNode child = children[i];
                bool hasPosition = m_Session.TryGetStatesGraphNodePosition(
                    m_StatesGraphChannelName,
                    child.Key,
                    child.Kind,
                    out Vector2 position);
                string detail = child switch
                {
                    XAnimationCompiledSelectorStateNode selector => $"Selector · {selector.Config.parameterName} · {selector.Children.Count} 分支",
                    XAnimationCompiledState state => $"{state.Config.stateType} · loop {state.Config.loop} · speed {state.Config.speed:0.###}",
                    _ => $"Normal · {child.Children.Count} 子节点",
                };
                nodes.Add(new XAnimationStatesGraphElement.NodeViewData(
                    child.Kind,
                    child.Name,
                    child.Key,
                    BuildStateUiKey(child.ChannelName, child.Key),
                    detail,
                    hasPosition,
                    position));
            }

            return nodes;
        }

        private List<XAnimationStatesGraphElement.NodeViewData> BuildSelectorStatesGraphNodes(
            XAnimationCompiledSelectorStateNode rootSelector)
        {
            List<XAnimationStatesGraphElement.NodeViewData> nodes = new();
            AddSelectorStatesGraphNode(nodes, rootSelector, 0, string.Empty, -1, true);
            AddSelectorStatesGraphChildren(nodes, rootSelector, 1);
            return nodes;
        }

        private void AddSelectorStatesGraphChildren(
            List<XAnimationStatesGraphElement.NodeViewData> nodes,
            XAnimationCompiledSelectorStateNode parentSelector,
            int depth)
        {
            for (int i = 0; i < parentSelector.Children.Count; i++)
            {
                XAnimationCompiledStateNode child = parentSelector.Children[i];
                AddSelectorStatesGraphNode(nodes, child, depth, parentSelector.Key, i, false);
                if (child is XAnimationCompiledSelectorStateNode childSelector)
                {
                    AddSelectorStatesGraphChildren(nodes, childSelector, depth + 1);
                }
            }
        }

        private void AddSelectorStatesGraphNode(
            List<XAnimationStatesGraphElement.NodeViewData> nodes,
            XAnimationCompiledStateNode node,
            int depth,
            string parentSelectorPath,
            int selectorValue,
            bool isSelectorRoot)
        {
            Vector2 position = default;
            string valuePrefix = selectorValue >= 0 ? $"Value {selectorValue} · " : string.Empty;
            string detail = node switch
            {
                XAnimationCompiledSelectorStateNode selector => $"{valuePrefix}Selector · {selector.Children.Count} 分支",
                XAnimationCompiledState state => $"{valuePrefix}{state.Config.stateType} · loop {state.Config.loop}",
                _ => valuePrefix + node.Kind,
            };
            string parameterName = node is XAnimationCompiledSelectorStateNode selectorNode
                ? selectorNode.Config.parameterName
                : string.Empty;
            nodes.Add(new XAnimationStatesGraphElement.NodeViewData(
                node.Kind,
                node.Name,
                node.Key,
                BuildStateUiKey(node.ChannelName, node.Key),
                detail,
                false,
                position,
                depth,
                parentSelectorPath,
                selectorValue,
                parameterName,
                isSelectorRoot));
        }

        private List<string> BuildStatesGraphIntParameters()
        {
            List<string> parameters = new();
            IReadOnlyList<XAnimationCompiledParameter> compiledParameters = m_Session.CompiledAsset.Parameters;
            for (int i = 0; i < compiledParameters.Count; i++)
            {
                if (compiledParameters[i].Type == XAnimationParameterType.Int)
                {
                    parameters.Add(compiledParameters[i].Name);
                }
            }

            return parameters;
        }

        private void SetStatesGraphNodePosition(string path, XAnimationStateNodeKind nodeKind, Vector2 position)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            try
            {
                m_Session.SetStatesGraphNodePosition(m_StatesGraphChannelName, path, nodeKind, position);
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

        private void AddStatesGraphNode(
            XAnimationStateNodeKind nodeKind,
            string parentPath,
            Vector2 graphPosition)
        {
            try
            {
                bool automaticSelectorLayout = !string.IsNullOrWhiteSpace(parentPath) &&
                                               m_Session.CompiledAsset.GetStateNode(m_StatesGraphChannelName, parentPath).Kind == XAnimationStateNodeKind.Selector;
                string nodeKey = nodeKind switch
                {
                    XAnimationStateNodeKind.State => m_Session.AddState(m_StatesGraphChannelName, parentPath),
                    XAnimationStateNodeKind.Normal => m_Session.AddNormalStateNode(m_StatesGraphChannelName, parentPath),
                    XAnimationStateNodeKind.Selector => m_Session.AddSelectorStateNode(m_StatesGraphChannelName, parentPath),
                    _ => throw new XAnimationException($"Cannot add State Node with kind '{nodeKind}'."),
                };
                if (!automaticSelectorLayout)
                {
                    m_Session.SetStatesGraphNodePosition(m_StatesGraphChannelName, nodeKey, nodeKind, graphPosition);
                }
                if (nodeKind == XAnimationStateNodeKind.State)
                {
                    m_PendingStateRenameKey = nodeKey;
                }
                m_StatesGraphSelectedStateUiKey = BuildStateUiKey(m_StatesGraphChannelName, nodeKey);
                RebuildStatePresentation(includeChannelPresentation: true);
                RebuildStatesGraphTab();
                SetStatus($"已创建 {nodeKind} State Node {m_StatesGraphChannelName} / {nodeKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteStatesGraphNode(string nodeKey)
        {
            XAnimationCompiledStateNode node = m_Session.CompiledAsset.GetStateNode(m_StatesGraphChannelName, nodeKey);
            string parentPath = node.ParentKey;
            bool deletingCurrentPath = string.Equals(m_StatesGraphCurrentPath, node.Key, StringComparison.Ordinal) ||
                                       m_StatesGraphCurrentPath.StartsWith(node.Key + "/", StringComparison.Ordinal);
            if (node.Kind == XAnimationStateNodeKind.State)
            {
                DeleteState(node.ChannelName, node.Key);
            }
            else
            {
                RemoveStateGroupNode(node.ChannelName, node.Key);
            }

            if (m_Session.CompiledAsset.TryGetStateNodeIndex(node.ChannelName, node.Key, out _))
            {
                return;
            }

            if (deletingCurrentPath)
            {
                m_StatesGraphCurrentPath = parentPath;
            }
            m_StatesGraphSelectedStateUiKey = string.Empty;
            RebuildStatesGraphTab();
        }

        private void SetStatesGraphTransitionState(string stateUiKey)
        {
            if (!TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState state))
            {
                return;
            }

            m_StatesGraphChannelName = state.ChannelName;
            m_StatesGraphCurrentPath = state.Key;
            m_StatesGraphSelectedStateUiKey = stateUiKey;
            m_DefaultTransitionEditingStateUiKey = stateUiKey;
            m_DefaultTransitionTabTransitionIndex = -1;
            m_DefaultTransitionTabPairIndex = -1;
            m_DefaultTransitionTabPairIsAuto = false;
            m_DefaultTransitionTabPairWaitingSwitch = false;
            RebuildStatesGraphTab();
        }

        private void SetStatesGraphSelectorParameter(string nodeKey, string parameterName)
        {
            try
            {
                m_Session.SetSelectorStateNodeParameter(m_StatesGraphChannelName, nodeKey, parameterName);
                m_StatesGraphSelectedStateUiKey = BuildStateUiKey(m_StatesGraphChannelName, nodeKey);
                RebuildStatePresentation(includeChannelPresentation: true);
                RebuildStatesGraphTab();
                SetStatus($"已将 Selector {nodeKey} 的 Parameter 设置为 {parameterName}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void BuildStatesGraphDetails(XAnimationCompiledStateNode selectedNode)
        {
            m_StatesGraphDetailsView.Clear();

            Label title = CreateBoldLabel("State Node");
            title.style.fontSize = SectionTitleFontSize;
            m_StatesGraphDetailsView.Add(title);

            if (selectedNode == null)
            {
                AddEmptyLabel(m_StatesGraphDetailsView, "No node selected");
                return;
            }

            TextField pathField = new("Path")
            {
                value = FormatStateDisplayPath(selectedNode.Key),
                isReadOnly = true,
            };
            pathField.style.marginTop = 8;
            m_StatesGraphDetailsView.Add(pathField);

            TextField kindField = new("Kind")
            {
                value = selectedNode.Kind.ToString(),
                isReadOnly = true,
            };
            m_StatesGraphDetailsView.Add(kindField);

            if (selectedNode is not XAnimationCompiledState selectedState)
            {
                return;
            }

            XAnimationStateConfig config = selectedState.Config;
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

                ChangeStateType(selectedState.ChannelName, selectedState.Key, stateType, evt.previousValue, stateTypeField);
                m_StatesGraphSelectedStateUiKey = BuildStateUiKey(selectedState.ChannelName, selectedState.Key);
                RebuildStatesGraphTab();
            });
            m_StatesGraphDetailsView.Add(stateTypeField);

            VisualElement editor = CreateStateEditor(selectedState);
            editor.style.marginTop = 8;
            m_StatesGraphDetailsView.Add(editor);
        }

        private sealed class XAnimationStatesGraphElement : VisualElement
        {
            public enum DisplayMode
            {
                Normal,
                Selector,
                State,
            }

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

            private sealed class AnimatorBreadcrumbElement : VisualElement
            {
                private const float ArrowWidth = 10f;
                private static readonly Color NormalBackground = new(0.16f, 0.17f, 0.19f, 1f);
                private static readonly Color CurrentBackground = new(0.21f, 0.22f, 0.24f, 1f);

                private readonly bool m_IsFirst;
                private readonly bool m_IsCurrent;
                private bool m_IsHovered;

                public AnimatorBreadcrumbElement(string text, bool isFirst, bool isCurrent, Action clicked)
                {
                    m_IsFirst = isFirst;
                    m_IsCurrent = isCurrent;

                    style.height = 22;
                    style.flexShrink = 0;
                    style.flexDirection = FlexDirection.Row;
                    style.alignItems = Align.Center;
                    style.justifyContent = Justify.FlexStart;
                    style.marginLeft = isFirst ? 0 : -ArrowWidth;
                    style.paddingLeft = isFirst ? 8 : ArrowWidth + 8;
                    style.paddingRight = ArrowWidth + 8;

                    Label label = new(text);
                    label.pickingMode = PickingMode.Ignore;
                    label.style.fontSize = 11;
                    label.style.color = isCurrent ? TextNormal : TextMuted;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    Add(label);

                    generateVisualContent += OnGenerateVisualContent;
                    RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
                    RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
                    this.AddManipulator(new Clickable(clicked));
                }

                private void SetHovered(bool hovered)
                {
                    m_IsHovered = hovered;
                    MarkDirtyRepaint();
                }

                private void OnGenerateVisualContent(MeshGenerationContext context)
                {
                    float width = layout.width;
                    float height = layout.height;
                    Color background = m_IsCurrent ? CurrentBackground : NormalBackground;
                    if (m_IsHovered)
                    {
                        background = Color.Lerp(background, AccentColor, 0.28f);
                    }

                    Painter2D painter = context.painter2D;
                    painter.fillColor = background;
                    painter.strokeColor = SectionDivider;
                    painter.lineWidth = 1f;
                    painter.BeginPath();
                    painter.MoveTo(Vector2.zero);
                    painter.LineTo(new Vector2(width - ArrowWidth, 0f));
                    painter.LineTo(new Vector2(width, height * 0.5f));
                    painter.LineTo(new Vector2(width - ArrowWidth, height));
                    painter.LineTo(new Vector2(0f, height));
                    if (!m_IsFirst)
                    {
                        painter.LineTo(new Vector2(ArrowWidth, height * 0.5f));
                    }
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
            }

            public readonly struct NodeViewData
            {
                public NodeViewData(
                    XAnimationStateNodeKind kind,
                    string title,
                    string path,
                    string nodeUiKey,
                    string detail,
                    bool hasPosition,
                    Vector2 position,
                    int depth = 0,
                    string parentSelectorPath = null,
                    int selectorValue = -1,
                    string parameterName = null,
                    bool isSelectorRoot = false)
                {
                    Kind = kind;
                    Title = title ?? string.Empty;
                    Path = path ?? string.Empty;
                    NodeUiKey = nodeUiKey ?? string.Empty;
                    Detail = detail ?? string.Empty;
                    HasPosition = hasPosition;
                    Position = position;
                    Depth = depth;
                    ParentSelectorPath = parentSelectorPath ?? string.Empty;
                    SelectorValue = selectorValue;
                    ParameterName = parameterName ?? string.Empty;
                    IsSelectorRoot = isSelectorRoot;
                }

                public XAnimationStateNodeKind Kind { get; }
                public string Title { get; }
                public string Path { get; }
                public string NodeUiKey { get; }
                public string Detail { get; }
                public bool HasPosition { get; }
                public Vector2 Position { get; }
                public int Depth { get; }
                public string ParentSelectorPath { get; }
                public int SelectorValue { get; }
                public string ParameterName { get; }
                public bool IsSelectorRoot { get; }

                public NodeViewData WithPosition(Vector2 position)
                {
                    return new NodeViewData(
                        Kind,
                        Title,
                        Path,
                        NodeUiKey,
                        Detail,
                        true,
                        position,
                        Depth,
                        ParentSelectorPath,
                        SelectorValue,
                        ParameterName,
                        IsSelectorRoot);
                }
            }

            private abstract class GraphMode
            {
                public abstract float NodeHeight { get; }
                public virtual bool CanAddNormal => true;
                public virtual bool UsesStateTransitionGraph => false;

                public virtual float GetNodeWidth(NodeViewData nodeData)
                {
                    return NodeWidth;
                }

                public abstract void BuildLayout(
                    XAnimationStatesGraphElement graph,
                    List<Vector2> positions);

                public virtual void AddNodeContent(
                    XAnimationStatesGraphElement graph,
                    VisualElement node,
                    NodeViewData nodeData)
                {
                }

                public virtual bool CanDragNode(NodeViewData nodeData)
                {
                    return true;
                }

                public virtual bool CanEnterNode(NodeViewData nodeData)
                {
                    return true;
                }

                public virtual bool CanDeleteNode(NodeViewData nodeData)
                {
                    return true;
                }

                public virtual void DrawOverlay(XAnimationStatesGraphElement graph, Painter2D painter)
                {
                }

                public virtual Vector2 AdjustCanvasAddPosition(
                    XAnimationStatesGraphElement graph,
                    Vector2 graphPoint)
                {
                    return graphPoint;
                }
            }

            private sealed class NormalGraphMode : GraphMode
            {
                public static readonly NormalGraphMode Instance = new();

                public override float NodeHeight => XAnimationStatesGraphElement.NodeHeight;

                public override void BuildLayout(
                    XAnimationStatesGraphElement graph,
                    List<Vector2> positions)
                {
                    int automaticCount = 0;
                    for (int i = 0; i < graph.m_Nodes.Count; i++)
                    {
                        if (!graph.m_Nodes[i].HasPosition)
                        {
                            automaticCount++;
                        }
                    }

                    int columns = Mathf.Clamp(
                        Mathf.Max(1, Mathf.FloorToInt((graph.GetViewportSize().x - CanvasPadding * 2f) / (NodeWidth + NodeGapX))),
                        1,
                        4);
                    int automaticColumns = Mathf.Min(columns, automaticCount);
                    int automaticRows = Mathf.CeilToInt((float)automaticCount / columns);
                    float layoutWidth = automaticColumns * NodeWidth + Mathf.Max(0, automaticColumns - 1) * NodeGapX;
                    float layoutHeight = automaticRows * NodeHeight + Mathf.Max(0, automaticRows - 1) * NodeGapY;
                    Vector2 automaticOrigin = graph.GetAutomaticLayoutOrigin(layoutWidth, layoutHeight);
                    int automaticIndex = 0;
                    for (int i = 0; i < graph.m_Nodes.Count; i++)
                    {
                        NodeViewData node = graph.m_Nodes[i];
                        Vector2 position = node.HasPosition
                            ? node.Position
                            : new Vector2(
                                automaticOrigin.x + automaticIndex % columns * (NodeWidth + NodeGapX),
                                automaticOrigin.y + automaticIndex / columns * (NodeHeight + NodeGapY));
                        if (!node.HasPosition)
                        {
                            automaticIndex++;
                        }
                        positions.Add(position);
                        graph.m_NodePositionByPath[node.Path] = position;
                    }
                }
            }

            private sealed class SelectorGraphMode : GraphMode
            {
                public static readonly SelectorGraphMode Instance = new();

                public override float NodeHeight => SelectorNodeHeight;
                public override bool CanAddNormal => false;

                public override float GetNodeWidth(NodeViewData nodeData)
                {
                    return nodeData.Kind == XAnimationStateNodeKind.Selector
                        ? SelectorNodeWidth
                        : NodeWidth;
                }

                public override void BuildLayout(
                    XAnimationStatesGraphElement graph,
                    List<Vector2> positions)
                {
                    Dictionary<string, List<int>> childrenByParent = new(StringComparer.Ordinal);
                    for (int i = 0; i < graph.m_Nodes.Count; i++)
                    {
                        positions.Add(Vector2.zero);
                        string parentPath = graph.m_Nodes[i].ParentSelectorPath;
                        if (string.IsNullOrWhiteSpace(parentPath))
                        {
                            continue;
                        }

                        if (!childrenByParent.TryGetValue(parentPath, out List<int> children))
                        {
                            children = new List<int>();
                            childrenByParent.Add(parentPath, children);
                        }
                        children.Add(i);
                    }

                    float nextLeafY = 0f;
                    LayoutSubtree(graph, 0, childrenByParent, positions, ref nextLeafY);

                    float layoutWidth = 0f;
                    float layoutHeight = 0f;
                    for (int i = 0; i < graph.m_Nodes.Count; i++)
                    {
                        NodeViewData node = graph.m_Nodes[i];
                        Vector2 position = positions[i];
                        layoutWidth = Mathf.Max(layoutWidth, position.x + GetNodeWidth(node));
                        layoutHeight = Mathf.Max(layoutHeight, position.y + SelectorNodeHeight);
                    }

                    Vector2 origin = graph.GetAutomaticLayoutOrigin(layoutWidth, layoutHeight);
                    for (int i = 0; i < graph.m_Nodes.Count; i++)
                    {
                        NodeViewData node = graph.m_Nodes[i];
                        Vector2 position = positions[i] + origin;
                        positions[i] = position;
                        graph.m_NodePositionByPath[node.Path] = position;
                    }
                }

                private static void LayoutSubtree(
                    XAnimationStatesGraphElement graph,
                    int nodeIndex,
                    Dictionary<string, List<int>> childrenByParent,
                    List<Vector2> positions,
                    ref float nextLeafY)
                {
                    NodeViewData node = graph.m_Nodes[nodeIndex];
                    float y;
                    if (childrenByParent.TryGetValue(node.Path, out List<int> children) && children.Count > 0)
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            LayoutSubtree(graph, children[i], childrenByParent, positions, ref nextLeafY);
                        }

                        float firstChildCenter = positions[children[0]].y + SelectorNodeHeight * 0.5f;
                        float lastChildCenter = positions[children[^1]].y + SelectorNodeHeight * 0.5f;
                        y = (firstChildCenter + lastChildCenter) * 0.5f - SelectorNodeHeight * 0.5f;
                    }
                    else
                    {
                        y = nextLeafY;
                        nextLeafY += SelectorNodeHeight + NodeGapY;
                    }

                    positions[nodeIndex] = new Vector2(
                        node.Depth * (SelectorNodeWidth + NodeGapX * 2f),
                        y);
                }

                public override void AddNodeContent(
                    XAnimationStatesGraphElement graph,
                    VisualElement node,
                    NodeViewData nodeData)
                {
                    if (nodeData.Kind == XAnimationStateNodeKind.Selector)
                    {
                        graph.AddSelectorParameterField(node, nodeData);
                    }
                }

                public override bool CanDragNode(NodeViewData nodeData)
                {
                    return false;
                }

                public override bool CanEnterNode(NodeViewData nodeData)
                {
                    return nodeData.Kind == XAnimationStateNodeKind.State;
                }

                public override bool CanDeleteNode(NodeViewData nodeData)
                {
                    return !nodeData.IsSelectorRoot;
                }

                public override void DrawOverlay(XAnimationStatesGraphElement graph, Painter2D painter)
                {
                    graph.DrawSelectorEdges(painter);
                }

                public override Vector2 AdjustCanvasAddPosition(
                    XAnimationStatesGraphElement graph,
                    Vector2 graphPoint)
                {
                    if (graph.m_NodePositionByPath.TryGetValue(graph.m_CurrentPath, out Vector2 rootPosition))
                    {
                        graphPoint.x = Mathf.Max(
                            graphPoint.x,
                            rootPosition.x + SelectorNodeWidth + NodeGapX * 2f);
                    }
                    return graphPoint;
                }
            }

            private sealed class StateGraphMode : GraphMode
            {
                public static readonly StateGraphMode Instance = new();

                public override float NodeHeight => XAnimationStatesGraphElement.NodeHeight;
                public override bool CanAddNormal => false;
                public override bool UsesStateTransitionGraph => true;

                public override void BuildLayout(
                    XAnimationStatesGraphElement graph,
                    List<Vector2> positions)
                {
                }
            }

            private const float MinZoom = 0.9f;
            private const float MaxZoom = 1.85f;
            private const float WheelZoomBase = 1.12f;
            private const float MinCanvasWidth = 720f;
            private const float MinCanvasHeight = 360f;
            private const float CanvasPadding = 42f;
            private const float NodeWidth = 168f;
            private const float SelectorNodeWidth = 240f;
            private const float NodeHeight = 64f;
            private const float SelectorNodeHeight = 88f;
            private const float NodeGapX = 34f;
            private const float NodeGapY = 28f;
            private const double DoubleClickInterval = 0.5;
            private const string NodeClassName = "xanimation-states-graph-node";

            private static readonly Color CanvasBg = new(0.095f, 0.10f, 0.115f, 1f);
            private static readonly Color CanvasGrid = new(0.78f, 0.79f, 0.80f, 0.075f);
            private static readonly Color CanvasGridMajor = new(0.78f, 0.79f, 0.80f, 0.13f);
            private static readonly Color NormalNodeBg = new(0.20f, 0.18f, 0.25f, 0.98f);
            private static readonly Color SelectorBg = new(0.20f, 0.25f, 0.18f, 0.98f);
            private static readonly Color StateBg = new(0.18f, 0.19f, 0.21f, 0.98f);
            private static readonly Color SelectedBg = new(0.16f, 0.24f, 0.34f, 0.98f);
            private static readonly Color NodeBorder = new(0.34f, 0.35f, 0.38f, 1f);
            private static readonly Color SelectedBorder = new(0.48f, 0.74f, 1f, 1f);
            private static readonly Color SelectorEdge = new(0.56f, 0.76f, 0.42f, 0.82f);

            private readonly List<NodeViewData> m_Nodes = new();
            private readonly Dictionary<string, Vector2> m_NodePositionByPath = new(StringComparer.Ordinal);
            private readonly List<string> m_IntParameters = new();
            private GraphMode m_Mode = NormalGraphMode.Instance;
            private ScrollView m_ScrollView;
            private VisualElement m_BreadcrumbRow;
            private VisualElement m_Canvas;
            private VisualElement m_GridCanvas;
            private VisualElement m_NodeLayer;
            private Label m_EmptyLabel;
            private XAnimationStateTransitionGraphElement m_StateTransitionGraphView;
            private string m_SelectedStateUiKey = string.Empty;
            private string m_EmptyMessage = string.Empty;
            private string m_CurrentPath = string.Empty;
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
            private string m_LastPointerDownNodePath = string.Empty;
            private double m_LastPointerDownTime;

            public XAnimationStatesGraphElement()
            {
                style.flexGrow = 1;
                style.minHeight = 0;
                style.backgroundColor = CanvasBg;
                BuildUi();
            }

            public event Action<string> ContainerDoubleClicked;
            public event Action<string> NodeSelected;
            public event Action<string, XAnimationStateNodeKind> NodeTreeFocusRequested;
            public event Action<string, XAnimationStateNodeKind, Vector2> NodePositionChanged;
            public event Action<Vector2> PanOffsetChanged;
            public event Action<XAnimationStateNodeKind, string, Vector2> AddNodeRequested;
            public event Action<string> DeleteNodeRequested;
            public event Action<string, string> SelectorParameterChanged;
            public event Action<int, int, bool> TransitionSelected;
            public event Action<int, int, bool> TransitionDeleteRequested;
            public event Action<string> TransitionStateEntered;
            public event Action<bool, Rect> TransitionAddRequested;
            public event Action<float> ZoomChanged;

            public float Zoom => m_Mode.UsesStateTransitionGraph ? m_StateTransitionGraphView.Zoom : m_Zoom;
            public VisualElement BreadcrumbRow => m_BreadcrumbRow;

            public void SetData(
                string channelName,
                string currentPath,
                IReadOnlyList<BreadcrumbViewData> breadcrumbs,
                IReadOnlyList<NodeViewData> nodes,
                DisplayMode displayMode,
                IReadOnlyList<string> intParameters,
                string selectedStateUiKey,
                Vector2 panOffset)
            {
                m_CurrentPath = currentPath ?? string.Empty;
                m_Mode = ResolveMode(displayMode);
                m_SelectedStateUiKey = selectedStateUiKey ?? string.Empty;
                m_EmptyMessage = string.Empty;
                m_PanOffset = panOffset;
                m_IntParameters.Clear();
                if (intParameters != null)
                {
                    for (int i = 0; i < intParameters.Count; i++)
                    {
                        m_IntParameters.Add(intParameters[i]);
                    }
                }
                m_Nodes.Clear();
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        m_Nodes.Add(nodes[i]);
                    }
                }

                RebuildBreadcrumbs(breadcrumbs);
                ApplyModeDisplay();
                RebuildGraph(channelName, currentPath);
                RefreshViewportAfterLayout();
            }

            public void SetStateData(
                string currentPath,
                IReadOnlyList<BreadcrumbViewData> breadcrumbs,
                string editingStateUiKey,
                string editingStateLabel,
                IReadOnlyList<XAnimationStateTransitionGraphElement.PairViewData> pairs,
                bool canAddPair)
            {
                m_CurrentPath = currentPath ?? string.Empty;
                m_Mode = ResolveMode(DisplayMode.State);
                m_SelectedStateUiKey = editingStateUiKey ?? string.Empty;
                m_EmptyMessage = string.Empty;
                m_IntParameters.Clear();
                m_Nodes.Clear();
                RebuildBreadcrumbs(breadcrumbs);
                ApplyModeDisplay();
                m_StateTransitionGraphView.SetCanAddPair(canAddPair);
                m_StateTransitionGraphView.SetData(editingStateUiKey, editingStateLabel, pairs);
            }

            public void SetEmpty(string message)
            {
                m_SelectedStateUiKey = string.Empty;
                m_EmptyMessage = message ?? string.Empty;
                m_CurrentPath = string.Empty;
                m_Mode = NormalGraphMode.Instance;
                m_IntParameters.Clear();
                m_Nodes.Clear();
                RebuildBreadcrumbs(Array.Empty<BreadcrumbViewData>());
                ApplyModeDisplay();
                RebuildGraph(message: message);
                RefreshViewportAfterLayout();
            }

            public void ResetView()
            {
                if (m_Mode.UsesStateTransitionGraph)
                {
                    m_StateTransitionGraphView.ResetView();
                    return;
                }

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
                if (m_Mode.UsesStateTransitionGraph)
                {
                    m_StateTransitionGraphView.RefreshViewportAfterLayout();
                    return;
                }

                RefreshCanvasViewport();
                schedule.Execute(RebuildGraphAfterLayout).ExecuteLater(0);
                schedule.Execute(RebuildGraphAfterLayout).ExecuteLater(16);
            }

            private void RebuildGraphAfterLayout()
            {
                RebuildGraph();
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

                m_StateTransitionGraphView = new XAnimationStateTransitionGraphElement();
                m_StateTransitionGraphView.style.display = DisplayStyle.None;
                m_StateTransitionGraphView.PairSelected += (transitionIndex, pairIndex, isAuto) =>
                    TransitionSelected?.Invoke(transitionIndex, pairIndex, isAuto);
                m_StateTransitionGraphView.PairDeleteRequested += (transitionIndex, pairIndex, isAuto) =>
                    TransitionDeleteRequested?.Invoke(transitionIndex, pairIndex, isAuto);
                m_StateTransitionGraphView.StateEditRequested += stateUiKey => TransitionStateEntered?.Invoke(stateUiKey);
                m_StateTransitionGraphView.AddPairRequested += (inState, activatorRect) =>
                    TransitionAddRequested?.Invoke(inState, activatorRect);
                m_StateTransitionGraphView.ZoomChanged += zoom => ZoomChanged?.Invoke(zoom);
                Add(m_StateTransitionGraphView);
            }

            private void ApplyModeDisplay()
            {
                bool showStateTransitions = m_Mode.UsesStateTransitionGraph;
                m_ScrollView.style.display = showStateTransitions ? DisplayStyle.None : DisplayStyle.Flex;
                m_StateTransitionGraphView.style.display = showStateTransitions ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private static GraphMode ResolveMode(DisplayMode displayMode)
            {
                return displayMode switch
                {
                    DisplayMode.Selector => SelectorGraphMode.Instance,
                    DisplayMode.State => StateGraphMode.Instance,
                    _ => NormalGraphMode.Instance,
                };
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
                    AnimatorBreadcrumbElement segment = new(
                        breadcrumb.Label,
                        i == 0,
                        i == breadcrumbs.Count - 1,
                        () => ContainerDoubleClicked?.Invoke(breadcrumb.Path));
                    segment.tooltip = string.IsNullOrWhiteSpace(breadcrumb.Path) ? "回到根路径。" : $"回到 {breadcrumb.Path}。";
                    m_BreadcrumbRow.Add(segment);
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
                float nodeHeight = GetNodeHeight();
                List<Vector2> positions = new(m_Nodes.Count);
                m_NodePositionByPath.Clear();
                m_Mode.BuildLayout(this, positions);
                float maxX = 0f;
                float maxY = 0f;
                for (int i = 0; i < m_Nodes.Count; i++)
                {
                    NodeViewData node = m_Nodes[i];
                    Vector2 position = positions[i];
                    maxX = Mathf.Max(maxX, position.x + GetNodeWidth(node) + CanvasPadding);
                    maxY = Mathf.Max(maxY, position.y + nodeHeight + CanvasPadding);
                }

                ApplyCanvasSize(Mathf.Max(MinCanvasWidth, maxX), Mathf.Max(MinCanvasHeight, maxY));

                for (int i = 0; i < m_Nodes.Count; i++)
                {
                    CreateNode(i, positions[i], m_Nodes[i]);
                }

                m_GridCanvas.MarkDirtyRepaint();
            }

            private void CreateNode(int nodeIndex, Vector2 graphPosition, NodeViewData nodeData)
            {
                bool selected = string.Equals(nodeData.NodeUiKey, m_SelectedStateUiKey, StringComparison.Ordinal);
                float nodeWidth = GetNodeWidth(nodeData);
                float nodeHeight = GetNodeHeight();
                Rect rect = ScaleRect(new Rect(graphPosition.x, graphPosition.y, nodeWidth, nodeHeight));
                VisualElement node = new();
                node.AddToClassList(NodeClassName);
                node.style.position = Position.Absolute;
                node.style.left = rect.x;
                node.style.top = rect.y;
                node.style.width = nodeWidth;
                node.style.height = nodeHeight;
                node.style.transformOrigin = new TransformOrigin(0f, 0f);
                node.style.scale = new Vector2(m_Zoom, m_Zoom);
                node.style.paddingLeft = 8;
                node.style.paddingRight = 8;
                node.style.paddingTop = 7;
                node.style.paddingBottom = 6;
                node.style.backgroundColor = selected
                    ? SelectedBg
                    : nodeData.Kind == XAnimationStateNodeKind.Selector
                        ? SelectorBg
                        : nodeData.Kind == XAnimationStateNodeKind.Normal ? NormalNodeBg : StateBg;
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
                title.style.fontSize = 12f;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.overflow = Overflow.Hidden;
                title.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(title);

                Label detailLabel = new(nodeData.Detail);
                detailLabel.style.color = TextMuted;
                detailLabel.style.fontSize = 10f;
                detailLabel.style.marginTop = 4;
                detailLabel.style.overflow = Overflow.Hidden;
                detailLabel.style.textOverflow = TextOverflow.Ellipsis;
                node.Add(detailLabel);

                m_Mode.AddNodeContent(this, node, nodeData);

                node.RegisterCallback<PointerDownEvent>(evt => OnNodePointerDown(evt, node, nodeIndex, nodeData, graphPosition));
                node.RegisterCallback<PointerMoveEvent>(OnNodePointerMove);
                node.RegisterCallback<PointerUpEvent>(OnNodePointerUp);
                node.RegisterCallback<PointerCancelEvent>(OnNodePointerCancel);
                node.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction(
                        "在 NodeTree 中定位",
                        _ => NodeTreeFocusRequested?.Invoke(nodeData.Path, nodeData.Kind));
                    if (nodeData.Kind == XAnimationStateNodeKind.Selector)
                    {
                        int directChildCount = 0;
                        for (int i = 0; i < m_Nodes.Count; i++)
                        {
                            if (string.Equals(m_Nodes[i].ParentSelectorPath, nodeData.Path, StringComparison.Ordinal))
                            {
                                directChildCount++;
                            }
                        }
                        Vector2 addPosition = new(
                            graphPosition.x + GetNodeWidth(nodeData) + NodeGapX * 2f,
                            graphPosition.y + directChildCount * (GetNodeHeight() + NodeGapY));
                        evt.menu.AppendAction(
                            "新增子 Node/State",
                            _ => AddNodeRequested?.Invoke(XAnimationStateNodeKind.State, nodeData.Path, addPosition),
                            _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                        evt.menu.AppendAction(
                            "新增子 Node/Selector",
                            _ => AddNodeRequested?.Invoke(XAnimationStateNodeKind.Selector, nodeData.Path, addPosition),
                            _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    }
                    if (m_Mode.CanDeleteNode(nodeData))
                    {
                        evt.menu.AppendAction(
                            "删除 Node",
                            _ => DeleteNodeRequested?.Invoke(nodeData.Path),
                            _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                    }
                    evt.StopPropagation();
                }));

                m_NodeLayer.Add(node);
            }

            private void AddSelectorParameterField(VisualElement node, NodeViewData nodeData)
            {
                DropdownField parameterField = new(
                    "Parameter",
                    m_IntParameters,
                    m_IntParameters.IndexOf(nodeData.ParameterName));
                parameterField.tooltip = "用于按子节点顺序选择状态的 Int Parameter。";
                parameterField.style.marginTop = 3;
                parameterField.style.height = 20;
                parameterField.style.minWidth = 0;
                parameterField.style.flexGrow = 1;
                ApplyDropdownFieldStyle(parameterField);
                parameterField.labelElement.style.width = 68;
                parameterField.labelElement.style.minWidth = 68;
                parameterField.labelElement.style.maxWidth = 68;
                parameterField.labelElement.style.flexShrink = 0;
                VisualElement parameterInput = parameterField.Q<VisualElement>(className: "unity-base-field__input");
                parameterInput.style.minWidth = 0;
                parameterInput.style.flexGrow = 1;
                parameterInput.style.flexShrink = 1;
                parameterField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                parameterField.RegisterValueChangedCallback(evt =>
                    SelectorParameterChanged?.Invoke(nodeData.Path, evt.newValue));
                node.Add(parameterField);
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

            private Vector2 GetAutomaticLayoutOrigin(float layoutWidth, float layoutHeight)
            {
                Vector2 visibleGraphSize = GetViewportSize();
                return new Vector2(
                    layoutWidth + CanvasPadding * 2f <= visibleGraphSize.x
                        ? (visibleGraphSize.x - layoutWidth) * 0.5f
                        : CanvasPadding,
                    layoutHeight + CanvasPadding * 2f <= visibleGraphSize.y
                        ? (visibleGraphSize.y - layoutHeight) * 0.5f
                        : CanvasPadding);
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
                Painter2D painter = context.painter2D;
                DrawGrid(painter, GetCanvasPaintRect(), 32f * m_Zoom, new Vector2(CanvasPadding, CanvasPadding) * m_Zoom + m_PanOffset);
                m_Mode.DrawOverlay(this, painter);
            }

            private void DrawSelectorEdges(Painter2D painter)
            {
                for (int i = 0; i < m_Nodes.Count; i++)
                {
                    NodeViewData child = m_Nodes[i];
                    if (string.IsNullOrWhiteSpace(child.ParentSelectorPath) ||
                        !m_NodePositionByPath.TryGetValue(child.ParentSelectorPath, out Vector2 parentPosition) ||
                        !m_NodePositionByPath.TryGetValue(child.Path, out Vector2 childPosition))
                    {
                        continue;
                    }

                    Rect parentRect = ScaleRect(new Rect(parentPosition.x, parentPosition.y, SelectorNodeWidth, GetNodeHeight()));
                    Rect childRect = ScaleRect(new Rect(childPosition.x, childPosition.y, NodeWidth, GetNodeHeight()));
                    Vector2 from = new(parentRect.xMax, parentRect.center.y);
                    Vector2 to = new(childRect.xMin, childRect.center.y);
                    float tangent = Mathf.Clamp(Mathf.Abs(to.x - from.x) * 0.42f, 36f * m_Zoom, 105f * m_Zoom);
                    Vector2 c1 = from + new Vector2(tangent, 0f);
                    Vector2 c2 = to - new Vector2(tangent, 0f);

                    painter.lineWidth = 2f * Mathf.Clamp(m_Zoom, 0.72f, 1.25f);
                    painter.strokeColor = SelectorEdge;
                    painter.BeginPath();
                    painter.MoveTo(from);
                    for (int sample = 1; sample <= 18; sample++)
                    {
                        painter.LineTo(EvaluateCubic(from, c1, c2, to, sample / 18f));
                    }
                    painter.Stroke();
                }
            }

            private static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
            {
                float inverse = 1f - t;
                return inverse * inverse * inverse * p0 +
                       3f * inverse * inverse * t * p1 +
                       3f * inverse * t * t * p2 +
                       t * t * t * p3;
            }

            private void OnCanvasContextMenu(ContextualMenuPopulateEvent evt)
            {
                if (IsStatesGraphNode(evt.target as VisualElement))
                {
                    return;
                }

                Vector2 canvasPoint = m_Canvas.WorldToLocal(evt.mousePosition);
                Vector2 graphPoint = m_Mode.AdjustCanvasAddPosition(this, CanvasToGraphPosition(canvasPoint));
                evt.menu.AppendAction(
                    "新建 Node/State",
                    _ => AddNodeRequested?.Invoke(XAnimationStateNodeKind.State, m_CurrentPath, graphPoint),
                    _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                if (m_Mode.CanAddNormal)
                {
                    evt.menu.AppendAction(
                        "新建 Node/Normal",
                        _ => AddNodeRequested?.Invoke(XAnimationStateNodeKind.Normal, m_CurrentPath, graphPoint),
                        _ => m_CanEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }
                evt.menu.AppendAction(
                    "新建 Node/Selector",
                    _ => AddNodeRequested?.Invoke(XAnimationStateNodeKind.Selector, m_CurrentPath, graphPoint),
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

                double pointerDownTime = EditorApplication.timeSinceStartup;
                bool doubleClick = evt.clickCount >= 2 ||
                                   string.Equals(m_LastPointerDownNodePath, nodeData.Path, StringComparison.Ordinal) &&
                                   pointerDownTime - m_LastPointerDownTime <= DoubleClickInterval;
                m_LastPointerDownNodePath = nodeData.Path;
                m_LastPointerDownTime = pointerDownTime;
                if (doubleClick && m_Mode.CanEnterNode(nodeData))
                {
                    m_LastPointerDownNodePath = string.Empty;
                    ContainerDoubleClicked?.Invoke(nodeData.Path);
                    evt.StopPropagation();
                    return;
                }

                if (!m_Mode.CanDragNode(nodeData))
                {
                    NodeSelected?.Invoke(nodeData.NodeUiKey);
                    evt.StopPropagation();
                    return;
                }

                m_IsDraggingNode = true;
                m_NodeDragPointerId = evt.pointerId;
                m_NodeDragIndex = nodeIndex;
                m_NodeDragClickCount = evt.clickCount;
                m_DraggingNode = node;
                m_DraggingNodeData = nodeData;
                m_NodeDragCurrentCanvasPosition = ScaleRect(new Rect(graphPosition.x, graphPosition.y, GetNodeWidth(nodeData), GetNodeHeight())).position;
                Vector2 localPointerPosition = GetLocalPointerPosition(evt.localPosition);
                m_NodeDragStartPointer = m_NodeDragCurrentCanvasPosition + localPointerPosition * m_Zoom;
                m_NodeDragStartPosition = graphPosition;
                m_NodeDragPointerOffset = localPointerPosition;
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

                Vector2 pointerPosition = m_NodeDragCurrentCanvasPosition + GetLocalPointerPosition(evt.localPosition) * m_Zoom;
                Vector2 screenDelta = pointerPosition - m_NodeDragStartPointer;
                Vector2 nextPosition = CanvasToGraphPosition(pointerPosition) - m_NodeDragPointerOffset;
                m_NodeDragCurrentPosition = nextPosition;
                if (screenDelta.sqrMagnitude > 9f)
                {
                    m_NodeDragMoved = true;
                }

                ApplyNodePosition(m_DraggingNode, nextPosition);
                m_NodePositionByPath[m_DraggingNodeData.Path] = nextPosition;
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
                    m_LastPointerDownNodePath = string.Empty;
                    if (m_NodeDragIndex >= 0 && m_NodeDragIndex < m_Nodes.Count)
                    {
                        m_Nodes[m_NodeDragIndex] = m_Nodes[m_NodeDragIndex].WithPosition(m_NodeDragCurrentPosition);
                    }

                    NodePositionChanged?.Invoke(m_DraggingNodeData.Path, m_DraggingNodeData.Kind, m_NodeDragCurrentPosition);
                }
                else if (invokeClick && m_IsDraggingNode)
                {
                    InvokeNodeClick(m_DraggingNodeData, m_NodeDragClickCount);
                }
                else if (!savePosition && !string.IsNullOrWhiteSpace(m_DraggingNodeData.Path))
                {
                    m_NodePositionByPath[m_DraggingNodeData.Path] = m_NodeDragStartPosition;
                    m_GridCanvas?.MarkDirtyRepaint();
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
                NodeSelected?.Invoke(nodeData.NodeUiKey);
                if (clickCount >= 2 && m_Mode.CanEnterNode(nodeData))
                {
                    ContainerDoubleClicked?.Invoke(nodeData.Path);
                }
            }

            private void ApplyNodePosition(VisualElement node, Vector2 graphPosition)
            {
                Rect rect = ScaleRect(new Rect(graphPosition.x, graphPosition.y, GetNodeWidth(m_DraggingNodeData), GetNodeHeight()));
                node.style.left = rect.x;
                node.style.top = rect.y;
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

            private float GetNodeHeight()
            {
                return m_Mode.NodeHeight;
            }

            private float GetNodeWidth(NodeViewData nodeData)
            {
                return m_Mode.GetNodeWidth(nodeData);
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
