using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    public readonly partial struct DynamicMeshAspect : IAspect
    {
        readonly RefRW<DynamicMeshState>          m_state;
        readonly DynamicBuffer<DynamicMeshVertex> m_vertices;

        public int vertexCount => m_vertices.Length / 3;

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

        public NativeArray<DynamicMeshVertex>.ReadOnly verticesRO
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = DynamicMeshState.CurrentFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount).AsReadOnly();
            }
        }

        public NativeArray<DynamicMeshVertex>.ReadOnly previousVertices
        {
            get
            {
                var state  = m_state.ValueRO;
                int offset = DynamicMeshState.PreviousFromMask[(int)(state.state & DynamicMeshState.Flags.RotationMask)];
                return m_vertices.AsNativeArray().GetSubArray(vertexCount * offset, vertexCount).AsReadOnly();
            }
        }

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

