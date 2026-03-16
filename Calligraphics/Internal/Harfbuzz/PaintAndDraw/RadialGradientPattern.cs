using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct RadialGradient : IPattern
    {
        //https://github.com/foo123/Gradient/blob/80e362bea2cb7deb3ab4c2125bf6fa49a726e4be/README.md
        NativeList<ColorStop> m_colorStops;
        PaintExtend paintExtend;
        float a;
        float b;
        float c;
        float x0;
        float y0;
        float r0;
        float x1;
        float y1;
        float r1;
        float2x3 inverseTransform;
        [MarshalAs(UnmanagedType.U1)]
        public bool isValid;
        public RadialGradient(float x0, float y0, float r0, float x1, float y1, float r1, PaintExtend paintExtend, float2x3 transform)
        {
            if (Hint.Unlikely((x0 == x1 && y0 == y1) && (r0 == r1)))
                isValid = false; //points idential, gradient ill formed, draw nothing https://learn.microsoft.com/en-us/typography/opentype/spec/colr 
            
            
            // the object to which the gradient will be applied needs to be transformed
            // prior to rasterization. Furthermore, additional transformation can be applied just to the gradient (and not to the obejct)
            // so we need to apply the inverse gradient transfrom to the bitmap coordinates when calling GetColor().
            var success = PaintUtils.Inverse(transform, out inverseTransform);
            if (!success)
                Debug.Log($"Failed to create inverse transform");

            this.x0 = x0;
            this.y0 = y0;
            this.x1 = x1;
            this.y1 = y1;
            this.r0 = r0;
            this.r1 = r1;            

            a = r0 * r0 - 2 * r0 * r1 + r1 * r1 - x0 * x0 + 2 * x0 * x1 - x1 * x1 - y0 * y0 + 2 * y0 * y1 - y1 * y1;
            b = -2 * r0 * r0 + 2 * r0 * r1 + 2 * x0 * x0 - 2 * x0 * x1 + 2 * y0 * y0 - 2 * y0 * y1;
            c = -x0 * x0 - y0 * y0 + r0 * r0;            

            m_colorStops = default;
            this.paintExtend = paintExtend;
            isValid = true;
        }

        public void InitializeColorLine(ColorLine colorLine)
        {
            colorLine.GetColorStops(0, out m_colorStops);
        }

        /// <summary>
        /// For a given pixel within the rendered glyph, this method calculates 
        /// the UV coordinates that a texture of the color gradient would have. 
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ColorARGB GetColor(float2 bitmapCoordinates)
        {
            var designSpaceCoordinates = PaintUtils.mul(inverseTransform, bitmapCoordinates);
            var x = designSpaceCoordinates.x;
            var y = designSpaceCoordinates.y;
            float t;
            var s = PaintUtils.QuadraticRoots(
                a,
                b - 2 * x * x0 + 2 * x * x1 - 2 * y * y0 + 2 * y * y1,
                c - x * x + 2 * x * x0 - y * y + 2 * y * y0,
                out float2 roots, out bool tangent);

            float px, py, pr;
            px = x - x0; py = y - y0;
            pr = math.sqrt(px * px + py * py);
            if (s == 0)
                return new ColorARGB(255, 255, 255, 255); //outside of cone is not painted.
            else if (s == 2)
            {
                t = math.max(roots[0], roots[1]);
                if (t < 0 && pr > (r0 + r1))
                    return new ColorARGB(255, 255, 255, 255); //outside of cone is not painted.
            }
            else
            {
                t = roots[0];
            }

            PaintUtils.ApplyWrapMode(ref t, paintExtend);
            //Debug.Log($"{x} {y}: {t}");
            return PaintUtils.SampleGradient(m_colorStops, t);
        }
    }
}
