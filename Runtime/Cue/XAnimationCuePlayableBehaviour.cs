using UnityEngine.Playables;

namespace XAnimationEngine
{
    internal sealed class XAnimationCuePlayableBehaviour : PlayableBehaviour
    {
        private XAnimationCueRuntime m_CueRuntime;

        public void Bind(XAnimationCueRuntime cueRuntime)
        {
            m_CueRuntime = cueRuntime;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            m_CueRuntime?.CollectCuesFromPlayableGraph();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            m_CueRuntime = null;
        }
    }
}
