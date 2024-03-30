using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    [EffectOptions(EffectOptionFlags.RequireUpdateWhenCulled | EffectOptionFlags.RequireUpdateWhenInputFrameDisconnected)]
    internal struct VirtualOutputEffect : IEffect<VirtualOutputEffect, VirtualOutputParameters>
    {
        internal unsafe struct Ptr
        {
            public VirtualOutputEffect* ptr;
        }

        SampleFrame m_currentStackFrame;
        SampleFrame m_listenerPreviousStackFrame;

        Entity m_stackEntity;
        int    m_indexInStack;
        bool   m_isInListenerStack;

        public bool TryGetFrame(Entity stackEntity, int indexInStack, int currentFrame, out SampleFrame.ReadOnly frame)
        {
            if (m_stackEntity == stackEntity && m_indexInStack < indexInStack)
            {
                frame = m_currentStackFrame.readOnly;
                return true;
            }

            if (m_isInListenerStack)
            {
                if (m_currentStackFrame.frameIndex == currentFrame)
                {
                    frame = m_listenerPreviousStackFrame.readOnly;
                    return true;
                }
                else if (m_currentStackFrame.frameIndex == currentFrame - 1)
                {
                    frame = m_currentStackFrame.readOnly;
                    return true;
                }
            }

            frame = default;
            return false;
        }

        public void OnAwake(in EffectContext context, in VirtualOutputParameters parameters)
        {
        }

        public unsafe void OnUpdate(in EffectContext effectContext, in UpdateContext updateContext, in VirtualOutputParameters parameters, ref SampleFrame frame)
        {
            m_stackEntity       = updateContext.stackEntity;
            m_indexInStack      = updateContext.indexInStack;
            m_isInListenerStack = updateContext.stackType == StackType.Listener;

            if (!frame.connected || parameters.volume <= 0f)
            {
                m_currentStackFrame.connected           = false;
                m_listenerPreviousStackFrame.connected  = false;
                m_currentStackFrame.frameIndex          = effectContext.currentFrame;
                m_listenerPreviousStackFrame.frameIndex = effectContext.currentFrame - 1;
                return;
            }

            if (!m_currentStackFrame.left.IsCreated)
            {
                m_currentStackFrame            = effectContext.sampleFramePool->Acquire(effectContext.frameSize);
                m_currentStackFrame.frameIndex = effectContext.currentFrame;
                m_currentStackFrame.connected  = false;
            }

            if (m_isInListenerStack)
            {
                (m_currentStackFrame, m_listenerPreviousStackFrame) = (m_listenerPreviousStackFrame, m_currentStackFrame);

                if (!m_currentStackFrame.left.IsCreated)
                {
                    m_currentStackFrame            = effectContext.sampleFramePool->Acquire(effectContext.frameSize);
                    m_currentStackFrame.frameIndex = effectContext.currentFrame;
                    m_currentStackFrame.connected  = false;
                }
                m_currentStackFrame.frameIndex          = effectContext.currentFrame;
                m_listenerPreviousStackFrame.frameIndex = effectContext.currentFrame - 1;
            }

            if (parameters.volume != 1f)
            {
                var lin  = frame.left;
                var rin  = frame.right;
                var lout = m_currentStackFrame.left;
                var rout = m_currentStackFrame.right;
                for (int i = 0; i < frame.length; i++)
                {
                    lout[i] = lin[i] * parameters.volume;
                    rout[i] = rin[i] * parameters.volume;
                }
            }
            else
            {
                m_currentStackFrame.left.CopyFrom(frame.left);
                m_currentStackFrame.right.CopyFrom(frame.right);
            }
        }

        public unsafe void OnDestroy(in EffectContext context)
        {
            Reset(context.sampleFramePool);
        }

        public unsafe void Reset(SampleFramePool* sampleFramePool)
        {
            if (m_currentStackFrame.left.IsCreated)
                sampleFramePool->Release(m_currentStackFrame);
            if (m_listenerPreviousStackFrame.left.IsCreated)
                sampleFramePool->Release(m_listenerPreviousStackFrame);
        }
    }
}

