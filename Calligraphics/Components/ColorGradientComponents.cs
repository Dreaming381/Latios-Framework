using Unity.Entities;
using UnityEngine;

namespace Latios.Calligraphics
{
    /// <summary>
    /// Definition of color gradients, stored on the WorldBlackboardEntity
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct TextColorGradient : IBufferElementData
    {
        public int     nameHash;
        public Color32 topLeft;
        public Color32 topRight;
        public Color32 bottomLeft;
        public Color32 bottomRight;
    }
}

