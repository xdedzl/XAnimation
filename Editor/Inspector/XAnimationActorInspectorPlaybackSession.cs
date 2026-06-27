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
        private readonly XAnimationEditorActor m_EditorActor = new();
        private readonly List<TransformSnapshot> m_TransformSnapshots = new();

        private XAnimationActor m_Actor;
        private Animator m_Animator;
        private TextAsset m_AnimationAsset;
        private int m_ActorInstanceId;
        private int m_AnimatorInstanceId;
        private int m_AnimationAssetInstanceId;
        private bool m_HasPoseSnapshot;
        private RuntimeAnimatorController m_AnimatorController;
        private bool m_AnimatorEnabled;
        private bool m_AnimatorApplyRootMotion;
        private AnimatorCullingMode m_AnimatorCullingMode;

        public bool IsLoaded => m_EditorActor.IsLoaded && m_Actor != null && m_Animator != null;
        public bool IsPaused => m_EditorActor.IsPaused;
        public float GlobalSpeed => m_EditorActor.GlobalSpeed;
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
            m_EditorActor.Initialize(compiledAsset, m_Animator);
        }

        public void PlayState(XAnimationActor actor, string stateKey, XAnimationTransitionOptions transition)
        {
            EnsureLoaded(actor);
            m_EditorActor.PlayState(stateKey, transition);
        }

        public void PlayState(XAnimationActor actor, string channelName, string stateKey, XAnimationTransitionOptions transition)
        {
            EnsureLoaded(actor);
            m_EditorActor.PlayState(channelName, stateKey, transition);
        }

        public void PlayClip(XAnimationActor actor, string clipKey, string channelName, XAnimationTransitionOptions transition)
        {
            EnsureLoaded(actor);
            m_EditorActor.PlayClip(clipKey, channelName, transition);
        }

        public XAnimationActionHandle PlayAction(XAnimationActor actor, string stateKey, XAnimationActionOptions options = default)
        {
            EnsureLoaded(actor);
            return m_EditorActor.PlayAction(stateKey, options);
        }

        public void StopAll(bool restorePose)
        {
            if (restorePose)
            {
                Dispose();
                return;
            }

            m_EditorActor.StopAllAndResume();
        }

        public void Pause()
        {
            m_EditorActor.Pause();
        }

        public void Resume()
        {
            m_EditorActor.Resume();
        }

        public void SetPaused(bool paused)
        {
            m_EditorActor.SetPaused(paused);
        }

        public void SetGlobalSpeed(float speed)
        {
            m_EditorActor.SetGlobalSpeed(speed);
        }

        public void SetRootMotionEnabled(bool enabled)
        {
            m_EditorActor.SetRootMotionEnabled(enabled);
        }

        public bool GetRootMotionEnabled()
        {
            return m_EditorActor.GetRootMotionEnabled();
        }

        public void Step(float deltaTime)
        {
            m_EditorActor.StepPaused(deltaTime);
        }

        public bool SeekChannel(string channelName, float normalizedTime)
        {
            return m_EditorActor.SeekChannelAndSync(channelName, normalizedTime);
        }

        public XAnimationChannelState GetChannelState(string channelName)
        {
            return m_EditorActor.GetChannelState(channelName);
        }

        public void SetParameter(string key, float value)
        {
            m_EditorActor.SetParameter(key, value);
        }

        public void SetParameter(string key, int value)
        {
            m_EditorActor.SetParameter(key, value);
        }

        public void SetParameter(string key, bool value)
        {
            m_EditorActor.SetParameter(key, value);
        }

        public void SetTrigger(string key)
        {
            m_EditorActor.SetTrigger(key);
        }

        public void ClearParameterOverrides()
        {
            m_EditorActor.ClearParameterOverrides();
        }

        public bool TryGetParameter(string key, out float value)
        {
            return m_EditorActor.TryGetParameter(key, out value);
        }

        public bool TryGetParameter(string key, out int value)
        {
            return m_EditorActor.TryGetParameter(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            return m_EditorActor.TryGetParameter(key, out value);
        }

        public void Dispose()
        {
            m_EditorActor.Dispose();

            RestoreAnimatorState();
            RestorePose();
            m_Actor = null;
            m_Animator = null;
            m_AnimationAsset = null;
            m_ActorInstanceId = 0;
            m_AnimatorInstanceId = 0;
            m_AnimationAssetInstanceId = 0;
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
