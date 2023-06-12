using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// An aspect for working with dynamic meshes that abstracts away the rotation mechanisms
    /// </summary>
    public readonly partial struct DynamicMeshAspect : IAspect
    {
        readonly RefRW<DynamicMeshState>          m_state;
        readonly DynamicBuffer<DynamicMeshVertex> m_vertices;

        /// <summary>
        /// The number of vertices in this dynamic mesh
        /// </summary>
        public int vertexCount => m_vertices.Length / 3;

        /// <summary>
        /// If true, the blend shapes have been acquired with write access already this frame.
        /// </summary>
        public bool isDirty => (m_state.ValueRO.state & DynamicMeshState.Flags.IsDirty) == DynamicMeshState.Flags.IsDirty;

        /// <summary>
        /// Provides the writable array of vertices for the current frame and sets isDirty to true.
        /// If isDirty is false prior to access, the existing values are undefined.
        /// </summary>
        public NativeArray<DynamicMeshVertex> verticesRW
        {
            get
            {
                ref var state   = ref m_state.ValueRW;
                int     offset  = DynamicMeshState.CurrentFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                state.state    |= DynamicMeshState.Flags.IsDirty;
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount);
            }
        }

        /// <summary>
        /// Provides the readonly array of vertices for the current frame.
        /// If isDirty is false, the values are undefined.
        /// </summary>
        public NativeArray<DynamicMeshVertex>.ReadOnly verticesRO
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = DynamicMeshState.CurrentFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount).AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the readonly array of blend vertices for the previous frame.
        /// </summary>
        public NativeArray<DynamicMeshVertex>.ReadOnly previousVertices
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = DynamicMeshState.PreviousFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount).AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the readonly array of vertices from two frames ago.
        /// </summary>
        public NativeArray<DynamicMeshVertex>.ReadOnly twoAgoVertices
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = DynamicMeshState.TwoAgoFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount).AsReadOnly();
            }
        }

        public static ComponentTypeSet RequiredComponentTypeSet => new ComponentTypeSet(ComponentType.ReadWrite<DynamicMeshState>(), ComponentType.ReadWrite<DynamicMeshVertex>());
    }
}

