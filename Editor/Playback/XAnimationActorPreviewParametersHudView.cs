#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorParameterUtility;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    internal sealed class XAnimationActorPreviewParametersHudView
    {
        private readonly XAnimationEditorActorPlaybackController m_Controller;
        private readonly Dictionary<string, FloatField> m_FloatFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Slider> m_FloatSliders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IntegerField> m_IntFields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Toggle> m_BoolFields = new(StringComparer.Ordinal);
        private XAnimationAsset m_Asset;
        private int m_ParameterCount = -1;
        private bool m_Expanded = true;
        private VisualElement m_List;

        public XAnimationActorPreviewParametersHudView(XAnimationEditorActorPlaybackController controller)
        {
            m_Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            Root = Build();
            Refresh();
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            XAnimationAsset asset = m_Controller.Asset;
            int parameterCount = asset?.parameters?.Length ?? 0;
            if (!ReferenceEquals(asset, m_Asset) || parameterCount != m_ParameterCount)
            {
                Rebuild(asset);
            }

            RefreshValues();
        }

        private VisualElement Build()
        {
            FoldoutCard card = CreateSectionFoldoutCard("Preview Parameters", m_Expanded, value => m_Expanded = value);
            card.Root.style.marginTop = 4;
            m_List = new VisualElement();
            card.Content.Add(m_List);
            return card.Root;
        }

        private void Rebuild(XAnimationAsset asset)
        {
            m_Asset = asset;
            m_ParameterCount = asset?.parameters?.Length ?? 0;
            m_List.Clear();
            m_FloatFields.Clear();
            m_FloatSliders.Clear();
            m_IntFields.Clear();
            m_BoolFields.Clear();

            XAnimationParameterConfig[] parameters = asset?.parameters ?? Array.Empty<XAnimationParameterConfig>();
            bool hasPreviewControl = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                VisualElement row = CreateParameterRow(parameters[i]);
                if (row == null)
                {
                    continue;
                }

                hasPreviewControl = true;
                m_List.Add(row);
            }

            if (!hasPreviewControl)
            {
                Label emptyLabel = new("No preview parameters");
                emptyLabel.style.color = TextMuted;
                emptyLabel.style.fontSize = BodyFontSize;
                emptyLabel.style.marginLeft = 4;
                m_List.Add(emptyLabel);
            }
        }

        private VisualElement CreateParameterRow(XAnimationParameterConfig parameter)
        {
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
            {
                return null;
            }

            return parameter.type switch
            {
                XAnimationParameterType.Float => CreateFloatRow(parameter),
                XAnimationParameterType.Bool => CreateBoolRow(parameter),
                XAnimationParameterType.Int => CreateIntRow(parameter),
                _ => null,
            };
        }

        private VisualElement CreateFloatRow(XAnimationParameterConfig parameter)
        {
            string parameterName = parameter.name;
            float value = GetFloatValue(parameter);
            bool useSlider = TryGetBlend1DPreviewRange(parameterName, out float min, out float max) ||
                             TryGetDirectionalPreviewRange(parameterName, out min, out max);

            VisualElement row = CreateParameterRowRoot(parameterName);
            FloatField valueField = new()
            {
                value = value
            };
            valueField.tooltip = "预览参数值，只影响当前 Actor 预览，不保存到资源。";
            ConfigureCompactNumberField(valueField);

            if (useSlider)
            {
                Slider slider = new(min, max)
                {
                    value = Mathf.Clamp(value, min, max)
                };
                slider.tooltip = $"Blend 参数范围来自 samples: [{min:0.###}, {max:0.###}]。";
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(evt =>
                {
                    valueField.SetValueWithoutNotify(evt.newValue);
                    SetFloatValue(parameterName, evt.newValue);
                });
                valueField.RegisterValueChangedCallback(evt =>
                {
                    slider.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, min, max));
                    SetFloatValue(parameterName, evt.newValue);
                });
                row.Add(slider);
                row.Add(valueField);
                m_FloatSliders[parameterName] = slider;
            }
            else
            {
                valueField.style.flexGrow = 1;
                valueField.style.width = StyleKeyword.Auto;
                valueField.style.minWidth = 64;
                valueField.style.maxWidth = StyleKeyword.None;
                valueField.RegisterValueChangedCallback(evt => SetFloatValue(parameterName, evt.newValue));
                row.Add(valueField);
            }

            Button zeroButton = CreateZeroButton(() =>
            {
                valueField.SetValueWithoutNotify(0f);
                if (m_FloatSliders.TryGetValue(parameterName, out Slider slider))
                {
                    slider.SetValueWithoutNotify(0f);
                }
                SetFloatValue(parameterName, 0f);
            });
            row.Add(zeroButton);
            m_FloatFields[parameterName] = valueField;
            return row;
        }

        private VisualElement CreateBoolRow(XAnimationParameterConfig parameter)
        {
            string parameterName = parameter.name;
            VisualElement row = CreateParameterRowRoot(parameterName);
            Toggle toggle = new("value")
            {
                value = GetBoolValue(parameter)
            };
            toggle.tooltip = "预览参数值，只影响当前 Actor 预览，不保存到资源。";
            toggle.style.flexGrow = 1;
            toggle.RegisterValueChangedCallback(evt => m_Controller.TrySetParameter(parameterName, evt.newValue));
            row.Add(toggle);
            m_BoolFields[parameterName] = toggle;
            return row;
        }

        private VisualElement CreateIntRow(XAnimationParameterConfig parameter)
        {
            string parameterName = parameter.name;
            VisualElement row = CreateParameterRowRoot(parameterName);
            IntegerField field = new("value")
            {
                value = GetIntValue(parameter)
            };
            field.tooltip = "预览参数值，只影响当前 Actor 预览，不保存到资源。";
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(evt => m_Controller.TrySetParameter(parameterName, evt.newValue));
            row.Add(field);

            Button zeroButton = CreateZeroButton(() =>
            {
                field.SetValueWithoutNotify(0);
                m_Controller.TrySetParameter(parameterName, 0);
            });
            row.Add(zeroButton);
            m_IntFields[parameterName] = field;
            return row;
        }

        private VisualElement CreateParameterRowRoot(string parameterName)
        {
            VisualElement row = Row();
            row.style.marginBottom = 3;
            row.style.minWidth = 0;

            Label label = new(parameterName);
            label.style.width = 82;
            label.style.flexShrink = 0;
            label.style.color = TextMuted;
            label.style.fontSize = BodyFontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);
            return row;
        }

        private static Button CreateZeroButton(Action action)
        {
            Button button = new(action)
            {
                text = "0"
            };
            button.tooltip = "把这个预览参数重置为 0。";
            ApplyClipIconButtonStyle(button);
            button.style.marginLeft = 4;
            return button;
        }

        private void SetFloatValue(string parameterName, float value)
        {
            m_Controller.TrySetParameter(parameterName, value);
            XAnimationSceneOverlaySelection.RequestRepaint();
        }

        private void RefreshValues()
        {
            XAnimationParameterConfig[] parameters = m_Asset?.parameters ?? Array.Empty<XAnimationParameterConfig>();
            for (int i = 0; i < parameters.Length; i++)
            {
                XAnimationParameterConfig parameter = parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
                {
                    continue;
                }

                string parameterName = parameter.name;
                switch (parameter.type)
                {
                    case XAnimationParameterType.Float:
                        if (m_FloatFields.TryGetValue(parameterName, out FloatField floatField))
                        {
                            float value = GetFloatValue(parameter);
                            floatField.SetValueWithoutNotify(value);
                            if (m_FloatSliders.TryGetValue(parameterName, out Slider slider))
                            {
                                slider.SetValueWithoutNotify(Mathf.Clamp(value, slider.lowValue, slider.highValue));
                            }
                        }
                        break;
                    case XAnimationParameterType.Bool:
                        if (m_BoolFields.TryGetValue(parameterName, out Toggle toggle))
                        {
                            toggle.SetValueWithoutNotify(GetBoolValue(parameter));
                        }
                        break;
                    case XAnimationParameterType.Int:
                        if (m_IntFields.TryGetValue(parameterName, out IntegerField intField))
                        {
                            intField.SetValueWithoutNotify(GetIntValue(parameter));
                        }
                        break;
                }
            }
        }

        private float GetFloatValue(XAnimationParameterConfig parameter)
        {
            return parameter != null && m_Controller.TryGetParameter(parameter.name, out float value)
                ? value
                : ConvertParameterDefaultToFloat(parameter?.defaultValue);
        }

        private bool GetBoolValue(XAnimationParameterConfig parameter)
        {
            return parameter != null && m_Controller.TryGetParameter(parameter.name, out bool value)
                ? value
                : ConvertParameterDefaultToBool(parameter?.defaultValue);
        }

        private int GetIntValue(XAnimationParameterConfig parameter)
        {
            return parameter != null && m_Controller.TryGetParameter(parameter.name, out int value)
                ? value
                : ConvertParameterDefaultToInt(parameter?.defaultValue);
        }

        private bool TryGetBlend1DPreviewRange(string parameterName, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (m_Asset?.states == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < m_Asset.states.Length; i++)
            {
                XAnimationStateConfig state = m_Asset.states[i];
                if (state == null ||
                    state.stateType != XAnimationStateType.Blend1D ||
                    !string.Equals(state.parameterName, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                XAnimationBlend1DSampleConfig[] samples = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    float threshold = samples[sampleIndex]?.threshold ?? 0f;
                    AddRangeValue(threshold, ref min, ref max, ref found);
                }
            }

            return NormalizeRange(found, ref min, ref max);
        }

        private bool TryGetDirectionalPreviewRange(string parameterName, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (m_Asset?.states == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < m_Asset.states.Length; i++)
            {
                XAnimationStateConfig state = m_Asset.states[i];
                if (state == null || !IsDirectionalBlendStateType(state.stateType))
                {
                    continue;
                }

                bool matchesX = string.Equals(state.parameterXName, parameterName, StringComparison.Ordinal);
                bool matchesY = string.Equals(state.parameterYName, parameterName, StringComparison.Ordinal);
                if (!matchesX && !matchesY)
                {
                    continue;
                }

                XAnimationBlend2DSimpleDirectionalSampleConfig[] samples = state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    XAnimationBlend2DSimpleDirectionalSampleConfig sample = samples[sampleIndex];
                    if (sample == null)
                    {
                        continue;
                    }

                    AddRangeValue(matchesX ? sample.positionX : sample.positionY, ref min, ref max, ref found);
                }
            }

            return NormalizeRange(found, ref min, ref max);
        }

        private static bool IsDirectionalBlendStateType(XAnimationStateType stateType)
        {
            return stateType == XAnimationStateType.Blend2DSimpleDirectional ||
                   stateType == XAnimationStateType.Blend2DFreeformDirectional;
        }

        private static void AddRangeValue(float value, ref float min, ref float max, ref bool found)
        {
            if (!found)
            {
                min = value;
                max = value;
                found = true;
                return;
            }

            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }

        private static bool NormalizeRange(bool found, ref float min, ref float max)
        {
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

        private static void ConfigureCompactNumberField(BaseField<float> field)
        {
            field.style.width = 64;
            field.style.minWidth = 64;
            field.style.maxWidth = 64;
            field.style.flexShrink = 0;
        }
    }
}
#endif
