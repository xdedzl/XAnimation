using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace XAnimationEngine
{
    internal static class XAnimationAimIKUtility
    {
        internal static Vector3 ClampDirection(Quaternion rootRotation, Vector3 worldDirection, float maximumYaw, float maximumPitch)
        {
            Vector3 localDirection = Quaternion.Inverse(rootRotation) * worldDirection.normalized;
            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float horizontalLength = Mathf.Sqrt(localDirection.x * localDirection.x + localDirection.z * localDirection.z);
            float pitch = Mathf.Atan2(localDirection.y, horizontalLength) * Mathf.Rad2Deg;

            yaw = Mathf.Clamp(yaw, -maximumYaw, maximumYaw) * Mathf.Deg2Rad;
            pitch = Mathf.Clamp(pitch, -maximumPitch, maximumPitch) * Mathf.Deg2Rad;
            float horizontalScale = Mathf.Cos(pitch);
            Vector3 clampedLocalDirection = new(
                Mathf.Sin(yaw) * horizontalScale,
                Mathf.Sin(pitch),
                Mathf.Cos(yaw) * horizontalScale);
            return rootRotation * clampedLocalDirection;
        }
    }

    internal struct XAnimationAimIKJob : IAnimationJob
    {
        public TransformStreamHandle Root;
        public TransformStreamHandle Aim;
        public NativeArray<TransformStreamHandle> Bones;
        public NativeArray<float> BoneWeights;
        public Vector3 AimAxis;
        public Vector3 TargetPosition;
        public float MaximumYaw;
        public float MaximumPitch;
        public float Weight;

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            if (Weight <= 0f)
            {
                return;
            }

            Vector3 aimPosition = Aim.GetPosition(stream);
            Vector3 targetDirection = TargetPosition - aimPosition;
            if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            Quaternion rootRotation = Root.GetRotation(stream);
            Vector3 clampedDirection = XAnimationAimIKUtility.ClampDirection(rootRotation, targetDirection, MaximumYaw, MaximumPitch);
            Vector3 currentDirection = Aim.GetRotation(stream) * AimAxis;
            Quaternion aimOffset = Quaternion.FromToRotation(currentDirection, clampedDirection);

            for (int i = 0; i < Bones.Length; i++)
            {
                TransformStreamHandle bone = Bones[i];
                Quaternion weightedOffset = Quaternion.SlerpUnclamped(Quaternion.identity, aimOffset, BoneWeights[i] * Weight);
                bone.SetRotation(stream, weightedOffset * bone.GetRotation(stream));
            }
        }
    }

    [RequireComponent(typeof(XAnimationActor))]
    [DisallowMultipleComponent]
    [AddComponentMenu("XAnimation/Aim IK")]
    public sealed class XAnimationAimIK : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("按根骨骼到末端骨骼顺序配置参与瞄准的骨骼链。")]
        private Transform[] m_Bones = Array.Empty<Transform>();

        [SerializeField]
        [Tooltip("与骨骼链一一对应的旋转分配权重，运行时会归一化。")]
        private float[] m_BoneWeights = Array.Empty<float>();

        [SerializeField]
        [Tooltip("用于计算当前武器瞄准方向的参考节点。")]
        private Transform m_AimTransform;

        [SerializeField]
        [Tooltip("瞄准参考节点局部空间中代表武器发射方向的轴。")]
        private Vector3 m_AimAxis = Vector3.forward;

        [SerializeField]
        [Range(0f, 180f)]
        [Tooltip("相对角色正面的最大水平瞄准角度。")]
        private float m_MaximumYaw = 65f;

        [SerializeField]
        [Range(0f, 90f)]
        [Tooltip("相对角色水平面的最大俯仰角度。")]
        private float m_MaximumPitch = 45f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Aim IK 对最终动画姿势的整体影响权重。")]
        private float m_Weight = 1f;

        [SerializeField]
        [Tooltip("编辑态 Scene 预览目标相对角色根节点的位置。")]
        private Vector3 m_PreviewTargetLocalPosition = new(0f, 0f, 5f);

        private XAnimationActor m_Actor;
        private XAnimationOutputJobHandle<XAnimationAimIKJob> m_JobHandle;
        private NativeArray<TransformStreamHandle> m_BoneHandles;
        private NativeArray<float> m_NormalizedBoneWeights;
        private Transform m_Target;
        private Vector3 m_TargetOffset;
        private Vector3 m_TargetPosition;
        private bool m_HasAimTarget;

        internal Transform[] Bones => m_Bones;
        internal Transform AimTransform => m_AimTransform;
        internal Vector3 AimAxis => m_AimAxis;
        internal float MaximumYaw => m_MaximumYaw;
        internal float MaximumPitch => m_MaximumPitch;
        internal float Weight => m_Weight;
        internal Vector3 PreviewTargetWorldPosition
        {
            get => transform.TransformPoint(m_PreviewTargetLocalPosition);
            set => m_PreviewTargetLocalPosition = transform.InverseTransformPoint(value);
        }

        private void Start()
        {
            m_Actor = GetComponent<XAnimationActor>();
            if (!m_Actor.IsInitialized)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} requires an initialized {nameof(XAnimationActor)}.");
            }

            Animator animator = m_Actor.Animator;
            ValidateConfiguration(animator);
            CreateOutputJob(animator);
            UpdateJobData();
        }

        private void Update()
        {
            if (m_Target == null)
            {
                return;
            }

            m_TargetPosition = m_Target.TransformPoint(m_TargetOffset);
            UpdateJobData();
        }

        private void OnEnable()
        {
            UpdateJobData();
        }

        private void OnDisable()
        {
            UpdateJobData();
        }

        private void OnDestroy()
        {
            m_JobHandle?.Dispose();
            if (m_BoneHandles.IsCreated)
            {
                m_BoneHandles.Dispose();
            }

            if (m_NormalizedBoneWeights.IsCreated)
            {
                m_NormalizedBoneWeights.Dispose();
            }
        }

        public void AimAt(Vector3 worldPosition)
        {
            m_Target = null;
            m_TargetOffset = Vector3.zero;
            m_TargetPosition = worldPosition;
            m_HasAimTarget = true;
            UpdateJobData();
        }

        public void AimAt(Transform target, Vector3 offset)
        {
            m_Target = target != null ? target : throw new ArgumentNullException(nameof(target));
            m_TargetOffset = offset;
            m_TargetPosition = target.TransformPoint(offset);
            m_HasAimTarget = true;
            UpdateJobData();
        }

        public void ClearAim()
        {
            m_Target = null;
            m_TargetOffset = Vector3.zero;
            m_HasAimTarget = false;
            UpdateJobData();
        }

        internal void ValidateConfiguration(Animator animator)
        {
            if (m_Bones == null || m_Bones.Length == 0)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} requires at least one bone.");
            }

            if (m_BoneWeights == null || m_BoneWeights.Length != m_Bones.Length)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Bone Weights must match the Bones array length.");
            }

            Transform animatorRoot = animator.transform;
            float totalWeight = 0f;
            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (m_Bones[i] == null || !IsInHierarchy(m_Bones[i], animatorRoot))
                {
                    throw new XAnimationException($"{nameof(XAnimationAimIK)} bone at index {i} must belong to the Actor Animator hierarchy.");
                }

                if (m_BoneWeights[i] < 0f)
                {
                    throw new XAnimationException($"{nameof(XAnimationAimIK)} bone weight at index {i} must be non-negative.");
                }

                totalWeight += m_BoneWeights[i];
            }

            if (totalWeight <= 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} requires a positive total bone weight.");
            }

            if (m_AimTransform == null || !IsInHierarchy(m_AimTransform, animatorRoot))
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Aim Transform must belong to the Actor Animator hierarchy.");
            }

            if (m_AimAxis.sqrMagnitude <= Mathf.Epsilon)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Aim Axis must be non-zero.");
            }

            if (m_MaximumYaw < 0f || m_MaximumYaw > 180f)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Maximum Yaw must be between 0 and 180 degrees.");
            }

            if (m_MaximumPitch < 0f || m_MaximumPitch > 90f)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Maximum Pitch must be between 0 and 90 degrees.");
            }

            if (m_Weight < 0f || m_Weight > 1f)
            {
                throw new XAnimationException($"{nameof(XAnimationAimIK)} Weight must be between 0 and 1.");
            }
        }

        internal float[] CreateNormalizedBoneWeights()
        {
            float totalWeight = 0f;
            for (int i = 0; i < m_BoneWeights.Length; i++)
            {
                totalWeight += m_BoneWeights[i];
            }

            float[] normalizedWeights = new float[m_BoneWeights.Length];
            for (int i = 0; i < m_BoneWeights.Length; i++)
            {
                normalizedWeights[i] = m_BoneWeights[i] / totalWeight;
            }

            return normalizedWeights;
        }

        private void CreateOutputJob(Animator animator)
        {
            m_BoneHandles = new NativeArray<TransformStreamHandle>(m_Bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_NormalizedBoneWeights = new NativeArray<float>(m_Bones.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            float[] normalizedWeights = CreateNormalizedBoneWeights();
            for (int i = 0; i < m_Bones.Length; i++)
            {
                m_BoneHandles[i] = animator.BindStreamTransform(m_Bones[i]);
                m_NormalizedBoneWeights[i] = normalizedWeights[i];
            }

            XAnimationAimIKJob job = new()
            {
                Root = animator.BindStreamTransform(animator.transform),
                Aim = animator.BindStreamTransform(m_AimTransform),
                Bones = m_BoneHandles,
                BoneWeights = m_NormalizedBoneWeights,
                AimAxis = m_AimAxis.normalized,
                TargetPosition = m_TargetPosition,
                MaximumYaw = m_MaximumYaw,
                MaximumPitch = m_MaximumPitch,
                Weight = 0f,
            };
            m_JobHandle = m_Actor.InsertOutputJob(job, nameof(XAnimationAimIK));
        }

        private void UpdateJobData()
        {
            if (m_JobHandle == null)
            {
                return;
            }

            XAnimationAimIKJob job = m_JobHandle.GetJobData();
            job.TargetPosition = m_TargetPosition;
            job.Weight = isActiveAndEnabled && m_HasAimTarget ? m_Weight : 0f;
            m_JobHandle.SetJobData(job);
        }

        private static bool IsInHierarchy(Transform child, Transform root)
        {
            for (Transform current = child; current != null; current = current.parent)
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
