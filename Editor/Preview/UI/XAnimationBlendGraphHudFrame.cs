#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    internal sealed class XAnimationBlendGraphHudFrame
    {
        public XAnimationBlendGraphHudFrame()
        {
            Root = new VisualElement();
            Root.style.width = 244f;
            Root.style.paddingLeft = 6;
            Root.style.paddingRight = 6;
            Root.style.paddingTop = 6;
            Root.style.paddingBottom = 6;
            Root.style.backgroundColor = new Color(0.10f, 0.10f, 0.12f, 0.92f);
            SetBorder(Root, PaneBorder, 1, 6);

            Header = Row();
            Header.style.marginBottom = 4f;
            Header.AddToClassList("xanim-freeform-graph-overlay-drag-handle");
            Root.Add(Header);

            TitleLabel = new Label("Blend Graph");
            TitleLabel.style.color = TextNormal;
            TitleLabel.style.fontSize = BodyFontSize;
            TitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            TitleLabel.style.flexGrow = 1f;
            Header.Add(TitleLabel);

            Content = new VisualElement();
            Content.style.flexDirection = FlexDirection.Column;
            Root.Add(Content);

            DirectionalGraph = new XAnimationDirectionalBlendGraphElement();
            DirectionalGraph.tooltip = "蓝点是 sample，红点是当前 2D 参数值，圆圈大小表示实时 weight。拖动红点可预览 directional blend。";
            DirectionalGraph.style.display = DisplayStyle.None;
            Content.Add(DirectionalGraph);

            Blend1DGraph = new XAnimationBlend1DGraphElement();
            Blend1DGraph.tooltip = "蓝色包络表示 Blend1D sample weight，红线与红点表示当前参数值。拖动红点可预览 Blend1D。";
            Blend1DGraph.style.display = DisplayStyle.None;
            Content.Add(Blend1DGraph);

            HintLabel = new Label();
            HintLabel.style.color = TextMuted;
            HintLabel.style.fontSize = BodyFontSize;
            HintLabel.style.whiteSpace = WhiteSpace.Normal;
            HintLabel.style.display = DisplayStyle.None;
            Content.Add(HintLabel);
        }

        public VisualElement Root { get; }
        public VisualElement Header { get; }
        public VisualElement Content { get; }
        public Label TitleLabel { get; }
        public XAnimationDirectionalBlendGraphElement DirectionalGraph { get; }
        public XAnimationBlend1DGraphElement Blend1DGraph { get; }
        public Label HintLabel { get; }
    }
}
#endif
