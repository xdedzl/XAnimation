#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

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
        private IVisualElementScheduledItem m_RefreshItem;

        public bool visible => XAnimationSceneOverlaySelection.TryGetSelectedSceneActor(out _);

        public override VisualElement CreatePanelContent()
        {
            m_Controller = XAnimationSceneOverlaySelection.Controller;
            m_Controller.RefreshSelection();
            m_View = new XAnimationPlaybackHudView(m_Controller);
            m_ParametersView = new XAnimationActorPreviewParametersHudView(m_Controller);
            m_View.Content?.Add(m_ParametersView.Root);
            m_RefreshItem = m_View.Root.schedule.Execute(() =>
            {
                m_Controller.RefreshSelection();
                m_View.Refresh();
                m_ParametersView.Refresh();
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
            base.OnWillBeDestroyed();
        }
    }
}
#endif
