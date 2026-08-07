#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    [CustomEditor(typeof(XAnimationAimIK))]
    public sealed class XAnimationAimIKEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox("在 Scene 视图中拖动黄色目标点；播放 XAnimation 场景预览时会使用相同的 Aim IK Output Job。", MessageType.Info);
        }

        private void OnSceneGUI()
        {
            XAnimationAimIK aimIK = (XAnimationAimIK)target;
            if (!aimIK.enabled || EditorUtility.IsPersistent(aimIK.gameObject) || aimIK.AimTransform == null)
            {
                return;
            }

            Transform actorRoot = aimIK.transform;
            Vector3 origin = aimIK.AimTransform.position;
            Vector3 targetPosition = aimIK.PreviewTargetWorldPosition;
            Vector3 rawDirection = targetPosition - origin;
            if (rawDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            float distance = rawDirection.magnitude;
            rawDirection /= distance;
            Vector3 clampedDirection = XAnimationAimIKUtility.ClampDirection(actorRoot.rotation, rawDirection, aimIK.MaximumYaw, aimIK.MaximumPitch);

            Handles.color = new Color(1f, 0.35f, 0.2f, 0.9f);
            Handles.DrawLine(origin, targetPosition, 2f);
            Handles.color = new Color(0.2f, 1f, 0.45f, 0.95f);
            Handles.DrawLine(origin, origin + clampedDirection * distance, 3f);

            float arcRadius = Mathf.Clamp(distance * 0.2f, 0.4f, 2f);
            Vector3 yawStart = Quaternion.AngleAxis(-aimIK.MaximumYaw, actorRoot.up) * actorRoot.forward;
            Handles.color = new Color(0.25f, 0.65f, 1f, 0.75f);
            Handles.DrawWireArc(origin, actorRoot.up, yawStart, aimIK.MaximumYaw * 2f, arcRadius);
            Vector3 pitchStart = Quaternion.AngleAxis(-aimIK.MaximumPitch, actorRoot.right) * actorRoot.forward;
            Handles.DrawWireArc(origin, actorRoot.right, pitchStart, aimIK.MaximumPitch * 2f, arcRadius * 0.85f);

            Handles.color = new Color(1f, 0.8f, 0.15f, 1f);
            float handleSize = HandleUtility.GetHandleSize(targetPosition) * 0.08f;
            Handles.SphereHandleCap(0, targetPosition, Quaternion.identity, handleSize, EventType.Repaint);
            Handles.Label(targetPosition + Vector3.up * handleSize, "Aim Target");

            EditorGUI.BeginChangeCheck();
            Vector3 newTargetPosition = Handles.PositionHandle(targetPosition, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(aimIK, "Move XAnimation Aim IK Target");
            aimIK.PreviewTargetWorldPosition = newTargetPosition;
            EditorUtility.SetDirty(aimIK);
            PrefabUtility.RecordPrefabInstancePropertyModifications(aimIK);
            SceneView.RepaintAll();
        }
    }
}
#endif
