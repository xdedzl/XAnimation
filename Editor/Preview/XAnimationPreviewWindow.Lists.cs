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
        private void RebuildClipList()
        {
            m_ClipListView.Clear();
            m_ClipLabelMap.Clear();
            m_ClipPathLabelMap.Clear();
            m_ClipRowMap.Clear();
            m_ClipPathRowMap.Clear();
            m_ClipVisualStateMap.Clear();
            m_ClipButtonMap.Clear();
            m_CueRowMap.Clear();
            m_CueIndexRowMap.Clear();
            m_CueTimelineMarkerMap.Clear();
            SetAddClipGroupButtonEnabled(m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset);
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            IReadOnlyList<XAnimationCompiledClip> clips = m_Session.CompiledAsset.Clips;
            if (clips.Count == 0 && m_TransientClipPathKeys.Count == 0)
            {
                Label emptyLabel = new("No clips");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_ClipListView.Add(emptyLabel);
                TryBeginPendingRename();
                RefreshSearchIndex();
                return;
            }

            ClipPathNode root = BuildClipPathTree(clips);

            int rowIndex = 0;
            bool hasRootClips = root.Clips.Count > 0;
            if (hasRootClips)
            {
                VisualElement rootClipContainer = new VisualElement();
                rootClipContainer.style.marginBottom = 2;
                RegisterClipPathDropTarget(rootClipContainer, rootClipContainer, string.Empty);
                for (int i = 0; i < root.Clips.Count; i++)
                {
                    ClipPathInfo pathInfo = root.Clips[i];
                    VisualElement row = CreateClipRow(pathInfo.Clip, rowIndex++);
                    RegisterClipRowDropTarget(row, pathInfo.ClipKey, string.Empty);
                    rootClipContainer.Add(row);
                }

                m_ClipListView.Add(rootClipContainer);
            }

            for (int i = 0; i < root.Children.Count; i++)
            {
                m_ClipListView.Add(CreateClipPathGroup(root.Children[i], ref rowIndex));
            }

            if (!hasRootClips)
            {
                VisualElement rootDropZone = CreateClipRootDropZone();
                m_ClipListView.Add(rootDropZone);
            }

            TryBeginPendingRename();
            RefreshSearchIndex();
        }

        private ClipPathNode BuildClipPathTree(IReadOnlyList<XAnimationCompiledClip> clips)
        {
            ClipPathNode root = new(string.Empty, string.Empty);
            if (clips != null)
            {
                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    XAnimationCompiledClip clip = clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }

                    AddClipPathInfo(root, BuildClipPathInfo(clip));
                }
            }

            List<string> transientPaths = new(m_TransientClipPathKeys);
            transientPaths.Sort(StringComparer.Ordinal);
            for (int pathIndex = 0; pathIndex < transientPaths.Count; pathIndex++)
            {
                GetOrCreateClipPathNode(root, transientPaths[pathIndex]);
            }

            return root;
        }

        private static void AddClipPathInfo(ClipPathNode root, ClipPathInfo pathInfo)
        {
            if (root == null || pathInfo.Clip == null)
            {
                return;
            }

            ClipPathNode parent = GetOrCreateClipPathNode(root, pathInfo.ParentPath);
            parent.Clips.Add(pathInfo);
        }

        private static ClipPathNode GetOrCreateClipPathNode(ClipPathNode root, string path)
        {
            List<string> segments = SplitClipPathSegments(path);
            ClipPathNode current = root;
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? segments[i]
                    : $"{currentPath}/{segments[i]}";
                ClipPathNode child = FindClipPathChild(current, segments[i], currentPath);
                if (child == null)
                {
                    child = new ClipPathNode(segments[i], currentPath);
                    current.Children.Add(child);
                }

                current = child;
            }

            return current;
        }

        private static ClipPathNode FindClipPathChild(ClipPathNode node, string name, string fullPath)
        {
            if (node == null)
            {
                return null;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                ClipPathNode child = node.Children[i];
                if (child != null &&
                    string.Equals(child.Name, name, StringComparison.Ordinal) &&
                    string.Equals(child.FullPath, fullPath, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private VisualElement CreateClipRootDropZone()
        {
            VisualElement rootDropZone = new VisualElement();
            rootDropZone.style.minHeight = 18;
            rootDropZone.style.marginBottom = 2;
            rootDropZone.style.borderTopWidth = 1;
            rootDropZone.style.borderBottomWidth = 1;
            rootDropZone.style.borderLeftWidth = 1;
            rootDropZone.style.borderRightWidth = 1;
            rootDropZone.style.borderTopColor = SectionDivider;
            rootDropZone.style.borderBottomColor = SectionDivider;
            rootDropZone.style.borderLeftColor = SectionDivider;
            rootDropZone.style.borderRightColor = SectionDivider;
            Label dropLabel = new("Drop Here To Root");
            dropLabel.style.color = TextMuted;
            dropLabel.style.fontSize = 10;
            dropLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropLabel.style.flexGrow = 1;
            rootDropZone.Add(dropLabel);
            RegisterClipPathDropTarget(rootDropZone, rootDropZone, string.Empty);
            return rootDropZone;
        }

        private void RebuildStateList()
        {
            m_StateListView.Clear();
            m_StateLabelMap.Clear();
            m_StateGroupLabelMap.Clear();
            m_StateRowMap.Clear();
            m_BlendStateEditorMap.Clear();
            m_StateNodeUiKeysInTreeOrder.Clear();
            m_StateGroupRowMap.Clear();
            m_StateGroupHeaderMap.Clear();
            m_StateVisualStateMap.Clear();
            m_BlendSampleRowMap.Clear();
            m_ChannelTreeBlendSampleRowMap.Clear();
            m_StateButtonMap.Clear();
            m_StateChannelMap.Clear();
            m_AddStateNodeButton?.SetEnabled(false);
            if (m_Session == null || !m_Session.IsLoaded)
            {
                EnsureStateTabChannelSelection();
                RefreshGlobalBlendGraph();
                RebuildStatesGraphTabIfVisible();
                RebuildPreviewInspector();
                return;
            }

            if (!EnsureStateTabChannelSelection())
            {
                RebuildPreviewInspector();
                return;
            }

            XAnimationCompiledChannel channel = m_Session.CompiledAsset.GetChannel(m_StateTabChannelName);
            StatePathNode rootNode = new(string.Empty, string.Empty);
            AppendCompiledStateNodes(rootNode, channel.RootStateNodes);
            m_AddStateNodeButton.SetEnabled(!m_Session.IsOverrideAsset);
            m_StateListView.Add(CreateStateChannelContent(channel, rootNode));

            TryBeginPendingRename();
            RebuildAutoTransitionEditor();
            RebuildDefaultTransitionsEditor();
            RefreshSearchIndex();
            RefreshGlobalBlendGraph();
            RebuildStatesGraphTabIfVisible();
            RebuildPreviewInspector();
            ApplyPendingFocusState();
        }

        private void RebuildParameterList()
        {
            m_ParameterListView.Clear();
            m_MainParameterPreviewView?.Clear();
            m_ParameterLabelMap.Clear();
            m_ParameterRowMap.Clear();
            SetAddParameterButtonEnabled(m_Session != null && m_Session.IsLoaded && !m_Session.IsOverrideAsset);

            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            IReadOnlyList<XAnimationCompiledParameter> parameters = m_Session.CompiledAsset.Parameters;
            if (parameters.Count == 0)
            {
                Label emptyLabel = new("No parameters");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_ParameterListView.Add(emptyLabel);
                AddEmptyParameterPreviewLabel();
                return;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                m_ParameterListView.Add(CreateParameterRow(parameters[i], i));
            }

            RebuildMainParameterPreview(parameters);
            TryBeginPendingRename();
            RefreshSearchIndex();
        }

        private void RebuildAutoTransitionEditor()
        {
            if (m_AutoTransitionEditorView == null)
            {
                return;
            }

            m_AutoTransitionEditorView.Clear();
            m_AutoTransitionRowMap.Clear();
            if (m_Session == null || !m_Session.IsLoaded)
            {
                SetAutoTransitionButtonsEnabled(false);
                RefreshSearchIndex();
                return;
            }

            if (!EnsureStateTabChannelSelection())
            {
                SetAutoTransitionButtonsEnabled(false);
                RefreshSearchIndex();
                return;
            }

            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            if (CountStatesInChannel(m_StateTabChannelName) == 0)
            {
                SetAutoTransitionButtonsEnabled(false);
                Label emptyLabel = new("No states");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_AutoTransitionEditorView.Add(emptyLabel);
                RefreshSearchIndex();
                return;
            }

            IReadOnlyList<XAnimationCompiledAutoTransition> autoTransitions = m_Session.CompiledAsset.AutoTransitions;
            int transitionCount = 0;
            for (int i = 0; i < autoTransitions.Count; i++)
            {
                if (string.Equals(autoTransitions[i]?.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
                {
                    transitionCount++;
                }
            }
            if (transitionCount == 0)
            {
                m_SelectedAutoTransitionStateUiKey = string.Empty;
                SetAutoTransitionButtonsEnabled(CanAddAutoTransition());
                Label emptyLabel = new("No auto transitions");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_AutoTransitionEditorView.Add(emptyLabel);
                RefreshSearchIndex();
                return;
            }

            if (string.IsNullOrWhiteSpace(m_SelectedAutoTransitionStateUiKey) ||
                !HasAutoTransition(m_SelectedAutoTransitionStateUiKey) ||
                !TryGetCompiledStateByUiKey(m_SelectedAutoTransitionStateUiKey, out XAnimationCompiledState selectedState) ||
                !string.Equals(selectedState.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
            {
                m_SelectedAutoTransitionStateUiKey = GetDefaultAutoTransitionStateUiKey(states);
            }

            if (m_CollapsedAutoTransitionKeys.Count > 1)
            {
                string expandedStateUiKey = null;
                foreach (XAnimationCompiledAutoTransition transition in autoTransitions)
                {
                    if (transition == null)
                    {
                        continue;
                    }

                    string stateUiKey = BuildAutoTransitionUiKey(transition);
                    if (m_CollapsedAutoTransitionKeys.Contains(stateUiKey))
                    {
                        expandedStateUiKey = stateUiKey;
                        break;
                    }
                }

                m_CollapsedAutoTransitionKeys.Clear();
                if (!string.IsNullOrWhiteSpace(expandedStateUiKey))
                {
                    m_CollapsedAutoTransitionKeys.Add(expandedStateUiKey);
                }
            }

            SetAutoTransitionButtonsEnabled(CanAddAutoTransition());

            int renderedCount = 0;
            for (int i = 0; i < autoTransitions.Count; i++)
            {
                XAnimationCompiledAutoTransition transition = autoTransitions[i];
                if (transition != null && string.Equals(transition.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
                {
                    m_AutoTransitionEditorView.Add(CreateAutoTransitionEditor(transition));
                    renderedCount++;
                }
            }

            if (renderedCount == 0)
            {
                m_SelectedAutoTransitionStateUiKey = string.Empty;
                Label emptyLabel = new("No auto transitions");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_AutoTransitionEditorView.Add(emptyLabel);
            }

            RefreshSearchIndex();
        }

        private void ScheduleAutoTransitionEditorRebuild()
        {
            if (rootVisualElement == null)
            {
                RebuildAutoTransitionEditor();
                return;
            }

            rootVisualElement.schedule.Execute(RebuildAutoTransitionEditor).StartingIn(0);
        }

        private void RebuildDefaultTransitionsEditor()
        {
            if (m_DefaultTransitionsEditorView == null)
            {
                RebuildStatesGraphTabIfVisible();
                return;
            }

            m_DefaultTransitionsEditorView.Clear();
            m_DefaultTransitionRowMap.Clear();
            if (m_Session == null || !m_Session.IsLoaded)
            {
                SetDefaultTransitionButtonsEnabled(false);
                RebuildStatesGraphTabIfVisible();
                RefreshSearchIndex();
                return;
            }

            if (!EnsureStateTabChannelSelection())
            {
                SetDefaultTransitionButtonsEnabled(false);
                RebuildStatesGraphTabIfVisible();
                RefreshSearchIndex();
                return;
            }

            bool hasEnoughStates = CountStatesInChannel(m_StateTabChannelName) >= 2;
            SetDefaultTransitionButtonsEnabled(!m_Session.IsOverrideAsset && hasEnoughStates);

            IReadOnlyList<XAnimationCompiledDefaultTransition> defaultTransitions = m_Session.CompiledAsset.DefaultTransitions;
            List<int> transitionIndices = new();
            for (int i = 0; i < defaultTransitions.Count; i++)
            {
                if (string.Equals(defaultTransitions[i]?.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
                {
                    transitionIndices.Add(i);
                }
            }

            if (transitionIndices.Count == 0)
            {
                m_SelectedDefaultTransitionIndex = -1;
                Label emptyLabel = new(hasEnoughStates ? "No default transitions" : "Default transitions require at least two states");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_DefaultTransitionsEditorView.Add(emptyLabel);
                RebuildStatesGraphTabIfVisible();
                RefreshSearchIndex();
                return;
            }

            if (!transitionIndices.Contains(m_SelectedDefaultTransitionIndex))
            {
                m_SelectedDefaultTransitionIndex = transitionIndices[0];
            }

            int expandedIndex = -1;
            for (int i = 0; i < transitionIndices.Count; i++)
            {
                int transitionIndex = transitionIndices[i];
                if (!m_CollapsedDefaultTransitionIndices.Contains(transitionIndex))
                {
                    if (expandedIndex < 0)
                    {
                        expandedIndex = transitionIndex;
                    }
                    else
                    {
                        m_CollapsedDefaultTransitionIndices.Add(transitionIndex);
                    }
                }
            }

            for (int i = 0; i < transitionIndices.Count; i++)
            {
                int transitionIndex = transitionIndices[i];
                m_DefaultTransitionsEditorView.Add(CreateDefaultTransitionEditor(transitionIndex, defaultTransitions[transitionIndex].Config));
            }

            RebuildStatesGraphTabIfVisible();
            RefreshSearchIndex();
        }

        private void ScheduleDefaultTransitionsEditorRebuild()
        {
            if (rootVisualElement == null)
            {
                RebuildDefaultTransitionsEditor();
                return;
            }

            rootVisualElement.schedule.Execute(RebuildDefaultTransitionsEditor).StartingIn(0);
        }

        private string GetDefaultAutoTransitionStateUiKey(IReadOnlyList<XAnimationCompiledState> states)
        {
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledAutoTransition> autoTransitions = m_Session.CompiledAsset.AutoTransitions;
                for (int i = 0; i < autoTransitions.Count; i++)
                {
                    XAnimationCompiledAutoTransition transition = autoTransitions[i];
                    if (transition != null &&
                        string.Equals(transition.ChannelName, m_StateTabChannelName, StringComparison.Ordinal) &&
                        m_Session.CompiledAsset.TryGetStateIndex(transition.ChannelName, transition.PreStateKey, out _))
                    {
                        return BuildAutoTransitionUiKey(transition);
                    }
                }
            }

            for (int i = 0; i < states.Count; i++)
            {
                if (string.Equals(states[i].ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
                {
                    return BuildStateUiKey(states[i]);
                }
            }

            return string.Empty;
        }

        private bool HasAutoTransition(string stateUiKey)
        {
            if (m_Session == null ||
                !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(stateUiKey) ||
                !TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState state))
            {
                return false;
            }

            return m_Session.CompiledAsset.TryGetAutoTransition(state.ChannelName, state.Key, out _);
        }

        private bool CanAddAutoTransition()
        {
            if (m_Session == null || !m_Session.IsLoaded || m_Session.IsOverrideAsset)
            {
                return false;
            }

            int eligibleStateCount = 0;
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (string.Equals(states[i].ChannelName, m_StateTabChannelName, StringComparison.Ordinal) &&
                    !states[i].Config.loop)
                {
                    eligibleStateCount++;
                }
            }

            int transitionCount = 0;
            IReadOnlyList<XAnimationCompiledAutoTransition> transitions = m_Session.CompiledAsset.AutoTransitions;
            for (int i = 0; i < transitions.Count; i++)
            {
                if (string.Equals(transitions[i]?.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
                {
                    transitionCount++;
                }
            }

            return transitionCount < eligibleStateCount;
        }

        private VisualElement CreateParameterRow(XAnimationCompiledParameter parameter, int rowIndex)
        {
            XAnimationParameterConfig config = parameter.Config;
            VisualElement container = CreateInteractiveRowContainer(rowIndex);
            VisualElement row = CreateRowContent();
            container.Add(row);

            string parameterName = parameter.Name;
            m_ParameterRowMap[parameterName] = container;
            EditableLabel label = new(parameterName);
            ConfigureEditableNameLabel(label, 112f);
            label.tooltip = "右键 Rename 编辑参数名。";
            label.SetEditable(true, EditableLabelEditTrigger.ContextMenu);
            label.EditStarted += BeginNameEdit;
            label.EditEnded += EndNameEdit;
            label.ValueCommitted += (_, newValue) => RenameParameter(parameterName, newValue, label);
            m_ParameterLabelMap[parameterName] = label;
            row.Add(label);

            List<string> typeNames = new(Enum.GetNames(typeof(XAnimationParameterType)));
            DropdownField typeField = new(
                typeNames,
                Mathf.Max(0, typeNames.IndexOf(config.type.ToString())));
            ApplyDropdownFieldStyle(typeField);
            typeField.tooltip = "参数类型。Blend1D 和 2D directional blend 只能绑定 Float 参数。";
            typeField.style.width = 88;
            typeField.style.marginLeft = 4;
            typeField.RegisterValueChangedCallback(evt => ChangeParameterType(parameterName, evt.newValue, evt.previousValue, typeField));
            row.Add(typeField);

            VisualElement valueField = CreateParameterDefaultValueField(parameterName, config);
            valueField.style.flexGrow = 1;
            valueField.style.marginLeft = 6;
            row.Add(valueField);

            Button deleteButton = new(() => DeleteParameter(parameterName))
            {
                text = "⌫"
            };
            deleteButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "Override 资源不能删除 parameter。"
                : "删除这个 parameter。";
            deleteButton.SetEnabled(m_Session != null && !m_Session.IsOverrideAsset);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;
            row.Add(deleteButton);

            return container;
        }

        private void RebuildMainParameterPreview(IReadOnlyList<XAnimationCompiledParameter> parameters)
        {
            if (m_MainParameterPreviewView == null)
            {
                return;
            }

            m_MainParameterPreviewView.Clear();
            bool hasPreviewControl = false;
            for (int i = 0; i < parameters.Count; i++)
            {
                VisualElement previewEditor = CreateParameterPreviewEditor(parameters[i]);
                if (previewEditor == null)
                {
                    continue;
                }

                hasPreviewControl = true;
                m_MainParameterPreviewView.Add(previewEditor);
            }

            if (!hasPreviewControl)
            {
                AddEmptyParameterPreviewLabel();
            }
        }

        private void AddEmptyParameterPreviewLabel()
        {
            if (m_MainParameterPreviewView == null)
            {
                return;
            }

            Label emptyLabel = new("No preview parameters");
            emptyLabel.style.color = TextMuted;
            emptyLabel.style.fontSize = BodyFontSize;
            emptyLabel.style.marginLeft = 4;
            m_MainParameterPreviewView.Add(emptyLabel);
        }

        private VisualElement CreateParameterDefaultValueField(string parameterName, XAnimationParameterConfig config)
        {
            switch (config.type)
            {
                case XAnimationParameterType.Float:
                {
                    FloatField field = new("default")
                    {
                        value = ConvertParameterDefaultToFloat(config.defaultValue)
                    };
                    field.tooltip = "Float 参数默认值，会保存到资源。";
                    field.RegisterValueChangedCallback(evt => ChangeParameterDefaultValue(parameterName, evt.newValue));
                    return field;
                }
                case XAnimationParameterType.Bool:
                {
                    Toggle toggle = new("default")
                    {
                        value = ConvertParameterDefaultToBool(config.defaultValue)
                    };
                    toggle.tooltip = "Bool 参数默认值，会保存到资源。";
                    toggle.RegisterValueChangedCallback(evt => ChangeParameterDefaultValue(parameterName, evt.newValue));
                    return toggle;
                }
                case XAnimationParameterType.Int:
                {
                    IntegerField field = new("default")
                    {
                        value = ConvertParameterDefaultToInt(config.defaultValue)
                    };
                    field.tooltip = "Int 参数默认值，会保存到资源。";
                    field.RegisterValueChangedCallback(evt => ChangeParameterDefaultValue(parameterName, evt.newValue));
                    return field;
                }
                case XAnimationParameterType.String:
                {
                    TextField field = new("default")
                    {
                        value = ConvertParameterDefaultToString(config.defaultValue)
                    };
                    field.tooltip = "String 参数默认值，会保存到资源。";
                    field.RegisterValueChangedCallback(evt => ChangeParameterDefaultValue(parameterName, evt.newValue));
                    return field;
                }
                case XAnimationParameterType.Trigger:
                default:
                {
                    Label label = new("Trigger has no default value");
                    label.style.color = TextMuted;
                    label.style.fontSize = BodyFontSize;
                    return label;
                }
            }
        }

        private VisualElement CreateStateChannelContent(XAnimationCompiledChannel channel, StatePathNode rootNode)
        {
            VisualElement statesContainer = new();
            statesContainer.style.minHeight = 18;
            RegisterStateChannelDropTarget(statesContainer, statesContainer, channel.Name, string.Empty);
            int rowIndex = 0;
            for (int i = 0; i < rootNode.Children.Count; i++)
            {
                statesContainer.Add(CreateStatePathGroup(channel.Name, rootNode.Children[i], ref rowIndex));
            }

            for (int i = 0; i < rootNode.States.Count; i++)
            {
                XAnimationCompiledState state = rootNode.States[i];
                VisualElement row = CreateStateRow(state, rowIndex++);
                RegisterStateRowDropTarget(row, channel.Name, state.Key, string.Empty);
                statesContainer.Add(row);
            }

            if (rowIndex == 0 && rootNode.Children.Count == 0)
            {
                Label emptyLabel = new("No states");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                statesContainer.Add(emptyLabel);
            }

            return statesContainer;
        }

        private VisualElement CreateStatePathGroup(string channelName, StatePathNode node, ref int rowIndex)
        {
            VisualElement group = CreateNestedListGroup();
            string groupKey = BuildStateGroupKey(channelName, node.FullPath);
            string nodeUiKey = BuildStateUiKey(channelName, node.FullPath);
            m_StateGroupRowMap[groupKey] = group;
            m_StateNodeUiKeysInTreeOrder.Add(nodeUiKey);

            VisualElement header = CreateListHeader();
            m_StateGroupHeaderMap[nodeUiKey] = header;
            Label foldoutLabel = CreateFoldoutGlyph(!IsStateGroupCollapsed(groupKey));
            header.Add(foldoutLabel);

            if (IsSelectorKind(node.Kind))
            {
                Label selectorMarker = CreateSmallInfoLabel("◆");
                selectorMarker.tooltip = "Selector State Node";
                selectorMarker.style.color = new Color(0.58f, 0.78f, 0.42f, 1f);
                header.Add(selectorMarker);
            }
            EditableLabel groupLabel = new(node.Name);
            ConfigureEditableNameLabel(groupLabel, 180f);
            groupLabel.tooltip = $"{FormatStateDisplayPath(node.FullPath)}\nKind: {node.Kind}\n单击选中并在右侧 Inspector 编辑；右键 Rename 编辑路径层级。";
            groupLabel.SetEditable(!m_Session.IsOverrideAsset, EditableLabelEditTrigger.ContextMenu);
            groupLabel.EditStarted += BeginNameEdit;
            groupLabel.EditEnded += EndNameEdit;
            groupLabel.ValueCommitted += (_, newValue) =>
                RenameStateGroup(channelName, node.FullPath, BuildRenamedStateNodePath(node.FullPath, newValue), groupLabel);
            RegisterStateGroupContextMenu(groupLabel, channelName, node.FullPath);
            m_StateGroupLabelMap[groupKey] = groupLabel;
            header.Add(groupLabel);

            VisualElement spacer = new();
            spacer.style.flexGrow = 1;
            header.Add(spacer);

            string infoText = IsSelectorKind(node.Kind)
                ? $"{GetSelectorKindLabel(node.Kind)} · {node.SelectorParameterName} · {node.Children.Count + node.States.Count} 分支"
                : $"Normal · {CountStatePathNodeStates(node)} states";
            Label info = CreateSmallInfoLabel(infoText);
            header.Add(info);

            Button addNodeButton = new() { text = "+" };
            addNodeButton.clicked += () => ShowAddStateNodeMenu(addNodeButton, channelName, node.FullPath);
            addNodeButton.tooltip = m_Session.IsOverrideAsset
                ? "Override 资源不能新增 State Node。"
                : "选择要在这个 Node 下新增的 Node 类型。";
            addNodeButton.SetEnabled(!m_Session.IsOverrideAsset);
            ApplyClipIconButtonStyle(addNodeButton, AccentColor);
            addNodeButton.style.marginLeft = 4;
            header.Add(addNodeButton);

            VisualElement foldoutHitArea = new();
            foldoutHitArea.tooltip = "展开/收起这个 State Node。";
            foldoutHitArea.style.position = Position.Absolute;
            foldoutHitArea.style.left = 0;
            foldoutHitArea.style.top = 0;
            foldoutHitArea.style.bottom = 0;
            foldoutHitArea.style.width = FoldoutGlyphWidth + 12f;
            header.Add(foldoutHitArea);
            group.Add(header);
            ApplyStateGroupSelectionVisualState(nodeUiKey);

            RegisterStateChannelDropTarget(group, header, channelName, node.FullPath);

            VisualElement content = new VisualElement();
            content.style.display = IsStateGroupCollapsed(groupKey) ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < node.Children.Count; i++)
            {
                content.Add(CreateStatePathGroup(channelName, node.Children[i], ref rowIndex));
            }

            for (int i = 0; i < node.States.Count; i++)
            {
                XAnimationCompiledState state = node.States[i];
                VisualElement row = CreateStateRow(state, rowIndex++);
                RegisterStateRowDropTarget(row, channelName, state.Key, node.FullPath);
                content.Add(row);
            }

            void Toggle()
            {
                bool expanded = content.style.display != DisplayStyle.None;
                content.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                SetFoldoutGlyphText(foldoutLabel, !expanded);
                SetStateGroupCollapsed(groupKey, expanded);
            }

            foldoutHitArea.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                Toggle();
                evt.StopImmediatePropagation();
            });
            addNodeButton.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || groupLabel.IsEditing)
                {
                    return;
                }

                SelectPreviewInspectorStateNode(nodeUiKey);
                rootVisualElement.Focus();
                evt.StopPropagation();
            });

            group.Add(content);
            return group;
        }

        private static void AppendCompiledStateNodes(
            StatePathNode parent,
            IReadOnlyList<XAnimationCompiledStateNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationCompiledStateNode node = nodes[i];
                if (node is XAnimationCompiledState state)
                {
                    parent.States.Add(state);
                    continue;
                }

                string selectorParameterName = GetSelectorParameterName(node);
                StatePathNode child = new(node.Name, node.Key, node.Kind, selectorParameterName);
                parent.Children.Add(child);
                AppendCompiledStateNodes(child, node.Children);
            }
        }

        private static bool IsSelectorKind(XAnimationStateNodeKind kind)
        {
            return kind == XAnimationStateNodeKind.Selector ||
                   kind == XAnimationStateNodeKind.IntSelector ||
                   kind == XAnimationStateNodeKind.StringSelector;
        }

        private static string GetSelectorKindLabel(XAnimationStateNodeKind kind)
        {
            return kind switch
            {
                XAnimationStateNodeKind.Selector => "Index Selector",
                XAnimationStateNodeKind.IntSelector => "Int Selector",
                XAnimationStateNodeKind.StringSelector => "String Selector",
                _ => kind.ToString(),
            };
        }

        private static string GetSelectorParameterName(XAnimationCompiledStateNode node)
        {
            return node switch
            {
                XAnimationCompiledSelectorStateNode selector => selector.Config.parameterName,
                XAnimationCompiledIntSelectorStateNode selector => selector.Config.parameterName,
                XAnimationCompiledStringSelectorStateNode selector => selector.Config.parameterName,
                _ => string.Empty,
            };
        }

        private static int CountStatePathNodeStates(StatePathNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int count = node.States.Count;
            for (int i = 0; i < node.Children.Count; i++)
            {
                count += CountStatePathNodeStates(node.Children[i]);
            }

            return count;
        }

        private static int CountContainerStateNodes(StatePathNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int count = string.IsNullOrWhiteSpace(node.FullPath) ? 0 : 1;
            for (int i = 0; i < node.Children.Count; i++)
            {
                count += CountContainerStateNodes(node.Children[i]);
            }

            return count;
        }

        private static string BuildRenamedStateNodePath(string oldPath, string newLeafName)
        {
            string normalizedNewLeafName = NormalizeStatePath(newLeafName);
            if (string.IsNullOrWhiteSpace(normalizedNewLeafName))
            {
                return string.Empty;
            }

            string parentPath = GetStatePathParent(oldPath);
            return string.IsNullOrWhiteSpace(parentPath)
                ? normalizedNewLeafName
                : $"{parentPath}/{normalizedNewLeafName}";
        }

        private VisualElement CreateClipPathGroup(ClipPathNode node, ref int rowIndex)
        {
            VisualElement group = CreateNestedListGroup();
            string groupKey = BuildClipPathKey(node.FullPath);
            m_ClipPathRowMap[groupKey] = group;

            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(!IsClipPathCollapsed(groupKey));
            header.Add(foldoutLabel);

            EditableLabel groupLabel = new(node.Name);
            m_ClipPathLabelMap[node.FullPath] = groupLabel;
            ConfigureEditableNameLabel(groupLabel, 180f);
            groupLabel.tooltip = $"{FormatClipDisplayPath(node.FullPath)}\n单击展开/收起这个 clip folder；右键 Rename 编辑路径层级。";
            groupLabel.SetEditable(!m_Session.IsOverrideAsset, EditableLabelEditTrigger.ContextMenu);
            groupLabel.EditStarted += BeginNameEdit;
            groupLabel.EditEnded += EndNameEdit;
            groupLabel.ValueCommitted += (_, newValue) =>
                RenameClipPath(node.FullPath, BuildRenamedClipFolderPath(node.FullPath, newValue), groupLabel);
            RegisterClipPathContextMenu(groupLabel, node.FullPath);
            header.Add(groupLabel);

            VisualElement spacer = new();
            spacer.style.flexGrow = 1;
            header.Add(spacer);

            int clipCount = CountClipPathNodeClips(node);
            Label info = CreateSmallInfoLabel($"{clipCount} clips");
            header.Add(info);

            Button addClipButton = new()
            {
                text = "+"
            };
            addClipButton.clicked += () => ShowAddClipMenu(addClipButton, node.FullPath);
            addClipButton.tooltip = m_Session.IsOverrideAsset
                ? "Override 资源不能新增 Clip 或 Group。"
                : "在这个 Group 下新增 Clip 或子 Group。";
            addClipButton.SetEnabled(!m_Session.IsOverrideAsset);
            ApplyClipIconButtonStyle(addClipButton, AccentColor);
            addClipButton.style.marginLeft = 4;
            header.Add(addClipButton);
            group.Add(header);

            RegisterClipPathDropTarget(group, header, node.FullPath);
            RegisterProjectClipDropTarget(group, node.FullPath, header);

            VisualElement content = new VisualElement();
            content.style.display = IsClipPathCollapsed(groupKey) ? DisplayStyle.None : DisplayStyle.Flex;
            for (int i = 0; i < node.Children.Count; i++)
            {
                content.Add(CreateClipPathGroup(node.Children[i], ref rowIndex));
            }

            for (int i = 0; i < node.Clips.Count; i++)
            {
                ClipPathInfo pathInfo = node.Clips[i];
                VisualElement row = CreateClipRow(pathInfo.Clip, rowIndex++);
                RegisterClipRowDropTarget(row, pathInfo.ClipKey, node.FullPath);
                content.Add(row);
            }

            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || groupLabel.IsEditing)
                {
                    return;
                }

                bool expanded = content.style.display != DisplayStyle.None;
                content.style.display = expanded ? DisplayStyle.None : DisplayStyle.Flex;
                SetFoldoutGlyphText(foldoutLabel, !expanded);
                SetClipPathCollapsed(groupKey, expanded);
                evt.StopPropagation();
            });

            group.Add(content);
            return group;
        }

        private static int CountClipPathNodeClips(ClipPathNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int count = node.Clips.Count;
            for (int i = 0; i < node.Children.Count; i++)
            {
                count += CountClipPathNodeClips(node.Children[i]);
            }

            return count;
        }

        private static string BuildRenamedClipFolderPath(string oldPath, string newLeafName)
        {
            string normalizedNewLeafName = NormalizeClipPathKey(newLeafName);
            if (string.IsNullOrWhiteSpace(normalizedNewLeafName))
            {
                return string.Empty;
            }

            string parentPath = GetClipPathParent(oldPath);
            return string.IsNullOrWhiteSpace(parentPath)
                ? normalizedNewLeafName
                : $"{parentPath}/{normalizedNewLeafName}";
        }

        private VisualElement CreateStateRow(XAnimationCompiledState state, int rowIndex)
        {
            string stateUiKey = BuildStateUiKey(state);
            VisualElement wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.marginBottom = 1;

            VisualElement container = CreateInteractiveRowContainer(rowIndex);
            Color baseColor = rowIndex % 2 == 0 ? ListRowEvenBg : ListRowOddBg;
            m_StateRowMap[stateUiKey] = container;
            m_StateNodeUiKeysInTreeOrder.Add(stateUiKey);
            m_StateChannelMap[stateUiKey] = state.ChannelName;

            VisualElement progressFill = CreateRowProgressFill();
            container.Add(progressFill);

            RowVisualState visualState = new()
            {
                BaseColor = baseColor,
                ProgressFill = progressFill,
            };
            m_StateVisualStateMap[stateUiKey] = visualState;
            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                visualState.Hovered = true;
                ApplyStateRowVisualState(stateUiKey);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                visualState.Hovered = false;
                ApplyStateRowVisualState(stateUiKey);
            });

            VisualElement row = CreateRowContent();
            container.Add(row);

            VisualElement summaryRow = new VisualElement();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.flexGrow = 1;
            summaryRow.style.flexShrink = 1;
            summaryRow.style.minWidth = 0;
            row.Add(summaryRow);

            string stateKey = state.Key;
            bool isBlendState = IsBlendStateType(state.Config.stateType);
            EditableLabel label = new(GetStatePathLeafName(stateKey));
            ConfigureEditableNameLabel(label, 78f);
            label.tooltip = isBlendState
                ? $"完整路径：{FormatStateDisplayPath(stateKey)}；单击选中，再次单击已选中的节点展开动画列表。"
                : string.IsNullOrWhiteSpace(GetStatePathParent(stateKey))
                    ? "单击选中并在右侧 Inspector 编辑；右键可 Rename，也可批量修改这个 state 用到的动画。"
                    : $"完整路径：{FormatStateDisplayPath(stateKey)}；单击在右侧 Inspector 编辑。";
            label.SetEditable(true, EditableLabelEditTrigger.None);
            label.EditStarted += BeginNameEdit;
            label.EditEnded += EndNameEdit;
            label.ValueCommitted += (_, newValue) => RenameState(state.ChannelName, stateKey, newValue, label);
            m_StateLabelMap[stateUiKey] = label;
            label.style.position = Position.Relative;
            RegisterStateLabelContextMenu(label, state);
            summaryRow.Add(label);

            List<string> stateTypeNames = new(Enum.GetNames(typeof(XAnimationStateType)));
            DropdownField stateTypeField = new(
                string.Empty,
                stateTypeNames,
                Mathf.Max(0, stateTypeNames.IndexOf(state.Config.stateType.ToString())));
            ApplyDropdownFieldStyle(stateTypeField);
            stateTypeField.style.width = 180;
            stateTypeField.style.minWidth = 120;
            stateTypeField.style.flexGrow = 2;
            stateTypeField.style.flexShrink = 1;
            stateTypeField.style.marginLeft = 6;
            stateTypeField.style.position = Position.Relative;
            stateTypeField.tooltip = "State 类型。";
            stateTypeField.RegisterValueChangedCallback(evt =>
            {
                if (!Enum.TryParse(evt.newValue, out XAnimationStateType stateType))
                {
                    return;
                }

                ChangeStateType(state.ChannelName, state.Key, stateType, evt.previousValue, stateTypeField);
            });
            summaryRow.Add(stateTypeField);

            if (state.Config.stateType == XAnimationStateType.Single)
            {
                XAnimationEditorSelectionField clipField = CreateClipSelectionField(string.Empty, state.Config.clipKey);
                clipField.style.width = 220;
                clipField.style.minWidth = 160;
                clipField.style.flexGrow = 3;
                clipField.style.flexShrink = 1;
                clipField.style.marginLeft = 6;
                clipField.style.position = Position.Relative;
                clipField.tooltip = "Single state 播放的 clipKey。";
                clipField.ValueChanged += (previousValue, newValue) => ChangeStateClipKey(state.ChannelName, state.Key, newValue, clipField, previousValue);
                AttachClipKeyPingButton(clipField, state.Config.clipKey, enabled: true);
                summaryRow.Add(clipField);
            }

            VisualElement blendEditor = null;
            if (isBlendState)
            {
                blendEditor = CreateChannelTreeBlendAnimationList(state);
                blendEditor.style.display = string.Equals(m_ExpandedBlendStateUiKey, stateUiKey, StringComparison.Ordinal)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                m_BlendStateEditorMap[stateUiKey] = blendEditor;
            }

            RegisterStateNameInteractions(label, blendEditor, stateUiKey, stateKey);

            VisualElement actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.alignItems = Align.Center;
            actionsRow.style.flexShrink = 0;
            actionsRow.style.marginLeft = 6;
            row.Add(actionsRow);

            Button playButton = new(() => ToggleStatePlayback(state))
            {
                text = "▶"
            };
            playButton.tooltip = "播放或暂停这个 state。Blend1D 和 2D directional blend 会读取绑定参数实时混合。";
            ApplyClipButtonStyle(playButton, false);
            playButton.style.flexShrink = 0;
            playButton.style.position = Position.Relative;
            actionsRow.Add(playButton);
            m_StateButtonMap[stateUiKey] = playButton;

            Button deleteButton = new(() => DeleteState(state.ChannelName, stateKey))
            {
                text = "⌫"
            };
            deleteButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "Override 资源不能删除 state。"
                : "删除这个 state。";
            deleteButton.SetEnabled(m_Session != null && !m_Session.IsOverrideAsset);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.flexShrink = 0;
            deleteButton.style.marginLeft = 3;
            deleteButton.style.position = Position.Relative;
            actionsRow.Add(deleteButton);

            wrapper.Add(container);
            if (blendEditor != null)
            {
                wrapper.Add(blendEditor);
            }
            ApplyStateRowVisualState(stateUiKey);
            return wrapper;
        }

        private VisualElement CreateChannelTreeBlendAnimationList(XAnimationCompiledState state)
        {
            VisualElement list = CreateFoldoutRowEditor();
            XAnimationStateConfig config = state.Config;
            if (config.stateType == XAnimationStateType.Blend1D)
            {
                XAnimationBlend1DSampleConfig[] samples = config.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
                for (int i = 0; i < samples.Length; i++)
                {
                    int sampleIndex = i;
                    XAnimationBlend1DSampleConfig sample = samples[i];
                    float threshold = sample?.threshold ?? 0f;
                    list.Add(CreateChannelTreeBlendAnimationRow(
                        state.ChannelName,
                        state.Key,
                        sampleIndex,
                        sample?.clipKey,
                        () => PreviewBlendSample(state.ChannelName, state.Key, threshold)));
                }
            }
            else
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                    config.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                for (int i = 0; i < samples.Length; i++)
                {
                    int sampleIndex = i;
                    XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[i];
                    float positionX = sample?.positionX ?? 0f;
                    float positionY = sample?.positionY ?? 0f;
                    list.Add(CreateChannelTreeBlendAnimationRow(
                        state.ChannelName,
                        state.Key,
                        sampleIndex,
                        sample?.clipKey,
                        () => PreviewDirectionalBlendSample(state.ChannelName, state.Key, positionX, positionY)));
                }
            }

            return list;
        }

        private VisualElement CreateChannelTreeBlendAnimationRow(
            string channelName,
            string stateKey,
            int sampleIndex,
            string clipKey,
            Action preview)
        {
            VisualElement row = CreateSubBox();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 1;
            row.style.position = Position.Relative;
            row.style.overflow = Overflow.Hidden;

            VisualElement weightFill = CreateProgressFill(BlendWeightFillBg);
            row.Add(weightFill);
            m_ChannelTreeBlendSampleRowMap[BuildBlendSampleRuntimeKey(channelName, stateKey, sampleIndex)] = new RowVisualState
            {
                BaseColor = new Color(0.14f, 0.14f, 0.15f, 1f),
                ProgressFill = weightFill,
            };

            Label indexLabel = new($"#{sampleIndex}");
            indexLabel.style.width = 28;
            indexLabel.style.flexShrink = 0;
            indexLabel.style.color = TextMuted;
            indexLabel.style.fontSize = BodyFontSize;
            indexLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexLabel.style.position = Position.Relative;
            row.Add(indexLabel);

            Label clipLabel = new(string.IsNullOrWhiteSpace(clipKey) ? "(Empty)" : clipKey);
            clipLabel.tooltip = clipKey;
            clipLabel.style.flexGrow = 1;
            clipLabel.style.flexShrink = 1;
            clipLabel.style.minWidth = 0;
            clipLabel.style.color = string.IsNullOrWhiteSpace(clipKey) ? TextMuted : TextNormal;
            clipLabel.style.fontSize = BodyFontSize;
            clipLabel.style.position = Position.Relative;
            row.Add(clipLabel);

            bool canLocate = HasClipAsset(clipKey);
            Button clipItemButton = CreateEmbeddedDropdownButton(
                "↗",
                "定位到 Clips 面板里当前动画对应的条目。",
                canLocate,
                () => FocusClipInInspector(clipKey),
                marginLeft: 4,
                marginRight: 2);
            row.Add(clipItemButton);

            Button pingButton = CreateEmbeddedDropdownButton(
                "◎",
                "定位当前动画对应的 AnimationClip 资源。",
                canLocate,
                () => PingClipAsset(clipKey),
                marginLeft: 2,
                marginRight: 2);
            row.Add(pingButton);

            Button previewButton = new(preview)
            {
                text = "▶"
            };
            previewButton.tooltip = "播放这个 Blend State 采样动画。";
            ApplyClipButtonStyle(previewButton, false);
            previewButton.style.marginLeft = 2;
            previewButton.style.position = Position.Relative;
            row.Add(previewButton);
            return row;
        }

        private static string GetStatePrimaryClipKey(XAnimationCompiledState state)
        {
            return state switch
            {
                XAnimationCompiledSingleState => state.Config.clipKey,
                XAnimationCompiledBlend1DState blendState when blendState.Samples.Count > 0 => blendState.Samples[0].Config.clipKey,
                XAnimationCompiledBlend2DSimpleDirectionalState directionalState when directionalState.Samples.Count > 0 => directionalState.Samples[0].Config.clipKey,
                XAnimationCompiledBlend2DFreeformDirectionalState directionalState when directionalState.Samples.Count > 0 => directionalState.Samples[0].Config.clipKey,
                _ => null,
            };
        }

        private void ToggleStatePlayback(XAnimationCompiledState state)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            MarkFreeformStateInteracted(state.ChannelName, state.Key);

            XAnimationChannelState channelState = m_Session.GetChannelState(state.ChannelName);
            bool isPlaying = channelState != null && string.Equals(channelState.stateKey, state.Key, StringComparison.Ordinal);
            if (isPlaying)
            {
                SetPlaybackTargetChannel(state.ChannelName);
                SetPlaybackPaused(!m_IsPaused);
                SetPauseButtonState(true, m_IsPaused);
                SetStepForwardButtonEnabled(true);
                RefreshPlaybackViews();
                SetStatus(m_IsPaused ? $"已暂停 state {state.Key}。" : $"已继续 state {state.Key}。");
                return;
            }

            ResumePlaybackChannel(state.ChannelName);
            SetPauseButtonState(true, false);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());
            PlayPreviewState(state.ChannelName, state.Key, BuildPreviewTransitionOptions());
            RefreshPlaybackViews();
            SetStatus($"正在播放 state {state.Key}。");
        }

        private void PreviewBlendSample(string channelName, string stateKey, float threshold)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);

            XAnimationCompiledState state = m_Session.CompiledAsset.GetState(channelName, stateKey);
            if (state is not XAnimationCompiledBlend1DState blendState)
            {
                return;
            }

            string parameterName = blendState.Config.parameterName;
            if (!string.IsNullOrWhiteSpace(parameterName))
            {
                TrySetPreviewParameter(parameterName, threshold);
            }

            PlayStateForSamplePreview(blendState, $"正在预览 {stateKey} sample，{parameterName} = {threshold:0.###}。");
        }

        private void PreviewDirectionalBlendSample(string channelName, string stateKey, float positionX, float positionY)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            MarkFreeformStateInteracted(channelName, stateKey);

            XAnimationCompiledState state = m_Session.CompiledAsset.GetState(channelName, stateKey);
            if (!TryGetDirectionalBlendSamples(state, out _))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(state.Config.parameterXName))
            {
                TrySetPreviewParameter(state.Config.parameterXName, positionX);
            }

            if (!string.IsNullOrWhiteSpace(state.Config.parameterYName))
            {
                TrySetPreviewParameter(state.Config.parameterYName, positionY);
            }

            PlayStateForSamplePreview(
                state,
                $"正在预览 {stateKey} sample，({state.Config.parameterXName}, {state.Config.parameterYName}) = ({positionX:0.###}, {positionY:0.###})。");
        }

        private void PlayStateForSamplePreview(XAnimationCompiledState state, string statusMessage)
        {
            if (m_Session == null || !m_Session.IsLoaded || state == null)
            {
                return;
            }

            MarkFreeformStateInteracted(state.ChannelName, state.Key);

            ResumePlaybackChannel(state.ChannelName);
            SetPauseButtonState(true, false);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());
            PlayPreviewState(state.ChannelName, state.Key, BuildPreviewTransitionOptions());

            RefreshPlaybackViews();
            SetStatus(statusMessage);
        }

        private VisualElement CreateCueRow(int cueIndex, XAnimationCueConfig cue, bool editable)
        {
            return CreateCueRow(new DisplayedCueEntry(
                cueIndex,
                cue != null ? cue.time : 0f,
                cue?.eventKey,
                cue?.payload,
                false), editable);
        }

        private VisualElement CreateCueRow(DisplayedCueEntry cue, bool editable)
        {
            return CreateCueRow(cue, editable, out _);
        }

        private VisualElement CreateCueRow(DisplayedCueEntry cue, bool editable, out FloatField timeField)
        {
            VisualElement row = CreateSubBox();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 1;
            row.tooltip = cue.IsReadOnlyDerived
                ? "这个 Cue 由 AnimationClip 上的 Animation Event 自动派生，只读显示。"
                : editable
                    ? "Cue 会在对应 clip 播放经过 normalized time 时触发。"
                    : "Override 资源只能预览 cue，不能编辑 base cue 配置。";
            row.userData = cue;

            VisualElement topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.minWidth = 0;
            topRow.style.marginBottom = 1;
            row.Add(topRow);

            Label indexLabel = new(cue.IsReadOnlyDerived ? "evt" : $"#{cue.CueIndex}");
            indexLabel.style.width = 28;
            indexLabel.style.flexShrink = 0;
            indexLabel.style.color = TextMuted;
            indexLabel.style.fontSize = BodyFontSize;
            indexLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            topRow.Add(indexLabel);

            FloatField localTimeField = new("time")
            {
                value = cue.Time
            };
            localTimeField.tooltip = "Cue 触发时间，范围是 clip normalized time [0, 1]。";
            localTimeField.SetEnabled(editable && !cue.IsReadOnlyDerived);
            localTimeField.style.flexGrow = 1;
            localTimeField.style.flexShrink = 1;
            localTimeField.style.flexBasis = 0;
            localTimeField.style.minWidth = 0;
            localTimeField.labelElement.style.width = 36;
            localTimeField.labelElement.style.minWidth = 36;
            localTimeField.labelElement.style.maxWidth = 36;
            VisualElement localTimeInput = localTimeField.Q<VisualElement>(className: "unity-base-field__input");
            if (localTimeInput != null)
            {
                localTimeInput.style.minWidth = 0;
            }
            if (!cue.IsReadOnlyDerived)
            {
                localTimeField.RegisterValueChangedCallback(evt => ChangeCueTime(cue.CueIndex, evt.newValue, localTimeField));
            }
            topRow.Add(localTimeField);
            timeField = localTimeField;

            if (cue.IsReadOnlyDerived)
            {
                Label readOnlyLabel = new("Animation Event");
                readOnlyLabel.tooltip = "来自 AnimationClip.events 的派生 Cue，只读。";
                readOnlyLabel.style.marginLeft = 6;
                readOnlyLabel.style.color = TextMuted;
                readOnlyLabel.style.fontSize = BodyFontSize;
                topRow.Add(readOnlyLabel);
            }
            else
            {
                Button deleteButton = new(() => DeleteCue(cue.CueIndex))
                {
                    text = "⌫"
                };
                deleteButton.tooltip = editable ? "删除这个 cue。" : "Override 资源不能删除 cue。";
                deleteButton.SetEnabled(editable);
                ApplyTrashButtonIcon(deleteButton);
                ApplyClipIconButtonStyle(deleteButton);
                deleteButton.style.marginLeft = 4;
                topRow.Add(deleteButton);
            }

            TextField eventKeyField = new("eventKey")
            {
                value = cue.EventKey,
                isDelayed = true
            };
            eventKeyField.tooltip = "Cue 触发时派发的事件 key，不能为空。";
            eventKeyField.SetEnabled(editable && !cue.IsReadOnlyDerived);
            if (!cue.IsReadOnlyDerived)
            {
                eventKeyField.RegisterValueChangedCallback(evt => ChangeCueEventKey(cue.CueIndex, evt.newValue, eventKeyField, evt.previousValue));
            }
            row.Add(eventKeyField);

            TextField payloadField = new("payload")
            {
                value = cue.Payload,
                isDelayed = true
            };
            payloadField.tooltip = "Cue 触发时携带的字符串 payload。";
            payloadField.SetEnabled(editable && !cue.IsReadOnlyDerived);
            if (!cue.IsReadOnlyDerived)
            {
                payloadField.RegisterValueChangedCallback(evt => ChangeCuePayload(cue.CueIndex, evt.newValue));
            }
            row.Add(payloadField);

            return row;
        }

        private VisualElement CreateClipCueEditor(XAnimationCompiledClip clip)
        {
            VisualElement box = CreateSubBox();
            box.style.marginTop = 2;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            Label title = new("Cues");
            title.style.flexGrow = 1;
            title.style.color = TextNormal;
            title.style.fontSize = BodyFontSize;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            string clipKey = clip?.Key ?? string.Empty;
            bool editable = m_Session != null && !m_Session.IsOverrideAsset;
            Button addButton = new(() => AddCue(clipKey))
            {
                text = "+"
            };
            addButton.tooltip = editable ? "在这个 clip 下新增一个 cue。" : "Override 资源不能新增 cue。";
            addButton.SetEnabled(editable);
            ApplyClipIconButtonStyle(addButton, AccentColor);
            header.Add(addButton);
            box.Add(header);

            XAnimationCueConfig[] cues = m_Session?.CompiledAsset.Asset.cues ?? Array.Empty<XAnimationCueConfig>();
            List<DisplayedCueEntry> clipCues = CollectClipCues(cues, clipKey);
            List<DisplayedCueEntry> derivedCues = CollectDerivedClipCues(clip);
            List<DisplayedCueEntry> timelineCues = new(clipCues.Count + derivedCues.Count);
            timelineCues.AddRange(clipCues);
            timelineCues.AddRange(derivedCues);
            timelineCues.Sort(CompareDisplayedCueEntries);

            Dictionary<int, FloatField> cueTimeFields = new();
            List<VisualElement> cueRows = new();
            for (int i = 0; i < clipCues.Count; i++)
            {
                DisplayedCueEntry cue = clipCues[i];
                VisualElement cueRow = CreateCueRow(cue, editable, out FloatField timeField);
                string cueKey = BuildCueSearchKey(clipKey, cue.CueIndex);
                m_CueRowMap[cueKey] = cueRow;
                m_CueIndexRowMap[cue.CueIndex] = cueRow;
                cueTimeFields[cue.CueIndex] = timeField;
                cueRows.Add(cueRow);
            }

            List<VisualElement> derivedCueRows = new();
            for (int i = 0; i < derivedCues.Count; i++)
            {
                VisualElement cueRow = CreateCueRow(derivedCues[i], editable: false);
                string cueKey = BuildDerivedCueSearchKey(clipKey, i);
                m_CueRowMap[cueKey] = cueRow;
                derivedCueRows.Add(cueRow);
            }

            box.Add(CreateCueTimeline(timelineCues, cueTimeFields, editable));

            for (int i = 0; i < cueRows.Count; i++)
            {
                box.Add(cueRows[i]);
            }

            if (derivedCueRows.Count > 0)
            {
                Label derivedLabel = new("Animation Events");
                derivedLabel.tooltip = "这些 Cue 由 AnimationClip.events 自动派生，只读显示。";
                derivedLabel.style.marginLeft = 4;
                derivedLabel.style.marginTop = cueRows.Count > 0 ? 2 : 1;
                derivedLabel.style.marginBottom = 1;
                derivedLabel.style.color = TextMuted;
                derivedLabel.style.fontSize = BodyFontSize;
                derivedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                box.Add(derivedLabel);

                for (int i = 0; i < derivedCueRows.Count; i++)
                {
                    box.Add(derivedCueRows[i]);
                }
            }

            if (cueRows.Count == 0 && derivedCueRows.Count == 0)
            {
                Label emptyLabel = new("No cues");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                emptyLabel.style.marginTop = 1;
                box.Add(emptyLabel);
            }

            return box;
        }

        private static List<DisplayedCueEntry> CollectClipCues(XAnimationCueConfig[] cues, string clipKey)
        {
            List<DisplayedCueEntry> results = new();
            if (cues == null)
            {
                return results;
            }

            for (int i = 0; i < cues.Length; i++)
            {
                XAnimationCueConfig cue = cues[i];
                if (cue == null || !string.Equals(cue.clipKey, clipKey, StringComparison.Ordinal))
                {
                    continue;
                }

                results.Add(new DisplayedCueEntry(
                    i,
                    Mathf.Clamp01(cue.time),
                    cue.eventKey,
                    cue.payload,
                    false));
            }

            results.Sort(CompareDisplayedCueEntries);
            return results;
        }

        private static List<DisplayedCueEntry> CollectDerivedClipCues(XAnimationCompiledClip clip)
        {
            List<DisplayedCueEntry> results = new();
            AnimationClip animationClip = clip?.Clip;
            if (animationClip == null)
            {
                return results;
            }

            AnimationEvent[] events = animationClip.events;
            if (events == null || events.Length == 0)
            {
                return results;
            }

            float clipLength = Mathf.Max(animationClip.length, 0.0001f);
            for (int i = 0; i < events.Length; i++)
            {
                AnimationEvent animationEvent = events[i];
                if (animationEvent == null || string.IsNullOrWhiteSpace(animationEvent.functionName))
                {
                    continue;
                }

                results.Add(new DisplayedCueEntry(
                    -1,
                    Mathf.Clamp01(animationEvent.time / clipLength),
                    animationEvent.functionName,
                    ResolveAnimationEventPayload(animationEvent),
                    true));
            }

            results.Sort(CompareDisplayedCueEntries);
            return results;
        }

        private VisualElement CreateCueTimeline(
            IReadOnlyList<DisplayedCueEntry> cues,
            Dictionary<int, FloatField> cueTimeFields,
            bool editable)
        {
            VisualElement box = new();
            box.style.marginTop = 2;
            box.style.marginBottom = 2;

            VisualElement track = new();
            track.style.position = Position.Relative;
            track.style.height = 30;
            track.style.backgroundColor = new Color(0.10f, 0.10f, 0.11f, 1f);
            track.style.borderTopWidth = 1;
            track.style.borderBottomWidth = 1;
            track.style.borderLeftWidth = 1;
            track.style.borderRightWidth = 1;
            track.style.borderTopColor = SectionDivider;
            track.style.borderBottomColor = SectionDivider;
            track.style.borderLeftColor = SectionDivider;
            track.style.borderRightColor = SectionDivider;
            track.style.borderTopLeftRadius = 2;
            track.style.borderTopRightRadius = 2;
            track.style.borderBottomLeftRadius = 2;
            track.style.borderBottomRightRadius = 2;
            track.tooltip = editable
                ? "Cue 时间轴。拖拽普通 Cue 标记可调整 normalized time。"
                : "Cue 时间轴。当前资源只读。";
            box.Add(track);

            AddCueTimelineTicks(track);

            for (int i = 0; i < cues.Count; i++)
            {
                DisplayedCueEntry cue = cues[i];
                VisualElement marker = CreateCueTimelineMarker(cue, editable);
                UpdateCueTimelineMarker(marker, cue.Time);
                if (!cue.IsReadOnlyDerived && cue.CueIndex >= 0)
                {
                    m_CueTimelineMarkerMap[cue.CueIndex] = marker;
                }

                RegisterCueTimelineMarkerDrag(marker, track, cue, cueTimeFields, editable);
                track.Add(marker);
            }

            return box;
        }

        private static void AddCueTimelineTicks(VisualElement track)
        {
            for (int i = 0; i <= 4; i++)
            {
                float normalized = i * 0.25f;

                VisualElement tick = new();
                tick.style.position = Position.Absolute;
                tick.style.left = Length.Percent(normalized * 100f);
                tick.style.top = 0;
                tick.style.bottom = 0;
                tick.style.width = 1;
                tick.style.backgroundColor = new Color(1f, 1f, 1f, i == 0 || i == 4 ? 0.18f : 0.10f);
                track.Add(tick);

                Label label = new(normalized.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                label.style.position = Position.Absolute;
                label.style.left = Length.Percent(normalized * 100f);
                label.style.bottom = 1;
                label.style.width = 32;
                label.style.marginLeft = i == 0 ? 2 : -16;
                label.style.color = TextMuted;
                label.style.fontSize = 9;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.pickingMode = PickingMode.Ignore;
                track.Add(label);
            }
        }

        private static VisualElement CreateCueTimelineMarker(DisplayedCueEntry cue, bool editable)
        {
            bool canEdit = editable && !cue.IsReadOnlyDerived && cue.CueIndex >= 0;
            VisualElement marker = new();
            marker.style.position = Position.Absolute;
            marker.style.top = 4;
            marker.style.width = 8;
            marker.style.height = 17;
            marker.style.marginLeft = -4;
            marker.style.backgroundColor = cue.IsReadOnlyDerived
                ? TextMuted
                : canEdit
                    ? AccentColor
                    : new Color(0.28f, 0.42f, 0.58f, 1f);
            marker.style.opacity = cue.IsReadOnlyDerived ? 0.78f : 1f;
            marker.style.borderTopLeftRadius = 2;
            marker.style.borderTopRightRadius = 2;
            marker.style.borderBottomLeftRadius = 2;
            marker.style.borderBottomRightRadius = 2;
            marker.tooltip = BuildCueTimelineMarkerTooltip(cue, cue.Time, canEdit);
            marker.userData = cue;
            marker.pickingMode = PickingMode.Position;
            return marker;
        }

        private void RegisterCueTimelineMarkerDrag(
            VisualElement marker,
            VisualElement track,
            DisplayedCueEntry cue,
            Dictionary<int, FloatField> cueTimeFields,
            bool editable)
        {
            if (marker == null || track == null || cue.IsReadOnlyDerived || cue.CueIndex < 0 || !editable)
            {
                return;
            }

            int activePointerId = PointerId.invalidPointerId;

            marker.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                activePointerId = evt.pointerId;
                marker.CapturePointer(activePointerId);
                ApplyCueTimelineDrag(cue, marker, track, cueTimeFields, new Vector2(evt.position.x, evt.position.y));
                evt.StopPropagation();
            });

            marker.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (activePointerId != evt.pointerId || !marker.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                ApplyCueTimelineDrag(cue, marker, track, cueTimeFields, new Vector2(evt.position.x, evt.position.y));
                evt.StopPropagation();
            });

            marker.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (activePointerId != evt.pointerId)
                {
                    return;
                }

                if (marker.HasPointerCapture(evt.pointerId))
                {
                    marker.ReleasePointer(evt.pointerId);
                }

                ResortCueRows(cue.CueIndex);
                activePointerId = PointerId.invalidPointerId;
                evt.StopPropagation();
            });

            marker.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                activePointerId = PointerId.invalidPointerId;
            });
        }

        private void ApplyCueTimelineDrag(
            DisplayedCueEntry cue,
            VisualElement marker,
            VisualElement track,
            Dictionary<int, FloatField> cueTimeFields,
            Vector2 pointerPosition)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (!TryGetCueTimelineTime(track, pointerPosition, out float normalizedTime))
            {
                return;
            }

            try
            {
                m_Session.SetCueTime(cue.CueIndex, normalizedTime, save: false);
                if (cueTimeFields != null && cueTimeFields.TryGetValue(cue.CueIndex, out FloatField timeField))
                {
                    timeField.SetValueWithoutNotify(normalizedTime);
                }

                UpdateCueTimelineMarker(marker, normalizedTime);
                marker.userData = CreateUpdatedCueEntry(cue, normalizedTime);
                marker.tooltip = BuildCueTimelineMarkerTooltip(cue, normalizedTime, canEdit: true);
                UpdateCueRowTime(cue.CueIndex, normalizedTime);
                ScheduleAssetSave();
                SetStatus($"Cue #{cue.CueIndex} time = {normalizedTime:0.###}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private static bool TryGetCueTimelineTime(VisualElement track, Vector2 pointerPosition, out float normalizedTime)
        {
            normalizedTime = 0f;
            Rect bounds = track.worldBound;
            if (bounds.width <= 0.0001f)
            {
                return false;
            }

            float pointerX = Mathf.Clamp(pointerPosition.x - bounds.xMin, 0f, bounds.width);
            normalizedTime = Mathf.Clamp01(pointerX / bounds.width);
            return true;
        }

        private static void UpdateCueTimelineMarker(VisualElement marker, float normalizedTime)
        {
            if (marker == null)
            {
                return;
            }

            marker.style.left = Length.Percent(Mathf.Clamp01(normalizedTime) * 100f);
        }

        private static string BuildCueTimelineMarkerTooltip(DisplayedCueEntry cue, float time, bool canEdit)
        {
            string source = cue.IsReadOnlyDerived
                ? "Animation Event"
                : canEdit
                    ? "Cue"
                    : "Cue (Read Only)";
            string payload = string.IsNullOrWhiteSpace(cue.Payload) ? string.Empty : $"\npayload: {cue.Payload}";
            string action = canEdit ? "\n拖拽可调整 time。" : string.Empty;
            return $"{source}\nkey: {cue.EventKey}\ntime: {Mathf.Clamp01(time):0.###}{payload}{action}";
        }

        private static int CompareDisplayedCueEntries(DisplayedCueEntry left, DisplayedCueEntry right)
        {
            int timeComparison = left.Time.CompareTo(right.Time);
            if (timeComparison != 0)
            {
                return timeComparison;
            }

            if (left.IsReadOnlyDerived != right.IsReadOnlyDerived)
            {
                return left.IsReadOnlyDerived ? 1 : -1;
            }

            int indexComparison = left.CueIndex.CompareTo(right.CueIndex);
            if (indexComparison != 0)
            {
                return indexComparison;
            }

            return string.Compare(left.EventKey, right.EventKey, StringComparison.Ordinal);
        }

        private void UpdateCueRowTime(int cueIndex, float normalizedTime)
        {
            if (!m_CueIndexRowMap.TryGetValue(cueIndex, out VisualElement row) ||
                row.userData is not DisplayedCueEntry cue)
            {
                return;
            }

            row.userData = CreateUpdatedCueEntry(cue, normalizedTime);
        }

        private void ResortCueRows(int cueIndex)
        {
            if (!m_CueIndexRowMap.TryGetValue(cueIndex, out VisualElement row))
            {
                return;
            }

            VisualElement parent = row.parent;
            if (parent == null)
            {
                return;
            }

            List<VisualElement> cueRows = new();
            int firstRowIndex = int.MaxValue;
            foreach (VisualElement child in parent.Children())
            {
                if (child.userData is not DisplayedCueEntry cue || cue.IsReadOnlyDerived)
                {
                    continue;
                }

                cueRows.Add(child);
                firstRowIndex = Mathf.Min(firstRowIndex, parent.IndexOf(child));
            }

            if (cueRows.Count <= 1 || firstRowIndex == int.MaxValue)
            {
                return;
            }

            cueRows.Sort((left, right) =>
            {
                DisplayedCueEntry leftCue = (DisplayedCueEntry)left.userData;
                DisplayedCueEntry rightCue = (DisplayedCueEntry)right.userData;
                return CompareDisplayedCueEntries(leftCue, rightCue);
            });

            for (int i = 0; i < cueRows.Count; i++)
            {
                parent.Remove(cueRows[i]);
            }

            for (int i = 0; i < cueRows.Count; i++)
            {
                parent.Insert(firstRowIndex + i, cueRows[i]);
            }
        }

        private static DisplayedCueEntry CreateUpdatedCueEntry(DisplayedCueEntry cue, float normalizedTime)
        {
            return new DisplayedCueEntry(
                cue.CueIndex,
                Mathf.Clamp01(normalizedTime),
                cue.EventKey,
                cue.Payload,
                cue.IsReadOnlyDerived);
        }

        private static string ResolveAnimationEventPayload(AnimationEvent animationEvent)
        {
            if (animationEvent == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(animationEvent.stringParameter))
            {
                return animationEvent.stringParameter;
            }

            if (animationEvent.intParameter != 0)
            {
                return animationEvent.intParameter.ToString();
            }

            if (!Mathf.Approximately(animationEvent.floatParameter, 0f))
            {
                return animationEvent.floatParameter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (animationEvent.objectReferenceParameter != null)
            {
                return animationEvent.objectReferenceParameter.name ?? string.Empty;
            }

            return string.Empty;
        }

        private VisualElement CreateClipRow(XAnimationCompiledClip clip, int rowIndex)
        {
            VisualElement wrapper = new VisualElement();
            wrapper.style.flexDirection = FlexDirection.Column;
            wrapper.style.marginBottom = 1;

            VisualElement container = CreateInteractiveRowContainer(rowIndex);
            Color baseColor = rowIndex % 2 == 0 ? ListRowEvenBg : ListRowOddBg;
            m_ClipRowMap[clip.Key] = container;
            VisualElement progressFill = CreateRowProgressFill();
            container.Add(progressFill);
            ClipRowVisualState visualState = new()
            {
                BaseColor = baseColor,
                ProgressFill = progressFill,
            };
            m_ClipVisualStateMap[clip.Key] = visualState;
            container.RegisterCallback<MouseEnterEvent>(_ =>
            {
                visualState.Hovered = true;
                ApplyClipRowVisualState(clip.Key);
            });
            container.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                visualState.Hovered = false;
                ApplyClipRowVisualState(clip.Key);
            });

            VisualElement row = CreateRowContent();
            container.Add(row);

            string clipKey = clip.Key;
            ClipPathInfo clipPathInfo = BuildClipPathInfo(clip);
            string clipTooltip = string.IsNullOrWhiteSpace(clipPathInfo.DisplayPath)
                ? clipKey
                : clipPathInfo.DisplayPath;
            container.tooltip = clipTooltip;

            string clipDisplayName = string.IsNullOrWhiteSpace(clipPathInfo.LeafName)
                ? clipKey
                : clipPathInfo.LeafName;
            EditableLabel label = new(clipDisplayName);
            ConfigureEditableNameLabel(label, 78f);
            label.tooltip = $"{clipTooltip}\n单击展开/收起 clip 配置；右键 Rename 编辑名称。";
            label.SetEditable(true, EditableLabelEditTrigger.ContextMenu);
            label.EditStarted += BeginNameEdit;
            label.EditEnded += EndNameEdit;
            label.ValueCommitted += (_, newValue) =>
                RenameClip(clipKey, BuildClipRenameKey(clipPathInfo.ParentPath, newValue), label, clipDisplayName);
            m_ClipLabelMap[clipKey] = label;
            label.style.position = Position.Relative;
            row.Add(label);

            VisualElement fileInfo = new VisualElement();
            fileInfo.style.flexGrow = 1;
            fileInfo.style.flexShrink = 1;
            fileInfo.style.minWidth = 140;
            fileInfo.style.marginLeft = 4;
            fileInfo.style.marginRight = 4;
            fileInfo.style.flexDirection = FlexDirection.Row;
            fileInfo.style.position = Position.Relative;
            row.Add(fileInfo);

            string activeClipPath = clip.Config.clipPath;

            ObjectField activeClipField = CreateClipObjectField(activeClipPath, editable: true);
            activeClipField.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "当前 Override 资源中的覆盖动画。可直接修改，不会写回 base 资源。"
                : "该 clip 对应的 AnimationClip 资源。可直接修改并保存到当前 XAnimation 文件。";
            activeClipField.style.flexGrow = 1;
            activeClipField.style.flexShrink = 1;
            activeClipField.style.minWidth = 120;
            activeClipField.style.maxWidth = 260;
            activeClipField.RegisterValueChangedCallback(evt => ChangeClipPath(clip, activeClipField, evt.previousValue as AnimationClip, evt.newValue as AnimationClip));
            fileInfo.Add(activeClipField);

            VisualElement editor = CreateClipEditor(clip);
            editor.style.display = m_ExpandedClipKeys.Contains(clipKey) ? DisplayStyle.Flex : DisplayStyle.None;
            RegisterClipNameInteractions(label, editor, clip);

            Button toggleButton = new(() =>
            {
                if (m_Session == null || !m_Session.IsLoaded) return;

                string channelName = m_PlayTargetChannelName;
                if (string.IsNullOrWhiteSpace(channelName))
                {
                    SetStatus("请先在 Target 中选择 channelName 后再调试播放 clip。", true);
                    return;
                }

                string playingChannelName = FindPlayingChannelName(clipKey);
                bool isPlaying = !string.IsNullOrWhiteSpace(playingChannelName);

                if (isPlaying)
                {
                    SetPlaybackTargetChannel(playingChannelName);
                    SetPlaybackPaused(!m_IsPaused);
                    SetPauseButtonState(true, m_IsPaused);
                    SetStepForwardButtonEnabled(true);
                    RefreshPlaybackViews();
                    SetStatus(m_IsPaused ? $"已暂停 {clipKey}。" : $"已继续 {clipKey}。");
                }
                else
                {
                    ResumePlaybackChannel(channelName);
                    SetPauseButtonState(true, false);
                    SetStepForwardButtonEnabled(true);
                    m_Session.SetGlobalSpeed(GetPlaybackSpeed());
                    m_Session.PlayClip(clipKey, channelName, BuildPreviewTransitionOptions());
                    RefreshPlaybackViews();
                    SetStatus($"正在 {channelName} 调试播放 {clipKey} ({clip.Clip.name})。");
                }
            })
            {
                text = "▶"
            };
            toggleButton.tooltip = "使用 Target.channelName 调试播放或暂停这个 clip。";
            ApplyClipButtonStyle(toggleButton, false);
            toggleButton.style.flexShrink = 0;
            toggleButton.style.marginLeft = 4;
            toggleButton.style.position = Position.Relative;
            row.Add(toggleButton);

            Button deleteButton = new(() => DeleteClip(clipKey))
            {
                text = "⌫"
            };
            deleteButton.tooltip = m_Session != null && m_Session.IsOverrideAsset
                ? "Override 资源不能删除 clip 结构。"
                : "删除这个 clip。";
            deleteButton.SetEnabled(m_Session != null && !m_Session.IsOverrideAsset);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.flexShrink = 0;
            deleteButton.style.marginLeft = 3;
            deleteButton.style.position = Position.Relative;
            row.Add(deleteButton);

            m_ClipButtonMap[clipKey] = toggleButton;
            wrapper.Add(container);
            wrapper.Add(editor);
            return wrapper;
        }

        private void RegisterClipNameInteractions(EditableLabel label, VisualElement editor, XAnimationCompiledClip clip)
        {
            bool isPressed = false;
            Vector2 startPosition = Vector2.zero;
            bool movedBeyondClickThreshold = false;
            bool dragStarted = false;

            label.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                if (label.IsEditing)
                {
                    ClearClipDragData();
                    isPressed = false;
                    movedBeyondClickThreshold = false;
                    dragStarted = false;
                    return;
                }

                isPressed = true;
                movedBeyondClickThreshold = false;
                dragStarted = false;
                startPosition = evt.mousePosition;
                ClearClipDragData();
                evt.StopPropagation();
            });
            label.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!isPressed || m_IsEditingName || label.IsEditing)
                {
                    return;
                }

                if (!movedBeyondClickThreshold && (evt.mousePosition - startPosition).sqrMagnitude >= 16f)
                {
                    movedBeyondClickThreshold = true;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData(ClipDragDataKey, clip.Key);
                    DragAndDrop.StartDrag($"Move {clip.Key}");
                    dragStarted = true;
                    evt.StopPropagation();
                }
            });
            label.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (!isPressed || evt.button != 0)
                {
                    return;
                }

                if (!movedBeyondClickThreshold && !label.IsEditing)
                {
                    bool expanded = editor.style.display == DisplayStyle.None;
                    editor.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    if (expanded)
                    {
                        m_ExpandedClipKeys.Add(clip.Key);
                    }
                    else
                    {
                        m_ExpandedClipKeys.Remove(clip.Key);
                    }
                }

                if (!dragStarted)
                {
                    ClearClipDragData();
                }

                isPressed = false;
                movedBeyondClickThreshold = false;
                dragStarted = false;
                evt.StopPropagation();
            });
        }

        private void RegisterStateNameInteractions(
            EditableLabel label,
            VisualElement blendEditor,
            string stateUiKey,
            string stateKey)
        {
            bool isPressed = false;
            Vector2 startPosition = Vector2.zero;
            bool movedBeyondClickThreshold = false;
            bool dragStarted = false;

            label.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || m_Session == null || !m_Session.IsLoaded)
                {
                    return;
                }

                if (label.IsEditing)
                {
                    ClearStateDragData();
                    isPressed = false;
                    movedBeyondClickThreshold = false;
                    dragStarted = false;
                    return;
                }

                isPressed = true;
                movedBeyondClickThreshold = false;
                dragStarted = false;
                startPosition = evt.mousePosition;
                ClearStateDragData();
                evt.StopPropagation();
            });
            label.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!isPressed || m_IsEditingName || label.IsEditing)
                {
                    return;
                }

                if (!movedBeyondClickThreshold && (evt.mousePosition - startPosition).sqrMagnitude >= 16f)
                {
                    movedBeyondClickThreshold = true;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData(StateDragDataKey, stateUiKey);
                    DragAndDrop.StartDrag($"Move {stateKey}");
                    dragStarted = true;
                    evt.StopPropagation();
                }
            });
            label.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (!isPressed || evt.button != 0)
                {
                    return;
                }

                bool selectState = !movedBeyondClickThreshold && !label.IsEditing;
                if (!dragStarted)
                {
                    ClearStateDragData();
                }

                isPressed = false;
                movedBeyondClickThreshold = false;
                dragStarted = false;
                evt.StopPropagation();

                if (selectState)
                {
                    bool wasSelected = string.Equals(
                        m_PreviewInspectorSelectedNodeUiKey,
                        stateUiKey,
                        StringComparison.Ordinal);
                    SelectPreviewInspectorStateNode(stateUiKey);
                    if (wasSelected && blendEditor != null)
                    {
                        SetBlendStateExpanded(stateUiKey, true);
                    }
                    rootVisualElement.Focus();
                }
            });
        }

        private void SetBlendStateExpanded(string stateUiKey, bool expanded)
        {
            if (expanded)
            {
                if (!string.Equals(m_ExpandedBlendStateUiKey, stateUiKey, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(m_ExpandedBlendStateUiKey) &&
                    m_BlendStateEditorMap.TryGetValue(m_ExpandedBlendStateUiKey, out VisualElement previousEditor))
                {
                    previousEditor.style.display = DisplayStyle.None;
                }

                m_ExpandedBlendStateUiKey = stateUiKey;
            }
            else if (string.Equals(m_ExpandedBlendStateUiKey, stateUiKey, StringComparison.Ordinal))
            {
                m_ExpandedBlendStateUiKey = null;
            }

            if (m_BlendStateEditorMap.TryGetValue(stateUiKey, out VisualElement editor))
            {
                editor.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool SelectAdjacentChannelTreeNode(bool selectNext)
        {
            if (m_StateNodeUiKeysInTreeOrder.Count == 0 ||
                !TryGetCompiledStateNodeByUiKey(m_PreviewInspectorSelectedNodeUiKey, out _))
            {
                return false;
            }

            List<string> visibleNodeUiKeys = new();
            for (int i = 0; i < m_StateNodeUiKeysInTreeOrder.Count; i++)
            {
                string nodeUiKey = m_StateNodeUiKeysInTreeOrder[i];
                if (TryGetCompiledStateNodeByUiKey(nodeUiKey, out XAnimationCompiledStateNode node) &&
                    IsStateNodeVisibleInChannelTree(node))
                {
                    visibleNodeUiKeys.Add(nodeUiKey);
                }
            }

            int selectedIndex = visibleNodeUiKeys.IndexOf(m_PreviewInspectorSelectedNodeUiKey);
            if (selectedIndex < 0)
            {
                return false;
            }

            int nextIndex = Mathf.Clamp(selectedIndex + (selectNext ? 1 : -1), 0, visibleNodeUiKeys.Count - 1);
            string nextNodeUiKey = visibleNodeUiKeys[nextIndex];
            SelectPreviewInspectorStateNode(nextNodeUiKey);
            ScrollChannelTreeNodeIntoView(nextNodeUiKey);
            rootVisualElement.Focus();
            return true;
        }

        private bool HandleChannelTreeHorizontalNavigation(bool expand)
        {
            if (!TryGetCompiledStateNodeByUiKey(
                    m_PreviewInspectorSelectedNodeUiKey,
                    out XAnimationCompiledStateNode selectedNode) ||
                !string.Equals(selectedNode.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
            {
                return false;
            }

            if (expand)
            {
                if (selectedNode is XAnimationCompiledState selectedState)
                {
                    if (!IsBlendStateType(selectedState.Config.stateType) ||
                        string.Equals(m_ExpandedBlendStateUiKey, m_PreviewInspectorSelectedNodeUiKey, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    SetBlendStateExpanded(m_PreviewInspectorSelectedNodeUiKey, true);
                    rootVisualElement.Focus();
                    return true;
                }

                string groupKey = BuildStateGroupKey(selectedNode.ChannelName, selectedNode.Key);
                if (IsStateGroupCollapsed(groupKey))
                {
                    SetStateGroupCollapsed(groupKey, false);
                    RebuildStateList();
                    ScrollChannelTreeNodeIntoView(m_PreviewInspectorSelectedNodeUiKey);
                    rootVisualElement.Focus();
                    return true;
                }

                XAnimationCompiledStateNode firstChild = FindFirstVisibleTreeChild(selectedNode);
                if (firstChild == null)
                {
                    return false;
                }

                string childUiKey = BuildStateUiKey(firstChild.ChannelName, firstChild.Key);
                SelectPreviewInspectorStateNode(childUiKey);
                ScrollChannelTreeNodeIntoView(childUiKey);
                rootVisualElement.Focus();
                return true;
            }

            if (selectedNode is XAnimationCompiledState selectedBlendState &&
                IsBlendStateType(selectedBlendState.Config.stateType) &&
                string.Equals(m_ExpandedBlendStateUiKey, m_PreviewInspectorSelectedNodeUiKey, StringComparison.Ordinal))
            {
                SetBlendStateExpanded(m_PreviewInspectorSelectedNodeUiKey, false);
                rootVisualElement.Focus();
                return true;
            }

            if (selectedNode.Kind != XAnimationStateNodeKind.State)
            {
                string groupKey = BuildStateGroupKey(selectedNode.ChannelName, selectedNode.Key);
                if (!IsStateGroupCollapsed(groupKey))
                {
                    SetStateGroupCollapsed(groupKey, true);
                    RebuildStateList();
                    ScrollChannelTreeNodeIntoView(m_PreviewInspectorSelectedNodeUiKey);
                    rootVisualElement.Focus();
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(selectedNode.ParentKey))
            {
                return false;
            }

            string parentUiKey = BuildStateUiKey(selectedNode.ChannelName, selectedNode.ParentKey);
            SelectPreviewInspectorStateNode(parentUiKey);
            ScrollChannelTreeNodeIntoView(parentUiKey);
            rootVisualElement.Focus();
            return true;
        }

        private static XAnimationCompiledStateNode FindFirstVisibleTreeChild(XAnimationCompiledStateNode node)
        {
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i].Kind != XAnimationStateNodeKind.State)
                {
                    return node.Children[i];
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                if (node.Children[i].Kind == XAnimationStateNodeKind.State)
                {
                    return node.Children[i];
                }
            }

            return null;
        }

        private bool IsStateNodeVisibleInChannelTree(XAnimationCompiledStateNode node)
        {
            if (!string.Equals(node.ChannelName, m_StateTabChannelName, StringComparison.Ordinal))
            {
                return false;
            }

            List<string> parentSegments = SplitStatePathSegments(node.ParentKey);
            string parentPath = string.Empty;
            for (int i = 0; i < parentSegments.Count; i++)
            {
                parentPath = BuildStatePathKey(parentPath, parentSegments[i]);
                if (IsStateGroupCollapsed(BuildStateGroupKey(node.ChannelName, parentPath)))
                {
                    return false;
                }
            }

            return true;
        }

        private void ScrollChannelTreeNodeIntoView(string nodeUiKey)
        {
            if (m_StateRowMap.TryGetValue(nodeUiKey, out VisualElement stateRow))
            {
                ScheduleInspectorScrollIntoView(stateRow);
                return;
            }

            if (TryGetCompiledStateNodeByUiKey(nodeUiKey, out XAnimationCompiledStateNode node) &&
                m_StateGroupRowMap.TryGetValue(
                    BuildStateGroupKey(node.ChannelName, node.Key),
                    out VisualElement groupRow))
            {
                ScheduleInspectorScrollIntoView(groupRow);
            }
        }

        private void BeginNameEdit()
        {
            m_IsEditingName = true;
            ClearStateDragData();
            ClearClipDragData();
        }

        private void EndNameEdit()
        {
            m_IsEditingName = false;
            ClearStateDragData();
            ClearClipDragData();
        }

        private static void ClearStateDragData()
        {
            DragAndDrop.SetGenericData(StateDragDataKey, null);
        }

        private static void ClearClipDragData()
        {
            DragAndDrop.SetGenericData(ClipDragDataKey, null);
        }

        private void AddChannel()
        {
            try
            {
                string channelName = m_Session.AddChannel();
                m_PendingChannelRenameKey = channelName;
                RebuildStructureAndPlaybackViews();
                SetStatus($"已新增 Channel {channelName}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteChannel(string channelName)
        {
            if (!m_Session.CanDeleteChannel(channelName, out string reason))
            {
                EditorUtility.DisplayDialog("无法删除 Channel", reason, "确定");
                return;
            }

            int stateCount = CountStatesInChannel(channelName);
            string message = stateCount > 0
                ? $"确定删除 Channel '{channelName}'？\n\n将同时移除该 Channel 下的 {stateCount} 个 State；Clip 资源不会被删除。"
                : $"确定删除 Channel '{channelName}'？";
            if (!EditorUtility.DisplayDialog("删除 Channel", message, "删除", "取消"))
            {
                return;
            }

            try
            {
                bool clearsInspector = TryGetCompiledStateNodeByUiKey(
                    m_PreviewInspectorSelectedNodeUiKey,
                    out XAnimationCompiledStateNode selectedNode) &&
                    string.Equals(selectedNode.ChannelName, channelName, StringComparison.Ordinal);
                m_Session.DeleteChannel(channelName);
                if (clearsInspector)
                {
                    ClearPreviewInspectorSelection();
                }
                RebuildStatePresentation(includeChannelPresentation: true);
                RefreshClipPlayingStates();
                SetStatus($"已删除 Channel {channelName}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddParameter()
        {
            try
            {
                string parameterName = m_Session.AddParameter();
                m_PendingParameterRenameKey = parameterName;
                RebuildParameterList();
                RebuildStateList();
                SetStatus($"已新增 Parameter {parameterName}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteParameter(string parameterName)
        {
            if (!EditorUtility.DisplayDialog("删除 Parameter", $"确定删除 Parameter '{parameterName}'？\n\n引用它的 Blend1D / 2D directional blend state 会清空对应 parameter。", "删除", "取消"))
            {
                return;
            }

            try
            {
                m_Session.DeleteParameter(parameterName);
                RebuildParameterList();
                RebuildStatePresentation();
                SetStatus($"已删除 Parameter {parameterName}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void RenameParameter(string oldName, string newName, EditableLabel label)
        {
            newName = newName?.Trim();
            try
            {
                m_Session.RenameParameter(oldName, newName);
                SetStatus($"Parameter {oldName} 已重命名为 {newName}。");
                RebuildParameterList();
                RebuildStatePresentation();
            }
            catch (Exception ex)
            {
                label.text = oldName;
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeParameterType(string parameterName, string typeName, string previousValue, DropdownField field)
        {
            try
            {
                if (!Enum.TryParse(typeName, out XAnimationParameterType type))
                {
                    return;
                }

                m_Session.SetParameterType(parameterName, type);
                RebuildParameterList();
                RebuildStatePresentation();
                SetStatus($"{parameterName} type = {type}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeParameterDefaultValue(string parameterName, object value)
        {
            try
            {
                m_Session.SetParameterDefaultValue(parameterName, value);
                SetStatus($"{parameterName} defaultValue 已更新。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private int CountStatesInChannel(string channelName)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return 0;
            }

            int count = 0;
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (state != null && string.Equals(state.ChannelName, channelName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private void AddState(string channelName)
        {
            if (!m_Session.CanAddState(out string reason))
            {
                EditorUtility.DisplayDialog("无法新增 State", reason, "确定");
                return;
            }

            try
            {
                string stateKey = m_Session.AddState(channelName);
                m_PendingStateRenameKey = stateKey;
                RebuildStatePresentation(includeChannelPresentation: true);
                SetStatus($"已在 {channelName} 新增 State {stateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddAutoTransition()
        {
            try
            {
                string preferredChannelName = m_StateTabChannelName;
                string preferredPreStateKey = null;
                if (TryGetCompiledStateByUiKey(m_SelectedAutoTransitionStateUiKey, out XAnimationCompiledState selectedState) &&
                    string.Equals(selectedState.ChannelName, preferredChannelName, StringComparison.Ordinal))
                {
                    preferredPreStateKey = selectedState.Key;
                }

                XAnimationCompiledAutoTransition transition = m_Session.AddAutoTransition(preferredChannelName, preferredPreStateKey);
                string stateUiKey = BuildAutoTransitionUiKey(transition);
                m_SelectedAutoTransitionStateUiKey = stateUiKey;
                SetAutoTransitionExpanded(stateUiKey, true);
                RebuildAutoTransitionEditor();
                RefreshChannelStates();
                SetStatus($"已新增 Auto Transition {transition.ChannelName}: {transition.PreStateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddDefaultTransition()
        {
            try
            {
                int transitionIndex = m_Session.AddDefaultTransition(m_StateTabChannelName);
                m_SelectedDefaultTransitionIndex = transitionIndex;
                SetDefaultTransitionExpanded(transitionIndex, true);
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已新增 Default Transition {transitionIndex + 1}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteDefaultTransition(int transitionIndex)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("删除 Default Transition", $"确定删除 Default Transition #{transitionIndex + 1}？", "删除", "取消"))
            {
                return;
            }

            try
            {
                m_Session.DeleteDefaultTransition(transitionIndex);
                m_SelectedDefaultTransitionIndex = Mathf.Clamp(transitionIndex - 1, -1, (m_Session.CompiledAsset.DefaultTransitions.Count) - 1);
                NormalizeCollapsedDefaultTransitionIndicesAfterDelete(transitionIndex);
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已删除 Default Transition {transitionIndex + 1}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddDefaultTransitionPair(int transitionIndex)
        {
            try
            {
                m_Session.AddDefaultTransitionPair(transitionIndex);
                m_SelectedDefaultTransitionIndex = transitionIndex;
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已新增 Default Transition pair。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteDefaultTransitionPair(int transitionIndex, int pairIndex)
        {
            try
            {
                m_Session.DeleteDefaultTransitionPair(transitionIndex, pairIndex);
                m_SelectedDefaultTransitionIndex = transitionIndex;
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"已删除 Default Transition pair。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeDefaultTransitionPair(
            int transitionIndex,
            int pairIndex,
            string preStateKey,
            string nextStateKey,
            XAnimationEditorSelectionField changedField,
            string previousValue)
        {
            try
            {
                m_Session.SetDefaultTransitionPair(transitionIndex, pairIndex, preStateKey, nextStateKey, save: false);
                ScheduleAssetSave();
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"Default Transition pair = {preStateKey} -> {nextStateKey}。");
            }
            catch (Exception ex)
            {
                changedField?.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeDefaultTransitionChannel(
            int transitionIndex,
            string channelName,
            DropdownField changedField,
            string previousValue)
        {
            try
            {
                m_Session.SetDefaultTransitionChannel(transitionIndex, channelName, save: false);
                ScheduleAssetSave();
                RebuildDefaultTransitionsEditor();
                RefreshChannelStates();
                SetStatus($"Default Transition channel = {channelName}。");
            }
            catch (Exception ex)
            {
                changedField?.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private bool PlayDefaultTransitionPairPre(string channelName, string preStateKey, string nextStateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(preStateKey) || string.IsNullOrWhiteSpace(nextStateKey))
            {
                return false;
            }

            XAnimationCompiledAsset compiled = m_Session.CompiledAsset;
            if (!compiled.TryGetStateIndex(channelName, preStateKey, out _) ||
                !compiled.TryGetStateIndex(channelName, nextStateKey, out _))
            {
                SetStatus($"无法预览：state 不存在。", true);
                return false;
            }

            ResumePlaybackChannel(channelName);
            SetPauseButtonState(true, false);
            SetStepForwardButtonEnabled(true);
            m_Session.SetGlobalSpeed(GetPlaybackSpeed());

            PlayPreviewState(channelName, preStateKey);

            RefreshPlaybackViews();
            SetStatus($"正在播放 {preStateKey}，点击 ⏭ 切换到 {nextStateKey}。");
            return true;
        }

        private void PlayDefaultTransitionPairNext(string channelName, string preStateKey, string nextStateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(channelName) ||
                string.IsNullOrWhiteSpace(nextStateKey))
            {
                return;
            }

            PlayPreviewState(channelName, nextStateKey);
            RefreshPlaybackViews();
            SetStatus($"Default Transition 切换: {preStateKey} -> {nextStateKey}。");
        }

        private void DeleteAutoTransition(string channelName, string preStateKey)
        {
            string stateUiKey = BuildStateUiKey(channelName, preStateKey);
            if (string.IsNullOrWhiteSpace(preStateKey) ||
                !HasAutoTransition(stateUiKey))
            {
                SetStatus("当前没有可删除的 Auto Transition。", true);
                return;
            }

            if (!EditorUtility.DisplayDialog("删除 Auto Transition", $"确定删除 Auto Transition '{channelName}: {preStateKey}'？", "删除", "取消"))
            {
                return;
            }

            try
            {
                m_Session.DeleteAutoTransition(channelName, preStateKey);
                if (string.Equals(m_SelectedAutoTransitionStateUiKey, stateUiKey, StringComparison.Ordinal))
                {
                    m_SelectedAutoTransitionStateUiKey = null;
                }

                m_CollapsedAutoTransitionKeys.Remove(stateUiKey);
                RebuildAutoTransitionEditor();
                RefreshChannelStates();
                SetStatus($"已删除 Auto Transition {channelName}: {preStateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteState(string channelName, string stateKey)
        {
            if (!EditorUtility.DisplayDialog("删除 State", $"确定删除 State '{stateKey}'？", "删除", "取消"))
            {
                return;
            }

            try
            {
                string stateUiKey = BuildStateUiKey(channelName, stateKey);
                m_Session.DeleteState(channelName, stateKey);
                if (string.Equals(m_SelectedAutoTransitionStateUiKey, stateUiKey, StringComparison.Ordinal))
                {
                    m_SelectedAutoTransitionStateUiKey = null;
                }

                m_CollapsedAutoTransitionKeys.Remove(stateUiKey);
                if (string.Equals(m_ExpandedBlendStateUiKey, stateUiKey, StringComparison.Ordinal))
                {
                    SetBlendStateExpanded(stateUiKey, false);
                }
                m_SelectedDefaultTransitionIndex = -1;
                if (string.Equals(m_PreviewInspectorSelectedNodeUiKey, stateUiKey, StringComparison.Ordinal))
                {
                    ClearPreviewInspectorSelection();
                }
                RebuildStateList();
                RebuildDefaultTransitionsEditor();
                RefreshStatePlaybackViews();
                SetStatus($"已删除 State {stateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddStateBehavior(string channelName, string stateKey, Type behaviorType)
        {
            try
            {
                m_Session.AddStateBehavior(channelName, stateKey, behaviorType);
                SelectPreviewInspectorStateNode(BuildStateUiKey(channelName, stateKey));
                RebuildStateList();
                SetStatus($"已为 State {stateKey} 新增 {behaviorType.Name}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteStateBehavior(string channelName, string stateKey, int behaviorIndex)
        {
            try
            {
                m_Session.DeleteStateBehavior(channelName, stateKey, behaviorIndex);
                RebuildStateList();
                SetStatus($"已删除 State {stateKey} behavior #{behaviorIndex}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void SetStateBehaviorFieldValue(
            string channelName,
            string stateKey,
            int behaviorIndex,
            string fieldName,
            object value)
        {
            try
            {
                m_Session.SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldName, value, save: false);
                ScheduleAssetSave();
                SetStatus($"{stateKey} behavior #{behaviorIndex}.{fieldName} 已更新。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddClip(string parentPath)
        {
            m_PendingAddClipParentPath = NormalizeClipPathKey(parentPath);
            m_OpenAnimationClipObjectPickerRequested = true;
            m_AnimationClipObjectPickerBridge.Focus();
            m_AnimationClipObjectPickerBridge.MarkDirtyRepaint();
        }

        private void HandleAnimationClipObjectPickerIMGUI()
        {
            UEvent current = UEvent.current;
            if (m_OpenAnimationClipObjectPickerRequested && current.type == EventType.Repaint)
            {
                m_OpenAnimationClipObjectPickerRequested = false;
                m_AnimationClipObjectPickerControlId = GetInstanceID();
                EditorGUIUtility.ShowObjectPicker<AnimationClip>(
                    null,
                    false,
                    string.Empty,
                    m_AnimationClipObjectPickerControlId);
                return;
            }

            if (m_AnimationClipObjectPickerControlId == 0 || current.type != EventType.ExecuteCommand)
            {
                return;
            }

            if (string.Equals(current.commandName, "ObjectSelectorClosed", StringComparison.Ordinal))
            {
                m_AnimationClipObjectPickerControlId = 0;
                m_PendingAddClipParentPath = null;
                current.Use();
                return;
            }

            if (!string.Equals(current.commandName, "ObjectSelectorSelectionDone", StringComparison.Ordinal))
            {
                return;
            }

            string parentPath = m_PendingAddClipParentPath;
            AnimationClip animationClip = EditorGUIUtility.GetObjectPickerObject() as AnimationClip;
            m_AnimationClipObjectPickerControlId = 0;
            m_PendingAddClipParentPath = null;
            current.Use();

            if (animationClip != null)
            {
                AddClip(parentPath, animationClip);
            }
        }

        private void AddClip(string parentPath, AnimationClip animationClip)
        {
            string clipPath = XAnimationEditorAssetResolver.BuildClipPath(animationClip);
            if (string.IsNullOrWhiteSpace(clipPath))
            {
                EditorUtility.DisplayDialog("无法新增 Clip", "所选 AnimationClip 没有可用的资源路径。", "确定");
                return;
            }

            try
            {
                string clipKey = m_Session.AddClip(parentPath, animationClip);
                RemoveMaterializedTransientClipPaths(parentPath);
                m_PendingClipRenameKey = clipKey;
                RebuildStructureAndPlaybackViews();
                SetStatus(string.IsNullOrWhiteSpace(parentPath)
                    ? $"已新增 Clip {clipKey}。"
                    : $"已在 {parentPath} 新增 Clip {clipKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddClipGroup(string parentPath)
        {
            parentPath = NormalizeClipPathKey(parentPath);
            const string defaultGroupName = "New Group";
            string groupName = defaultGroupName;
            int suffix = 1;
            while (HasClipPath(BuildClipRenameKey(parentPath, groupName)))
            {
                groupName = $"{defaultGroupName} {suffix++}";
            }

            string groupPath = BuildClipRenameKey(parentPath, groupName);
            m_TransientClipPathKeys.Add(groupPath);
            ExpandClipPath(groupPath);
            m_PendingClipPathRenameKey = groupPath;
            RebuildClipList();
            SetStatus($"已创建临时 Clip Folder {groupPath}。");
        }

        private void RemoveMaterializedTransientClipPaths(string clipParentPath)
        {
            clipParentPath = NormalizeClipPathKey(clipParentPath);
            if (string.IsNullOrWhiteSpace(clipParentPath))
            {
                return;
            }

            m_TransientClipPathKeys.RemoveWhere(path =>
                string.Equals(path, clipParentPath, StringComparison.Ordinal) ||
                clipParentPath.StartsWith($"{path}/", StringComparison.Ordinal));
        }

        private void RenameTransientClipPath(string oldPath, string newPath)
        {
            Dictionary<string, string> renamedPaths = new(StringComparer.Ordinal);
            foreach (string path in m_TransientClipPathKeys)
            {
                if (!string.Equals(path, oldPath, StringComparison.Ordinal) &&
                    !path.StartsWith($"{oldPath}/", StringComparison.Ordinal))
                {
                    continue;
                }

                string suffix = path.Length == oldPath.Length
                    ? string.Empty
                    : path[(oldPath.Length + 1)..];
                renamedPaths[path] = string.IsNullOrWhiteSpace(suffix)
                    ? newPath
                    : $"{newPath}/{suffix}";
            }

            foreach (KeyValuePair<string, string> pair in renamedPaths)
            {
                m_TransientClipPathKeys.Remove(pair.Key);
                m_TransientClipPathKeys.Add(pair.Value);
            }
        }

        private void ClearTransientClipPath(string path)
        {
            string parentPath = GetClipPathParent(path);
            Dictionary<string, string> movedPaths = new(StringComparer.Ordinal);
            foreach (string transientPath in m_TransientClipPathKeys)
            {
                if (string.Equals(transientPath, path, StringComparison.Ordinal))
                {
                    movedPaths[transientPath] = string.Empty;
                    continue;
                }

                if (!transientPath.StartsWith($"{path}/", StringComparison.Ordinal))
                {
                    continue;
                }

                string suffix = transientPath[(path.Length + 1)..];
                movedPaths[transientPath] = string.IsNullOrWhiteSpace(parentPath)
                    ? suffix
                    : $"{parentPath}/{suffix}";
            }

            foreach (KeyValuePair<string, string> pair in movedPaths)
            {
                m_TransientClipPathKeys.Remove(pair.Key);
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    m_TransientClipPathKeys.Add(pair.Value);
                }
            }
        }

        private void RenameClipPath(string oldPath, string newPath, EditableLabel label)
        {
            oldPath = NormalizeClipPathKey(oldPath);
            newPath = NormalizeClipPathKey(newPath);
            try
            {
                if (string.IsNullOrWhiteSpace(newPath))
                {
                    throw new XAnimationException("Clip folder path cannot be empty.");
                }

                if (!string.Equals(oldPath, newPath, StringComparison.Ordinal) && HasClipPath(newPath))
                {
                    throw new XAnimationException($"Clip folder '{newPath}' already exists.");
                }

                bool hasPersistedClips = HasPersistedClipPath(oldPath);
                if (hasPersistedClips)
                {
                    m_Session.RenameClipPath(oldPath, newPath);
                }

                RenameTransientClipPath(oldPath, newPath);
                ExpandClipPath(newPath);
                SetStatus($"Clip Folder {oldPath} 已重命名为 {newPath}。");
                if (hasPersistedClips)
                {
                    RebuildStructureAndPlaybackViews();
                }
                else
                {
                    RebuildClipList();
                }
            }
            catch (Exception ex)
            {
                label.text = GetClipPathLeafName(oldPath);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteClipPath(string path)
        {
            path = NormalizeClipPathKey(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("删除 Clip Folder", $"确定删除 Clip Folder '{path}'？\n\n组内 clips 会保留，并移动到上一层。", "删除", "取消"))
            {
                return;
            }

            try
            {
                string parentPath = GetClipPathParent(path);
                bool hasPersistedClips = HasPersistedClipPath(path);
                if (hasPersistedClips)
                {
                    m_Session.ClearClipPath(path);
                }

                ClearTransientClipPath(path);
                ExpandClipPath(parentPath);
                if (hasPersistedClips)
                {
                    RebuildStructureAndPlaybackViews();
                }
                else
                {
                    RebuildClipList();
                }
                SetStatus(string.IsNullOrWhiteSpace(parentPath)
                    ? $"已删除 Clip Folder {path}，clips 已移动到根层级。"
                    : $"已删除 Clip Folder {path}，clips 已移动到 {parentPath}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteClip(string clipKey)
        {
            if (!EditorUtility.DisplayDialog("删除 Clip", $"确定删除 Clip '{clipKey}'？", "删除", "取消"))
            {
                return;
            }

            try
            {
                m_Session.DeleteClip(clipKey);
                m_ExpandedClipKeys.Remove(clipKey);
                RebuildStructureAndPlaybackViews();
                SetStatus($"已删除 Clip {clipKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private float GetNewCueDefaultTime(string clipKey)
        {
            return TryGetCurrentClipNormalizedTime(clipKey, out float normalizedTime)
                ? normalizedTime
                : 0f;
        }

        private bool TryGetCurrentClipNormalizedTime(string clipKey, out float normalizedTime)
        {
            normalizedTime = 0f;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
            float bestBlendWeight = 0f;
            bool hasBlendMatch = false;
            for (int i = 0; i < channels.Count; i++)
            {
                XAnimationChannelState state = m_Session.GetChannelState(channels[i].Name);
                if (state == null)
                {
                    continue;
                }

                if (string.Equals(state.clipKey, clipKey, StringComparison.Ordinal))
                {
                    normalizedTime = Mathf.Clamp01(state.normalizedTime);
                    return true;
                }

                XAnimationBlendClipState[] blendClips = state.blendClips;
                if (blendClips == null)
                {
                    continue;
                }

                for (int blendIndex = 0; blendIndex < blendClips.Length; blendIndex++)
                {
                    XAnimationBlendClipState blendClip = blendClips[blendIndex];
                    if (blendClip == null || !string.Equals(blendClip.clipKey, clipKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    float effectiveWeight = Mathf.Max(0f, blendClip.weight * state.weight);
                    if (!hasBlendMatch || effectiveWeight >= bestBlendWeight)
                    {
                        hasBlendMatch = true;
                        bestBlendWeight = effectiveWeight;
                        normalizedTime = Mathf.Clamp01(blendClip.normalizedTime);
                    }
                }
            }

            return hasBlendMatch;
        }

        private string GetCueClipKey(int cueIndex)
        {
            XAnimationCueConfig[] cues = m_Session?.CompiledAsset.Asset.cues ?? Array.Empty<XAnimationCueConfig>();
            return cueIndex >= 0 && cueIndex < cues.Length ? cues[cueIndex]?.clipKey : null;
        }

        private void AddCue(string clipKey)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            try
            {
                float defaultTime = GetNewCueDefaultTime(clipKey);
                int cueIndex = m_Session.AddCue(clipKey, defaultTime);
                string resolvedClipKey = GetCueClipKey(cueIndex) ?? clipKey;
                if (!string.IsNullOrWhiteSpace(resolvedClipKey))
                {
                    m_ExpandedClipKeys.Add(resolvedClipKey);
                }

                RebuildClipList();
                string cueKey = BuildCueSearchKey(resolvedClipKey, cueIndex);
                if (!string.IsNullOrWhiteSpace(resolvedClipKey) &&
                    m_CueRowMap.TryGetValue(cueKey, out VisualElement row))
                {
                    ScheduleInspectorScrollIntoView(row);
                    FlashElement(row);
                }

                SetStatus($"已在 {resolvedClipKey} 新增 Cue #{cueIndex}，time = {defaultTime:0.###}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteCue(int cueIndex)
        {
            if (!EditorUtility.DisplayDialog("删除 Cue", $"确定删除 Cue #{cueIndex}？", "删除", "取消"))
            {
                return;
            }

            try
            {
                m_Session.DeleteCue(cueIndex);
                RebuildClipList();
                SetStatus($"已删除 Cue #{cueIndex}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeCueClipKey(int cueIndex, string clipKey, DropdownField field, string previousValue)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                field.SetValueWithoutNotify(previousValue);
                return;
            }

            try
            {
                m_Session.SetCueClipKey(cueIndex, clipKey);
                RebuildClipList();
                SetStatus($"Cue #{cueIndex} clipKey = {clipKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeCueTime(int cueIndex, float time, FloatField field)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            float clampedTime = Mathf.Clamp01(time);
            if (!Mathf.Approximately(clampedTime, time))
            {
                field.SetValueWithoutNotify(clampedTime);
            }

            try
            {
                m_Session.SetCueTime(cueIndex, clampedTime, save: false);
                if (m_CueTimelineMarkerMap.TryGetValue(cueIndex, out VisualElement marker))
                {
                    UpdateCueTimelineMarker(marker, clampedTime);
                    if (marker.userData is DisplayedCueEntry cue)
                    {
                        marker.userData = CreateUpdatedCueEntry(cue, clampedTime);
                        marker.tooltip = BuildCueTimelineMarkerTooltip(cue, clampedTime, canEdit: true);
                    }
                }

                UpdateCueRowTime(cueIndex, clampedTime);
                ResortCueRows(cueIndex);
                ScheduleAssetSave();
                SetStatus($"Cue #{cueIndex} time = {clampedTime:0.###}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeCueEventKey(int cueIndex, string eventKey, TextField field, string previousValue)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                field.SetValueWithoutNotify(previousValue);
                return;
            }

            try
            {
                m_Session.SetCueEventKey(cueIndex, eventKey);
                SetStatus($"Cue #{cueIndex} eventKey = {eventKey?.Trim()}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeCuePayload(int cueIndex, string payload)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            try
            {
                m_Session.SetCuePayload(cueIndex, payload);
                SetStatus($"Cue #{cueIndex} payload 已更新。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private static string BuildClipRenameKey(string parentPath, string leafName)
        {
            string normalizedLeafName = NormalizeClipPathKey(leafName);
            if (string.IsNullOrWhiteSpace(normalizedLeafName))
            {
                return string.Empty;
            }

            string normalizedParentPath = NormalizeClipPathKey(parentPath);
            return string.IsNullOrWhiteSpace(normalizedParentPath)
                ? normalizedLeafName
                : $"{normalizedParentPath}/{normalizedLeafName}";
        }

        private void RenameClip(string oldKey, string newKey, EditableLabel label, string oldDisplayName = null)
        {
            newKey = newKey?.Trim();
            try
            {
                m_Session.RenameClip(oldKey, newKey);
                SetStatus($"Clip {oldKey} 已重命名为 {newKey}。");
                if (m_ExpandedClipKeys.Remove(oldKey) && !string.IsNullOrWhiteSpace(newKey))
                {
                    m_ExpandedClipKeys.Add(newKey.Trim());
                }
                RebuildStructureAndPlaybackViews();
            }
            catch (Exception ex)
            {
                label.text = string.IsNullOrWhiteSpace(oldDisplayName) ? oldKey : oldDisplayName;
                SetStatus(ex.Message);
                Debug.LogException(ex);
            }
        }

        private void RenameChannel(string oldName, string newName, EditableLabel label)
        {
            newName = newName?.Trim();
            try
            {
                string selectedNodeKey =
                    TryGetCompiledStateNodeByUiKey(m_PreviewInspectorSelectedNodeUiKey, out XAnimationCompiledStateNode selectedNode) &&
                    string.Equals(selectedNode.ChannelName, oldName, StringComparison.Ordinal)
                        ? selectedNode.Key
                        : null;
                string expandedBlendStateKey =
                    !string.IsNullOrWhiteSpace(m_ExpandedBlendStateUiKey) &&
                    TryGetCompiledStateByUiKey(m_ExpandedBlendStateUiKey, out XAnimationCompiledState expandedBlendState) &&
                    string.Equals(expandedBlendState.ChannelName, oldName, StringComparison.Ordinal)
                        ? expandedBlendState.Key
                        : null;
                m_Session.RenameChannel(oldName, newName);
                if (string.Equals(m_StateTabChannelName, oldName, StringComparison.Ordinal))
                {
                    m_StateTabChannelName = newName;
                }
                if (string.Equals(m_StatesGraphChannelName, oldName, StringComparison.Ordinal))
                {
                    m_StatesGraphChannelName = newName;
                }
                if (!string.IsNullOrWhiteSpace(selectedNodeKey))
                {
                    m_PreviewInspectorSelectedNodeUiKey = BuildStateUiKey(newName, selectedNodeKey);
                    if (m_PreviewInspectorSelectionKind == PreviewInspectorSelectionKind.Transition)
                    {
                        m_DefaultTransitionEditingStateUiKey = m_PreviewInspectorSelectedNodeUiKey;
                    }
                }
                if (!string.IsNullOrWhiteSpace(expandedBlendStateKey))
                {
                    m_ExpandedBlendStateUiKey = BuildStateUiKey(newName, expandedBlendStateKey);
                }
                SetStatus($"Channel {oldName} 已重命名为 {newName}。");
                RebuildStatePresentation(includeChannelPresentation: true);
                RefreshClipPlayingStates();
            }
            catch (Exception ex)
            {
                label.text = oldName;
                SetStatus(ex.Message);
                Debug.LogException(ex);
            }
        }

        private void RenameState(string channelName, string oldKey, string newKey, EditableLabel label)
        {
            newKey = BuildStatePathKey(GetStatePathParent(oldKey), newKey);
            try
            {
                string oldUiKey = BuildStateUiKey(channelName, oldKey);
                string newUiKey = BuildStateUiKey(channelName, newKey);
                m_Session.RenameState(channelName, oldKey, newKey);
                if (string.Equals(m_SelectedAutoTransitionStateUiKey, oldUiKey, StringComparison.Ordinal))
                {
                    m_SelectedAutoTransitionStateUiKey = newUiKey;
                }

                bool autoTransitionWasExpanded = m_CollapsedAutoTransitionKeys.Remove(oldUiKey);
                if (autoTransitionWasExpanded && !string.IsNullOrWhiteSpace(newKey))
                {
                    m_CollapsedAutoTransitionKeys.Add(newUiKey);
                }

                SetStatus($"State {oldKey} 已重命名为 {newKey}。");
                if (string.Equals(m_PreviewInspectorSelectedNodeUiKey, oldUiKey, StringComparison.Ordinal))
                {
                    m_PreviewInspectorSelectedNodeUiKey = newUiKey;
                }
                if (string.Equals(m_DefaultTransitionEditingStateUiKey, oldUiKey, StringComparison.Ordinal))
                {
                    m_DefaultTransitionEditingStateUiKey = newUiKey;
                }
                if (string.Equals(m_ExpandedBlendStateUiKey, oldUiKey, StringComparison.Ordinal))
                {
                    m_ExpandedBlendStateUiKey = newUiKey;
                }
                if (string.Equals(m_StatesGraphChannelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(m_StatesGraphCurrentPath, oldKey, StringComparison.Ordinal))
                {
                    m_StatesGraphCurrentPath = newKey;
                }
                RebuildStateList();
                RebuildDefaultTransitionsEditor();
                RefreshStatePlaybackViews();
            }
            catch (Exception ex)
            {
                label.text = GetStatePathLeafName(oldKey);
                SetStatus(ex.Message);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateType(string channelName, string stateKey, XAnimationStateType stateType, string previousValue, DropdownField field)
        {
            try
            {
                m_Session.SetStateType(channelName, stateKey, stateType);
                if (!IsBlendStateType(stateType) &&
                    string.Equals(m_ExpandedBlendStateUiKey, BuildStateUiKey(channelName, stateKey), StringComparison.Ordinal))
                {
                    m_ExpandedBlendStateUiKey = null;
                }
                RebuildStatePresentation();
                SetStatus($"{stateKey} stateType = {stateType}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateChannel(string sourceChannelName, string stateKey, string targetChannelName, DropdownField field, string previousValue)
        {
            try
            {
                m_Session.SetStateChannel(sourceChannelName, stateKey, targetChannelName);
                RebuildStatePresentation();
                SetStatus($"{stateKey} channel = {targetChannelName}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateClipKey(string channelName, string stateKey, string clipKey, XAnimationEditorSelectionField field, string previousValue)
        {
            try
            {
                m_Session.SetStateClipKey(channelName, stateKey, clipKey);
                RebuildStateList();
                RestartStateIfPlaying(stateKey, channelName);
                RefreshStatePlaybackViews();
                SetStatus($"{stateKey} clipKey = {clipKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateBlendParameter(string channelName, string stateKey, string parameterName, DropdownField field, string previousValue)
        {
            try
            {
                m_Session.SetStateBlendParameter(channelName, stateKey, parameterName);
                RebuildStatePresentation();
                SetStatus($"{stateKey} parameter = {parameterName}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateDirectionalBlendParameters(
            string channelName,
            string stateKey,
            string parameterXName,
            string parameterYName,
            DropdownField parameterXField,
            DropdownField parameterYField,
            string previousXValue,
            string previousYValue)
        {
            try
            {
                m_Session.SetStateDirectionalBlendParameters(channelName, stateKey, parameterXName, parameterYName);
                MarkFreeformStateInteracted(channelName, stateKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} parameters = ({parameterXName}, {parameterYName})。");
            }
            catch (Exception ex)
            {
                parameterXField.SetValueWithoutNotify(previousXValue);
                parameterYField.SetValueWithoutNotify(previousYValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void RenameStateGroup(string channelName, string oldName, string newName, EditableLabel label)
        {
            newName = NormalizeStatePath(newName);
            try
            {
                if (string.IsNullOrWhiteSpace(newName))
                {
                    throw new XAnimationException("State node path cannot be empty.");
                }

                m_Session.RenameStatePath(channelName, oldName, newName);
                string oldUiKey = BuildStateUiKey(channelName, oldName);
                string newUiKey = BuildStateUiKey(channelName, newName);
                m_PreviewInspectorSelectedNodeUiKey = RemapStatesGraphPath(
                    m_PreviewInspectorSelectedNodeUiKey,
                    oldUiKey,
                    newUiKey);
                m_DefaultTransitionEditingStateUiKey = RemapStatesGraphPath(
                    m_DefaultTransitionEditingStateUiKey,
                    oldUiKey,
                    newUiKey);
                m_ExpandedBlendStateUiKey = RemapStatesGraphPath(
                    m_ExpandedBlendStateUiKey,
                    oldUiKey,
                    newUiKey);
                if (string.Equals(m_StatesGraphChannelName, channelName, StringComparison.Ordinal))
                {
                    m_StatesGraphCurrentPath = RemapStatesGraphPath(m_StatesGraphCurrentPath, oldName, newName);
                }
                SetStatus($"State Node {oldName} 已重命名为 {newName}。");
                RebuildStatePresentation();
            }
            catch (Exception ex)
            {
                label.text = FormatStateDisplayPath(oldName);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void RemoveStateGroupNode(string channelName, string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("移除 State Node", $"确定移除 State Node '{channelName} / {groupName}' 及其全部子节点？", "移除", "取消"))
            {
                return;
            }

            try
            {
                string removedNodeUiKey = BuildStateUiKey(channelName, groupName);
                m_Session.DeleteContainerStateNode(channelName, groupName);
                if (string.Equals(m_PreviewInspectorSelectedNodeUiKey, removedNodeUiKey, StringComparison.Ordinal) ||
                    m_PreviewInspectorSelectedNodeUiKey?.StartsWith(removedNodeUiKey + "/", StringComparison.Ordinal) == true)
                {
                    ClearPreviewInspectorSelection();
                }
                SetStatus($"已移除 State Node {channelName} / {groupName}。");
                RebuildStatePresentation();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddState(string channelName, string groupName)
        {
            if (!m_Session.CanAddState(out string reason))
            {
                EditorUtility.DisplayDialog("无法新增 State", reason, "确定");
                return;
            }

            try
            {
                string stateKey = m_Session.AddState(channelName, groupName);
                m_PendingStateRenameKey = stateKey;
                RebuildStatePresentation(includeChannelPresentation: true);
                SetStatus($"已在 {channelName} / {groupName} 新增 State {stateKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ShowAddStateNodeMenu(VisualElement activator, string channelName, string parentPath)
        {
            GenericMenu menu = new();
            bool selectorParent = !string.IsNullOrWhiteSpace(parentPath) &&
                                  IsSelectorKind(m_Session.CompiledAsset.GetStateNode(channelName, parentPath).Kind);
            menu.AddItem(
                new GUIContent("State"),
                false,
                () => AddState(channelName, parentPath));
            if (!selectorParent)
            {
                menu.AddItem(
                    new GUIContent("Normal"),
                    false,
                    () => AddStateNode(channelName, parentPath, XAnimationStateNodeKind.Normal));
            }
            menu.AddItem(
                new GUIContent("Index Selector"),
                false,
                () => AddStateNode(channelName, parentPath, XAnimationStateNodeKind.Selector));
            menu.AddItem(
                new GUIContent("Int Selector"),
                false,
                () => AddStateNode(channelName, parentPath, XAnimationStateNodeKind.IntSelector));
            menu.AddItem(
                new GUIContent("String Selector"),
                false,
                () => AddStateNode(channelName, parentPath, XAnimationStateNodeKind.StringSelector));
            menu.DropDown(activator.worldBound);
        }

        private void AddStateNode(string channelName, string parentPath, XAnimationStateNodeKind nodeKind)
        {
            parentPath = NormalizeStatePath(parentPath);
            try
            {
                string nodeKey = nodeKind switch
                {
                    XAnimationStateNodeKind.Normal => m_Session.AddNormalStateNode(channelName, parentPath),
                    XAnimationStateNodeKind.Selector => m_Session.AddSelectorStateNode(channelName, parentPath),
                    XAnimationStateNodeKind.IntSelector => m_Session.AddIntSelectorStateNode(channelName, parentPath),
                    XAnimationStateNodeKind.StringSelector => m_Session.AddStringSelectorStateNode(channelName, parentPath),
                    _ => throw new XAnimationException($"Cannot add container State Node with kind '{nodeKind}'."),
                };
                SetStateGroupCollapsed(BuildStateGroupKey(channelName, nodeKey), false);
                RebuildStatePresentation(includeChannelPresentation: true);
                SetStatus($"已创建 {nodeKind} State Node {channelName} / {nodeKey}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddStateAllowedNextState(string channelName, string stateKey)
        {
            try
            {
                m_Session.AddStateAllowedNextState(channelName, stateKey);
                RebuildStateList();
                SetStatus($"{stateKey} allowedNextStateKeys += 1。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddStateAllowedPreviousState(string channelName, string stateKey)
        {
            try
            {
                m_Session.AddStateAllowedPreviousState(channelName, stateKey);
                RebuildStateList();
                SetStatus($"{stateKey} allowedPreviousStateKeys += 1。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteStateAllowedNextState(string channelName, string stateKey, int index)
        {
            try
            {
                m_Session.DeleteStateAllowedNextState(channelName, stateKey, index);
                RebuildStateList();
                SetStatus($"{stateKey} 删除 allowedNextStateKeys[{index}]。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteStateAllowedPreviousState(string channelName, string stateKey, int index)
        {
            try
            {
                m_Session.DeleteStateAllowedPreviousState(channelName, stateKey, index);
                RebuildStateList();
                SetStatus($"{stateKey} 删除 allowedPreviousStateKeys[{index}]。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateAllowedNextState(string channelName, string stateKey, int index, string targetStateKey, XAnimationEditorSelectionField field, string previousValue)
        {
            try
            {
                m_Session.SetStateAllowedNextState(channelName, stateKey, index, targetStateKey);
                RebuildStateList();
                SetStatus($"{stateKey} allowedNextStateKeys[{index}] = {targetStateKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeStateAllowedPreviousState(string channelName, string stateKey, int index, string sourceStateKey, XAnimationEditorSelectionField field, string previousValue)
        {
            try
            {
                m_Session.SetStateAllowedPreviousState(channelName, stateKey, index, sourceStateKey);
                RebuildStateList();
                SetStatus($"{stateKey} allowedPreviousStateKeys[{index}] = {sourceStateKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddBlendSample(string channelName, string stateKey)
        {
            try
            {
                m_Session.AddBlendSample(channelName, stateKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} 已新增 Blend1D sample。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void AddDirectionalBlendSample(string channelName, string stateKey)
        {
            try
            {
                m_Session.AddDirectionalBlendSample(channelName, stateKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} 已新增 2D directional blend sample。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteBlendSample(string channelName, string stateKey, int sampleIndex)
        {
            try
            {
                m_Session.DeleteBlendSample(channelName, stateKey, sampleIndex);
                RebuildStatePresentation();
                SetStatus($"{stateKey} 已删除 Blend1D sample #{sampleIndex}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void DeleteDirectionalBlendSample(string channelName, string stateKey, int sampleIndex)
        {
            try
            {
                m_Session.DeleteDirectionalBlendSample(channelName, stateKey, sampleIndex);
                RebuildStatePresentation();
                SetStatus($"{stateKey} 已删除 2D directional blend sample #{sampleIndex}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeBlendSampleClipKey(string channelName, string stateKey, int sampleIndex, string clipKey, XAnimationEditorSelectionField field, string previousValue)
        {
            try
            {
                m_Session.SetBlendSampleClipKey(channelName, stateKey, sampleIndex, clipKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} sample #{sampleIndex} clip = {clipKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeBlendSampleThreshold(string channelName, string stateKey, int sampleIndex, float threshold, FloatField field, float previousValue)
        {
            try
            {
                m_Session.SetBlendSampleThreshold(channelName, stateKey, sampleIndex, threshold);
                RebuildStatePresentation();
                SetStatus($"{stateKey} sample #{sampleIndex} threshold = {threshold:0.###}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeDirectionalBlendSampleClipKey(string channelName, string stateKey, int sampleIndex, string clipKey, XAnimationEditorSelectionField field, string previousValue)
        {
            try
            {
                m_Session.SetDirectionalBlendSampleClipKey(channelName, stateKey, sampleIndex, clipKey);
                MarkFreeformStateInteracted(channelName, stateKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} directional sample #{sampleIndex} clip = {clipKey}。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(previousValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void ChangeDirectionalBlendSamplePosition(
            string channelName,
            string stateKey,
            int sampleIndex,
            float positionX,
            float positionY,
            FloatField field,
            float previousXValue,
            float previousYValue,
            bool isX)
        {
            try
            {
                XAnimationStateConfig stateConfig = m_Session.CompiledAsset.GetState(channelName, stateKey).Config;
                XAnimationBlend2DSimpleDirectionalSampleConfig sample =
                    stateConfig.directionalSamples[sampleIndex];
                float newX = isX ? positionX : sample.positionX;
                float newY = isX ? sample.positionY : positionY;
                m_Session.SetDirectionalBlendSamplePosition(channelName, stateKey, sampleIndex, newX, newY);
                MarkFreeformStateInteracted(channelName, stateKey);
                RebuildStatePresentation();
                SetStatus($"{stateKey} directional sample #{sampleIndex} position = ({newX:0.###}, {newY:0.###})。");
            }
            catch (Exception ex)
            {
                field.SetValueWithoutNotify(isX ? previousXValue : previousYValue);
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void RegisterProjectClipDropTarget(
            VisualElement target,
            string parentPath = null,
            VisualElement groupHeader = null)
        {
            parentPath = NormalizeClipPathKey(parentPath);
            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!CanAcceptProjectClips())
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                SetProjectClipDropHighlight(target, groupHeader, true);
                evt.StopPropagation();
            });
            target.RegisterCallback<DragLeaveEvent>(_ => SetProjectClipDropHighlight(target, groupHeader, false));
            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                List<AnimationClip> animationClips = GetDraggedProjectClips();
                if (animationClips.Count == 0)
                {
                    SetProjectClipDropHighlight(target, groupHeader, false);
                    return;
                }

                DragAndDrop.AcceptDrag();
                SetProjectClipDropHighlight(target, groupHeader, false);
                try
                {
                    IReadOnlyList<string> clipKeys = m_Session.AddClips(parentPath, animationClips);
                    RemoveMaterializedTransientClipPaths(parentPath);
                    ExpandClipPath(parentPath);
                    RebuildStructureAndPlaybackViews();
                    SetStatus(string.IsNullOrWhiteSpace(parentPath)
                        ? $"已从 Project 添加 {clipKeys.Count} 个 Clip 到根层级。"
                        : $"已从 Project 添加 {clipKeys.Count} 个 Clip 到 {parentPath}。");
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message, true);
                    Debug.LogException(ex);
                }

                evt.StopPropagation();
            });
        }

        private bool CanAcceptProjectClips()
        {
            return !m_IsEditingName &&
                   m_SelectedDebugToolbarGroup == DebugToolbarGroup.Clip &&
                   m_Session != null &&
                   m_Session.IsLoaded &&
                   !m_Session.IsOverrideAsset &&
                   DragAndDrop.GetGenericData(ClipDragDataKey) == null &&
                   HasDraggedProjectClips();
        }

        private static bool HasDraggedProjectClips()
        {
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is AnimationClip)
                {
                    return true;
                }

                string assetPath = AssetDatabase.GetAssetPath(objects[i]);
                if (string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<AnimationClip> GetDraggedProjectClips()
        {
            List<AnimationClip> clips = new();
            HashSet<int> instanceIds = new();
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is AnimationClip clip)
                {
                    AddDraggedProjectClip(clips, instanceIds, clip);
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(objects[i]);
                if (!string.Equals(Path.GetExtension(assetPath), ".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int subAssetIndex = 0; subAssetIndex < subAssets.Length; subAssetIndex++)
                {
                    if (subAssets[subAssetIndex] is AnimationClip subClip &&
                        !subClip.name.Contains("__preview__", StringComparison.Ordinal))
                    {
                        AddDraggedProjectClip(clips, instanceIds, subClip);
                    }
                }
            }

            return clips;
        }

        private static void AddDraggedProjectClip(
            List<AnimationClip> clips,
            HashSet<int> instanceIds,
            AnimationClip clip)
        {
            if (instanceIds.Add(clip.GetInstanceID()) &&
                !string.IsNullOrWhiteSpace(XAnimationEditorAssetResolver.BuildClipPath(clip)))
            {
                clips.Add(clip);
            }
        }

        private static void SetProjectClipDropHighlight(VisualElement target, bool highlighted)
        {
            float width = highlighted ? 1f : 0f;
            Color color = highlighted ? AccentColor : Color.clear;
            target.style.borderLeftWidth = width;
            target.style.borderRightWidth = width;
            target.style.borderTopWidth = width;
            target.style.borderBottomWidth = width;
            target.style.borderLeftColor = color;
            target.style.borderRightColor = color;
            target.style.borderTopColor = color;
            target.style.borderBottomColor = color;
        }

        private void SetProjectClipDropHighlight(
            VisualElement target,
            VisualElement groupHeader,
            bool highlighted)
        {
            if (groupHeader == null)
            {
                SetProjectClipDropHighlight(target, highlighted);
                return;
            }

            SetProjectClipDropHighlight(m_InspectorScrollView, false);
            groupHeader.style.backgroundColor = highlighted ? AccentColor : ListHeaderBg;
        }

        private void RegisterStateChannelDropTarget(VisualElement group, VisualElement groupHeader, string channelName, string groupName)
        {
            group.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                string stateUiKey = DragAndDrop.GetGenericData(StateDragDataKey) as string;
                if (!CanDropState(stateUiKey, channelName, groupName))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                groupHeader.style.backgroundColor = AccentColor;
                evt.StopPropagation();
            });
            group.RegisterCallback<DragLeaveEvent>(_ => groupHeader.style.backgroundColor = ListHeaderBg);
            group.RegisterCallback<DragPerformEvent>(evt =>
            {
                string stateUiKey = DragAndDrop.GetGenericData(StateDragDataKey) as string;
                if (!CanDropState(stateUiKey, channelName, groupName))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                groupHeader.style.backgroundColor = ListHeaderBg;
                MoveState(stateUiKey, channelName, insertBeforeStateKey: null, groupName);
                evt.StopPropagation();
            });
        }

        private void RegisterStateRowDropTarget(VisualElement row, string channelName, string insertBeforeStateKey, string groupName)
        {
            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                string stateUiKey = DragAndDrop.GetGenericData(StateDragDataKey) as string;
                if (!CanDropState(stateUiKey, channelName, groupName, insertBeforeStateKey))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                row.style.borderTopColor = AccentColor;
                row.style.borderTopWidth = 2;
                evt.StopPropagation();
            });
            row.RegisterCallback<DragLeaveEvent>(_ =>
            {
                row.style.borderTopColor = Color.clear;
                row.style.borderTopWidth = 0;
            });
            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                string stateUiKey = DragAndDrop.GetGenericData(StateDragDataKey) as string;
                if (!CanDropState(stateUiKey, channelName, groupName, insertBeforeStateKey))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                row.style.borderTopColor = Color.clear;
                row.style.borderTopWidth = 0;
                MoveState(stateUiKey, channelName, insertBeforeStateKey, groupName);
                evt.StopPropagation();
            });
        }

        private void RegisterClipPathDropTarget(VisualElement group, VisualElement groupHeader, string path)
        {
            group.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                string clipKey = DragAndDrop.GetGenericData(ClipDragDataKey) as string;
                if (!CanDropClip(clipKey, path))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                groupHeader.style.backgroundColor = AccentColor;
                evt.StopPropagation();
            });
            group.RegisterCallback<DragLeaveEvent>(_ => groupHeader.style.backgroundColor = ListHeaderBg);
            group.RegisterCallback<DragPerformEvent>(evt =>
            {
                string clipKey = DragAndDrop.GetGenericData(ClipDragDataKey) as string;
                if (!CanDropClip(clipKey, path))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                groupHeader.style.backgroundColor = ListHeaderBg;
                MoveClip(clipKey, path);
                evt.StopPropagation();
            });
        }

        private void RegisterClipRowDropTarget(VisualElement row, string insertBeforeClipKey, string path)
        {
            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                string clipKey = DragAndDrop.GetGenericData(ClipDragDataKey) as string;
                if (!CanDropClip(clipKey, path, insertBeforeClipKey))
                {
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                row.style.borderTopColor = AccentColor;
                row.style.borderTopWidth = 2;
                evt.StopPropagation();
            });
            row.RegisterCallback<DragLeaveEvent>(_ =>
            {
                row.style.borderTopColor = Color.clear;
                row.style.borderTopWidth = 0;
            });
            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                string clipKey = DragAndDrop.GetGenericData(ClipDragDataKey) as string;
                if (!CanDropClip(clipKey, path, insertBeforeClipKey))
                {
                    return;
                }

                DragAndDrop.AcceptDrag();
                row.style.borderTopColor = Color.clear;
                row.style.borderTopWidth = 0;
                MoveClip(clipKey, path, insertBeforeClipKey);
                evt.StopPropagation();
            });
        }

        private bool CanDropState(string stateUiKey, string channelName, string groupName, string insertBeforeStateKey = null)
        {
            if (m_IsEditingName ||
                m_Session == null || !m_Session.IsLoaded ||
                string.IsNullOrWhiteSpace(stateUiKey) ||
                string.IsNullOrWhiteSpace(channelName))
            {
                return false;
            }

            if (!TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState sourceState) ||
                !m_StateChannelMap.TryGetValue(stateUiKey, out string currentChannel))
            {
                return false;
            }

            string currentGroupName = GetStatePathParent(sourceState.Key);
            string targetGroupName = NormalizeStatePath(groupName);
            return !string.Equals(sourceState.Key, insertBeforeStateKey, StringComparison.Ordinal) ||
                !string.Equals(currentChannel, channelName, StringComparison.Ordinal) ||
                !string.Equals(currentGroupName, targetGroupName, StringComparison.Ordinal);
        }

        private bool CanDropClip(string clipKey, string path, string insertBeforeClipKey = null)
        {
            if (m_IsEditingName ||
                m_Session == null || !m_Session.IsLoaded ||
                m_Session.IsOverrideAsset ||
                string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            if (!m_Session.CompiledAsset.TryGetClipIndex(clipKey, out _))
            {
                return false;
            }

            string currentPath = GetClipPathParent(clipKey);
            string targetPath = NormalizeClipPathKey(path);
            return !string.Equals(clipKey, insertBeforeClipKey, StringComparison.Ordinal) ||
                !string.Equals(currentPath, targetPath, StringComparison.Ordinal);
        }

        private void MoveState(string stateUiKey, string channelName, string insertBeforeStateKey = null, string groupName = null)
        {
            if (!TryGetCompiledStateByUiKey(stateUiKey, out XAnimationCompiledState sourceState))
            {
                SetStatus("无法解析要移动的 State。", true);
                return;
            }

            string normalizedGroup = NormalizeStatePath(groupName);
            string stateKey = sourceState.Key;
            string sourceUiKey = BuildStateUiKey(sourceState.ChannelName, stateKey);
            m_Session.MoveState(sourceState.ChannelName, stateKey, channelName, insertBeforeStateKey, normalizedGroup);
            string targetKey = BuildStatePathKey(normalizedGroup, GetStatePathLeafName(stateKey));
            string targetUiKey = BuildStateUiKey(channelName, targetKey);
            m_StateChannelMap[targetUiKey] = channelName;
            if (string.Equals(m_PreviewInspectorSelectedNodeUiKey, sourceUiKey, StringComparison.Ordinal))
            {
                m_PreviewInspectorSelectedNodeUiKey = targetUiKey;
            }
            if (string.Equals(m_DefaultTransitionEditingStateUiKey, sourceUiKey, StringComparison.Ordinal))
            {
                m_DefaultTransitionEditingStateUiKey = targetUiKey;
            }
            if (string.Equals(m_ExpandedBlendStateUiKey, sourceUiKey, StringComparison.Ordinal))
            {
                m_ExpandedBlendStateUiKey = targetUiKey;
            }
            if (string.Equals(m_StatesGraphChannelName, sourceState.ChannelName, StringComparison.Ordinal) &&
                string.Equals(m_StatesGraphCurrentPath, stateKey, StringComparison.Ordinal))
            {
                m_StatesGraphCurrentPath = string.Equals(sourceState.ChannelName, channelName, StringComparison.Ordinal)
                    ? targetKey
                    : string.Empty;
                m_StatesGraphEditingStateTransitions = false;
            }
            RebuildStatePresentation();
            SetStatus(string.IsNullOrWhiteSpace(normalizedGroup)
                ? $"{stateKey} 已移动到 {channelName}。"
                : $"{stateKey} 已移动到 {channelName} / {normalizedGroup}。");
        }

        private void MoveClip(string clipKey, string path, string insertBeforeClipKey = null)
        {
            path = NormalizeClipPathKey(path);
            try
            {
                m_Session.MoveClip(clipKey, path, insertBeforeClipKey);
                RemoveMaterializedTransientClipPaths(path);
                ExpandClipPath(path);
                RebuildStructureAndPlaybackViews();
                SetStatus(string.IsNullOrWhiteSpace(path)
                    ? $"{clipKey} 已移动到根层级。"
                    : $"{clipKey} 已移动到 {path}。");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
            }
        }

        private void TryBeginPendingRename()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(m_PendingClipRenameKey) &&
                m_ClipLabelMap.TryGetValue(m_PendingClipRenameKey, out EditableLabel clipLabel))
            {
                string clipKey = m_PendingClipRenameKey;
                m_PendingClipRenameKey = null;
                rootVisualElement.schedule.Execute(() =>
                {
                    if (clipLabel != null)
                    {
                        m_ExpandedClipKeys.Remove(clipKey);
                        clipLabel.BeginEdit();
                    }
                }).StartingIn(0);
            }

            if (!string.IsNullOrWhiteSpace(m_PendingClipPathRenameKey) &&
                m_ClipPathLabelMap.TryGetValue(m_PendingClipPathRenameKey, out EditableLabel clipPathLabel))
            {
                m_PendingClipPathRenameKey = null;
                rootVisualElement.schedule.Execute(clipPathLabel.BeginEdit).StartingIn(0);
            }

            string pendingStateUiKey = !string.IsNullOrWhiteSpace(m_PendingStateRenameKey)
                ? ResolveStateUiKey(m_PendingStateRenameKey)
                : null;
            if (!string.IsNullOrWhiteSpace(pendingStateUiKey) &&
                m_StateLabelMap.TryGetValue(pendingStateUiKey, out EditableLabel stateLabel))
            {
                m_PendingStateRenameKey = null;
                rootVisualElement.schedule.Execute(() =>
                {
                    if (stateLabel != null)
                    {
                        stateLabel.BeginEdit();
                    }
                }).StartingIn(0);
            }

            if (!string.IsNullOrWhiteSpace(m_PendingParameterRenameKey) &&
                m_ParameterLabelMap.TryGetValue(m_PendingParameterRenameKey, out EditableLabel parameterLabel))
            {
                m_PendingParameterRenameKey = null;
                rootVisualElement.schedule.Execute(() =>
                {
                    parameterLabel?.BeginEdit();
                }).StartingIn(0);
            }

            if (!string.IsNullOrWhiteSpace(m_PendingChannelRenameKey) &&
                m_ChannelLabelMap.TryGetValue(m_PendingChannelRenameKey, out EditableLabel channelLabel))
            {
                m_PendingChannelRenameKey = null;
                rootVisualElement.schedule.Execute(() =>
                {
                    channelLabel?.BeginEdit();
                }).StartingIn(0);
            }
        }

    }
}
#endif
