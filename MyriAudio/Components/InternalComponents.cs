using Unity.Audio;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Myri
{
    internal unsafe struct ListenerGraphState : ICleanupComponentData
    {
        public DSPNode                                 listenerMixNode;
        public DSPConnection                           masterOutputConnection;
        public int                                     masterPortIndex;
        public int                                     inletPortCount;
        public UnsafeList<DSPConnection>               ildConnections;
        public BlobAssetReference<ListenerProfileBlob> lastUsedProfile;
    }
}

