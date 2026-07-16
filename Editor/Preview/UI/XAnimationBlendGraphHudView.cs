#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using XAnimationEngine;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    internal sealed class XAnimationBlendGraphHudView
    {
        private readonly XAnimationEditorActorPlaybackController m_Controller;
        private readonly XAnimationBlendGraphHudFrame m_Frame;

        public XAnimationBlendGraphHudView(XAnimationEditorActorPlaybackController controller)
        {
            m_Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_Frame = new XAnimationBlendGraphHudFrame();
            m_Frame.Header.tooltip = "SceneView XAnimation Blend Graph Overlay。";
            m_Frame.DirectionalGraph.tooltip = "蓝点是 sample，红点是当前 2D 参数值。拖动红点可写入当前 Actor 参数。";
            m_Frame.Blend1DGraph.tooltip = "蓝色包络表示 Blend1D sample weight，红线与红点表示当前参数值。拖动红点可写入当前 Actor 参数。";
            Root = m_Frame.Root;
            Refresh();
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            m_Controller.RefreshSelection();
            XAnimationStateConfig state = ResolveBlendState();
            if (state == null)
            {
                m_Frame.TitleLabel.text = "Blend Graph";
                m_Frame.DirectionalGraph.style.display = DisplayStyle.None;
                m_Frame.Blend1DGraph.style.display = DisplayStyle.None;
                m_Frame.HintLabel.style.display = DisplayStyle.Flex;
                m_Frame.HintLabel.text = "当前 Actor 没有正在播放或可显示的 Blend 状态。";
                return;
            }

            string stateKey = XAnimationEditorStateNodeUtility.GetStateKey(m_Controller.Asset, state);
            m_Frame.TitleLabel.text = state.stateType switch
            {
                XAnimationStateType.Blend1D => $"{stateKey} | Blend1D",
                XAnimationStateType.Blend2DSimpleDirectional => $"{stateKey} | Simple 2D Directional",
                XAnimationStateType.Blend2DFreeformDirectional => $"{stateKey} | Freeform 2D Blend",
                _ => stateKey,
            };

            if (state.stateType == XAnimationStateType.Blend1D)
            {
                ShowBlend1D(state);
                return;
            }

            ShowDirectional(state);
        }

        private XAnimationStateConfig ResolveBlendState()
        {
            return m_Controller.TryGetPlayingBlendState(out XAnimationStateConfig state) ? state : null;
        }

        private void ShowBlend1D(XAnimationStateConfig state)
        {
            m_Frame.DirectionalGraph.style.display = DisplayStyle.None;
            m_Frame.Blend1DGraph.style.display = DisplayStyle.Flex;
            m_Frame.HintLabel.style.display = DisplayStyle.None;

            List<XAnimationBlend1DGraphElement.SampleViewData> samples = new();
            XAnimationBlend1DSampleConfig[] source = state.samples ?? Array.Empty<XAnimationBlend1DSampleConfig>();
            for (int i = 0; i < source.Length; i++)
            {
                XAnimationBlend1DSampleConfig sample = source[i];
                if (sample == null)
                {
                    continue;
                }

                samples.Add(new XAnimationBlend1DGraphElement.SampleViewData(
                    sample.clipKey,
                    sample.threshold,
                    ResolveBlendWeight(sample.clipKey)));
            }

            float current = GetFloatParameterValue(state.parameterName);
            float min = samples.Count > 0 ? samples[0].Threshold : -1f;
            float max = samples.Count > 0 ? samples[0].Threshold : 1f;
            for (int i = 1; i < samples.Count; i++)
            {
                min = Mathf.Min(min, samples[i].Threshold);
                max = Mathf.Max(max, samples[i].Threshold);
            }

            m_Frame.Blend1DGraph.SetData(new XAnimationBlend1DGraphElement.GraphData(
                samples,
                current,
                min,
                max,
                !string.IsNullOrWhiteSpace(state.parameterName),
                null,
                value =>
                {
                    m_Controller.TrySetParameter(state.parameterName, value);
                    Refresh();
                }));
        }

        private void ShowDirectional(XAnimationStateConfig state)
        {
            m_Frame.Blend1DGraph.style.display = DisplayStyle.None;
            m_Frame.DirectionalGraph.style.display = DisplayStyle.Flex;
            m_Frame.HintLabel.style.display = DisplayStyle.None;

            List<XAnimationDirectionalBlendGraphElement.SampleViewData> samples = new();
            XAnimationBlend2DSimpleDirectionalSampleConfig[] source = state.directionalSamples ?? Array.Empty<XAnimationBlend2DSimpleDirectionalSampleConfig>();
            for (int i = 0; i < source.Length; i++)
            {
                XAnimationBlend2DSimpleDirectionalSampleConfig sample = source[i];
                if (sample == null)
                {
                    continue;
                }

                samples.Add(new XAnimationDirectionalBlendGraphElement.SampleViewData(
                    sample.clipKey,
                    sample.positionX,
                    sample.positionY,
                    ResolveBlendWeight(sample.clipKey)));
            }

            Vector2 current = new(
                GetFloatParameterValue(state.parameterXName),
                GetFloatParameterValue(state.parameterYName));
            bool canDrag = !string.IsNullOrWhiteSpace(state.parameterXName) ||
                           !string.IsNullOrWhiteSpace(state.parameterYName);

            m_Frame.DirectionalGraph.SetData(new XAnimationDirectionalBlendGraphElement.GraphData(
                samples,
                current,
                canDrag,
                null,
                value =>
                {
                    if (!string.IsNullOrWhiteSpace(state.parameterXName))
                    {
                        m_Controller.TrySetParameter(state.parameterXName, value.x);
                    }

                    if (!string.IsNullOrWhiteSpace(state.parameterYName))
                    {
                        m_Controller.TrySetParameter(state.parameterYName, value.y);
                    }

                    Refresh();
                }));
        }

        private float ResolveBlendWeight(string clipKey)
        {
            if (string.IsNullOrWhiteSpace(clipKey) ||
                !m_Controller.TryGetDominantPlaybackState(out XAnimationChannelState state) ||
                state.blendClips == null)
            {
                return 0f;
            }

            for (int i = 0; i < state.blendClips.Length; i++)
            {
                XAnimationBlendClipState blendClip = state.blendClips[i];
                if (blendClip != null && string.Equals(blendClip.clipKey, clipKey, StringComparison.Ordinal))
                {
                    return blendClip.weight;
                }
            }

            return string.Equals(state.clipKey, clipKey, StringComparison.Ordinal) ? Mathf.Max(state.weight, state.channelWeight) : 0f;
        }

        private float GetFloatParameterValue(string parameterName)
        {
            if (!string.IsNullOrWhiteSpace(parameterName) &&
                m_Controller.TryGetParameter(parameterName, out float value))
            {
                return value;
            }

            XAnimationParameterConfig[] parameters = m_Controller.Asset?.parameters ?? Array.Empty<XAnimationParameterConfig>();
            for (int i = 0; i < parameters.Length; i++)
            {
                XAnimationParameterConfig parameter = parameters[i];
                if (parameter != null &&
                    parameter.type == XAnimationParameterType.Float &&
                    string.Equals(parameter.name, parameterName, StringComparison.Ordinal) &&
                    TryConvertDefaultFloat(parameter.defaultValue, out float defaultValue))
                {
                    return defaultValue;
                }
            }

            return 0f;
        }

        private static bool TryConvertDefaultFloat(object rawValue, out float value)
        {
            switch (rawValue)
            {
                case float floatValue:
                    value = floatValue;
                    return true;
                case double doubleValue:
                    value = (float)doubleValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case string stringValue:
                    return float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    value = 0f;
                    return false;
            }
        }
    }
}
#endif
