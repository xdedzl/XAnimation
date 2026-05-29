using System;
using System.Reflection;
using UnityEngine;

namespace XAnimationEngine
{
    internal sealed class XAnimationDriverScheduler
    {
#if UNITY_EDITOR
        private const string EditorUpdateRunnerTypeName = "XFramework.Animation.XAnimationEditorUpdateRunner, XAnimationEditor";
        private static MethodInfo s_RegisterEditorUpdateRunnerMethod;
        private static MethodInfo s_UnregisterEditorUpdateRunnerMethod;
#endif

        private readonly XAnimationRuntime m_Runtime;

        private bool m_IsStepping;
        private bool m_IsRegisteredForAutomaticUpdate;
        private bool m_HasPreparedFrame;

        internal XAnimationDriverScheduler(XAnimationRuntime runtime)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        internal bool IsRegisteredForAutomaticUpdate => m_IsRegisteredForAutomaticUpdate && m_Runtime.IsInitialized;

        internal void RunStep(float deltaTime)
        {
            bool originalPaused = m_Runtime.IsPaused;
            m_IsStepping = true;
            try
            {
                m_Runtime.RunManualFrame(deltaTime);
            }
            finally
            {
                m_IsStepping = false;
                m_Runtime.SetPaused(originalPaused);
            }
        }

        internal void TickFromScheduler(float deltaTime)
        {
            TickPrepareFromScheduler(deltaTime);
            TickFinalizeFromScheduler();
        }

        internal void TickPrepareFromScheduler(float deltaTime)
        {
            m_HasPreparedFrame = false;
            m_HasPreparedFrame = m_Runtime.PrepareFromScheduler(deltaTime, m_IsStepping);
        }

        internal void TickFinalizeFromScheduler()
        {
            if (!m_HasPreparedFrame)
            {
                return;
            }

            m_HasPreparedFrame = false;
            m_Runtime.FinalizeFromScheduler();
        }

        internal void RegisterForAutomaticUpdate()
        {
            if (m_IsRegisteredForAutomaticUpdate)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RegisterEditorUpdateRunner();
                m_IsRegisteredForAutomaticUpdate = true;
                return;
            }
#endif

            XAnimationRuntimePlayerLoopRunner.Register(this);
            m_IsRegisteredForAutomaticUpdate = true;
        }

        internal void UnregisterFromAutomaticUpdate()
        {
            if (!m_IsRegisteredForAutomaticUpdate)
            {
                return;
            }

#if UNITY_EDITOR
            UnregisterEditorUpdateRunner();
#endif
            XAnimationRuntimePlayerLoopRunner.Unregister(this);
            m_IsRegisteredForAutomaticUpdate = false;
        }

#if UNITY_EDITOR
        private void RegisterEditorUpdateRunner()
        {
            InvokeEditorUpdateRunner(ref s_RegisterEditorUpdateRunnerMethod, "Register");
        }

        private void UnregisterEditorUpdateRunner()
        {
            InvokeEditorUpdateRunner(ref s_UnregisterEditorUpdateRunnerMethod, "Unregister");
        }

        private void InvokeEditorUpdateRunner(ref MethodInfo cachedMethod, string methodName)
        {
            cachedMethod ??= Type.GetType(EditorUpdateRunnerTypeName)?.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);

            cachedMethod?.Invoke(null, new object[] { this });
        }
#endif
    }
}
