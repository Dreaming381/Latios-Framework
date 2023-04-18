using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    public readonly partial struct BlendShapesAspect : IAspect
    {
        readonly RefRW<BlendShapeState>          m_state;
        readonly DynamicBuffer<BlendShapeWeight> m_weights;

        public int weightCount => m_weights.Length / 3;

        public NativeArray<float> weightsRW
        {
            get
            {
                ref var state   = ref m_state.ValueRW;
                int     offset  = BlendShapeState.CurrentFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                state.state    |= BlendShapeState.Flags.IsDirty;
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>();
            }
        }

        public NativeArray<float>.ReadOnly weightsRO
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = BlendShapeState.CurrentFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>().AsReadOnly();
            }
        }

        public NativeArray<float>.ReadOnly previousWeights
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = BlendShapeState.PreviousFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>().AsReadOnly();
            }
        }

        public NativeArray<float>.ReadOnly twoAgoWeights
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = BlendShapeState.TwoAgoFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>().AsReadOnly();
            }
        }
    }
}

