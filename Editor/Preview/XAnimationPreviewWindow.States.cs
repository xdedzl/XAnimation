#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        private static VisualElement CreateFoldoutRowEditor()
        {
            VisualElement editor = CreateSubBox();
            editor.style.marginLeft = 4;
            editor.style.marginRight = 4;
            editor.style.marginTop = 1;
            editor.style.marginBottom = 2;
            return editor;
        }

        private static VisualElement CreateStateConfigSection()
        {
            VisualElement box = CreateSubBox();
            box.style.marginTop = 2;
            return box;
        }

        private VisualElement CreateBlendSampleEditor(string channelName, string stateKey, XAnimationStateConfig config, VisualElement parameterField = null)
        {
            XAnimationBlend1DSampleConfig[] samples = config.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            return CreateSamplesSection(
                channelName,
                stateKey,
                "Samples",
                "右键批量修改这个 Blend1D state 用到的所有动画。",
                !m_CollapsedBlendSampleStateKeys.Contains(BuildStateUiKey(channelName, stateKey)),
                collapsed => SetCollapsed(m_CollapsedBlendSampleStateKeys, BuildStateUiKey(channelName, stateKey), collapsed),
                () => AddBlendSample(channelName, stateKey),
                "为这个 Blend1D state 新增采样点。",
                parameterField,
                samples.Length,
                (content, editable) =>
                {
                    for (int i = 0; i < samples.Length; i++)
                    {
                        content.Add(CreateBlendSampleRow(channelName, stateKey, i, samples[i], editable));
                    }
                });
        }

        private VisualElement CreateDirectionalBlendSampleEditor(
            string channelName,
            string stateKey,
            XAnimationStateConfig config,
            VisualElement parameterXField = null,
            VisualElement parameterYField = null)
        {
            XAnimationBlend2DSimpleDirectionalSampleConfig[] samples =
                config.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            VisualElement parameterRow = null;
            if (parameterXField != null || parameterYField != null)
            {
                parameterRow = new VisualElement();
                parameterRow.style.flexDirection = FlexDirection.Column;
                parameterRow.style.alignItems = Align.Stretch;
                parameterRow.style.marginBottom = 2;

                if (parameterXField != null)
                {
                    ConfigureDirectionalParameterField(parameterXField);
                    parameterRow.Add(parameterXField);
                }

                if (parameterYField != null)
                {
                    ConfigureDirectionalParameterField(parameterYField);
                    parameterYField.style.marginTop = 2;
                    parameterRow.Add(parameterYField);
                }
            }

            return CreateSamplesSection(
                channelName,
                stateKey,
                "Directional Samples",
                "右键批量修改这个 directional state 用到的所有动画。",
                !m_CollapsedDirectionalSampleStateKeys.Contains(BuildStateUiKey(channelName, stateKey)),
                collapsed => SetCollapsed(m_CollapsedDirectionalSampleStateKeys, BuildStateUiKey(channelName, stateKey), collapsed),
                () => AddDirectionalBlendSample(channelName, stateKey),
                $"为这个 {config.stateType} state 新增采样点。",
                parameterRow,
                samples.Length,
                (content, editable) =>
                {
                    for (int i = 0; i < samples.Length; i++)
                    {
                        content.Add(CreateDirectionalBlendSampleRow(channelName, stateKey, i, samples[i], editable));
                    }
                });
        }

        private static void ConfigureDirectionalParameterField(VisualElement field)
        {
            field.style.flexGrow = 0;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            field.style.alignSelf = Align.Stretch;

            if (field is not DropdownField dropdown)
            {
                return;
            }

            dropdown.style.minWidth = 0;
            if (dropdown.labelElement != null)
            {
                dropdown.labelElement.style.minWidth = 72;
                dropdown.labelElement.style.width = 72;
                dropdown.labelElement.style.flexShrink = 0;
            }

            void ApplyInputStyle()
            {
                VisualElement input = dropdown.Q<VisualElement>(className: "unity-base-field__input");
                if (input != null)
                {
                    input.style.minWidth = 64;
                    input.style.flexShrink = 1;
                }
            }

            ApplyInputStyle();
            dropdown.RegisterCallback<AttachToPanelEvent>(_ => ApplyInputStyle());
        }

        private VisualElement CreateSamplesSection(
            string channelName,
            string stateKey,
            string titleText,
            string titleTooltip,
            bool expanded,
            Action<bool> setCollapsed,
            Action addSample,
            string addTooltip,
            VisualElement leadingContent,
            int sampleCount,
            Action<VisualElement, bool> buildRows)
        {
            VisualElement box = CreateSubBox();
            box.style.marginTop = 2;
            SetPadding(box, 0);
            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(expanded);
            Label title = CreateSectionTitleLabel(titleText);
            title.tooltip = titleTooltip;
            RegisterBatchEditStateClipsContextMenu(title, channelName, stateKey);
            header.Add(foldoutLabel);
            header.Add(title);

            bool editable = m_Session != null && !m_Session.IsOverrideAsset;
            Button addButton = new(addSample) { text = "+" };
            addButton.tooltip = editable ? addTooltip : "Override 资源不能新增采样点。";
            addButton.SetEnabled(editable);
            ApplyClipIconButtonStyle(addButton, AccentColor);
            header.Add(addButton);

            if (leadingContent != null)
            {
                leadingContent.style.marginBottom = 2;
            }

            if (leadingContent != null)
            {
                VisualElement leadingWrapper = new();
                ApplyPrettyContentStyle(leadingWrapper);
                leadingWrapper.Add(leadingContent);
                box.Add(leadingWrapper);
            }
            box.Add(header);

            VisualElement content = new() { style = { display = expanded ? DisplayStyle.Flex : DisplayStyle.None } };
            ApplyPrettyContentStyle(content);
            box.Add(content);

            buildRows(content, editable);
            if (sampleCount == 0)
            {
                AddEmptyLabel(content, "No samples");
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
                setCollapsed(!nextExpanded);
                evt.StopPropagation();
            });
            return box;
        }

        private VisualElement CreateStateBehaviorEditor(string channelName, string stateKey, XAnimationStateConfig config)
        {
            XAnimationStateBehavior[] behaviors = config.behaviors ?? Array.Empty<XAnimationStateBehavior>();
            string stateUiKey = BuildStateUiKey(channelName, stateKey);
            bool expanded = m_ExpandedBehaviorStateKeys.Contains(stateUiKey);
            bool editable = m_Session != null && !m_Session.IsOverrideAsset;

            VisualElement box = CreateSubBox();
            box.style.marginTop = 2;
            SetPadding(box, 0);

            VisualElement header = CreateListHeader();
            Label foldoutLabel = CreateFoldoutGlyph(expanded);
            Label title = CreateSectionTitleLabel("Behaviors");
            title.tooltip = "State 进入、更新、退出时调用的 XAnimationStateBehavior 列表。";
            header.Add(foldoutLabel);
            header.Add(title);

            Button addButton = null;
            addButton = new Button(() => ShowStateBehaviorTypeMenu(addButton, channelName, stateKey))
            {
                text = "+"
            };
            addButton.tooltip = editable
                ? "选择并新增一个 XAnimationStateBehavior。"
                : "Override 资源不能编辑 state behavior。";
            addButton.SetEnabled(editable);
            ApplyClipIconButtonStyle(addButton, AccentColor);
            header.Add(addButton);
            box.Add(header);

            VisualElement content = new() { style = { display = expanded ? DisplayStyle.Flex : DisplayStyle.None } };
            ApplyPrettyContentStyle(content);
            box.Add(content);

            for (int i = 0; i < behaviors.Length; i++)
            {
                content.Add(CreateStateBehaviorRow(channelName, stateKey, i, behaviors[i], editable));
            }

            if (behaviors.Length == 0)
            {
                AddEmptyLabel(content, "No behaviors");
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
                SetExpanded(m_ExpandedBehaviorStateKeys, stateUiKey, nextExpanded);
                evt.StopPropagation();
            });

            return box;
        }

        private void ShowStateBehaviorTypeMenu(VisualElement anchor, string channelName, string stateKey)
        {
            GenericMenu menu = new();
            List<Type> behaviorTypes = CollectStateBehaviorTypes();
            if (behaviorTypes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No XAnimationStateBehavior Types"));
            }
            else
            {
                for (int i = 0; i < behaviorTypes.Count; i++)
                {
                    Type behaviorType = behaviorTypes[i];
                    menu.AddItem(
                        new GUIContent(GetStateBehaviorMenuName(behaviorType)),
                        false,
                        () => AddStateBehavior(channelName, stateKey, behaviorType));
                }
            }

            menu.DropDown(anchor.worldBound);
        }

        private static List<Type> CollectStateBehaviorTypes()
        {
            List<Type> behaviorTypes = new();
            TypeCache.TypeCollection derivedTypes = TypeCache.GetTypesDerivedFrom<XAnimationStateBehavior>();
            for (int i = 0; i < derivedTypes.Count; i++)
            {
                Type type = derivedTypes[i];
                if (type == null ||
                    type.IsAbstract ||
                    type.ContainsGenericParameters ||
                    !typeof(XAnimationStateBehavior).IsAssignableFrom(type) ||
                    type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                behaviorTypes.Add(type);
            }

            behaviorTypes.Sort((left, right) => string.Compare(left.FullName, right.FullName, StringComparison.Ordinal));
            return behaviorTypes;
        }

        private static string GetStateBehaviorMenuName(Type behaviorType)
        {
            return string.IsNullOrWhiteSpace(behaviorType.Namespace)
                ? behaviorType.Name
                : $"{behaviorType.Namespace}/{behaviorType.Name}";
        }

        private VisualElement CreateStateBehaviorRow(
            string channelName,
            string stateKey,
            int behaviorIndex,
            XAnimationStateBehavior behavior,
            bool editable)
        {
            VisualElement row = CreateSubBox();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 2;

            VisualElement header = new();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.minWidth = 0;
            row.Add(header);

            Label typeLabel = new(behavior != null ? behavior.GetType().Name : "Missing Behavior");
            typeLabel.tooltip = behavior?.GetType().FullName ?? "Behavior 反序列化失败，保存时会被跳过。";
            typeLabel.style.flexGrow = 1;
            typeLabel.style.flexShrink = 1;
            typeLabel.style.minWidth = 0;
            typeLabel.style.color = TextNormal;
            typeLabel.style.fontSize = BodyFontSize;
            typeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(typeLabel);

            Button deleteButton = new(() => DeleteStateBehavior(channelName, stateKey, behaviorIndex))
            {
                text = "⌫"
            };
            deleteButton.tooltip = editable ? "删除这个 behavior。" : "Override 资源不能删除 state behavior。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            header.Add(deleteButton);

            if (behavior == null)
            {
                return row;
            }

            FieldInfo[] fields = behavior.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            if (fields.Length == 0)
            {
                AddEmptyLabel(row, "No public fields");
                return row;
            }

            VisualElement fieldsBox = new();
            fieldsBox.style.marginTop = 3;
            fieldsBox.style.flexDirection = FlexDirection.Column;
            row.Add(fieldsBox);

            for (int i = 0; i < fields.Length; i++)
            {
                VisualElement fieldEditor = CreateStateBehaviorFieldEditor(
                    channelName,
                    stateKey,
                    behaviorIndex,
                    behavior,
                    fields[i],
                    editable);
                if (fieldEditor != null)
                {
                    fieldsBox.Add(fieldEditor);
                }
            }

            return row;
        }

        private VisualElement CreateStateBehaviorFieldEditor(
            string channelName,
            string stateKey,
            int behaviorIndex,
            XAnimationStateBehavior behavior,
            FieldInfo fieldInfo,
            bool editable)
        {
            Type fieldType = fieldInfo.FieldType;
            object value = fieldInfo.GetValue(behavior);
            if (fieldType == typeof(string))
            {
                TextField field = new(fieldInfo.Name)
                {
                    value = value as string ?? string.Empty
                };
                field.SetEnabled(editable);
                field.RegisterValueChangedCallback(evt =>
                    SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldInfo.Name, evt.newValue));
                return field;
            }

            if (fieldType == typeof(float))
            {
                FloatField field = new(fieldInfo.Name)
                {
                    value = value is float floatValue ? floatValue : 0f
                };
                field.SetEnabled(editable);
                field.RegisterValueChangedCallback(evt =>
                    SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldInfo.Name, evt.newValue));
                return field;
            }

            if (fieldType == typeof(int))
            {
                IntegerField field = new(fieldInfo.Name)
                {
                    value = value is int intValue ? intValue : 0
                };
                field.SetEnabled(editable);
                field.RegisterValueChangedCallback(evt =>
                    SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldInfo.Name, evt.newValue));
                return field;
            }

            if (fieldType == typeof(bool))
            {
                Toggle field = new(fieldInfo.Name)
                {
                    value = value is bool boolValue && boolValue
                };
                field.SetEnabled(editable);
                field.RegisterValueChangedCallback(evt =>
                    SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldInfo.Name, evt.newValue));
                return field;
            }

            if (fieldType.IsEnum)
            {
                Enum enumValue = value as Enum ?? (Enum)Enum.GetValues(fieldType).GetValue(0);
                EnumField field = new(fieldInfo.Name, enumValue);
                field.SetEnabled(editable);
                field.RegisterValueChangedCallback(evt =>
                    SetStateBehaviorFieldValue(channelName, stateKey, behaviorIndex, fieldInfo.Name, evt.newValue));
                return field;
            }

            Label unsupportedLabel = new($"{fieldInfo.Name}: {fieldType.Name}");
            unsupportedLabel.tooltip = "当前编辑器暂不支持这个字段类型。";
            unsupportedLabel.style.color = TextMuted;
            unsupportedLabel.style.fontSize = BodyFontSize;
            unsupportedLabel.style.marginTop = 2;
            return unsupportedLabel;
        }

        private static void SetCollapsed(HashSet<string> set, string key, bool collapsed)
        {
            if (collapsed)
            {
                set.Add(key);
            }
            else
            {
                set.Remove(key);
            }
        }

        private static void SetExpanded(HashSet<string> set, string key, bool expanded)
        {
            if (expanded)
            {
                set.Add(key);
            }
            else
            {
                set.Remove(key);
            }
        }

        private void RegisterBatchEditStateClipsContextMenu(VisualElement target, string stateKey)
        {
            RegisterBatchEditStateClipsContextMenu(target, null, stateKey);
        }

        private void RegisterBatchEditStateClipsContextMenu(VisualElement target, string channelName, string stateKey)
        {
            if (target == null || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            target.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(
                    "Batch Edit State Clips",
                    _ => OpenBatchClipSettingsForState(channelName, stateKey),
                    _ => CollectAnimationClipsForState(channelName, stateKey).Count > 0
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled);
            }));
        }

        private void RegisterStateLabelContextMenu(EditableLabel label, XAnimationCompiledState state)
        {
            if (label == null || state == null)
            {
                return;
            }

            label.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(
                    "Rename",
                    _ => label.BeginEdit(),
                    _ => label.IsEditing ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Batch Edit State Clips",
                    _ => OpenBatchClipSettingsForState(state.ChannelName, state.Key),
                    _ => CollectAnimationClipsForState(state.ChannelName, state.Key).Count > 0
                        ? DropdownMenuAction.Status.Normal
                        : DropdownMenuAction.Status.Disabled);
            }));
        }

        private void RegisterStateGroupContextMenu(
            EditableLabel label,
            string channelName,
            string groupName)
        {
            if (label == null || string.IsNullOrWhiteSpace(channelName) || string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            label.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                bool selectorParent = IsSelectorKind(m_Session.CompiledAsset.GetStateNode(channelName, groupName).Kind);
                evt.menu.AppendAction(
                    "Rename",
                    _ => label.BeginEdit(),
                    _ => label.IsEditing || m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Add Node/State",
                    _ => AddState(channelName, groupName),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                if (!selectorParent)
                {
                    evt.menu.AppendAction(
                        "Add Node/Normal",
                        _ => AddStateNode(channelName, groupName, XAnimationStateNodeKind.Normal),
                        _ => m_Session == null || m_Session.IsOverrideAsset
                            ? DropdownMenuAction.Status.Disabled
                            : DropdownMenuAction.Status.Normal);
                }

                evt.menu.AppendAction(
                    "Add Node/Index Selector",
                    _ => AddStateNode(channelName, groupName, XAnimationStateNodeKind.Selector),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction(
                    "Add Node/Int Selector",
                    _ => AddStateNode(channelName, groupName, XAnimationStateNodeKind.IntSelector),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                evt.menu.AppendAction(
                    "Add Node/String Selector",
                    _ => AddStateNode(channelName, groupName, XAnimationStateNodeKind.StringSelector),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);


                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Remove Node",
                    _ => RemoveStateGroupNode(channelName, groupName),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);
            }));
        }

        private void RegisterClipPathContextMenu(EditableLabel label, string path)
        {
            if (label == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            label.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(
                    "Rename",
                    _ => label.BeginEdit(),
                    _ => label.IsEditing || m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Add Group",
                    _ => AddClipGroup(path),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                evt.menu.AppendAction(
                    "Add Clip",
                    _ => AddClip(path),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);

                evt.menu.AppendSeparator();

                evt.menu.AppendAction(
                    "Delete Group",
                    _ => DeleteClipPath(path),
                    _ => m_Session == null || m_Session.IsOverrideAsset
                        ? DropdownMenuAction.Status.Disabled
                        : DropdownMenuAction.Status.Normal);
            }));
        }

        private void OpenBatchClipSettingsForState(string stateKey)
        {
            OpenBatchClipSettingsForState(null, stateKey);
        }

        private void OpenBatchClipSettingsForState(string channelName, string stateKey)
        {
            List<AnimationClip> clips = CollectAnimationClipsForState(channelName, stateKey);
            if (clips.Count == 0)
            {
                SetStatus($"state {stateKey} 没有可用于批量设置的动画。", true);
                return;
            }

            XAnimationClipBatchSettingsWindow.ShowWindowWithClips(clips);
        }

        private List<AnimationClip> CollectAnimationClipsForState(string stateKey)
        {
            return CollectAnimationClipsForState(null, stateKey);
        }

        private List<AnimationClip> CollectAnimationClipsForState(string channelName, string stateKey)
        {
            List<AnimationClip> clips = new List<AnimationClip>();
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return clips;
            }

            XAnimationAsset asset = m_Session?.CompiledAsset?.Asset;
            if (asset?.clips == null || asset.clips.Length == 0)
            {
                return clips;
            }

            if (string.IsNullOrWhiteSpace(channelName) && m_Session.CompiledAsset.IsStateKeyAmbiguous(stateKey))
            {
                SetStatus($"State '{stateKey}' 存在于多个 channel，无法通过裸 stateKey 批量编辑。", true);
                return clips;
            }

            XAnimationCompiledState state = string.IsNullOrWhiteSpace(channelName)
                ? m_Session.CompiledAsset.GetState(stateKey)
                : m_Session.CompiledAsset.GetState(channelName, stateKey);
            if (state == null)
            {
                return clips;
            }

            Dictionary<string, XAnimationClipConfig> clipConfigByKey = new Dictionary<string, XAnimationClipConfig>(StringComparer.Ordinal);
            for (int i = 0; i < asset.clips.Length; i++)
            {
                XAnimationClipConfig clipConfig = asset.clips[i];
                if (clipConfig == null || string.IsNullOrWhiteSpace(clipConfig.key))
                {
                    continue;
                }

                clipConfigByKey[clipConfig.key] = clipConfig;
            }

            HashSet<string> addedClipPaths = new HashSet<string>(StringComparer.Ordinal);

            void TryAddClipByKey(string clipKey)
            {
                if (string.IsNullOrWhiteSpace(clipKey) || !clipConfigByKey.TryGetValue(clipKey, out XAnimationClipConfig clipConfig))
                {
                    return;
                }

                AnimationClip clip = XAnimationEditorAssetResolver.ResolveAnimationClip(clipConfig.clipPath);
                if (clip == null)
                {
                    return;
                }

                string resolvedClipPath = XAnimationEditorAssetResolver.BuildClipPath(clip);
                if (!addedClipPaths.Add(resolvedClipPath))
                {
                    return;
                }

                clips.Add(clip);
            }

            switch (state)
            {
                case XAnimationCompiledSingleState singleState:
                    TryAddClipByKey(singleState.Config.clipKey);
                    break;
                case XAnimationCompiledBlend1DState blend1DState:
                    for (int i = 0; i < blend1DState.Samples.Count; i++)
                    {
                        TryAddClipByKey(blend1DState.Samples[i].Config.clipKey);
                    }
                    break;
                case XAnimationCompiledBlend2DSimpleDirectionalState directionalState:
                    for (int i = 0; i < directionalState.Samples.Count; i++)
                    {
                        TryAddClipByKey(directionalState.Samples[i].Config.clipKey);
                    }
                    break;
                case XAnimationCompiledBlend2DFreeformDirectionalState freeformDirectionalState:
                    for (int i = 0; i < freeformDirectionalState.Samples.Count; i++)
                    {
                        TryAddClipByKey(freeformDirectionalState.Samples[i].Config.clipKey);
                    }
                    break;
            }

            return clips;
        }

        private VisualElement CreateParameterPreviewEditor(XAnimationCompiledParameter parameter)
        {
            if (parameter == null)
            {
                return null;
            }

            switch (parameter.Type)
            {
                case XAnimationParameterType.Float:
                {
                    float previewValue = GetPreviewFloatParameterValue(parameter);
                    if (TryGetBlend1DPreviewRange(parameter.Name, out float min, out float max) ||
                        TryGetDirectionalPreviewRange(parameter.Name, out min, out max))
                    {
                        return CreateFloatPreviewParameterRow(parameter.Name, previewValue, min, max, useSlider: true);
                    }

                    return CreateFloatPreviewParameterRow(parameter.Name, previewValue, previewValue, previewValue, useSlider: false);
                }
                case XAnimationParameterType.Bool:
                    return CreateBoolPreviewParameterRow(parameter.Name, GetPreviewBoolParameterValue(parameter));
                case XAnimationParameterType.Int:
                    return CreateIntPreviewParameterRow(parameter.Name, GetPreviewIntParameterValue(parameter));
                case XAnimationParameterType.String:
                    return CreateStringPreviewParameterRow(parameter.Name, GetPreviewStringParameterValue(parameter));
                default:
                    return null;
            }
        }

        private static bool IsDirectionalBlendStateType(XAnimationStateType stateType)
        {
            return stateType == XAnimationStateType.Blend2DSimpleDirectional ||
                   stateType == XAnimationStateType.Blend2DFreeformDirectional;
        }

        private static bool IsBlendStateType(XAnimationStateType stateType)
        {
            return stateType == XAnimationStateType.Blend1D ||
                   IsDirectionalBlendStateType(stateType);
        }

        private static bool TryGetDirectionalBlendSamples(
            XAnimationCompiledState state,
            out IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> samples)
        {
            switch (state)
            {
                case XAnimationCompiledBlend2DSimpleDirectionalState simpleState:
                    samples = simpleState.Samples;
                    return true;
                case XAnimationCompiledBlend2DFreeformDirectionalState freeformState:
                    samples = freeformState.Samples;
                    return true;
                default:
                    samples = null;
                    return false;
            }
        }

        private bool TryGetBlend1DPreviewRange(string parameterName, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            bool found = false;
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] is not XAnimationCompiledBlend1DState blendState)
                {
                    continue;
                }

                if (!string.Equals(blendState.Config.parameterName, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (blendState.Samples.Count == 0)
                {
                    continue;
                }

                float stateMin = blendState.Samples[0].Threshold;
                float stateMax = blendState.Samples[0].Threshold;
                for (int sampleIndex = 1; sampleIndex < blendState.Samples.Count; sampleIndex++)
                {
                    float threshold = blendState.Samples[sampleIndex].Threshold;
                    stateMin = Mathf.Min(stateMin, threshold);
                    stateMax = Mathf.Max(stateMax, threshold);
                }

                if (!found)
                {
                    min = stateMin;
                    max = stateMax;
                    found = true;
                }
                else
                {
                    min = Mathf.Min(min, stateMin);
                    max = Mathf.Max(max, stateMax);
                }
            }

            if (!found)
            {
                return false;
            }

            if (Mathf.Approximately(min, max))
            {
                max = min + 1f;
            }

            return true;
        }

        private bool TryGetDirectionalPreviewRange(string parameterName, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            bool found = false;
            IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
            for (int i = 0; i < states.Count; i++)
            {
                XAnimationCompiledState state = states[i];
                if (!TryGetDirectionalBlendSamples(state, out IReadOnlyList<XAnimationCompiledBlend2DSimpleDirectionalSample> samples))
                {
                    continue;
                }

                bool matchesX = string.Equals(state.Config.parameterXName, parameterName, StringComparison.Ordinal);
                bool matchesY = string.Equals(state.Config.parameterYName, parameterName, StringComparison.Ordinal);
                if (!matchesX && !matchesY)
                {
                    continue;
                }

                if (samples.Count == 0)
                {
                    continue;
                }

                for (int sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                {
                    float sampleValue = matchesX ? samples[sampleIndex].Config.positionX : samples[sampleIndex].Config.positionY;
                    if (!found)
                    {
                        min = sampleValue;
                        max = sampleValue;
                        found = true;
                    }
                    else
                    {
                        min = Mathf.Min(min, sampleValue);
                        max = Mathf.Max(max, sampleValue);
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            if (Mathf.Approximately(min, max))
            {
                max = min + 1f;
            }

            return true;
        }

        private VisualElement CreateFloatPreviewParameterRow(
            string parameterName,
            float defaultValue,
            float min,
            float max,
            bool useSlider)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            Label label = new(parameterName);
            label.style.width = 82;
            label.style.flexShrink = 0;
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            FloatField valueField = new()
            {
                value = defaultValue
            };
            valueField.tooltip = "预览参数值，只影响当前 Preview Session，不保存到资源。";
            ConfigureCompactNumberField(valueField);

            if (useSlider)
            {
                Slider slider = new(min, max)
                {
                    value = defaultValue
                };
                slider.tooltip = $"Blend 参数范围来自 samples: [{min:0.###}, {max:0.###}]。";
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(evt =>
                {
                    valueField.SetValueWithoutNotify(evt.newValue);
                    SetPreviewFloatParameter(parameterName, evt.newValue);
                });
                valueField.RegisterValueChangedCallback(evt =>
                {
                    slider.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, min, max));
                    SetPreviewFloatParameter(parameterName, evt.newValue);
                });
                row.Add(slider);
                row.Add(valueField);
            }
            else
            {
                valueField.style.flexGrow = 1;
                valueField.style.width = StyleKeyword.Auto;
                valueField.style.minWidth = 64;
                valueField.style.maxWidth = StyleKeyword.None;
                valueField.RegisterValueChangedCallback(evt => SetPreviewFloatParameter(parameterName, evt.newValue));
                row.Add(valueField);
            }

            Button zeroButton = new(() =>
            {
                valueField.SetValueWithoutNotify(0f);
                SetPreviewFloatParameter(parameterName, 0f);
            })
            {
                text = "0"
            };
            zeroButton.tooltip = "把这个预览参数重置为 0。";
            ApplyClipIconButtonStyle(zeroButton);
            zeroButton.style.marginLeft = 4;
            row.Add(zeroButton);

            return row;
        }

        private List<XAnimationCompiledParameter> GetFloatParameters()
        {
            List<XAnimationCompiledParameter> parameters = new();
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return parameters;
            }

            IReadOnlyList<XAnimationCompiledParameter> compiledParameters = m_Session.CompiledAsset.Parameters;
            for (int i = 0; i < compiledParameters.Count; i++)
            {
                XAnimationCompiledParameter parameter = compiledParameters[i];
                if (parameter.Type == XAnimationParameterType.Float)
                {
                    parameters.Add(parameter);
                }
            }

            return parameters;
        }

        private void SetPreviewFloatParameter(string parameterName, float value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (TrySetPreviewParameter(parameterName, value))
            {
                RefreshPreviewAfterParameterChanged();
            }
        }

        private bool TrySetPreviewParameter(string parameterName, float value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            try
            {
                m_Session.SetPreviewParameter(parameterName, value);
                SetStatus($"Preview parameter {parameterName} = {value:0.###}。");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
                return false;
            }
        }

        private bool TrySetPreviewParameter(string parameterName, bool value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            try
            {
                m_Session.SetPreviewParameter(parameterName, value);
                SetStatus($"Preview parameter {parameterName} = {value}。");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
                return false;
            }
        }

        private bool TrySetPreviewParameter(string parameterName, int value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            try
            {
                m_Session.SetPreviewParameter(parameterName, value);
                SetStatus($"Preview parameter {parameterName} = {value}。");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
                return false;
            }
        }

        private bool TrySetPreviewParameter(string parameterName, string value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            try
            {
                m_Session.SetPreviewParameter(parameterName, value);
                SetStatus($"Preview parameter {parameterName} = {value}。");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                Debug.LogException(ex);
                return false;
            }
        }

        private void RefreshPreviewAfterParameterChanged(bool rebuildParameterList = false)
        {
            if (rebuildParameterList)
            {
                RebuildParameterList();
            }

            m_Session?.SyncPreviewFrame();
            RefreshStatePlayingStates();
            RefreshChannelStates();
            RenderPreview();
            Repaint();
        }

        private VisualElement CreateBoolPreviewParameterRow(string parameterName, bool value)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            Label label = new(parameterName);
            label.style.width = 82;
            label.style.flexShrink = 0;
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            Toggle toggle = new("value")
            {
                value = value
            };
            toggle.tooltip = "预览参数值，只影响当前 Preview Session，不保存到资源。";
            toggle.style.flexGrow = 1;
            toggle.RegisterValueChangedCallback(evt => SetPreviewBoolParameter(parameterName, evt.newValue));
            row.Add(toggle);

            return row;
        }

        private void SetPreviewBoolParameter(string parameterName, bool value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (TrySetPreviewParameter(parameterName, value))
            {
                RefreshPreviewAfterParameterChanged();
            }
        }

        private VisualElement CreateIntPreviewParameterRow(string parameterName, int value)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            Label label = new(parameterName);
            label.style.width = 82;
            label.style.flexShrink = 0;
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            IntegerField valueField = new("value")
            {
                value = value
            };
            valueField.tooltip = "预览参数值，只影响当前 Preview Session，不保存到资源。";
            valueField.style.flexGrow = 1;
            valueField.RegisterValueChangedCallback(evt => SetPreviewIntParameter(parameterName, evt.newValue));
            row.Add(valueField);

            Button zeroButton = new(() =>
            {
                valueField.SetValueWithoutNotify(0);
                SetPreviewIntParameter(parameterName, 0);
            })
            {
                text = "0"
            };
            zeroButton.tooltip = "把这个预览参数重置为 0。";
            ApplyClipIconButtonStyle(zeroButton);
            zeroButton.style.marginLeft = 4;
            row.Add(zeroButton);

            return row;
        }

        private void SetPreviewIntParameter(string parameterName, int value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (TrySetPreviewParameter(parameterName, value))
            {
                RefreshPreviewAfterParameterChanged();
            }
        }

        private VisualElement CreateStringPreviewParameterRow(string parameterName, string value)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            Label label = new(parameterName);
            label.style.width = 82;
            label.style.flexShrink = 0;
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            TextField valueField = new("value")
            {
                value = value
            };
            valueField.tooltip = "预览参数值，只影响当前 Preview Session，不保存到资源。";
            valueField.style.flexGrow = 1;
            valueField.RegisterValueChangedCallback(evt => SetPreviewStringParameter(parameterName, evt.newValue));
            row.Add(valueField);
            return row;
        }

        private void SetPreviewStringParameter(string parameterName, string value)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return;
            }

            if (TrySetPreviewParameter(parameterName, value))
            {
                RefreshPreviewAfterParameterChanged();
            }
        }

        private float GetPreviewFloatParameterValue(XAnimationCompiledParameter parameter)
        {
            if (parameter == null)
            {
                return 0f;
            }

            if (m_Session != null && m_Session.TryGetPreviewParameter(parameter.Name, out float value))
            {
                return value;
            }

            return ConvertParameterDefaultToFloat(parameter.Config.defaultValue);
        }

        private bool GetPreviewBoolParameterValue(XAnimationCompiledParameter parameter)
        {
            if (parameter == null)
            {
                return false;
            }

            if (m_Session != null && m_Session.TryGetPreviewParameter(parameter.Name, out bool value))
            {
                return value;
            }

            return ConvertParameterDefaultToBool(parameter.Config.defaultValue);
        }

        private int GetPreviewIntParameterValue(XAnimationCompiledParameter parameter)
        {
            if (parameter == null)
            {
                return 0;
            }

            if (m_Session != null && m_Session.TryGetPreviewParameter(parameter.Name, out int value))
            {
                return value;
            }

            return ConvertParameterDefaultToInt(parameter.Config.defaultValue);
        }

        private string GetPreviewStringParameterValue(XAnimationCompiledParameter parameter)
        {
            if (parameter == null)
            {
                return string.Empty;
            }

            if (m_Session != null && m_Session.TryGetPreviewParameter(parameter.Name, out string value))
            {
                return value;
            }

            return ConvertParameterDefaultToString(parameter.Config.defaultValue);
        }

        private VisualElement CreateBlendSampleRow(string channelName, string stateKey, int sampleIndex, XAnimationBlend1DSampleConfig sample, bool editable)
        {
            VisualElement row = CreateSubBox();
            string sampleClipKey = sample?.clipKey ?? string.Empty;
            string rowKey = BuildBlendSampleRuntimeKey(channelName, stateKey, sampleIndex);
            VisualElement weightFill = CreateProgressFill(BlendWeightFillBg);
            row.Add(weightFill);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 1;
            row.style.position = Position.Relative;
            row.style.overflow = Overflow.Hidden;
            m_BlendSampleRowMap[rowKey] = new RowVisualState
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

            XAnimationEditorSelectionField clipField = CreateClipSelectionField(string.Empty, sampleClipKey);
            clipField.SetEnabled(editable);
            clipField.style.flexGrow = 1;
            clipField.style.position = Position.Relative;
            clipField.ValueChanged += (previousValue, newValue) => ChangeBlendSampleClipKey(channelName, stateKey, sampleIndex, newValue, clipField, previousValue);
            AttachClipKeyPingButton(clipField, sampleClipKey, editable);
            row.Add(clipField);

            Label thresholdLabel = new("threshold");
            thresholdLabel.style.marginLeft = 6;
            thresholdLabel.style.marginRight = 4;
            thresholdLabel.style.flexShrink = 0;
            thresholdLabel.style.color = TextMuted;
            thresholdLabel.style.fontSize = 10;
            thresholdLabel.style.whiteSpace = WhiteSpace.NoWrap;
            thresholdLabel.style.position = Position.Relative;
            row.Add(thresholdLabel);

            FloatField thresholdField = new()
            {
                value = sample?.threshold ?? 0f
            };
            thresholdField.SetEnabled(editable);
            thresholdField.tooltip = "一维 Blend 轴上的采样位置，必须保持严格递增。";
            ConfigureCompactNumberField(thresholdField);
            thresholdField.style.width = 76;
            thresholdField.style.minWidth = 76;
            thresholdField.style.maxWidth = 76;
            thresholdField.style.position = Position.Relative;
            thresholdField.RegisterValueChangedCallback(evt => ChangeBlendSampleThreshold(channelName, stateKey, sampleIndex, evt.newValue, thresholdField, evt.previousValue));
            row.Add(thresholdField);

            Button previewButton = new(() => PreviewBlendSample(channelName, stateKey, thresholdField.value))
            {
                text = "▶"
            };
            previewButton.tooltip = "预览这个 Blend1D 采样点，并把绑定参数设置到当前 threshold。";
            ApplyClipButtonStyle(previewButton, false);
            previewButton.style.marginLeft = 4;
            previewButton.style.position = Position.Relative;
            row.Add(previewButton);

            Button deleteButton = new(() => DeleteBlendSample(channelName, stateKey, sampleIndex))
            {
                text = "⌫"
            };
            deleteButton.tooltip = editable ? "删除这个采样点。" : "Override 资源不能删除采样点。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;
            deleteButton.style.position = Position.Relative;
            row.Add(deleteButton);

            return row;
        }

        private VisualElement CreateDirectionalBlendSampleRow(
            string channelName,
            string stateKey,
            int sampleIndex,
            XAnimationBlend2DSimpleDirectionalSampleConfig sample,
            bool editable)
        {
            VisualElement row = CreateSubBox();
            string sampleClipKey = sample?.clipKey ?? string.Empty;
            string rowKey = BuildBlendSampleRuntimeKey(channelName, stateKey, sampleIndex);
            VisualElement weightFill = CreateProgressFill(BlendWeightFillBg);
            row.Add(weightFill);
            row.style.flexDirection = FlexDirection.Column;
            row.style.alignItems = Align.Stretch;
            row.style.marginBottom = 1;
            row.style.position = Position.Relative;
            row.style.overflow = Overflow.Hidden;
            m_BlendSampleRowMap[rowKey] = new RowVisualState
            {
                BaseColor = new Color(0.14f, 0.14f, 0.15f, 1f),
                ProgressFill = weightFill,
            };

            VisualElement positionRow = Row();
            positionRow.style.position = Position.Relative;
            positionRow.style.marginBottom = 2;
            row.Add(positionRow);

            Label indexLabel = new($"#{sampleIndex}");
            indexLabel.style.width = 28;
            indexLabel.style.flexShrink = 0;
            indexLabel.style.color = TextMuted;
            indexLabel.style.fontSize = BodyFontSize;
            indexLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            indexLabel.style.position = Position.Relative;
            positionRow.Add(indexLabel);

            Label xLabel = new("x");
            xLabel.style.marginLeft = 0;
            xLabel.style.marginRight = 4;
            xLabel.style.flexShrink = 0;
            xLabel.style.color = TextMuted;
            xLabel.style.fontSize = 10;
            xLabel.style.position = Position.Relative;
            positionRow.Add(xLabel);

            FloatField xField = new() { value = sample?.positionX ?? 0f };
            xField.SetEnabled(editable);
            ConfigureCompactNumberField(xField);
            xField.style.width = 60;
            xField.style.minWidth = 60;
            xField.style.maxWidth = 60;
            xField.style.position = Position.Relative;
            xField.RegisterValueChangedCallback(evt =>
                ChangeDirectionalBlendSamplePosition(
                    channelName,
                    stateKey,
                    sampleIndex,
                    evt.newValue,
                    sample?.positionY ?? 0f,
                    xField,
                    evt.previousValue,
                    sample?.positionY ?? 0f,
                    true));
            positionRow.Add(xField);

            Label yLabel = new("y");
            yLabel.style.marginLeft = 6;
            yLabel.style.marginRight = 4;
            yLabel.style.flexShrink = 0;
            yLabel.style.color = TextMuted;
            yLabel.style.fontSize = 10;
            yLabel.style.position = Position.Relative;
            positionRow.Add(yLabel);

            FloatField yField = new() { value = sample?.positionY ?? 0f };
            yField.SetEnabled(editable);
            ConfigureCompactNumberField(yField);
            yField.style.width = 60;
            yField.style.minWidth = 60;
            yField.style.maxWidth = 60;
            yField.style.position = Position.Relative;
            yField.RegisterValueChangedCallback(evt =>
                ChangeDirectionalBlendSamplePosition(
                    channelName,
                    stateKey,
                    sampleIndex,
                    sample?.positionX ?? 0f,
                    evt.newValue,
                    yField,
                    sample?.positionX ?? 0f,
                    evt.previousValue,
                    false));
            positionRow.Add(yField);

            VisualElement clipRow = Row();
            clipRow.style.position = Position.Relative;
            row.Add(clipRow);

            XAnimationEditorSelectionField clipField = CreateClipSelectionField(string.Empty, sampleClipKey);
            clipField.SetEnabled(editable);
            clipField.style.flexGrow = 1;
            clipField.style.flexShrink = 1;
            clipField.style.minWidth = 0;
            clipField.style.position = Position.Relative;
            clipField.ValueChanged += (previousValue, newValue) =>
                ChangeDirectionalBlendSampleClipKey(channelName, stateKey, sampleIndex, newValue, clipField, previousValue);
            AttachClipKeyPingButton(clipField, sampleClipKey, editable);
            clipRow.Add(clipField);

            Button previewButton = new(() => PreviewDirectionalBlendSample(channelName, stateKey, xField.value, yField.value))
            {
                text = "▶"
            };
            previewButton.tooltip = "预览这个二维采样点，并把绑定参数设置到当前 (x, y)。";
            ApplyClipButtonStyle(previewButton, false);
            previewButton.style.marginLeft = 4;
            previewButton.style.position = Position.Relative;
            clipRow.Add(previewButton);

            Button deleteButton = new(() => DeleteDirectionalBlendSample(channelName, stateKey, sampleIndex))
            {
                text = "⌫"
            };
            deleteButton.tooltip = editable ? "删除这个采样点。" : "Override 资源不能删除采样点。";
            deleteButton.SetEnabled(editable);
            ApplyTrashButtonIcon(deleteButton);
            ApplyClipIconButtonStyle(deleteButton);
            deleteButton.style.marginLeft = 4;
            deleteButton.style.position = Position.Relative;
            clipRow.Add(deleteButton);

            return row;
        }

        private DropdownField CreateChannelDropdown(string label, string currentValue)
        {
            List<string> choices = new();
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    choices.Add(channels[i].Name);
                }
            }

            EnsureDropdownChoice(choices, currentValue);
            DropdownField field = new(label, choices, Mathf.Max(0, choices.IndexOf(currentValue ?? string.Empty)));
            ApplyDropdownFieldStyle(field);
            return field;
        }

        private void RefreshPlayTargetChannelChoices()
        {
            List<string> choices = new();
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledChannel> channels = m_Session.CompiledAsset.Channels;
                for (int i = 0; i < channels.Count; i++)
                {
                    choices.Add(channels[i].Name);
                }
            }

            string selected = !string.IsNullOrWhiteSpace(m_PlayTargetChannelName) && choices.Contains(m_PlayTargetChannelName)
                ? m_PlayTargetChannelName
                    : choices.Count > 0
                        ? choices[0]
                        : string.Empty;

            m_PlayTargetChannelName = selected;
            m_PlaybackHudView?.Refresh();
        }

        private XAnimationTransitionOptions BuildPreviewTransitionOptions()
        {
            if (!m_ApplyTransitionRequestOverrides)
            {
                return null;
            }

            return new XAnimationTransitionOptions
            {
                fadeIn = Mathf.Max(0f, m_PlayFadeInOverride),
                fadeOut = Mathf.Max(0f, m_PlayFadeOutOverride),
                priority = m_PlayPriorityOverride,
                interruptible = m_PlayInterruptibleOverride,
                enterTime = Mathf.Clamp01(m_PlayEnterTimeOverride),
            };
        }

        private XAnimationEditorSelectionField CreateClipSelectionField(string label, string currentValue)
        {
            XAnimationEditorSelectionField field = new(label, currentValue, _ => { });
            void ShowMenu(XAnimationEditorSelectionField target)
            {
                List<ClipSelectionItem> items = CollectSelectableClips();
                List<SearchableSelectionItem> entries = BuildClipSelectionEntries(items);
                SearchableSelectionWindow.Show(
                    GetSelectionActivatorRect(target),
                    "Select Clip",
                    target.value,
                    entries,
                    selected => target.value = selected);
            }

            field = new XAnimationEditorSelectionField(label, currentValue, ShowMenu);
            return field;
        }

        private void AttachClipKeyPingButton(XAnimationEditorSelectionField clipField, string clipKey, bool enabled)
        {
            if (clipField == null)
            {
                return;
            }

            Button clipItemButton = CreateEmbeddedDropdownButton(
                "↗",
                "定位到 Clips 面板里当前 clipKey 对应的条目。",
                enabled && HasClipAsset(clipField?.value ?? clipKey),
                () => FocusClipInInspector(clipField?.value ?? clipKey),
                marginLeft: 4,
                marginRight: 2);

            Button pingButton = CreateEmbeddedDropdownButton(
                "◎",
                "定位当前 clipKey 对应的 AnimationClip 资源。",
                enabled && HasClipAsset(clipField?.value ?? clipKey),
                () => PingClipAsset(clipField?.value ?? clipKey),
                marginLeft: 2,
                marginRight: 4);

            clipField.ValueChanged += (_, newValue) =>
            {
                bool canLocate = enabled && HasClipAsset(newValue);
                clipItemButton.SetEnabled(canLocate);
                pingButton.SetEnabled(canLocate);
            };

            clipField.AddTrailingElement(clipItemButton);
            clipField.AddTrailingElement(pingButton);
        }

        private void AttachStatePingButton(XAnimationEditorSelectionField stateField, string stateKey, bool enabled)
        {
            if (stateField == null)
            {
                return;
            }

            Button stateItemButton = CreateEmbeddedDropdownButton(
                "↗",
                "定位到 States 面板里当前 stateKey 对应的条目。",
                enabled && HasState(stateField?.value ?? stateKey),
                () => FocusStateInInspector(stateField?.value ?? stateKey),
                marginLeft: 4,
                marginRight: 4);

            stateField.ValueChanged += (_, newValue) =>
            {
                stateItemButton.SetEnabled(enabled && HasState(newValue));
            };

            stateField.AddTrailingElement(stateItemButton);
        }

        private void AttachDropdownInspectorButton(
            DropdownField dropdown,
            Func<string> currentValueGetter,
            Func<bool> canLocate,
            Action onLocate,
            string tooltip)
        {
            if (dropdown == null)
            {
                return;
            }

            Button locateButton = CreateEmbeddedDropdownButton(
                "↗",
                tooltip,
                canLocate(),
                onLocate,
                marginLeft: 4,
                marginRight: 4);

            dropdown.RegisterValueChangedCallback(_ =>
            {
                locateButton.SetEnabled(canLocate());
            });

            AttachDropdownButtons(dropdown, locateButton);
        }

        private static Button CreateEmbeddedDropdownButton(
            string text,
            string tooltip,
            bool enabled,
            Action onClick,
            int marginLeft,
            int marginRight)
        {
            Button button = new(onClick)
            {
                text = text
            };
            button.tooltip = tooltip;
            button.SetEnabled(enabled);
            ApplyClipIconButtonStyle(button);
            button.style.marginLeft = marginLeft;
            button.style.marginRight = marginRight;
            button.style.flexShrink = 0;
            return button;
        }

        private static void AttachDropdownButtons(DropdownField dropdown, params Button[] buttons)
        {
            if (dropdown == null || buttons == null || buttons.Length == 0)
            {
                return;
            }

            void TryAttach()
            {
                VisualElement input = dropdown.Q<VisualElement>(className: "unity-base-field__input");
                if (input == null)
                {
                    return;
                }

                VisualElement arrow = input.Q<VisualElement>(className: "unity-base-popup-field__arrow");
                if (arrow == null)
                {
                    return;
                }

                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    if (button == null || button.parent != null)
                    {
                        continue;
                    }

                    int arrowIndex = input.IndexOf(arrow);
                    input.Insert(Mathf.Max(0, arrowIndex), button);
                }
            }

            TryAttach();
            dropdown.RegisterCallback<AttachToPanelEvent>(_ => TryAttach());
        }

        private bool HasClipAsset(string clipKey)
        {
            return TryGetClipAsset(clipKey, out _);
        }

        private void PingClipAsset(string clipKey)
        {
            if (!TryGetClipAsset(clipKey, out AnimationClip clip))
            {
                SetStatus(string.IsNullOrWhiteSpace(clipKey)
                    ? "当前没有可定位的 clipKey。"
                    : $"没有找到 clipKey '{clipKey}' 对应的 AnimationClip 资源。", true);
                return;
            }

            EditorGUIUtility.PingObject(clip);
            SetStatus($"已定位动画资源: {clip.name}。");
        }

        private bool TryGetClipAsset(string clipKey, out AnimationClip clip)
        {
            clip = null;
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(clipKey))
            {
                return false;
            }

            try
            {
                clip = m_Session.CompiledAsset.GetClip(clipKey).Clip;
            }
            catch (Exception)
            {
                clip = null;
            }

            return clip != null;
        }

        private DropdownField CreateStateKeyDropdown(string label, string currentValue, string excludeStateKey = null, bool includeNone = false)
        {
            const string noneChoice = "None";
            List<string> choices = new();
            if (includeNone)
            {
                choices.Add(noneChoice);
            }

            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
                for (int i = 0; i < states.Count; i++)
                {
                    string stateKey = states[i].Key;
                    if (string.Equals(stateKey, excludeStateKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    choices.Add(stateKey);
                }
            }

            string selected = string.IsNullOrWhiteSpace(currentValue) && includeNone ? noneChoice : currentValue ?? string.Empty;
            EnsureDropdownChoice(choices, selected);
            DropdownField field = new(label, choices, Mathf.Max(0, choices.IndexOf(selected)));
            ApplyDropdownFieldStyle(field);
            return field;
        }

        private XAnimationEditorSelectionField CreateAutoTransitionPreStateSelectionField(string label, string currentValue, string channelName)
        {
            void ShowMenu(XAnimationEditorSelectionField target)
            {
                List<SearchableSelectionItem> entries = BuildAutoTransitionPreStateSelectionEntries(channelName, target.value);
                SearchableSelectionWindow.Show(
                    GetSelectionActivatorRect(target),
                    "Select Auto Transition Pre State",
                    target.value,
                    entries,
                    selected => target.value = selected);
            }

            XAnimationEditorSelectionField field = new(label, currentValue, ShowMenu);
            AttachStatePingButton(field, currentValue, enabled: true);
            return field;
        }

        private List<SearchableSelectionItem> BuildAutoTransitionPreStateSelectionEntries(string channelName, string currentValue)
        {
            List<SearchableSelectionItem> entries = new();
            HashSet<string> occupiedPreStates = new(StringComparer.Ordinal);
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledAutoTransition> autoTransitions = m_Session.CompiledAsset.AutoTransitions;
                for (int i = 0; i < autoTransitions.Count; i++)
                {
                    XAnimationCompiledAutoTransition transition = autoTransitions[i];
                    if (transition == null ||
                        !string.Equals(transition.ChannelName, channelName, StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(transition.PreStateKey) ||
                        string.Equals(transition.PreStateKey, currentValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    occupiedPreStates.Add(transition.PreStateKey);
                }

                IReadOnlyList<XAnimationCompiledState> states = m_Session.CompiledAsset.States;
                for (int i = 0; i < states.Count; i++)
                {
                    XAnimationCompiledState state = states[i];
                    if (!string.Equals(state.ChannelName, channelName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string stateKey = state.Key;
                    bool isCurrent = string.Equals(stateKey, currentValue, StringComparison.Ordinal);
                    bool isLoop = state.Config.loop;
                    bool isOccupied = occupiedPreStates.Contains(stateKey);
                    bool isEnabled = isCurrent || (!isLoop && !isOccupied);
                    string parentPath = GetStatePathParent(stateKey);
                    bool hasParentPath = !string.IsNullOrWhiteSpace(parentPath);
                    string title = hasParentPath
                        ? $"{state.ChannelName} - {FormatStateDisplayPath(parentPath)} / {GetStatePathLeafName(stateKey)}"
                        : $"{state.ChannelName} - {stateKey}";
                    string detail = isLoop && !isCurrent
                        ? "循环 state 不能配置 Auto Transition"
                        : isOccupied
                            ? "已存在 Auto Transition"
                            : hasParentPath
                                ? $"path={FormatStateDisplayPath(parentPath)}"
                                : string.Empty;
                    string searchText = $"{stateKey} {state.ChannelName} {parentPath} {title} {detail}";
                    string groupKey = hasParentPath ? $"{state.ChannelName} - {parentPath}" : string.Empty;
                    entries.Add(new SearchableSelectionItem(stateKey, title, detail, searchText, groupKey, isEnabled: isEnabled));
                }
            }

            return entries;
        }

        private bool HasState(string stateKey)
        {
            return m_Session != null &&
                   m_Session.IsLoaded &&
                   !string.IsNullOrWhiteSpace(stateKey) &&
                   m_Session.CompiledAsset.TryGetStateNodeIndex(stateKey, out int nodeIndex) &&
                   m_Session.CompiledAsset.StateNodes[nodeIndex].IsPlayable;
        }

        private bool HasChannel(string channelName)
        {
            return m_Session != null &&
                   m_Session.IsLoaded &&
                   !string.IsNullOrWhiteSpace(channelName) &&
                   m_Session.CompiledAsset.TryGetChannelIndex(channelName, out _);
        }

        private DropdownField CreateFloatParameterDropdown(string label, string currentValue)
        {
            List<string> choices = new();
            if (m_Session != null && m_Session.IsLoaded)
            {
                IReadOnlyList<XAnimationCompiledParameter> parameters = m_Session.CompiledAsset.Parameters;
                for (int i = 0; i < parameters.Count; i++)
                {
                    XAnimationCompiledParameter parameter = parameters[i];
                    if (parameter.Type == XAnimationParameterType.Float)
                    {
                        choices.Add(parameter.Name);
                    }
                }
            }

            EnsureDropdownChoice(choices, currentValue);
            DropdownField field = new(label, choices, Mathf.Max(0, choices.IndexOf(currentValue ?? string.Empty)));
            ApplyDropdownFieldStyle(field);
            return field;
        }

        private static void EnsureDropdownChoice(List<string> choices, string currentValue)
        {
            currentValue ??= string.Empty;
            if (choices.Count == 0 || !choices.Contains(currentValue))
            {
                choices.Insert(0, currentValue);
            }
        }

        private static string NormalizeOptionalStateDropdownValue(string value)
        {
            return string.Equals(value, "None", StringComparison.Ordinal) ? string.Empty : value ?? string.Empty;
        }

        private static string NormalizeStatePath(string path)
        {
            return XAnimationStatePathUtility.NormalizePath(path);
        }

        private static string FormatStateDisplayPath(string path)
        {
            return XAnimationStatePathUtility.FormatDisplayPath(path);
        }

        private static string GetStatePathParent(string path)
        {
            return XAnimationStatePathUtility.GetParentPath(path);
        }

        private static string GetStatePathLeafName(string path)
        {
            return XAnimationStatePathUtility.GetLeafName(path);
        }

        private static List<string> SplitStatePathSegments(string path)
        {
            List<string> segments = new();
            string normalizedPath = NormalizeStatePath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return segments;
            }

            string[] rawSegments = normalizedPath.Split('/');
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = rawSegments[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    segments.Add(segment);
                }
            }

            return segments;
        }

        private static string BuildStatePathKey(string parentPath, string leafName)
        {
            return XAnimationStatePathUtility.BuildPath(parentPath, leafName);
        }

        private static ClipPathInfo BuildClipPathInfo(XAnimationCompiledClip clip)
        {
            string fullPath = NormalizeClipPathKey(clip?.Key);
            string parentPath = GetClipPathParent(fullPath);
            string leafName = GetClipPathLeafName(fullPath);
            string displayPath = FormatClipDisplayPath(fullPath);
            return new ClipPathInfo(clip, fullPath, parentPath, leafName, displayPath);
        }

        private static string NormalizeClipPathKey(string path)
        {
            List<string> segments = new();
            AddClipPathSegments(segments, path);
            return segments.Count == 0 ? string.Empty : string.Join("/", segments);
        }

        private static string FormatClipDisplayPath(string path)
        {
            string normalizedPath = NormalizeClipPathKey(path);
            return string.IsNullOrWhiteSpace(normalizedPath)
                ? string.Empty
                : normalizedPath.Replace("/", " / ");
        }

        private static List<string> SplitClipPathSegments(string path)
        {
            List<string> segments = new();
            AddClipPathSegments(segments, path);
            return segments;
        }

        private static void AddClipPathSegments(List<string> segments, string path)
        {
            if (segments == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string[] rawSegments = path.Split('/');
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = rawSegments[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    segments.Add(segment);
                }
            }
        }

        private static string GetClipPathParent(string path)
        {
            string normalizedPath = NormalizeClipPathKey(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex > 0 ? normalizedPath[..slashIndex] : string.Empty;
        }

        private static string GetClipPathLeafName(string path)
        {
            string normalizedPath = NormalizeClipPathKey(path);
            int slashIndex = normalizedPath.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex + 1 < normalizedPath.Length
                ? normalizedPath[(slashIndex + 1)..]
                : normalizedPath;
        }

        private static string BuildStateGroupKey(string channelName, string groupName)
        {
            return $"{channelName ?? string.Empty}::{NormalizeStatePath(groupName)}";
        }

        private static string BuildClipPathKey(string groupName)
        {
            return NormalizeClipPathKey(groupName);
        }

        private bool IsStateGroupCollapsed(string groupKey)
        {
            return !string.IsNullOrWhiteSpace(groupKey) && !m_ExpandedStateGroupKeys.Contains(groupKey);
        }

        private void SetStateGroupCollapsed(string groupKey, bool collapsed)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            if (collapsed)
            {
                m_ExpandedStateGroupKeys.Remove(groupKey);
            }
            else
            {
                m_ExpandedStateGroupKeys.Add(groupKey);
            }
        }

        private void ExpandStateGroupForState(string stateKey)
        {
            if (m_Session?.CompiledAsset != null && m_Session.CompiledAsset.IsStateKeyAmbiguous(stateKey))
            {
                return;
            }

            ExpandStateGroupForState(FindStateChannelName(stateKey), stateKey);
        }

        private void ExpandStateGroupForState(string channelName, string stateKey)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(stateKey))
            {
                return;
            }

            XAnimationCompiledState state = string.IsNullOrWhiteSpace(channelName)
                ? m_Session.CompiledAsset.GetState(stateKey)
                : m_Session.CompiledAsset.GetState(channelName, stateKey);
            string parentPath = GetStatePathParent(state?.Key);
            List<string> segments = SplitStatePathSegments(parentPath);
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = BuildStatePathKey(currentPath, segments[i]);
                SetStateGroupCollapsed(BuildStateGroupKey(state.ChannelName, currentPath), false);
            }
        }

        private bool IsClipPathCollapsed(string groupKey)
        {
            return !string.IsNullOrWhiteSpace(groupKey) && !m_ExpandedClipPathKeys.Contains(groupKey);
        }

        private void SetClipPathCollapsed(string groupKey, bool collapsed)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            if (collapsed)
            {
                m_ExpandedClipPathKeys.Remove(groupKey);
            }
            else
            {
                m_ExpandedClipPathKeys.Add(groupKey);
            }
        }

        private void ExpandClipPath(string path)
        {
            List<string> segments = SplitClipPathSegments(path);
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? segments[i]
                    : $"{currentPath}/{segments[i]}";
                SetClipPathCollapsed(BuildClipPathKey(currentPath), false);
            }
        }

        private void ExpandClipPathForClip(string clipKey)
        {
            if (m_Session == null || !m_Session.IsLoaded || string.IsNullOrWhiteSpace(clipKey))
            {
                return;
            }

            XAnimationCompiledClip clip = m_Session.CompiledAsset.GetClip(clipKey);
            ClipPathInfo pathInfo = BuildClipPathInfo(clip);
            List<string> segments = SplitClipPathSegments(pathInfo.ParentPath);
            string currentPath = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                currentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? segments[i]
                    : $"{currentPath}/{segments[i]}";
                SetClipPathCollapsed(BuildClipPathKey(currentPath), false);
            }
        }

        private bool HasClipPath(string path)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            path = NormalizeClipPathKey(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            foreach (string transientPath in m_TransientClipPathKeys)
            {
                if (string.Equals(transientPath, path, StringComparison.Ordinal) ||
                    transientPath.StartsWith($"{path}/", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return HasPersistedClipPath(path);
        }

        private bool HasPersistedClipPath(string path)
        {
            path = NormalizeClipPathKey(path);
            IReadOnlyList<XAnimationCompiledClip> clips = m_Session.CompiledAsset.Clips;
            for (int i = 0; i < clips.Count; i++)
            {
                string clipParentPath = GetClipPathParent(clips[i]?.Key);
                if (string.Equals(clipParentPath, path, StringComparison.Ordinal) ||
                    clipParentPath.StartsWith($"{path}/", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasStatePath(string channelName, string path)
        {
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return false;
            }

            path = NormalizeStatePath(path);
            if (string.IsNullOrWhiteSpace(channelName) || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            IReadOnlyList<XAnimationCompiledStateNode> nodes = m_Session.CompiledAsset.StateNodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationCompiledStateNode node = nodes[i];
                if (node != null &&
                    string.Equals(node.ChannelName, channelName, StringComparison.Ordinal) &&
                    string.Equals(node.Key, path, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private List<StateSelectionItem> CollectSelectableStates(
            string excludeStateKey = null,
            bool includeNone = false,
            string channelFilterName = null,
            bool includeSelectors = false)
        {
            List<StateSelectionItem> items = new();
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return items;
            }

            IReadOnlyList<XAnimationCompiledStateNode> nodes = m_Session.CompiledAsset.StateNodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                XAnimationCompiledStateNode node = nodes[i];
                if (node == null ||
                    !node.IsPlayable ||
                    (!includeSelectors && node.Kind != XAnimationStateNodeKind.State) ||
                    string.Equals(node.Key, excludeStateKey, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(channelFilterName) &&
                        !string.Equals(node.ChannelName, channelFilterName, StringComparison.Ordinal)))
                {
                    continue;
                }

                items.Add(new StateSelectionItem(node.Key, node.ChannelName));
            }

            return items;
        }

        private List<ClipSelectionItem> CollectSelectableClips()
        {
            List<ClipSelectionItem> items = new();
            if (m_Session == null || !m_Session.IsLoaded)
            {
                return items;
            }

            IReadOnlyList<XAnimationCompiledClip> clips = m_Session.CompiledAsset.Clips;
            for (int i = 0; i < clips.Count; i++)
            {
                XAnimationCompiledClip clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                ClipPathInfo pathInfo = BuildClipPathInfo(clip);
                items.Add(new ClipSelectionItem(clip.Key, pathInfo.DisplayPath, pathInfo.ParentPath));
            }

            return items;
        }

        private static List<SearchableSelectionItem> BuildStateSelectionEntries(
            List<StateSelectionItem> items,
            bool includeNone,
            bool includeChannelName = true)
        {
            List<SearchableSelectionItem> entries = new();
            if (includeNone)
            {
                entries.Add(new SearchableSelectionItem(string.Empty, "None", "Clear selection", "none clear empty"));
            }

            if (items == null)
            {
                return entries;
            }

            for (int i = 0; i < items.Count; i++)
            {
                StateSelectionItem item = items[i];
                string title = FormatStateSelectionTitle(item, includeChannelName);
                string detail = item.HasParentPath
                    ? $"path={FormatStateDisplayPath(item.ParentPath)}"
                    : string.Empty;
                string searchText = $"{item.StateKey} {item.ChannelName} {item.ParentPath} {title}";
                string groupKey = item.HasParentPath
                    ? includeChannelName ? $"{item.ChannelName} - {item.ParentPath}" : item.ParentPath
                    : string.Empty;
                entries.Add(new SearchableSelectionItem(item.StateKey, title, detail, searchText, groupKey));
            }

            return entries;
        }

        private static List<SearchableSelectionItem> BuildScopedStateSelectionEntries(List<StateSelectionItem> items)
        {
            List<SearchableSelectionItem> entries = new();
            if (items == null)
            {
                return entries;
            }

            for (int i = 0; i < items.Count; i++)
            {
                StateSelectionItem item = items[i];
                string title = FormatStateSelectionTitle(item);
                string detail = item.HasParentPath
                    ? $"path={FormatStateDisplayPath(item.ParentPath)}"
                    : string.Empty;
                string searchText = $"{item.StateKey} {item.ChannelName} {item.ParentPath} {title}";
                string groupKey = item.HasParentPath ? $"{item.ChannelName} - {item.ParentPath}" : string.Empty;
                entries.Add(new SearchableSelectionItem(BuildStateUiKey(item.ChannelName, item.StateKey), title, detail, searchText, groupKey));
            }

            return entries;
        }

        private static string FormatStateSelectionTitle(StateSelectionItem item, bool includeChannelName = true)
        {
            if (!includeChannelName)
            {
                return item.HasParentPath
                    ? $"{FormatStateDisplayPath(item.ParentPath)} / {item.LeafName}"
                    : item.StateKey;
            }

            return item.HasParentPath
                ? $"{item.ChannelName} - {FormatStateDisplayPath(item.ParentPath)} / {item.LeafName}"
                : $"{item.ChannelName} - {item.StateKey}";
        }

        private static List<SearchableSelectionItem> BuildClipSelectionEntries(List<ClipSelectionItem> items)
        {
            List<SearchableSelectionItem> entries = new();
            if (items == null)
            {
                return entries;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ClipSelectionItem item = items[i];
                string title = string.IsNullOrWhiteSpace(item.DisplayPath)
                    ? item.ClipKey
                    : item.DisplayPath;
                string detail = string.IsNullOrWhiteSpace(item.ParentPath)
                    ? "root"
                    : $"path={FormatClipDisplayPath(item.ParentPath)}";
                string searchText = $"{item.ClipKey} {title}";
                entries.Add(new SearchableSelectionItem(item.ClipKey, title, detail, searchText, item.ParentPath));
            }

            return entries;
        }

        private static Rect GetSelectionActivatorRect(VisualElement element)
        {
            Rect world = element.worldBound;
            return GUIUtility.GUIToScreenRect(new Rect(world.xMin, world.yMin, world.width, world.height));
        }

        private XAnimationEditorSelectionField CreateStateSelectionField(
            string label,
            string currentValue,
            string excludeStateKey = null,
            bool includeNone = false,
            string channelFilterName = null,
            bool includeSelectors = false)
        {
            XAnimationEditorSelectionField field = new(label, string.IsNullOrWhiteSpace(currentValue) && includeNone ? string.Empty : currentValue, _ => { });
            void ShowMenu(XAnimationEditorSelectionField target)
            {
                List<StateSelectionItem> items = CollectSelectableStates(excludeStateKey, includeNone, channelFilterName, includeSelectors);
                List<SearchableSelectionItem> entries = BuildStateSelectionEntries(items, includeNone);
                SearchableSelectionWindow.Show(
                    GetSelectionActivatorRect(target),
                    "Select State",
                    target.value,
                    entries,
                    selected => target.value = selected);
            }

            field = new XAnimationEditorSelectionField(label, string.IsNullOrWhiteSpace(currentValue) && includeNone ? string.Empty : currentValue, ShowMenu);
            AttachStatePingButton(field, currentValue, enabled: true);
            return field;
        }

    }
}
#endif
