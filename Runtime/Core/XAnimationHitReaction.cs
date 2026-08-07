using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace XAnimationEngine
{
    internal struct XAnimationHitReactionJob : IAnimationJob
    {
        public TransformStreamHandle Root;
        public NativeArray<TransformStreamHandle> Bones;
        public Vector3 Axis;
        public NativeArray<float> Angle;

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            float angle = Angle[0] / Bones.Length;
            Vector3 worldAxis = Root.GetRotation(stream) * Axis;
            Quaternion offset = Quaternion.AngleAxis(angle, worldAxis);
            for (int i = Bones.Length - 1; i >= 0; i--)
            {
                TransformStreamHandle bone = Bones[i];
                bone.SetRotation(stream, offset * bone.GetRotation(stream));
            }
        }
    }

    [RequireComponent(typeof(XAnimationActor))]
    [AddComponentMenu("XAnimation/Hit Reaction")]
    public sealed class XAnimationHitReaction : MonoBehaviour
    {
        [SerializeField] private Transform[] m_Bones = Array.Empty<Transform>();
        [SerializeField] private float m_MaximumAngle = 45f;
        [SerializeField] private float m_SmoothingTime = 0.25f;

        private XAnimationActor m_Actor;
        private XAnimationOutputJobHandle<XAnimationHitReactionJob> m_JobHandle;
        private NativeArray<TransformStreamHandle> m_BoneHandles;
        private NativeArray<float> m_Angle;
        private float m_AngularVelocity;

        internal Transform[] Bones => m_Bones;
        internal float MaximumAngle => m_MaximumAngle;
        internal float SmoothingTime => m_SmoothingTime;

        private void Start()
        {
            m_Actor = GetComponent<XAnimationActor>();
            ValidateConfiguration();

            Animator animator = m_Actor.Animator;
            m_BoneHandles = new NativeArray<TransformStreamHandle>(m_Bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < m_Bones.Length; i++)
            {
                m_BoneHandles[i] = animator.BindStreamTransform(m_Bones[i]);
            }

            m_Angle = new NativeArray<float>(1, Allocator.Persistent);
            XAnimationHitReactionJob job = new()
            {
                Root = animator.BindStreamTransform(animator.transform),
                Bones = m_BoneHandles,
                Axis = Vector3.right,
                Angle = m_Angle,
            };
            m_JobHandle = m_Actor.InsertOutputJob(job, nameof(XAnimationHitReaction));
            enabled = false;
        }

        public void Hit(Vector3 worldDirection, float force)
        {
            if (force < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(force), force, "Hit force must be non-negative.");
            }

            if (m_JobHandle == null || !m_JobHandle.IsValid)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} is not attached to the current PlayableGraph.");
            }

            Vector3 localDirection = m_Actor.Animator.transform.InverseTransformDirection(worldDirection);
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                throw new ArgumentException("Hit direction must have a non-zero horizontal component.", nameof(worldDirection));
            }

            localDirection.Normalize();
            XAnimationHitReactionJob job = m_JobHandle.GetJobData();
            job.Axis = Vector3.Cross(Vector3.up, localDirection).normalized;
            m_JobHandle.SetJobData(job);
            m_AngularVelocity = force;
            enabled = true;
        }

        private void Update()
        {
            float angle = Mathf.SmoothDamp(m_Angle[0], 0f, ref m_AngularVelocity, m_SmoothingTime, Mathf.Infinity, Time.deltaTime);
            m_Angle[0] = Mathf.Clamp(angle, 0f, m_MaximumAngle);
            if (m_Angle[0] == 0f && m_AngularVelocity == 0f)
            {
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            m_JobHandle?.Dispose();
            if (m_Angle.IsCreated)
            {
                m_Angle.Dispose();
            }

            if (m_BoneHandles.IsCreated)
            {
                m_BoneHandles.Dispose();
            }
        }

        private void ValidateConfiguration()
        {
            if (!m_Actor.IsInitialized)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} requires an initialized {nameof(XAnimationActor)}.");
            }

            if (m_Bones == null || m_Bones.Length == 0)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} requires at least one bone.");
            }

            Transform animatorRoot = m_Actor.Animator.transform;
            for (int i = 0; i < m_Bones.Length; i++)
            {
                Transform bone = m_Bones[i];
                if (bone == null || !IsInHierarchy(bone, animatorRoot))
                {
                    throw new XAnimationException($"{nameof(XAnimationHitReaction)} bone at index {i} must belong to the Actor Animator hierarchy.");
                }
            }

            if (m_MaximumAngle < 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} Maximum Angle must be non-negative.");
            }

            if (m_SmoothingTime <= 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationHitReaction)} Smoothing Time must be greater than zero.");
            }
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
    }
}
