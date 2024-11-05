using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    // GameObjectEntity entities tend to be very small in size. Therefore, we can splurge a little on the DynamicBuffer.
    // This consumes 64 bytes.
    [InternalBufferCapacity(4)]
    internal struct GraphicsEventTunnelDestination : IBufferElementData, IEquatable<GraphicsEventTunnelDestination>
    {
        public UnityObjectRef<GraphicsEventTunnelBase>     tunnel;
        public UnityObjectRef<GraphicsEventBufferReceptor> requestor;
        public int                                         eventTypeIndex;

        public bool Equals(GraphicsEventTunnelDestination other)
        {
            return tunnel.Equals(other.tunnel) && requestor.Equals(other.requestor) && eventTypeIndex.Equals(other.eventTypeIndex);
        }

        public override int GetHashCode()
        {
            return new int3(tunnel.GetHashCode(), requestor.GetHashCode(), eventTypeIndex).GetHashCode();
        }
    }

    // Consumes 32 bytes
    [InternalBufferCapacity(2)]
    internal struct GraphicsGlobalBufferDestination : IBufferElementData, IEquatable<GraphicsGlobalBufferDestination>
    {
        public UnityObjectRef<GraphicsGlobalBufferReceptor> requestor;
        public int                                          shaderPropertyId;

        public bool Equals(GraphicsGlobalBufferDestination other )
        {
            return requestor.Equals(other.requestor) && shaderPropertyId.Equals(other.shaderPropertyId);
        }

        public override int GetHashCode()
        {
            return new int2(requestor.GetHashCode(), shaderPropertyId).GetHashCode();
        }
    }
}

