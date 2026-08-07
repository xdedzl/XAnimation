using System;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationDebugGraphBuilder
    {
        public XAnimationDebugGraphSnapshot Build(XAnimationRuntimeDebugContext context)
        {
            if (!context.IsInitialized)
            {
                return XAnimationDebugGraphSnapshot.Invalid("XAnimationDriver is not initialized.");
            }

            PlayableGraph graph = context.Graph;
            if (!graph.IsValid())
            {
                return XAnimationDebugGraphSnapshot.Invalid(
                    "PlayableGraph is invalid.",
                    string.Empty);
            }

            string graphName = GetGraphName(graph, context.AnimatorName);
            XAnimationDebugSnapshotBuilder builder = new();
            XAnimationDebugNodeSnapshot graphNode = builder.CreateNode(0, graphName, "PlayableGraph");
            graphNode.isConnected = true;
            graphNode.isActive = graph.IsPlaying();
            graphNode.details = $"Time Update Mode: {graph.GetTimeUpdateMode()}";

            XAnimationDebugNodeSnapshot outputNode = builder.CreateNode(graphNode.id, "XAnimationOutput", "AnimationPlayableOutput");
            outputNode.isConnected = IsOutputConnected(context);
            outputNode.isActive = outputNode.isConnected;
            outputNode.details = !string.IsNullOrEmpty(context.AnimatorName)
                ? $"Animator: {context.AnimatorName}"
                : "Animator: <null>";

            IReadOnlyList<XAnimationChannel> runtimeChannels = context.Channels ?? Array.Empty<XAnimationChannel>();
            XAnimationDebugChannelSnapshot[] channels = new XAnimationDebugChannelSnapshot[runtimeChannels.Count];
            XAnimationDebugNodeSnapshot outputSourceParent = BuildOutputJobs(context, builder, outputNode);
            if (context.UseDirectChannelOutput)
            {
                BuildDirectChannelOutput(builder, outputSourceParent, runtimeChannels, channels, context.GlobalSpeed);
            }
            else
            {
                BuildLayerMixerOutput(context, builder, outputSourceParent, runtimeChannels, channels);
            }

            graphNode.children = new[] { outputNode };

            return new XAnimationDebugGraphSnapshot
            {
                graphName = graphName,
                isValid = true,
                isPlaying = graph.IsPlaying(),
                isDisposed = false,
                animatorName = context.AnimatorName,
                message = string.Empty,
                channels = channels,
                rootNodes = new[] { graphNode },
            };
        }

        private static string GetGraphName(PlayableGraph graph, string animatorName)
        {
#if UNITY_EDITOR
            string graphName = graph.GetEditorName();
            if (!string.IsNullOrEmpty(graphName)) return graphName;
#endif
            return !string.IsNullOrEmpty(animatorName) ? $"XAnimationDriver_{animatorName}" : "PlayableGraph";
        }

        private static XAnimationDebugNodeSnapshot BuildOutputJobs(XAnimationRuntimeDebugContext context, XAnimationDebugSnapshotBuilder builder, XAnimationDebugNodeSnapshot outputNode)
        {
            IReadOnlyList<XAnimationOutputJobNode> outputJobs = context.OutputJobs ?? Array.Empty<XAnimationOutputJobNode>();
            XAnimationDebugNodeSnapshot parent = outputNode;
            for (int i = outputJobs.Count - 1; i >= 0; i--)
            {
                XAnimationOutputJobNode outputJob = outputJobs[i];
                AnimationScriptPlayable playable = outputJob.Playable;
                XAnimationDebugNodeSnapshot jobNode = builder.CreateNode(parent.id, outputJob.Name, "AnimationScriptPlayable");
                jobNode.jobType = outputJob.JobType.FullName ?? outputJob.JobType.Name;
                jobNode.outputJobSequence = outputJob.Sequence;
                jobNode.inputIndex = 0;
                jobNode.inputWeight = playable.IsValid() ? playable.GetInputWeight(0) : 0f;
                jobNode.effectiveWeight = jobNode.inputWeight;
                jobNode.isConnected = IsOutputJobConnected(context, outputJobs, i);
                jobNode.isActive = jobNode.isConnected && context.Graph.IsPlaying();
                jobNode.details = $"Playable: AnimationScriptPlayable\nJob: {jobNode.jobType}\nInsertion Sequence: #{jobNode.outputJobSequence}";
                parent.children = new[] { jobNode };
                parent = jobNode;
            }

            return parent;
        }

        private static bool IsOutputConnected(XAnimationRuntimeDebugContext context)
        {
            if (!context.Output.IsOutputValid())
            {
                return false;
            }

            IReadOnlyList<XAnimationOutputJobNode> outputJobs = context.OutputJobs ?? Array.Empty<XAnimationOutputJobNode>();
            Playable expectedSource = outputJobs.Count > 0 ? outputJobs[outputJobs.Count - 1].Playable : GetOutputBasePlayable(context);
            return context.Output.GetSourcePlayable().Equals(expectedSource);
        }

        private static bool IsOutputJobConnected(XAnimationRuntimeDebugContext context, IReadOnlyList<XAnimationOutputJobNode> outputJobs, int index)
        {
            AnimationScriptPlayable playable = outputJobs[index].Playable;
            if (!playable.IsValid())
            {
                return false;
            }

            Playable expectedInput = index > 0 ? outputJobs[index - 1].Playable : GetOutputBasePlayable(context);
            bool inputConnected = playable.GetInputCount() == 1 && playable.GetInput(0).Equals(expectedInput);
            if (index == outputJobs.Count - 1)
            {
                return inputConnected && context.Output.IsOutputValid() && context.Output.GetSourcePlayable().Equals((Playable)playable);
            }

            AnimationScriptPlayable downstream = outputJobs[index + 1].Playable;
            return inputConnected && downstream.IsValid() && downstream.GetInputCount() == 1 && downstream.GetInput(0).Equals((Playable)playable);
        }

        private static Playable GetOutputBasePlayable(XAnimationRuntimeDebugContext context)
        {
            return context.UseDirectChannelOutput ? context.Channels[0].Mixer : context.LayerMixer;
        }

        private static void BuildDirectChannelOutput(XAnimationDebugSnapshotBuilder builder, XAnimationDebugNodeSnapshot outputSourceParent, IReadOnlyList<XAnimationChannel> runtimeChannels, XAnimationDebugChannelSnapshot[] channels, float globalSpeed)
        {
            XAnimationChannel channel = runtimeChannels.Count > 0 ? runtimeChannels[0] : null;
            float layerWeight = channel != null ? channel.ChannelWeight : 0f;
            if (channel != null)
            {
                channels[0] = channel.BuildDebugChannelSnapshot(layerWeight);
                outputSourceParent.children = new[]
                {
                    channel.BuildDebugNode(builder, outputSourceParent.id, 0, layerWeight, globalSpeed),
                };
                return;
            }

            outputSourceParent.children = Array.Empty<XAnimationDebugNodeSnapshot>();
        }

        private static void BuildLayerMixerOutput(XAnimationRuntimeDebugContext context, XAnimationDebugSnapshotBuilder builder, XAnimationDebugNodeSnapshot outputSourceParent, IReadOnlyList<XAnimationChannel> runtimeChannels, XAnimationDebugChannelSnapshot[] channels)
        {
            AnimationLayerMixerPlayable layerMixer = context.LayerMixer;
            bool layerMixerValid = layerMixer.IsValid();
            XAnimationDebugNodeSnapshot layerMixerNode = builder.CreateNode(outputSourceParent.id, "Layer Mixer", "AnimationLayerMixerPlayable");
            layerMixerNode.isConnected = layerMixerValid;
            layerMixerNode.isActive = layerMixerValid;
            layerMixerNode.inputWeight = 1f;
            layerMixerNode.effectiveWeight = 1f;
            layerMixerNode.details = $"Input Count: {(layerMixerValid ? layerMixer.GetInputCount() : 0)}";

            XAnimationDebugNodeSnapshot[] channelNodes = new XAnimationDebugNodeSnapshot[runtimeChannels.Count];
            for (int i = 0; i < runtimeChannels.Count; i++)
            {
                XAnimationChannel channel = runtimeChannels[i];
                float layerWeight = layerMixerValid ? layerMixer.GetInputWeight(i) : 0f;
                channels[i] = channel.BuildDebugChannelSnapshot(layerWeight);
                channelNodes[i] = channel.BuildDebugNode(builder, layerMixerNode.id, i, layerWeight, context.GlobalSpeed);
            }

            layerMixerNode.children = channelNodes;
            outputSourceParent.children = new[] { layerMixerNode };
        }
    }
}
