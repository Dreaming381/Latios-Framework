
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz
{
    /// <summary>
    /// IPattern behave like textures: are sampled by a given vertex, convert that into a "UV", and return the color for that UV
    /// </summary>
    internal interface IPattern
    {
        /// <summary>
        /// For a given vertex (/object space pixel) of the rendered glyph, this method calculates the UV coordinates that 
        /// a texture of the color gradient would have. These gradients can be rotated/scaled etc by the provided AffineTransforms. 
        /// </summary>
        public ColorBGRA GetColor(float2 bitmapCoordinates);
    }
}
