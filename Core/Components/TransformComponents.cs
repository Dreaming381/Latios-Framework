using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    #region Extreme Hierarchy
    [Serializable]
    internal struct Depth : IComponentData
    {
        public byte depth;
    }

    [Serializable]
    internal struct ChunkDepthMask : IComponentData
    {
        public BitField32 chunkDepthMask;
    }
    #endregion
}

