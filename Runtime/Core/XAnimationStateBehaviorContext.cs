using UnityEngine;

namespace XAnimationEngine
{
    public readonly struct XAnimationStateBehaviorContext
    {
        public XAnimationStateBehaviorContext(
            Animator animator,
            string channelName,
            string stateKey,
            string clipKey,
            int playbackId,
            float normalizedTime,
            float totalNormalizedTime,
            float speed,
            float deltaTime,
            XAnimationStateExitReason? exitReason)
        {
            Animator = animator;
            ChannelName = channelName ?? string.Empty;
            StateKey = stateKey ?? string.Empty;
            ClipKey = clipKey ?? string.Empty;
            PlaybackId = playbackId;
            NormalizedTime = normalizedTime;
            TotalNormalizedTime = totalNormalizedTime;
            Speed = speed;
            DeltaTime = deltaTime;
            ExitReason = exitReason;
        }

        public Animator Animator { get; }
        public string ChannelName { get; }
        public string StateKey { get; }
        public string ClipKey { get; }
        public int PlaybackId { get; }
        public float NormalizedTime { get; }
        public float TotalNormalizedTime { get; }
        public float Speed { get; }
        public float DeltaTime { get; }
        public XAnimationStateExitReason? ExitReason { get; }
    }
}
