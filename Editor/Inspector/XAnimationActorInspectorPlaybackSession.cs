#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XAnimationEngine;

namespace XAnimationEditor
{
    internal sealed class XAnimationActorInspectorPlaybackSession : IDisposable
    {
        private readonly XAnimationAssetLoader m_AssetLoader = new(new XAnimationEditorAssetResolver());
        private readonly Dictionary<string, float> m_FloatParameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> m_IntParameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> m_BoolParameters = new(StringComparer.Ordinal);
        private readonly List<TransformSnapshot> m_TransformSnapshots = new();

        private XAnimationActor m_Actor;
        private Animator m_Animator;
        private TextAsset m_AnimationAsset;
        private XAnimationDriver m_Driver;
        private int m_ActorInstanceId;
        private int m_AnimatorInstanceId;
        private int m_AnimationAssetInstanceId;
        private bool m_HasPoseSnapshot;
        private RuntimeAnimatorController m_AnimatorController;
        private bool m_AnimatorEnabled;
        private bool m_AnimatorApplyRootMotion;
        private AnimatorCullingMode m_AnimatorCullingMode;

        public bool IsLoaded => m_Driver != null && m_Actor != null && m_Animator != null;
        public bool IsPaused => m_Driver != null && m_Driver.IsPaused;
        public float GlobalSpeed => m_Driver != null ? m_Driver.GlobalSpeed : 1f;
        public XAnimationActor Actor => m_Actor;

        public bool Matches(XAnimationActor actor)
        {
            if (actor == null)
            {
                return false;
            }

            Animator animator = ResolveAnimator(actor);
            return m_ActorInstanceId == actor.GetInstanceID() &&
                   m_AnimatorInstanceId == (animator != null ? animator.GetInstanceID() : 0) &&
                   m_AnimationAssetInstanceId == (actor.AnimationAsset != null ? actor.AnimationAsset.GetInstanceID() : 0);
        }

        public bool CanPreviewActor(XAnimationActor actor, out string message)
        {
            message = string.Empty;
            if (actor == null)
            {
                message = "当前没有选中的 XAnimationActor。";
                return false;
            }

            if (PrefabUtility.IsPartOfPrefabAsset(actor.gameObject))
            {
                message = "Project prefab asset 请使用 Preview Window 预览。";
                return false;
            }

            if (actor.AnimationAsset == null)
            {
                message = "当前 XAnimationActor 没有绑定 animation asset。";
                return false;
            }

            if (ResolveAnimator(actor) == null)
            {
                message = "当前 XAnimationActor 没有可用 Animator。";
                return false;
            }

            return true;
        }

        public void EnsureLoaded(XAnimationActor actor)
        {
            if (IsLoaded && Matches(actor))
            {
                return;
            }

            Dispose();
            if (!CanPreviewActor(actor, out string message))
            {
                throw new XAnimationException(message);
            }

            m_Actor = actor;
            m_Animator = ResolveAnimator(actor);
            m_AnimationAsset = actor.AnimationAsset;
            m_ActorInstanceId = actor.GetInstanceID();
            m_AnimatorInstanceId = m_Animator.GetInstanceID();
            m_AnimationAssetInstanceId = m_AnimationAsset.GetInstanceID();

            CachePose();
            CacheAnimatorState();
            ConfigureAnimatorForPreview();

            XAnimationCompiledAsset compiledAsset = m_AssetLoader.Load(m_AnimationAsset);
            m_Driver = new XAnimationDriver();
            m_Driver.Initialize(compiledAsset, m_Animator);
            m_Driver.SetUpdateMode(XAnimationUpdateMode.Manual);
            m_Driver.SetUnityAnimationEventsEnabled(false);
            m_Driver.SetGlobalSpeed(1f);
            m_Driver.SetRootMotionEnabled(false);
            RestoreParameters();
        }

        public void PlayState(XAnimationActor actor, string stateKey, XAnimationTransitionOptions transition)
        {
            EnsureLoaded(actor);
            m_Driver.SetPaused(false);
            m_Driver.PlayState(stateKey, transition);
        }

        public void PlayClip(XAnimationActor actor, string clipKey, string channelName, XAnimationTransitionOptions transition)
        {
            EnsureLoaded(actor);
            m_Driver.SetPaused(false);
            m_Driver.PlayClip(clipKey, channelName, transition);
        }

        public void StopAll(bool restorePose)
        {
            if (restorePose)
            {
                Dispose();
                return;
            }

            if (m_Driver != null)
            {
                m_Driver.StopAll();
                m_Driver.SetPaused(false);
            }
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

        public void SetRootMotionEnabled(bool enabled)
        {
            m_Driver?.SetRootMotionEnabled(enabled);
        }

        public bool GetRootMotionEnabled()
        {
            return m_Driver != null && m_Driver.ShouldApplyNativeRootMotion();
        }

        public void Step(float deltaTime)
        {
            if (m_Driver == null)
            {
                return;
            }

            m_Driver.SetPaused(true);
            m_Driver.Step(deltaTime);
        }

        public bool SeekChannel(string channelName, float normalizedTime)
        {
            if (m_Driver == null)
            {
                return false;
            }

            bool result = m_Driver.SeekChannel(channelName, normalizedTime);
            if (result)
            {
                m_Driver.SyncFrame();
            }

            return result;
        }

        public XAnimationChannelState GetChannelState(string channelName)
        {
            return m_Driver != null && !string.IsNullOrWhiteSpace(channelName)
                ? m_Driver.GetChannelState(channelName)
                : null;
        }

        public void SetParameter(string key, float value)
        {
            m_FloatParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetParameter(string key, int value)
        {
            m_IntParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetParameter(string key, bool value)
        {
            m_BoolParameters[key] = value;
            m_Driver?.SetParameter(key, value);
        }

        public void SetTrigger(string key)
        {
            m_Driver?.SetTrigger(key);
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
                m_FloatParameters[key] = value;
                return true;
            }

            return m_FloatParameters.TryGetValue(key, out value);
        }

        public bool TryGetParameter(string key, out int value)
        {
            if (m_Driver != null && m_Driver.TryGetParameter(key, out value))
            {
                m_IntParameters[key] = value;
                return true;
            }

            return m_IntParameters.TryGetValue(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            if (m_Driver != null && m_Driver.TryGetParameter(key, out value))
            {
                m_BoolParameters[key] = value;
                return true;
            }

            return m_BoolParameters.TryGetValue(key, out value);
        }

        public void Dispose()
        {
            if (m_Driver != null)
            {
                try
                {
                    m_Driver.StopAll();
                }
                catch (Exception)
                {
                    // The editor can tear down playables during domain or mode changes.
                }

                m_Driver.Dispose();
                m_Driver = null;
            }

            RestoreAnimatorState();
            RestorePose();
            m_Actor = null;
            m_Animator = null;
            m_AnimationAsset = null;
            m_ActorInstanceId = 0;
            m_AnimatorInstanceId = 0;
            m_AnimationAssetInstanceId = 0;
        }

        private void RestoreParameters()
        {
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

        private void CachePose()
        {
            m_TransformSnapshots.Clear();
            if (m_Actor == null)
            {
                return;
            }

            Transform root = m_Actor.transform;
            CacheTransform(root);
            if (m_Animator != null && m_Animator.transform != root)
            {
                Transform animatorRoot = m_Animator.transform;
                Transform[] children = animatorRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    CacheTransform(children[i]);
                }
            }

            m_HasPoseSnapshot = true;
        }

        private void CacheTransform(Transform transform)
        {
            if (transform == null)
            {
                return;
            }

            for (int i = 0; i < m_TransformSnapshots.Count; i++)
            {
                if (m_TransformSnapshots[i].Transform == transform)
                {
                    return;
                }
            }

            m_TransformSnapshots.Add(new TransformSnapshot(transform));
        }

        private void RestorePose()
        {
            if (!m_HasPoseSnapshot)
            {
                return;
            }

            for (int i = 0; i < m_TransformSnapshots.Count; i++)
            {
                m_TransformSnapshots[i].Restore();
            }

            m_TransformSnapshots.Clear();
            m_HasPoseSnapshot = false;
        }

        private void CacheAnimatorState()
        {
            if (m_Animator == null)
            {
                return;
            }

            m_AnimatorController = m_Animator.runtimeAnimatorController;
            m_AnimatorEnabled = m_Animator.enabled;
            m_AnimatorApplyRootMotion = m_Animator.applyRootMotion;
            m_AnimatorCullingMode = m_Animator.cullingMode;
        }

        private void ConfigureAnimatorForPreview()
        {
            if (m_Animator == null)
            {
                return;
            }

            m_Animator.runtimeAnimatorController = null;
            m_Animator.enabled = true;
            m_Animator.applyRootMotion = false;
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void RestoreAnimatorState()
        {
            if (m_Animator == null)
            {
                return;
            }

            m_Animator.runtimeAnimatorController = m_AnimatorController;
            m_Animator.enabled = m_AnimatorEnabled;
            m_Animator.applyRootMotion = m_AnimatorApplyRootMotion;
            m_Animator.cullingMode = m_AnimatorCullingMode;
            m_AnimatorController = null;
        }

        private static Animator ResolveAnimator(XAnimationActor actor)
        {
            if (actor == null)
            {
                return null;
            }

            Animator animator = actor.Animator != null ? actor.Animator : actor.GetComponent<Animator>();
            return animator != null ? animator : actor.GetComponentInChildren<Animator>(true);
        }

        private readonly struct TransformSnapshot
        {
            public TransformSnapshot(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            public Transform Transform { get; }
            private Vector3 LocalPosition { get; }
            private Quaternion LocalRotation { get; }
            private Vector3 LocalScale { get; }

            public void Restore()
            {
                if (Transform == null)
                {
                    return;
                }

                Transform.localPosition = LocalPosition;
                Transform.localRotation = LocalRotation;
                Transform.localScale = LocalScale;
            }
        }
    }
}
#endif
