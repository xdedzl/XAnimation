using System;
using Newtonsoft.Json;

namespace XAnimationEngine
{
    [Serializable]
    [JsonConverter(typeof(XAnimationStateBehaviorJsonConverter))]
    public abstract class XAnimationStateBehavior
    {
        public virtual void OnStateEnter(in XAnimationStateBehaviorContext context)
        {
        }

        public virtual void OnStateUpdate(in XAnimationStateBehaviorContext context)
        {
        }

        public virtual void OnStateExit(in XAnimationStateBehaviorContext context)
        {
        }

        internal XAnimationStateBehavior Clone()
        {
            return XAnimationStateBehaviorJsonConverter.CloneBehavior(this);
        }
    }
}
