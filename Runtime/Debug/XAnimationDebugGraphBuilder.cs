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
            outputNode.isConnected = context.Output.IsOutputValid();
            outputNode.isActive = outputNode.isConnected;
            outputNode.details = !string.IsNullOrEmpty(context.AnimatorName)
                ? $"Animator: {context.AnimatorName}"
                : "Animator: <null>";

            IReadOnlyList<XAnimationChannel> runtimeChannels = context.Channels ?? Array.Empty<XAnimationChannel>();
            XAnimationDebugChannelSnapshot[] channels = new XAnimationDebugChannelSnapshot[runtimeChannels.Count];
            if (context.UseDirectChannelOutput)
            {
                BuildDirectChannelOutput(builder, outputNode, runtimeChannels, channels, context.GlobalSpeed);
            }
            else
            {
                BuildLayerMixerOutput(context, builder, outputNode, runtimeChannels, channels);
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

        private static void BuildDirectChannelOutput(
            XAnimationDebugSnapshotBuilder builder,
            XAnimationDebugNodeSnapshot outputNode,
            IReadOnlyList<XAnimationChannel> runtimeChannels,
            XAnimationDebugChannelSnapshot[] channels,
            float globalSpeed)
        {
            XAnimationChannel channel = runtimeChannels.Count > 0 ? runtimeChannels[0] : null;
            float layerWeight = channel != null ? channel.ChannelWeight : 0f;
            if (channel != null)
            {
                channels[0] = channel.BuildDebugChannelSnapshot(layerWeight);
                outputNode.children = new[]
                {
                    channel.BuildDebugNode(builder, outputNode.id, 0, layerWeight, globalSpeed),
                };
                return;
            }

            outputNode.children = Array.Empty<XAnimationDebugNodeSnapshot>();
        }

        private static void BuildLayerMixerOutput(
            XAnimationRuntimeDebugContext context,
            XAnimationDebugSnapshotBuilder builder,
            XAnimationDebugNodeSnapshot outputNode,
            IReadOnlyList<XAnimationChannel> runtimeChannels,
            XAnimationDebugChannelSnapshot[] channels)
        {
            AnimationLayerMixerPlayable layerMixer = context.LayerMixer;
            bool layerMixerValid = layerMixer.IsValid();
            XAnimationDebugNodeSnapshot layerMixerNode = builder.CreateNode(outputNode.id, "Layer Mixer", "AnimationLayerMixerPlayable");
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
            outputNode.children = new[] { layerMixerNode };
        }
    }
}
