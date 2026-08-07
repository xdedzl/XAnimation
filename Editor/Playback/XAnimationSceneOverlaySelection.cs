#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    [InitializeOnLoad]
    internal static class XAnimationSceneOverlaySelection
    {
        private static XAnimationEditorActorPlaybackController s_Controller;

        static XAnimationSceneOverlaySelection()
        {
            Selection.selectionChanged += RepaintSceneViews;
            EditorApplication.hierarchyChanged += RepaintSceneViews;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += DisposeController;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeController;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                s_Controller?.Dispose();
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                s_Controller?.Dispose();
                s_Controller = null;
            }

            RepaintSceneViews();
        }

        public static XAnimationEditorActorPlaybackController Controller
        {
            get
            {
                s_Controller ??= new XAnimationEditorActorPlaybackController();
                return s_Controller;
            }
        }

        public static bool HasSelectedActorPlayingBlendState()
        {
            if (!TryGetSelectedSceneActor(out _))
            {
                return false;
            }

            Controller.RefreshSelection();
            return Controller.TryGetPlayingBlendState(out _);
        }

        public static bool TryGetSelectedSceneActor(out XAnimationActor actor)
        {
            actor = null;
            GameObject selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected))
            {
                return false;
            }

            actor = selected.GetComponent<XAnimationActor>();
            return actor != null;
        }

        public static void RequestRepaint()
        {
            RepaintSceneViews();
        }

        private static void RepaintSceneViews()
        {
            s_Controller?.RefreshSelection();
            SceneView.RepaintAll();
        }

        private static void DisposeController()
        {
            s_Controller?.Dispose();
            s_Controller = null;
        }
    }
}
#endif
