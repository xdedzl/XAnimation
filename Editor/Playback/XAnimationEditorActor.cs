#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    internal sealed class XAnimationEditorActor : IDisposable
    {
        private readonly Dictionary<string, float> m_FloatParameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> m_IntParameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> m_BoolParameters = new(StringComparer.Ordinal);

        private XAnimationDriver m_Driver;
        private XAnimationCompiledAsset m_CompiledAsset;
        private Animator m_Animator;
        private bool m_RootMotionEnabled;
        private Action<XAnimationCueEvent> m_CueTriggered;
        private Action<XAnimationStateEvent> m_OnStateEnter;
        private Action<XAnimationStateEvent> m_OnStateExit;

        public bool IsLoaded => m_Driver != null && m_Animator != null;
        public bool IsPaused => m_Driver != null && m_Driver.IsPaused;
        public float GlobalSpeed => m_Driver != null ? m_Driver.GlobalSpeed : 1f;

        public event Action<XAnimationCueEvent> CueTriggered
        {
            add
            {
                m_CueTriggered += value;
                if (m_Driver != null)
                {
                    m_Driver.CueTriggered += value;
                }
            }
            remove
            {
                if (m_Driver != null)
                {
                    m_Driver.CueTriggered -= value;
                }
                m_CueTriggered -= value;
            }
        }

        public event Action<XAnimationStateEvent> OnStateEnter
        {
            add
            {
                m_OnStateEnter += value;
                if (m_Driver != null)
                {
                    m_Driver.OnStateEnter += value;
                }
            }
            remove
            {
                if (m_Driver != null)
                {
                    m_Driver.OnStateEnter -= value;
                }
                m_OnStateEnter -= value;
            }
        }

        public event Action<XAnimationStateEvent> OnStateExit
        {
            add
            {
                m_OnStateExit += value;
                if (m_Driver != null)
                {
                    m_Driver.OnStateExit += value;
                }
            }
            remove
            {
                if (m_Driver != null)
                {
                    m_Driver.OnStateExit -= value;
                }
                m_OnStateExit -= value;
            }
        }

        public void Initialize(XAnimationCompiledAsset compiledAsset, Animator animator, bool rootMotionEnabled = false)
        {
            DisposeDriver();
            m_CompiledAsset = compiledAsset ?? throw new XAnimationException("XAnimation editor actor compiled asset cannot be null.");
            m_Animator = animator != null ? animator : throw new XAnimationException("XAnimation editor actor animator cannot be null.");
            m_RootMotionEnabled = rootMotionEnabled;
            CreateDriver(paused: false, globalSpeed: 1f);
        }

        public void Rebuild(XAnimationCompiledAsset compiledAsset, Animator animator)
        {
            if (compiledAsset == null || animator == null)
            {
                DisposeDriver();
                m_CompiledAsset = compiledAsset;
                m_Animator = animator;
                return;
            }

            DisposeDriver();
            m_CompiledAsset = compiledAsset;
            m_Animator = animator;
            CreateDriver(paused: false, globalSpeed: 1f);
        }

        public void Dispose()
        {
            DisposeDriver(stopAll: true);
            m_CompiledAsset = null;
            m_Animator = null;
        }

        public void PlayState(string stateKey, XAnimationTransitionOptions transition = default)
        {
            EnsureLoaded();
            m_Driver.SetPaused(false);
            m_Driver.PlayState(stateKey, transition);
        }

        public void PlayState(string channelName, string stateKey, XAnimationTransitionOptions transition = default)
        {
            EnsureLoaded();
            m_Driver.SetPaused(false);
            m_Driver.PlayState(channelName, stateKey, transition);
        }

        public XAnimationActionHandle PlayAction(string stateKey, XAnimationActionOptions options = default)
        {
            EnsureLoaded();
            m_Driver.SetPaused(false);
            return m_Driver.PlayAction(stateKey, options);
        }

        public XAnimationActionHandle PlayAction(string channelName, string stateKey, XAnimationActionOptions options = default)
        {
            EnsureLoaded();
            m_Driver.SetPaused(false);
            return m_Driver.PlayAction(channelName, stateKey, options);
        }

        public void PlayClip(string clipKey, string channelName, XAnimationTransitionOptions transition = default)
        {
            EnsureLoaded();
            m_Driver.SetPaused(false);
            m_Driver.PlayClip(clipKey, channelName, transition);
        }

        public void Stop(string channelName, float fadeOut = default)
        {
            if (m_Driver == null)
            {
                return;
            }

            m_Driver.Stop(channelName, fadeOut);
        }

        public void StopAll()
        {
            if (m_Driver == null)
            {
                return;
            }

            m_Driver.StopAll();
        }

        public void StopAllAndResume()
        {
            StopAll();
            SetPaused(false);
        }

        public void Pause()
        {
            m_Driver?.Pause();
        }

        public void Resume()
        {
            m_Driver?.Resume();
        }

        public void SetPaused(bool paused)
        {
            m_Driver?.SetPaused(paused);
        }

        public void SetGlobalSpeed(float speed)
        {
            m_Driver?.SetGlobalSpeed(speed);
        }

        public void Step(float deltaTime)
        {
            if (m_Driver == null)
            {
                return;
            }

            m_Driver.Step(deltaTime);
        }

        public void StepPaused(float deltaTime)
        {
            SetPaused(true);
            Step(deltaTime);
        }

        public bool SeekChannel(string channelName, float normalizedTime)
        {
            return m_Driver != null && m_Driver.SeekChannel(channelName, normalizedTime);
        }

        public bool SeekChannelAndSync(string channelName, float normalizedTime)
        {
            bool result = SeekChannel(channelName, normalizedTime);
            if (result)
            {
                SyncFrame();
            }

            return result;
        }

        public void SyncFrame()
        {
            m_Driver?.SyncFrame();
        }

        public void SetChannelWeight(string channelName, float weight)
        {
            EnsureLoaded();
            m_Driver.SetChannelWeight(channelName, weight);
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            m_RootMotionEnabled = enabled;
            m_Driver?.SetRootMotionEnabled(enabled);
        }

        public bool GetRootMotionEnabled()
        {
            return m_RootMotionEnabled;
        }

        public void SetParameter(string key, float value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            m_FloatParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetParameter(string key, int value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            m_IntParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetParameter(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            m_BoolParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetTrigger(string key)
        {
            m_Driver?.SetTrigger(key);
        }

        public void ResetTrigger(string key)
        {
            m_Driver?.ResetTrigger(key);
        }

        public void ClearParameterOverrides()
        {
            m_FloatParameters.Clear();
            m_IntParameters.Clear();
            m_BoolParameters.Clear();
        }

        public bool TryGetParameter(string key, out float value)
        {
            if (m_Driver != null && m_Driver.TryGetParameter(key, out value))
            {
                return true;
            }

            return m_FloatParameters.TryGetValue(key, out value);
        }

        public bool TryGetParameter(string key, out int value)
        {
            if (m_Driver != null && m_Driver.TryGetParameter(key, out value))
            {
                return true;
            }

            return m_IntParameters.TryGetValue(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            if (m_Driver != null && m_Driver.TryGetParameter(key, out value))
            {
                return true;
            }

            return m_BoolParameters.TryGetValue(key, out value);
        }

        public bool TryGetTrigger(string key, out bool value)
        {
            value = false;
            return m_Driver != null && m_Driver.TryGetTrigger(key, out value);
        }

        public XAnimationChannelState GetChannelState(string channelName)
        {
            return m_Driver != null &&
                   !string.IsNullOrWhiteSpace(channelName) &&
                   m_Driver.TryGetCurrentState(channelName, out XAnimationChannelState state)
                ? state
                : null;
        }

        public XAnimationDebugGraphSnapshot GetDebugGraphSnapshot()
        {
            return IsLoaded
                ? m_Driver.GetDebugGraphSnapshot()
                : XAnimationDebugGraphSnapshot.Invalid("XAnimation editor actor is not loaded.");
        }

        public void PreloadAll()
        {
            EnsureLoaded();
            m_Driver.PreloadAll();
        }

        private void CreateDriver(bool paused, float globalSpeed)
        {
            m_Driver = new XAnimationDriver();
            m_Driver.Initialize(m_CompiledAsset, m_Animator);
            m_Driver.SetUpdateMode(XAnimationUpdateMode.Manual);
            m_Driver.SetUnityAnimationEventsEnabled(false);
            m_Driver.SetPaused(paused);
            m_Driver.SetGlobalSpeed(globalSpeed);
            m_Driver.SetRootMotionEnabled(m_RootMotionEnabled);
            AttachEvents();
            RestoreParameterOverrides();
        }

        private void RestoreParameterOverrides()
        {
            if (m_Driver == null)
            {
                return;
            }

            foreach (KeyValuePair<string, float> kvp in m_FloatParameters)
            {
                m_Driver.SetParameter(kvp.Key, kvp.Value);
            }

            foreach (KeyValuePair<string, int> kvp in m_IntParameters)
            {
                m_Driver.SetParameter(kvp.Key, kvp.Value);
            }

            foreach (KeyValuePair<string, bool> kvp in m_BoolParameters)
            {
                m_Driver.SetParameter(kvp.Key, kvp.Value);
            }
        }

        private void AttachEvents()
        {
            if (m_Driver == null)
            {
                return;
            }

            if (m_CueTriggered != null)
            {
                m_Driver.CueTriggered += m_CueTriggered;
            }

            if (m_OnStateEnter != null)
            {
                m_Driver.OnStateEnter += m_OnStateEnter;
            }

            if (m_OnStateExit != null)
            {
                m_Driver.OnStateExit += m_OnStateExit;
            }
        }

        private void DetachEvents()
        {
            if (m_Driver == null)
            {
                return;
            }

            if (m_CueTriggered != null)
            {
                m_Driver.CueTriggered -= m_CueTriggered;
            }

            if (m_OnStateEnter != null)
            {
                m_Driver.OnStateEnter -= m_OnStateEnter;
            }

            if (m_OnStateExit != null)
            {
                m_Driver.OnStateExit -= m_OnStateExit;
            }
        }

        private void DisposeDriver(bool stopAll = false)
        {
            if (m_Driver == null)
            {
                return;
            }

            try
            {
                if (stopAll)
                {
                    m_Driver.StopAll();
                }
            }
            catch (Exception)
            {
                // The editor can tear down playables during domain or mode changes.
            }

            DetachEvents();
            m_Driver.Dispose();
            m_Driver = null;
        }

        private void EnsureLoaded()
        {
            if (!IsLoaded)
            {
                throw new XAnimationException("XAnimation editor actor is not loaded.");
            }
        }
    }
}
#endif
