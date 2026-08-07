using System;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationOutputJobNode
    {
        internal XAnimationOutputJobNode(XAnimationRuntime owner, string name, Type jobType, int sequence, AnimationScriptPlayable playable)
        {
            Owner = owner;
            Name = name;
            JobType = jobType;
            Sequence = sequence;
            Playable = playable;
        }

        internal XAnimationRuntime Owner { get; private set; }
        internal string Name { get; }
        internal Type JobType { get; }
        internal int Sequence { get; }
        internal AnimationScriptPlayable Playable { get; private set; }
        internal bool IsValid => Owner != null && Playable.IsValid();

        internal void Invalidate()
        {
            Owner = null;
            Playable = default;
        }
    }

    public sealed class XAnimationOutputJobHandle<TJob> : IDisposable where TJob : struct, IAnimationJob
    {
        private readonly XAnimationOutputJobNode m_Node;

        internal XAnimationOutputJobHandle(XAnimationOutputJobNode node)
        {
            m_Node = node;
        }

        public string Name => m_Node.Name;
        public bool IsValid => m_Node.IsValid;

        public TJob GetJobData()
        {
            ThrowIfInvalid();
            return m_Node.Playable.GetJobData<TJob>();
        }

        public void SetJobData(TJob jobData)
        {
            ThrowIfInvalid();
            m_Node.Playable.SetJobData(jobData);
        }

        public void Dispose()
        {
            if (!IsValid)
            {
                return;
            }

            m_Node.Owner.RemoveOutputJob(m_Node);
        }

        private void ThrowIfInvalid()
        {
            if (!IsValid)
            {
                throw new XAnimationException($"Output job handle '{Name}' is no longer valid for the current PlayableGraph.");
            }
        }
    }
}
