using Unity.Collections;
using Unity.Entities;

namespace Latios.Calligraphics
{
    internal struct TextColorGradientArray
    {
        private NativeArray<TextColorGradient> textColorGradients;
        public readonly int Length => textColorGradients.Length;
        public readonly TextColorGradient this[int index] => textColorGradients[index];

        public void Initialize(Entity textColorGradientEntity, BufferLookup<TextColorGradient> textColorGradientLookup)
        {
            if (textColorGradientLookup.TryGetBuffer(textColorGradientEntity, out var buffer))
                textColorGradients = buffer.AsNativeArray();
            else
                textColorGradients = default;
        }
    }
}

