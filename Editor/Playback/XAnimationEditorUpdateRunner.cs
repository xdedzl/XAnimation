#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    [InitializeOnLoad]
    internal static class XAnimationEditorUpdateRunner
    {
        private static readonly List<XAnimationDriverScheduler> s_Schedulers = new();
        private static double s_LastTime;

        static XAnimationEditorUpdateRunner()
        {
            s_LastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        internal static void Register(XAnimationDriverScheduler scheduler)
        {
            if (scheduler == null)
            {
                return;
            }

            if (!s_Schedulers.Contains(scheduler))
            {
                s_Schedulers.Add(scheduler);
            }
        }

        internal static void Unregister(XAnimationDriverScheduler scheduler)
        {
            if (scheduler == null)
            {
                return;
            }

            s_Schedulers.Remove(scheduler);
        }

        private static void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)System.Math.Max(0d, now - s_LastTime);
            s_LastTime = now;

            if (Application.isPlaying)
            {
                return;
            }

            for (int i = s_Schedulers.Count - 1; i >= 0; i--)
            {
                XAnimationDriverScheduler scheduler = s_Schedulers[i];
                if (scheduler == null || !scheduler.IsRegisteredForAutomaticUpdate)
                {
                    s_Schedulers.RemoveAt(i);
                    continue;
                }

                scheduler.TickFromScheduler(deltaTime);
            }
        }
    }
}
#endif
