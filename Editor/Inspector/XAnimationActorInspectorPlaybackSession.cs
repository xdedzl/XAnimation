#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using XAnimationEngine;

namespace XAnimationEditor
{
    internal sealed class XAnimationActorInspectorPlaybackSession : IDisposable
    {
        private readonly XAnimationAssetLoader m_AssetLoader = new(new XAnimationEditorAssetResolver());
        private readonly XAnimationEditorActor m_EditorActor = new();
        private readonly XAnimationActorOutputJobsPreview m_OutputJobsPreview = new();
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

            m_OutputJobsPreview.Validate(m_Actor, m_Animator);

            CachePose();
            CacheAnimatorState();
            ConfigureAnimatorForPreview();

            XAnimationCompiledAsset compiledAsset = m_AssetLoader.Load(m_AnimationAsset);
            m_EditorActor.Initialize(compiledAsset, m_Animator);
            m_OutputJobsPreview.Initialize(m_Actor, m_Animator, m_EditorActor);
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

        public void Stop(string channelName)
        {
            m_EditorActor.Stop(channelName);
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

        public void SetChannelWeight(string channelName, float weight)
        {
            m_EditorActor.SetChannelWeight(channelName, weight);
        }

        public float GetChannelWeight(string channelName)
        {
            return m_EditorActor.GetChannelWeight(channelName);
        }

        public void PauseChannel(string channelName)
        {
            m_EditorActor.PauseChannel(channelName);
        }

        public void ResumeChannel(string channelName)
        {
            m_EditorActor.ResumeChannel(channelName);
        }

        public bool IsChannelPaused(string channelName)
        {
            return m_EditorActor.IsChannelPaused(channelName);
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

        public void SyncFrame()
        {
            m_EditorActor.SyncFrame();
        }

        public bool SeekChannel(string channelName, float normalizedTime)
        {
            return m_EditorActor.SeekChannelAndSync(channelName, normalizedTime);
        }

        public XAnimationChannelState GetChannelState(string channelName)
        {
            return m_EditorActor.GetChannelState(channelName);
        }

        public XAnimationDebugGraphSnapshot GetDebugGraphSnapshot()
        {
            return m_EditorActor.GetDebugGraphSnapshot();
        }

        public void PreviewHit(Vector3 worldDirection, float force)
        {
            m_OutputJobsPreview.Hit(worldDirection, force);
        }

        public void SetParameter(string key, float value)
        {
            m_EditorActor.SetParameter(key, value);
        }

        public void SetParameter(string key, int value)
        {
            m_EditorActor.SetParameter(key, value);
        }

        public void SetParameter(string key, string value)
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

        public bool TryGetParameter(string key, out string value)
        {
            return m_EditorActor.TryGetParameter(key, out value);
        }

        public bool TryGetParameter(string key, out bool value)
        {
            return m_EditorActor.TryGetParameter(key, out value);
        }

        public void Dispose()
        {
            m_OutputJobsPreview.Dispose();
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

    internal sealed class XAnimationActorOutputJobsPreview : IDisposable
    {
        private readonly List<IOutputJobBinding> m_Bindings = new();
        private readonly List<HitReactionBinding> m_HitReactions = new();
        private readonly List<AimIKBinding> m_AimIKs = new();

        private XAnimationEditorActor m_EditorActor;
        private double m_LastUpdateTime;

        public void Validate(XAnimationActor actor, Animator animator)
        {
            MonoBehaviour[] components = actor.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (!component.enabled)
                {
                    continue;
                }

                switch (component)
                {
                    case XAnimationHitReaction hitReaction:
                        ValidateHitReaction(hitReaction, animator);
                        break;
                    case XAnimationDamping damping:
                        CollectDampingBones(damping, animator);
                        break;
                    case XAnimationAimIK aimIK:
                        aimIK.ValidateConfiguration(animator);
                        break;
                }
            }
        }

        public void Initialize(XAnimationActor actor, Animator animator, XAnimationEditorActor editorActor)
        {
            Dispose();
            m_EditorActor = editorActor;

            MonoBehaviour[] components = actor.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (!component.enabled)
                {
                    continue;
                }

                switch (component)
                {
                    case XAnimationHitReaction hitReaction:
                    {
                        HitReactionBinding binding = new(hitReaction, animator, editorActor);
                        m_HitReactions.Add(binding);
                        m_Bindings.Add(binding);
                        break;
                    }
                    case XAnimationDamping damping:
                        m_Bindings.Add(new DampingBinding(damping, animator, editorActor));
                        break;
                    case XAnimationAimIK aimIK:
                    {
                        AimIKBinding binding = new(aimIK, animator, editorActor);
                        m_AimIKs.Add(binding);
                        m_Bindings.Add(binding);
                        break;
                    }
                }
            }

            if (m_HitReactions.Count > 0 || m_AimIKs.Count > 0)
            {
                m_LastUpdateTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += OnEditorUpdate;
            }
        }

        public void Hit(Vector3 worldDirection, float force)
        {
            if (force < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(force), force, "Hit force must be non-negative.");
            }

            if (m_HitReactions.Count == 0)
            {
                throw new XAnimationException("The selected XAnimationActor has no enabled XAnimationHitReaction to preview.");
            }

            for (int i = 0; i < m_HitReactions.Count; i++)
            {
                m_HitReactions[i].Hit(worldDirection, force);
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= OnEditorUpdate;
            for (int i = m_Bindings.Count - 1; i >= 0; i--)
            {
                m_Bindings[i].Dispose();
            }

            m_Bindings.Clear();
            m_HitReactions.Clear();
            m_AimIKs.Clear();
            m_EditorActor = null;
        }

        private void OnEditorUpdate()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            float deltaTime = (float)Math.Max(0d, currentTime - m_LastUpdateTime);
            m_LastUpdateTime = currentTime;

            bool requiresEvaluation = false;
            for (int i = 0; i < m_HitReactions.Count; i++)
            {
                requiresEvaluation |= m_HitReactions[i].Update(deltaTime);
            }

            for (int i = 0; i < m_AimIKs.Count; i++)
            {
                requiresEvaluation |= m_AimIKs[i].Update();
            }

            if (!requiresEvaluation)
            {
                return;
            }

            m_EditorActor.SyncFrame();
            SceneView.RepaintAll();
        }

        private static void ValidateHitReaction(XAnimationHitReaction hitReaction, Animator animator)
        {
            Transform[] bones = hitReaction.Bones;
            if (bones == null || bones.Length == 0)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} requires at least one bone.");
            }

            Transform animatorRoot = animator.transform;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !IsInHierarchy(bones[i], animatorRoot))
                {
                    throw new XAnimationException($"{nameof(XAnimationHitReaction)} bone at index {i} must belong to the Actor Animator hierarchy.");
                }
            }

            if (hitReaction.MaximumAngle < 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} Maximum Angle must be non-negative.");
            }

            if (hitReaction.SmoothingTime <= 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} Smoothing Time must be greater than zero.");
            }
        }

        private static Transform[] CollectDampingBones(XAnimationDamping damping, Animator animator)
        {
            if (damping.EndBone == null)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} requires an End Bone.");
            }

            if (damping.BoneCount < 2)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} Bone Count must be at least two.");
            }

            if (damping.DampingTime <= 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} Damping Time must be greater than zero.");
            }

            Transform animatorRoot = animator.transform;
            Transform[] bones = new Transform[damping.BoneCount];
            Transform bone = damping.EndBone;
            for (int i = bones.Length - 1; i >= 0; i--)
            {
                if (bone == null || bone == animatorRoot)
                {
                    throw new XAnimationException($"{nameof(XAnimationDamping)} parent chain does not contain {damping.BoneCount} bones below the Animator root.");
                }

                bones[i] = bone;
                bone = bone.parent;
            }

            if (!IsInHierarchy(bone, animatorRoot))
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} End Bone must belong to the Actor Animator hierarchy and leave a parent for the root handle.");
            }

            return bones;
        }

        private static bool IsInHierarchy(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return true;
                }
            }

            return false;
        }

        private interface IOutputJobBinding : IDisposable
        {
        }

        private sealed class HitReactionBinding : IOutputJobBinding
        {
            private readonly Animator m_Animator;
            private readonly float m_MaximumAngle;
            private readonly float m_SmoothingTime;
            private readonly NativeArray<TransformStreamHandle> m_BoneHandles;
            private NativeArray<float> m_Angle;
            private readonly XAnimationOutputJobHandle<XAnimationHitReactionJob> m_Handle;

            private float m_AngularVelocity;
            private bool m_IsActive;

            public HitReactionBinding(XAnimationHitReaction hitReaction, Animator animator, XAnimationEditorActor editorActor)
            {
                m_Animator = animator;
                m_MaximumAngle = hitReaction.MaximumAngle;
                m_SmoothingTime = hitReaction.SmoothingTime;

                Transform[] bones = hitReaction.Bones;
                m_BoneHandles = new NativeArray<TransformStreamHandle>(bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < bones.Length; i++)
                {
                    m_BoneHandles[i] = animator.BindStreamTransform(bones[i]);
                }

                m_Angle = new NativeArray<float>(1, Allocator.Persistent);
                XAnimationHitReactionJob job = new()
                {
                    Root = animator.BindStreamTransform(animator.transform),
                    Bones = m_BoneHandles,
                    Axis = Vector3.right,
                    Angle = m_Angle,
                };
                m_Handle = editorActor.InsertOutputJob(job, nameof(XAnimationHitReaction));
            }

            public void Hit(Vector3 worldDirection, float force)
            {
                Vector3 localDirection = m_Animator.transform.InverseTransformDirection(worldDirection);
                localDirection.y = 0f;
                if (localDirection.sqrMagnitude <= Mathf.Epsilon)
                {
                    throw new ArgumentException("Hit direction must have a non-zero horizontal component.", nameof(worldDirection));
                }

                localDirection.Normalize();
                XAnimationHitReactionJob job = m_Handle.GetJobData();
                job.Axis = Vector3.Cross(Vector3.up, localDirection).normalized;
                m_Handle.SetJobData(job);
                m_AngularVelocity = force;
                m_IsActive = true;
            }

            public bool Update(float deltaTime)
            {
                if (!m_IsActive)
                {
                    return false;
                }

                float angle = Mathf.SmoothDamp(m_Angle[0], 0f, ref m_AngularVelocity, m_SmoothingTime, Mathf.Infinity, deltaTime);
                m_Angle[0] = Mathf.Clamp(angle, 0f, m_MaximumAngle);
                if (m_Angle[0] == 0f && m_AngularVelocity == 0f)
                {
                    m_IsActive = false;
                }

                return true;
            }

            public void Dispose()
            {
                m_Handle.Dispose();
                m_Angle.Dispose();
                m_BoneHandles.Dispose();
            }
        }

        private sealed class DampingBinding : IOutputJobBinding
        {
            private readonly NativeArray<TransformStreamHandle> m_JointHandles;
            private readonly NativeArray<Vector3> m_LocalPositions;
            private readonly NativeArray<Quaternion> m_LocalRotations;
            private readonly NativeArray<Vector3> m_Positions;
            private readonly NativeArray<Vector3> m_Velocities;
            private readonly XAnimationOutputJobHandle<XAnimationDampingJob> m_Handle;

            public DampingBinding(XAnimationDamping damping, Animator animator, XAnimationEditorActor editorActor)
            {
                Transform[] bones = CollectDampingBones(damping, animator);
                int boneCount = bones.Length;
                m_JointHandles = new NativeArray<TransformStreamHandle>(boneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_LocalPositions = new NativeArray<Vector3>(boneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_LocalRotations = new NativeArray<Quaternion>(boneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_Positions = new NativeArray<Vector3>(boneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_Velocities = new NativeArray<Vector3>(boneCount, Allocator.Persistent);

                for (int i = 0; i < boneCount; i++)
                {
                    Transform bone = bones[i];
                    m_JointHandles[i] = animator.BindStreamTransform(bone);
                    m_LocalPositions[i] = bone.localPosition;
                    m_LocalRotations[i] = bone.localRotation;
                    m_Positions[i] = bone.position;
                }

                XAnimationDampingJob job = new()
                {
                    RootHandle = animator.BindStreamTransform(bones[0].parent),
                    JointHandles = m_JointHandles,
                    LocalPositions = m_LocalPositions,
                    LocalRotations = m_LocalRotations,
                    Positions = m_Positions,
                    Velocities = m_Velocities,
                    DampingTime = damping.DampingTime,
                };
                m_Handle = editorActor.InsertOutputJob(job, $"{nameof(XAnimationDamping)}:{damping.EndBone.name}");
            }

            public void Dispose()
            {
                m_Handle.Dispose();
                m_JointHandles.Dispose();
                m_LocalPositions.Dispose();
                m_LocalRotations.Dispose();
                m_Positions.Dispose();
                m_Velocities.Dispose();
            }
        }

        private sealed class AimIKBinding : IOutputJobBinding
        {
            private readonly XAnimationAimIK m_AimIK;
            private readonly NativeArray<TransformStreamHandle> m_BoneHandles;
            private readonly NativeArray<float> m_BoneWeights;
            private readonly XAnimationOutputJobHandle<XAnimationAimIKJob> m_Handle;
            private Vector3 m_LastTargetPosition;
            private Vector3 m_LastAimAxis;
            private float m_LastMaximumYaw;
            private float m_LastMaximumPitch;
            private float m_LastWeight;
            private bool m_HasPreviewState;

            public AimIKBinding(XAnimationAimIK aimIK, Animator animator, XAnimationEditorActor editorActor)
            {
                m_AimIK = aimIK;
                Transform[] bones = aimIK.Bones;
                float[] normalizedWeights = aimIK.CreateNormalizedBoneWeights();
                m_BoneHandles = new NativeArray<TransformStreamHandle>(bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_BoneWeights = new NativeArray<float>(bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < bones.Length; i++)
                {
                    m_BoneHandles[i] = animator.BindStreamTransform(bones[i]);
                    m_BoneWeights[i] = normalizedWeights[i];
                }

                XAnimationAimIKJob job = new()
                {
                    Root = animator.BindStreamTransform(animator.transform),
                    Aim = animator.BindStreamTransform(aimIK.AimTransform),
                    Bones = m_BoneHandles,
                    BoneWeights = m_BoneWeights,
                    AimAxis = aimIK.AimAxis.normalized,
                    TargetPosition = aimIK.PreviewTargetWorldPosition,
                    MaximumYaw = aimIK.MaximumYaw,
                    MaximumPitch = aimIK.MaximumPitch,
                    Weight = aimIK.Weight,
                };
                m_Handle = editorActor.InsertOutputJob(job, nameof(XAnimationAimIK));
            }

            public bool Update()
            {
                Vector3 targetPosition = m_AimIK.PreviewTargetWorldPosition;
                Vector3 aimAxis = m_AimIK.AimAxis.normalized;
                float maximumYaw = m_AimIK.MaximumYaw;
                float maximumPitch = m_AimIK.MaximumPitch;
                float weight = m_AimIK.Weight;
                if (m_HasPreviewState &&
                    targetPosition == m_LastTargetPosition &&
                    aimAxis == m_LastAimAxis &&
                    Mathf.Approximately(maximumYaw, m_LastMaximumYaw) &&
                    Mathf.Approximately(maximumPitch, m_LastMaximumPitch) &&
                    Mathf.Approximately(weight, m_LastWeight))
                {
                    return false;
                }

                XAnimationAimIKJob job = m_Handle.GetJobData();
                job.TargetPosition = targetPosition;
                job.AimAxis = aimAxis;
                job.MaximumYaw = maximumYaw;
                job.MaximumPitch = maximumPitch;
                job.Weight = weight;
                m_Handle.SetJobData(job);

                m_LastTargetPosition = targetPosition;
                m_LastAimAxis = aimAxis;
                m_LastMaximumYaw = maximumYaw;
                m_LastMaximumPitch = maximumPitch;
                m_LastWeight = weight;
                m_HasPreviewState = true;
                return true;
            }

            public void Dispose()
            {
                m_Handle.Dispose();
                m_BoneHandles.Dispose();
                m_BoneWeights.Dispose();
            }
        }
    }
}
#endif
