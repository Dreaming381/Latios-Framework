using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Myri
{
    internal struct IldOutputConnection
    {
        public int           ildOutputPort;
        public int           nodeInputPort;
        public float         attenuation;
        public DSPNode       node;
        public DSPConnection connection;
    }

    internal unsafe struct ListenerGraphState : ISystemStateComponentData
    {
        public UnsafeList<DSPNode>                nodes;
        public UnsafeList<DSPConnection>          connections;
        public UnsafeList<IldOutputConnection>    ildConnections;
        public BlobAssetReference<IldProfileBlob> lastUsedProfile;
    }

    internal struct EntityOutputGraphState : ISystemStateComponentData
    {
        public DSPConnection connection;
        public int           portIndex;
    }

    /*internal unsafe struct MasterGraphState
       {
        public UnsafeList<int> portFreelist;
        public DSPGraph graph;
        public DSPNode mixdownNode;
        public DSPConnection driverConnection;
       }*/
}

