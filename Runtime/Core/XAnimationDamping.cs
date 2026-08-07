using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;

namespace XAnimationEngine
{
    internal struct XAnimationDampingJob : IAnimationJob
    {
        public TransformStreamHandle RootHandle;
        public NativeArray<TransformStreamHandle> JointHandles;
        public NativeArray<Vector3> LocalPositions;
        public NativeArray<Quaternion> LocalRotations;
        public NativeArray<Vector3> Positions;
        public NativeArray<Vector3> Velocities;
        public float DampingTime;

        public void ProcessRootMotion(AnimationStream stream)
        {
        }

        public void ProcessAnimation(AnimationStream stream)
        {
            ComputeDampedPositions(stream);
            ComputeJointLocalRotations(stream);
        }

        private void ComputeDampedPositions(AnimationStream stream)
        {
            Vector3 rootPosition = RootHandle.GetPosition(stream);
            Quaternion rootRotation = RootHandle.GetRotation(stream);
            Vector3 parentPosition = rootPosition + rootRotation * LocalPositions[0];
            Quaternion parentRotation = rootRotation * LocalRotations[0];
            Positions[0] = parentPosition;

            for (int i = 1; i < JointHandles.Length; i++)
            {
                Vector3 targetPosition = parentPosition + parentRotation * LocalPositions[i];
                Vector3 velocity = Velocities[i];
                Vector3 newPosition = Vector3.SmoothDamp(Positions[i], targetPosition, ref velocity, DampingTime, Mathf.Infinity, stream.deltaTime);
                newPosition = parentPosition + (newPosition - parentPosition).normalized * LocalPositions[i].magnitude;
                Velocities[i] = velocity;
                Positions[i] = newPosition;
                parentPosition = newPosition;
                parentRotation *= LocalRotations[i];
            }
        }

        private void ComputeJointLocalRotations(AnimationStream stream)
        {
            Quaternion parentRotation = RootHandle.GetRotation(stream);
            for (int i = 0; i < JointHandles.Length - 1; i++)
            {
                Quaternion rotation = parentRotation * LocalRotations[i];
                Vector3 direction = (rotation * LocalPositions[i + 1]).normalized;
                Vector3 newDirection = (Positions[i + 1] - Positions[i]).normalized;
                rotation = Quaternion.FromToRotation(direction, newDirection) * rotation;
                JointHandles[i].SetLocalRotation(stream, Quaternion.Inverse(parentRotation) * rotation);
                parentRotation = rotation;
            }
        }
    }

    [RequireComponent(typeof(XAnimationActor))]
    [AddComponentMenu("XAnimation/Damping")]
    public sealed class XAnimationDamping : MonoBehaviour
    {
        [SerializeField] private Transform m_EndBone;
        [SerializeField] private int m_BoneCount = 3;
        [SerializeField] private float m_DampingTime = 0.15f;

        private XAnimationOutputJobHandle<XAnimationDampingJob> m_JobHandle;
        private NativeArray<TransformStreamHandle> m_JointHandles;
        private NativeArray<Vector3> m_LocalPositions;
        private NativeArray<Quaternion> m_LocalRotations;
        private NativeArray<Vector3> m_Positions;
        private NativeArray<Vector3> m_Velocities;

        internal Transform EndBone => m_EndBone;
        internal int BoneCount => m_BoneCount;
        internal float DampingTime => m_DampingTime;

        private void Start()
        {
            XAnimationActor actor = GetComponent<XAnimationActor>();
            Transform[] bones = CollectBones(actor);
            Animator animator = actor.Animator;

            m_JointHandles = new NativeArray<TransformStreamHandle>(m_BoneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_LocalPositions = new NativeArray<Vector3>(m_BoneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_LocalRotations = new NativeArray<Quaternion>(m_BoneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Positions = new NativeArray<Vector3>(m_BoneCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Velocities = new NativeArray<Vector3>(m_BoneCount, Allocator.Persistent);

            for (int i = 0; i < bones.Length; i++)
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
                DampingTime = m_DampingTime,
            };
            m_JobHandle = actor.InsertOutputJob(job, $"{nameof(XAnimationDamping)}:{m_EndBone.name}");
        }

        private void OnDestroy()
        {
            m_JobHandle?.Dispose();
            if (m_JointHandles.IsCreated)
            {
                m_JointHandles.Dispose();
            }

            if (m_LocalPositions.IsCreated)
            {
                m_LocalPositions.Dispose();
            }

            if (m_LocalRotations.IsCreated)
            {
                m_LocalRotations.Dispose();
            }

            if (m_Positions.IsCreated)
            {
                m_Positions.Dispose();
            }

            if (m_Velocities.IsCreated)
            {
                m_Velocities.Dispose();
            }
        }

        private Transform[] CollectBones(XAnimationActor actor)
        {
            if (!actor.IsInitialized)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} requires an initialized {nameof(XAnimationActor)}.");
            }

            if (m_EndBone == null)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} requires an End Bone.");
            }

            if (m_BoneCount < 2)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} Bone Count must be at least two.");
            }

            if (m_DampingTime <= 0f)
            {
                throw new XAnimationException($"{nameof(XAnimationDamping)} Damping Time must be greater than zero.");
            }

            Transform animatorRoot = actor.Animator.transform;
            Transform[] bones = new Transform[m_BoneCount];
            Transform bone = m_EndBone;
            for (int i = m_BoneCount - 1; i >= 0; i--)
            {
                if (bone == null || bone == animatorRoot)
                {
                    throw new XAnimationException($"{nameof(XAnimationDamping)} parent chain does not contain {m_BoneCount} bones below the Animator root.");
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
    }
}
