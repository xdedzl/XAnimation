#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace XAnimationEditor
{
    [Overlay(typeof(SceneView), "XAnimation.SceneBlendGraphOverlay", "XAnimation Blend Graph",
        defaultDockZone = DockZone.RightColumn,
        defaultDockPosition = DockPosition.Top,
        defaultLayout = Layout.Panel)]
    internal sealed class XAnimationSceneBlendGraphOverlay : Overlay, ITransientOverlay
    {
        private XAnimationEditorActorPlaybackController m_Controller;
        private XAnimationBlendGraphHudView m_View;
        private IVisualElementScheduledItem m_RefreshItem;

        public bool visible => XAnimationSceneOverlaySelection.HasSelectedActorPlayingBlendState();

        public override VisualElement CreatePanelContent()
        {
            m_Controller = XAnimationSceneOverlaySelection.Controller;
            m_Controller.RefreshSelection();
            m_View = new XAnimationBlendGraphHudView(m_Controller);
            m_RefreshItem = m_View.Root.schedule.Execute(() =>
            {
                m_Controller.RefreshSelection();
                m_View.Refresh();
            }).Every(33);
            return m_View.Root;
        }

        public override void OnWillBeDestroyed()
        {
            m_RefreshItem?.Pause();
            m_RefreshItem = null;
            m_Controller = null;
            m_View = null;
            base.OnWillBeDestroyed();
        }
    }
}
#endif
