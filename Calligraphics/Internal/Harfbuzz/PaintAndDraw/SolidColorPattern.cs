using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct SolidColor : IPattern
    {
        ColorBGRA m_colorBGRA;
        public SolidColor(ColorBGRA colorBGRA)
        {
            m_colorBGRA = colorBGRA;
        }
        /// <summary>
        /// For a given pixel within the rendered glyph, this method calculates the UV coordinates that
        /// a texture of the color gradient would have. 'Solid fill' has same color for for every UV, so can do shortcut here.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColorBGRA GetColor(float2 bitmapCoordinate)
        {
            return m_colorBGRA;
        }
    }
}

