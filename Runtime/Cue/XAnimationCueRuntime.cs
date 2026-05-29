using System;
using System.Collections.Generic;
using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationCueRuntime : IDisposable
    {
        private readonly XAnimationCueDispatcher m_Dispatcher = new();
        private readonly List<XAnimationCueEvent> m_PendingCueEvents = new();
        private IReadOnlyList<XAnimationChannel> m_Channels = Array.Empty<XAnimationChannel>();
        private ScriptPlayable<XAnimationCuePlayableBehaviour> m_CuePlayable;
        private ScriptPlayableOutput m_CueOutput;

        internal XAnimationCueRuntime()
        {
            m_Dispatcher.CueTriggered += RaiseCueTriggered;
        }

        internal event Action<XAnimationCueEvent> CueTriggered;

        internal void Register(IReadOnlyDictionary<string, List<XAnimationCompiledCue>> cuesByClipKey)
        {
            m_PendingCueEvents.Clear();
            m_Dispatcher.Register(cuesByClipKey);
        }

        internal void RegisterClipCues(string clipKey, IReadOnlyList<XAnimationCompiledCue> cues)
        {
            m_Dispatcher.RegisterClipCues(clipKey, cues);
        }

        internal void BindChannels(IReadOnlyList<XAnimationChannel> channels)
        {
            m_Channels = channels ?? Array.Empty<XAnimationChannel>();
        }

        internal void EnsurePlayable(PlayableGraph graph)
        {
            if (!graph.IsValid() || !m_Dispatcher.HasAnyCues)
            {
                return;
            }

            if (m_CuePlayable.IsValid() && m_CueOutput.IsOutputValid())
            {
                return;
            }

            m_CuePlayable = ScriptPlayable<XAnimationCuePlayableBehaviour>.Create(graph);
            XAnimationCuePlayableBehaviour behaviour = m_CuePlayable.GetBehaviour();
            behaviour.Bind(this);
            m_CueOutput = ScriptPlayableOutput.Create(graph, "XAnimationCueOutput");
            m_CueOutput.SetSourcePlayable(m_CuePlayable);
        }

        internal bool TryPlay(
            XAnimationChannel channel,
            Func<int, XAnimationPlaybackRuntimeOptions, XAnimationStatePlaybackInstance> playbackFactory,
            XAnimationTransitionRequest request,
            out XAnimationStatePlaybackInstance playback,
            out XAnimationTransitionRejectReason rejectReason)
        {
            if (channel == null)
            {
                throw new ArgumentNullException(nameof(channel));
            }

            return channel.TryPlay(playbackFactory, request, m_Dispatcher, out playback, out rejectReason);
        }

        internal void StopChannel(XAnimationChannel channel, float fadeOut)
        {
            channel?.Stop(fadeOut, m_Dispatcher);
        }

        internal void FinalizeChannelFrame(XAnimationChannel channel, bool dispatchCues)
        {
            channel?.FinalizeFrame(m_Dispatcher, dispatchCues);
        }

        internal void DisposeChannel(XAnimationChannel channel)
        {
            channel?.Dispose(m_Dispatcher);
        }

        internal void CollectCuesFromPlayableGraph()
        {
            if (!m_Dispatcher.HasAnyCues)
            {
                return;
            }

            for (int i = 0; i < m_Channels.Count; i++)
            {
                m_Channels[i].CollectCues(m_Dispatcher, QueueCueEvent);
            }
        }

        internal void Flush()
        {
            if (m_PendingCueEvents.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_PendingCueEvents.Count; i++)
            {
                m_Dispatcher.Raise(m_PendingCueEvents[i]);
            }

            m_PendingCueEvents.Clear();
        }

        internal void Clear()
        {
            m_Dispatcher.Clear();
            m_PendingCueEvents.Clear();
            m_CuePlayable = default;
            m_CueOutput = default;
            m_Channels = Array.Empty<XAnimationChannel>();
        }

        public void Dispose()
        {
            Clear();
        }

        private void QueueCueEvent(XAnimationCueEvent cueEvent)
        {
            if (cueEvent != null)
            {
                m_PendingCueEvents.Add(cueEvent);
            }
        }

        private void RaiseCueTriggered(XAnimationCueEvent cueEvent)
        {
            CueTriggered?.Invoke(cueEvent);
        }
    }
}
