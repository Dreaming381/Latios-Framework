using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct SolidColor : IPattern
    {
        ColorARGB m_colorARGB;
        public SolidColor(ColorARGB colorARGB)
        {
            m_colorARGB = colorARGB;
        }
        /// <summary>
        /// For a given pixel within the rendered glyph, this method calculates the UV coordinates that 
        /// a texture of the color gradient would have. 'Solid fill' has same color for for every UV, so can do shortcut here.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColorARGB GetColor(float2 bitmapCoordinate)
        {
            return m_colorARGB;
        }
    }
}
