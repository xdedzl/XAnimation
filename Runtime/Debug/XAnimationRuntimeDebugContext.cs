using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal readonly struct XAnimationRuntimeDebugContext
    {
        public XAnimationRuntimeDebugContext(bool isInitialized, PlayableGraph graph, AnimationPlayableOutput output, AnimationLayerMixerPlayable layerMixer, bool useDirectChannelOutput, Animator animator, IReadOnlyList<XAnimationChannel> channels, IReadOnlyList<XAnimationOutputJobNode> outputJobs, float globalSpeed)
        {
            IsInitialized = isInitialized;
            Graph = graph;
            Output = output;
            LayerMixer = layerMixer;
            UseDirectChannelOutput = useDirectChannelOutput;
            Animator = animator;
            Channels = channels;
            OutputJobs = outputJobs;
            GlobalSpeed = globalSpeed;
        }

        public bool IsInitialized { get; }
        public PlayableGraph Graph { get; }
        public AnimationPlayableOutput Output { get; }
        public AnimationLayerMixerPlayable LayerMixer { get; }
        public bool UseDirectChannelOutput { get; }
        public Animator Animator { get; }
        public IReadOnlyList<XAnimationChannel> Channels { get; }
        public IReadOnlyList<XAnimationOutputJobNode> OutputJobs { get; }
        public float GlobalSpeed { get; }
        public string AnimatorName => Animator != null ? Animator.name : string.Empty;
    }
}
