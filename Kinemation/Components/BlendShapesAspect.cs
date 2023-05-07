using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// An aspect for working with blend shapes that abstracts away the rotation mechanisms.
    /// </summary>
    public readonly partial struct BlendShapesAspect : IAspect
    {
        readonly RefRW<BlendShapeState>          m_state;
        readonly DynamicBuffer<BlendShapeWeight> m_weights;

        /// <summary>
        /// The number of blend shape weight parameters for this mesh
        /// </summary>
        public int weightCount => m_weights.Length / 3;

        /// <summary>
        /// If true, the blend shapes have been acquired with write access already this frame.
        /// </summary>
        public bool isDirty => (m_state.ValueRO.state & BlendShapeState.Flags.IsDirty) == BlendShapeState.Flags.IsDirty;

        /// <summary>
        /// Provides the writable array of blend shape weights for the current frame and sets isDirty to true.
        /// If isDirty is false prior to access, the existing values are undefined.
        /// </summary>
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

        /// <summary>
        /// Provides the readonly array of blend shape weights for the current frame.
        /// If isDirty is false, the values are undefined.
        /// </summary>
        public NativeArray<float>.ReadOnly weightsRO
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = BlendShapeState.CurrentFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>().AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the readonly array of blend shape weights for the previous frame.
        /// </summary>
        public NativeArray<float>.ReadOnly previousWeights
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = BlendShapeState.PreviousFromMask[(int)(state.state & BlendShapeState.Flags.RotationMask)];
                return m_weights.AsNativeArray().GetSubArray(weightCount * offset, weightCount).Reinterpret<float>().AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the readonly array of blend shape weights from two frames ago.
        /// </summary>
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

