#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static XAnimationEditor.XAnimationEditorUi;

namespace XAnimationEditor
{
    [Overlay(typeof(SceneView), "XAnimation.ScenePlaybackOverlay", "XAnimation Playback",
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel)]
    internal sealed class XAnimationScenePlaybackOverlay : Overlay, ITransientOverlay
    {
        private XAnimationEditorActorPlaybackController m_Controller;
        private XAnimationPlaybackHudView m_View;
        private XAnimationActorPreviewParametersHudView m_ParametersView;
        private XAnimationActorOutputJobsHudView m_OutputJobsView;
        private IVisualElementScheduledItem m_RefreshItem;

        public bool visible => XAnimationSceneOverlaySelection.TryGetSelectedSceneActor(out _);

        public override VisualElement CreatePanelContent()
        {
            m_Controller = XAnimationSceneOverlaySelection.Controller;
            m_Controller.RefreshSelection();
            m_View = new XAnimationPlaybackHudView(m_Controller);
            m_ParametersView = new XAnimationActorPreviewParametersHudView(m_Controller);
            m_OutputJobsView = new XAnimationActorOutputJobsHudView(m_Controller);
            m_View.Content?.Add(m_ParametersView.Root);
            m_View.Content?.Add(m_OutputJobsView.Root);
            m_RefreshItem = m_View.Root.schedule.Execute(() =>
            {
                m_Controller.RefreshSelection();
                m_View.Refresh();
                m_ParametersView.Refresh();
                m_OutputJobsView.Refresh();
            }).Every(33);
            return m_View.Root;
        }

        public override void OnWillBeDestroyed()
        {
            m_RefreshItem?.Pause();
            m_RefreshItem = null;
            m_Controller = null;
            m_View = null;
            m_ParametersView = null;
            m_OutputJobsView = null;
            base.OnWillBeDestroyed();
        }
    }

    internal sealed class XAnimationActorOutputJobsHudView
    {
        private readonly XAnimationEditorActorPlaybackController m_Controller;
        private readonly Label m_SummaryLabel;
        private readonly VisualElement m_HitControls;
        private readonly Vector3Field m_WorldDirectionField;
        private readonly FloatField m_ForceField;
        private readonly Button m_HitButton;

        public XAnimationActorOutputJobsHudView(XAnimationEditorActorPlaybackController controller)
        {
            m_Controller = controller ?? throw new ArgumentNullException(nameof(controller));

            FoldoutCard card = CreateSectionFoldoutCard("Output Jobs Preview", true, _ => { });
            card.Root.style.marginTop = 4;
            Root = card.Root;

            m_SummaryLabel = new();
            m_SummaryLabel.style.color = TextMuted;
            m_SummaryLabel.style.fontSize = BodyFontSize;
            m_SummaryLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Content.Add(m_SummaryLabel);

            m_HitControls = new VisualElement();
            m_HitControls.style.marginTop = 4;
            card.Content.Add(m_HitControls);

            m_WorldDirectionField = new Vector3Field("World Direction")
            {
                value = Vector3.forward
            };
            m_WorldDirectionField.tooltip = "命中点或攻击者指向角色的世界空间作用方向，水平分量必须非零。";
            m_WorldDirectionField.AddToClassList(BaseField<Vector3>.alignedFieldUssClassName);
            m_HitControls.Add(m_WorldDirectionField);

            m_ForceField = new FloatField("Force")
            {
                value = 180f
            };
            m_ForceField.tooltip = "正向角速度，单位为度/秒。";
            m_ForceField.AddToClassList(BaseField<float>.alignedFieldUssClassName);
            m_HitControls.Add(m_ForceField);

            m_HitButton = new Button(() => m_Controller.PreviewHit(m_WorldDirectionField.value, m_ForceField.value))
            {
                text = "Hit"
            };
            m_HitButton.style.height = 22;
            m_HitButton.style.marginTop = 4;
            m_HitControls.Add(m_HitButton);

            Refresh();
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            int hitCount = m_Controller.PreviewHitReactionCount;
            int dampingCount = m_Controller.PreviewDampingCount;
            int aimIKCount = m_Controller.PreviewAimIKCount;
            bool hasOutputJobs = hitCount > 0 || dampingCount > 0 || aimIKCount > 0;
            Root.style.display = !Application.isPlaying && hasOutputJobs ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasOutputJobs)
            {
                return;
            }

            m_SummaryLabel.text = $"Hit Reaction: {hitCount}  |  Damping: {dampingCount}  |  Aim IK: {aimIKCount}\n播放场景 Actor 时自动插入这些 Output Jobs；选择 Aim IK 组件可在 Scene 视图拖动目标。";
            m_HitControls.style.display = hitCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_HitButton.SetEnabled(hitCount > 0 && m_ForceField.value >= 0f && m_WorldDirectionField.value.sqrMagnitude > Mathf.Epsilon);
        }
    }
}
#endif
