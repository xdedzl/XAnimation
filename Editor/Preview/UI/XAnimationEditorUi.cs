#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace XAnimationEditor
{
    internal sealed class FoldoutCard
    {
        public VisualElement Root;
        public VisualElement Content;
        public Action<bool> SetExpanded;
        public Action RefreshState;
    }

    internal class RowVisualState
    {
        public Color BaseColor;
        public bool Hovered;
        public bool Playing;
        public float Progress;
        public VisualElement ProgressFill;
    }

    internal sealed class ClipRowVisualState : RowVisualState
    {
        public bool Flashing;
        public int FlashVersion;
    }

    internal static class XAnimationEditorUi
    {
        public const float SectionTitleFontSize = 12f;
        public const float BodyFontSize = 11f;
        public const float ClipIconButtonSize = 22f;
        public const float FoldoutGlyphWidth = 14f;
        public const float PrettyHeaderHeight = 26f;
        public const float PrettyRowMinHeight = 22f;
        public const float PrettyBorderWidth = 1f;
        public const float PrettyBodyPadding = 3f;

        public static readonly Color PaneBorder = new(0.07f, 0.07f, 0.07f, 1f);
        public static readonly Color AccentColor = new(0.30f, 0.55f, 0.95f, 1f);
        public static readonly Color DangerColor = new(0.75f, 0.25f, 0.25f, 1f);
        public static readonly Color TextMuted = new(0.60f, 0.60f, 0.62f, 1f);
        public static readonly Color TextNormal = new(0.85f, 0.85f, 0.87f, 1f);
        public static readonly Color SectionDivider = PaneBorder;
        public static readonly Color HoverBg = new(0.24f, 0.24f, 0.26f, 1f);
        public static readonly Color ListGroupBg = new(0.15f, 0.15f, 0.15f, 1f);
        public static readonly Color ListRowEvenBg = new(0.16f, 0.16f, 0.17f, 1f);
        public static readonly Color ListRowOddBg = new(0.19f, 0.19f, 0.20f, 1f);
        public static readonly Color ListHeaderBg = new(0.20f, 0.20f, 0.20f, 1f);
        public static readonly Color PlayingBg = new(0.20f, 0.35f, 0.55f, 0.65f);
        public static readonly Color ProgressFillBg = new(0.20f, 0.55f, 0.95f, 0.55f);

        public static VisualElement CreateCard(string titleText, VisualElement titleAction = null)
        {
            bool hasVisibleTitle = !string.IsNullOrWhiteSpace(titleText);
            VisualElement card = new();
            card.style.marginBottom = 2;
            SetBorder(card, PaneBorder, PrettyBorderWidth);
            card.style.backgroundColor = new Color(0.15f, 0.15f, 0.16f, 1f);

            VisualElement titleRow = Row();
            ApplyPrettyHeaderStyle(titleRow);

            Label label = new(titleText);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = SectionTitleFontSize;
            label.style.color = TextNormal;
            label.style.flexShrink = 0;

            if (titleAction == null)
            {
                label.style.flexGrow = 1;
                titleRow.Add(label);
            }
            else if (hasVisibleTitle)
            {
                label.style.flexGrow = 1;
                label.style.minWidth = 0;
                titleRow.Add(label);
                titleAction.style.flexShrink = 0;
                titleAction.style.marginLeft = 8;
                titleRow.Add(titleAction);
            }
            else
            {
                titleAction.style.flexShrink = 0;
                titleAction.style.marginLeft = 0;
                titleRow.Add(label);
                titleRow.Add(titleAction);
            }

            card.Add(titleRow);
            return card;
        }

        public static FoldoutCard CreateFoldoutCard(
            string titleText,
            bool expanded,
            Action<bool> setExpanded,
            VisualElement titleAction = null)
        {
            VisualElement card = CreateCard(titleText, titleAction);
            VisualElement titleRow = card[0];
            Label label = titleRow.Q<Label>();
            VisualElement content = new();
            ApplyPrettyContentStyle(content);
            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            card.Add(content);

            void ApplyExpanded(bool value)
            {
                expanded = value;
                setExpanded?.Invoke(value);
                content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (label != null)
                {
                    label.text = FormatFoldoutTitleText(value, titleText);
                }

                titleRow.style.borderBottomWidth = value ? PrettyBorderWidth : 0f;
            }

            ApplyExpanded(expanded);
            titleRow.tooltip = string.IsNullOrWhiteSpace(titleText) ? "点击展开/收起分区。" : $"点击展开/收起 {titleText} 分区。";
            titleRow.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                if (evt.target is VisualElement target)
                {
                    if (titleAction != null && (ReferenceEquals(target, titleAction) || titleAction.Contains(target)))
                    {
                        return;
                    }

                    for (VisualElement current = target; current != null && current != titleRow; current = current.hierarchy.parent)
                    {
                        if (current.ClassListContains("xanim-playback-overlay-drag-handle"))
                        {
                            return;
                        }
                    }
                }

                ApplyExpanded(!expanded);
                evt.StopPropagation();
            });

            return new FoldoutCard { Root = card, Content = content, SetExpanded = ApplyExpanded };
        }

        public static FoldoutCard CreateSectionFoldoutCard(
            string titleText,
            bool expanded,
            Action<bool> setExpanded,
            VisualElement titleAction = null,
            Func<bool> canToggle = null,
            string headerTooltip = null,
            bool allowActionAreaBackgroundToggle = false)
        {
            VisualElement root = CreateSubBox();
            SetPadding(root, 0);
            VisualElement header = Row();
            ApplyPrettyHeaderStyle(header);

            bool hasVisibleTitle = !string.IsNullOrWhiteSpace(titleText);
            Label toggleLabel = CreateFoldoutGlyph(expanded);
            toggleLabel.style.marginRight = hasVisibleTitle ? 0 : 4;

            Label label = new();
            label.style.color = TextNormal;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.flexGrow = 1;
            header.Add(toggleLabel);
            if (hasVisibleTitle)
            {
                header.Add(label);
            }

            if (titleAction != null)
            {
                titleAction.style.flexShrink = hasVisibleTitle ? 0 : 1;
                titleAction.style.flexGrow = hasVisibleTitle ? 0 : 1;
                titleAction.style.minWidth = hasVisibleTitle ? StyleKeyword.Null : 0;
                titleAction.style.maxWidth = hasVisibleTitle ? StyleKeyword.Null : Length.Percent(100f);
                titleAction.style.marginLeft = hasVisibleTitle ? 6 : 0;
                header.Add(titleAction);
            }

            VisualElement content = new();
            ApplyPrettyContentStyle(content);
            root.Add(header);
            root.Add(content);

            bool ShouldIgnoreToggleTarget(VisualElement target)
            {
                if (titleAction == null || target == null || !titleAction.Contains(target))
                {
                    return false;
                }

                if (!allowActionAreaBackgroundToggle)
                {
                    return true;
                }

                for (VisualElement current = target; current != null && current != titleAction; current = current.hierarchy.parent)
                {
                    if (current is Button || current is BindableElement)
                    {
                        return true;
                    }
                }

                return false;
            }

            void RefreshState()
            {
                bool toggleable = canToggle?.Invoke() ?? true;
                bool isExpanded = toggleable && expanded;
                SetFoldoutGlyphText(toggleLabel, isExpanded);
                label.text = hasVisibleTitle ? titleText : string.Empty;
                toggleLabel.style.color = toggleable ? TextNormal : TextMuted;
                label.style.color = toggleable ? TextNormal : TextMuted;
                content.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                header.style.borderBottomWidth = isExpanded ? PrettyBorderWidth : 0f;
            }

            void ApplyExpanded(bool value)
            {
                expanded = value;
                setExpanded?.Invoke(value);
                RefreshState();
            }

            RefreshState();
            string tooltip = string.IsNullOrWhiteSpace(headerTooltip) ? $"点击展开/收起 {titleText}。" : headerTooltip;
            header.tooltip = tooltip;
            toggleLabel.tooltip = tooltip;
            label.tooltip = tooltip;
            root.tooltip = tooltip;
            header.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 ||
                    evt.target is VisualElement target && ShouldIgnoreToggleTarget(target) ||
                    !(canToggle?.Invoke() ?? true))
                {
                    return;
                }

                ApplyExpanded(!expanded);
                evt.StopPropagation();
            });

            return new FoldoutCard { Root = root, Content = content, SetExpanded = ApplyExpanded, RefreshState = RefreshState };
        }

        public static VisualElement CreateSubBox()
        {
            VisualElement box = new();
            box.style.marginTop = 2;
            SetPadding(box, PrettyBodyPadding);
            SetBorder(box, PaneBorder, PrettyBorderWidth);
            box.style.backgroundColor = ListGroupBg;
            return box;
        }

        public static VisualElement CreateListGroup(float marginBottom = 2f, float marginLeft = 0f)
        {
            VisualElement group = new();
            group.style.marginBottom = marginBottom;
            group.style.marginLeft = marginLeft;
            SetPadding(group, 0);
            group.style.backgroundColor = ListGroupBg;
            SetBorder(group, PaneBorder, PrettyBorderWidth);
            return group;
        }

        public static VisualElement CreateNestedListGroup()
        {
            VisualElement group = CreateListGroup(marginBottom: 0f, marginLeft: 4f);
            group.style.marginTop = 2;
            group.style.backgroundColor = new Color(0.15f, 0.15f, 0.16f, 1f);
            return group;
        }

        public static VisualElement CreateListHeader(float marginBottom = 0f)
        {
            VisualElement header = Row();
            header.style.marginBottom = marginBottom;
            ApplyPrettyHeaderStyle(header);
            header.style.borderBottomWidth = PrettyBorderWidth;
            return header;
        }

        public static Label CreateFoldoutGlyph(bool expanded)
        {
            Label label = new();
            ApplyFoldoutGlyphStyle(label);
            SetFoldoutGlyphText(label, expanded);
            return label;
        }

        public static void SetFoldoutGlyphText(Label label, bool expanded)
        {
            if (label == null)
            {
                return;
            }

            label.text = expanded ? "▾" : "▸";
        }

        public static string FormatFoldoutTitleText(bool expanded, string titleText)
        {
            string glyph = expanded ? "▾" : "▸";
            return string.IsNullOrWhiteSpace(titleText) ? glyph : $"{glyph} {titleText}";
        }

        private static void ApplyFoldoutGlyphStyle(Label label)
        {
            label.style.width = FoldoutGlyphWidth;
            label.style.minWidth = FoldoutGlyphWidth;
            label.style.maxWidth = FoldoutGlyphWidth;
            label.style.flexShrink = 0;
            label.style.color = TextNormal;
            label.style.fontSize = SectionTitleFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        public static Label CreateSmallInfoLabel(string text)
        {
            Label label = new(text);
            label.style.color = TextMuted;
            label.style.fontSize = 10;
            label.style.flexShrink = 0;
            return label;
        }

        public static Label CreateBoldLabel(string text)
        {
            Label label = new(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = TextNormal;
            return label;
        }

        public static Label CreateSectionTitleLabel(string text)
        {
            Label label = CreateBoldLabel(text);
            label.style.flexGrow = 1;
            label.style.fontSize = BodyFontSize;
            return label;
        }

        public static VisualElement CreateInteractiveRowContainer(int rowIndex)
        {
            VisualElement container = new();
            container.style.position = Position.Relative;
            container.style.overflow = Overflow.Hidden;
            container.style.minHeight = PrettyRowMinHeight;
            container.style.backgroundColor = RowBaseColor(rowIndex);
            return container;
        }

        public static VisualElement CreateRowContainer(int rowIndex)
        {
            VisualElement container = CreateInteractiveRowContainer(rowIndex);
            container.style.marginBottom = 0;
            return container;
        }

        public static VisualElement CreateRowContent()
        {
            VisualElement row = Row();
            SetPadding(row, 2, 4);
            row.style.minHeight = PrettyRowMinHeight;
            row.style.position = Position.Relative;
            return row;
        }

        public static VisualElement CreateRowProgressFill()
        {
            return CreateProgressFill(ProgressFillBg);
        }

        public static VisualElement CreateProgressFill(Color color)
        {
            VisualElement fill = new();
            fill.pickingMode = PickingMode.Ignore;
            fill.style.position = Position.Absolute;
            fill.style.left = 0f;
            fill.style.top = 0f;
            fill.style.bottom = 0f;
            fill.style.width = Length.Percent(0f);
            fill.style.backgroundColor = color;
            fill.style.visibility = Visibility.Hidden;
            return fill;
        }

        public static Color RowBaseColor(int rowIndex)
        {
            return rowIndex % 2 == 0 ? ListRowEvenBg : ListRowOddBg;
        }

        public static void ApplyRowVisualState(VisualElement row, RowVisualState state)
        {
            if (row == null || state == null)
            {
                return;
            }

            row.style.backgroundColor = state.Playing ? PlayingBg : state.Hovered ? HoverBg : state.BaseColor;
            ApplyRowProgressVisualState(state);
        }

        public static void ApplyRowProgressVisualState(RowVisualState state)
        {
            if (state?.ProgressFill == null)
            {
                return;
            }

            float progress = Mathf.Clamp01(state.Progress);
            state.ProgressFill.style.width = Length.Percent(progress * 100f);
            state.ProgressFill.style.visibility = progress > 0f ? Visibility.Visible : Visibility.Hidden;
        }

        public static void AddEmptyLabel(VisualElement root, string text)
        {
            if (root == null)
            {
                return;
            }

            Label label = new(text);
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.marginLeft = 4;
            root.Add(label);
        }

        public static Toggle CreateHeaderApplyToggle(bool value, string tooltip)
        {
            Toggle toggle = new("Apply") { value = value };
            toggle.tooltip = tooltip;
            toggle.style.flexShrink = 0;
            toggle.style.unityFontStyleAndWeight = FontStyle.Normal;
            return toggle;
        }

        public static void ConfigureCompactPlaybackField(BaseField<float> field, float valueWidth)
        {
            ConfigureCompactPlaybackField(field, null, valueWidth);
        }

        public static void ConfigureCompactPlaybackField(BaseField<float> field, string labelText, float valueWidth)
        {
            field.label = string.Empty;
            ConfigureCompactPlaybackElement(field, valueWidth);
        }

        public static void ConfigureCompactPlaybackElement(VisualElement field, float valueWidth)
        {
            field.style.width = valueWidth;
            field.style.minWidth = valueWidth;
            field.style.maxWidth = valueWidth;
            field.style.flexShrink = 0;
            field.style.alignSelf = Align.Center;
        }

        public static VisualElement CreatePlaybackFieldContainer(string labelText, VisualElement field, float labelWidth)
        {
            VisualElement container = Row();
            container.style.marginTop = 2;
            container.style.marginBottom = 2;
            container.style.minWidth = 0;

            Label label = new(labelText);
            label.style.width = labelWidth;
            label.style.minWidth = labelWidth;
            label.style.maxWidth = labelWidth;
            label.style.flexShrink = 0;
            label.style.fontSize = 10;
            label.style.color = TextMuted;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginRight = 6;
            container.Add(label);
            if (field is DropdownField)
            {
                field.style.flexGrow = 1;
                field.style.flexShrink = 1;
                field.style.minWidth = 0;
                field.style.maxWidth = StyleKeyword.None;
            }
            container.Add(field);
            return container;
        }

        public static VisualElement CreatePlaybackToggleRow(string labelText, Toggle toggle, float labelWidth)
        {
            toggle.label = string.Empty;
            toggle.style.flexShrink = 0;
            toggle.style.marginLeft = 0;
            return CreatePlaybackFieldContainer(labelText, toggle, labelWidth);
        }

        public static VisualElement CreatePlaybackFieldPairRow(
            string leftLabel,
            VisualElement leftField,
            string rightLabel,
            VisualElement rightField,
            float labelWidth,
            float valueWidth)
        {
            VisualElement row = Row();
            row.style.marginTop = 2;
            row.style.marginBottom = 2;
            row.style.minWidth = 0;

            ConfigureCompactPlaybackElement(leftField, valueWidth);
            VisualElement leftContainer = CreatePlaybackFieldContainer(leftLabel, leftField, labelWidth);
            leftContainer.style.marginTop = 0;
            leftContainer.style.marginBottom = 0;
            row.Add(leftContainer);

            ConfigureCompactPlaybackElement(rightField, valueWidth);
            VisualElement rightContainer = CreatePlaybackFieldContainer(rightLabel, rightField, labelWidth);
            rightContainer.style.marginTop = 0;
            rightContainer.style.marginBottom = 0;
            rightContainer.style.marginLeft = 10;
            row.Add(rightContainer);
            return row;
        }

        public static void ApplyClipIconButtonStyle(Button button, Color? bgColor = null, float size = ClipIconButtonSize)
        {
            button.style.backgroundColor = bgColor ?? ListHeaderBg;
            button.style.color = bgColor.HasValue ? Color.white : TextNormal;
            SetBorder(button, PaneBorder, 1, 3);
            SetPadding(button, 0);
            button.style.fontSize = 12;
            SetFixedSize(button, size, size);
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
        }

        public static void ApplyDropdownFieldStyle(DropdownField field, float height = ClipIconButtonSize)
        {
            if (field == null)
            {
                return;
            }

            field.style.minHeight = height;
            field.style.height = height;
            field.style.maxHeight = height;
            field.style.backgroundColor = Color.clear;
            field.style.borderTopWidth = 0;
            field.style.borderBottomWidth = 0;
            field.style.borderLeftWidth = 0;
            field.style.borderRightWidth = 0;

            if (field.labelElement != null)
            {
                field.labelElement.style.color = TextMuted;
                field.labelElement.style.fontSize = BodyFontSize;
                if (string.IsNullOrWhiteSpace(field.label))
                {
                    field.labelElement.style.display = DisplayStyle.None;
                }
            }

            void ApplyInnerStyle()
            {
                VisualElement input = field.Q<VisualElement>(className: "unity-base-field__input");
                if (input != null)
                {
                    input.style.backgroundColor = ListHeaderBg;
                    SetBorder(input, PaneBorder, 1, 3);
                    input.style.minHeight = height;
                    input.style.height = height;
                    input.style.maxHeight = height;
                    input.style.paddingLeft = 6;
                    input.style.paddingRight = 4;
                }

                TextElement text = field.Q<TextElement>(className: "unity-popup-field__text");
                if (text != null)
                {
                    text.style.color = TextNormal;
                    text.style.fontSize = BodyFontSize;
                    text.style.unityTextAlign = TextAnchor.MiddleLeft;
                }

                VisualElement arrow = input?.Q<VisualElement>(className: "unity-base-popup-field__arrow");
                if (arrow != null)
                {
                    arrow.style.marginLeft = 2;
                    arrow.style.marginRight = 2;
                }
            }

            ApplyInnerStyle();
            field.RegisterCallback<AttachToPanelEvent>(_ => ApplyInnerStyle());
        }

        public static void ApplyIconButtonStyle(Button button, bool isPlaying)
        {
            button.text = isPlaying ? "■" : "▶";
            button.style.width = 28;
            button.style.minWidth = 28;
            button.style.height = 22;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Color.white;
            button.style.backgroundColor = isPlaying ? DangerColor : AccentColor;
        }

        public static void ApplyTrashButtonIcon(Button button)
        {
            Texture icon = EditorGUIUtility.IconContent("TreeEditor.Trash").image ??
                           EditorGUIUtility.IconContent("d_TreeEditor.Trash").image;
            if (icon == null)
            {
                button.text = "⌫";
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
            button.Add(image);
        }

        public static void ConfigureEditableNameLabel(EditableLabel label, float width)
        {
            label.style.width = width;
            label.style.flexShrink = 0;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = BodyFontSize;
            label.style.color = TextNormal;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;

            TextElement textElement = label.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.fontSize = BodyFontSize;
                textElement.style.color = TextNormal;
                textElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            TextField textField = label.Q<TextField>();
            if (textField == null)
            {
                return;
            }

            textField.style.marginTop = 0;
            textField.style.marginBottom = 0;
            textField.style.fontSize = BodyFontSize;
            VisualElement input = textField.Q("unity-text-input");
            if (input != null)
            {
                input.style.fontSize = BodyFontSize;
            }
        }

        public static void SetBorder(VisualElement element, Color color, float width = 1f, float radius = 0f)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            if (radius > 0f)
            {
                SetRadius(element, radius);
            }
        }

        public static void SetPadding(VisualElement element, float all)
        {
            SetPadding(element, all, all);
        }

        public static void SetPadding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
        }

        public static void SetMargin(VisualElement element, float all)
        {
            element.style.marginLeft = all;
            element.style.marginRight = all;
            element.style.marginTop = all;
            element.style.marginBottom = all;
        }

        public static void SetMargin(VisualElement element, float top, float right, float bottom, float left)
        {
            element.style.marginTop = top;
            element.style.marginRight = right;
            element.style.marginBottom = bottom;
            element.style.marginLeft = left;
        }

        public static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        public static void SetFixedSize(VisualElement element, float width, float height)
        {
            element.style.width = width;
            element.style.minWidth = width;
            element.style.maxWidth = width;
            element.style.height = height;
            element.style.minHeight = height;
            element.style.maxHeight = height;
        }

        public static void ApplyPrettyHeaderStyle(VisualElement header)
        {
            header.style.height = PrettyHeaderHeight;
            header.style.minHeight = PrettyHeaderHeight;
            header.style.maxHeight = PrettyHeaderHeight;
            header.style.backgroundColor = ListHeaderBg;
            header.style.borderBottomColor = PaneBorder;
            header.style.borderBottomWidth = 0f;
            SetPadding(header, 0, 0);
        }

        public static void ApplyPrettyContentStyle(VisualElement content)
        {
            SetPadding(content, 2, PrettyBodyPadding);
        }

        public static VisualElement Row()
        {
            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }
    }
}
#endif
