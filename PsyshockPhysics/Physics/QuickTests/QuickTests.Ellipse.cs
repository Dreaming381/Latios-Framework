using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class QuickTests
    {
        /// <summary>
        /// Tests if the point lies on the perimeter or inside the enclosed area of the ellipse.
        /// </summary>
        /// <param name="halfExtents">The distance from the ellipse center (0, 0) to the perimeter on each axis</param>
        /// <param name="testPoint">The point relative to the ellipse to test for being on or inside the ellipse</param>
        /// <returns>True if the point is on or inside the ellipse, false otherwise</returns>
        public static bool IsOnOrInsideEllipse(float2 halfExtents, float2 testPoint)
        {
            if (math.any(halfExtents <= math.EPSILON))
            {
                var absTestPoint = math.abs(testPoint);
                return absTestPoint.Equals(math.min(absTestPoint, halfExtents));
            }

            var testPointSq   = testPoint * testPoint;
            var halfExtentsSq = halfExtents * halfExtents;
            var result        = math.csum(testPointSq / halfExtentsSq);
            return result <= 1f;
        }

        /// <summary>
        /// Finds the closest point on the ellipse perimeter using a rapidly converging numerical method.
        /// This works reliably for test points outside the perimeter, and is semi-reliably for points inside.
        /// </summary>
        /// <param name="halfExtents">The distance from the ellipse center (0, 0) to the perimeter on each axis</param>
        /// <param name="testPoint">The point relative to the ellipse to find the closest perimeter point to</param>
        /// <returns>A point on the perimeter of the ellipse that is closest to the query point</returns>
        public static float2 ClosestPointOnEllipse(float2 halfExtents, float2 testPoint)
        {
            // This first check is custom.
            var isZeroExtent = halfExtents <= math.EPSILON;
            if (math.any(isZeroExtent))
            {
                //The ellipse has degenerated into a line segment.
                return math.select(math.min(halfExtents, testPoint), 0f, isZeroExtent);
            }

            // The following code is an optimized version of the algorithm found here: https://github.com/0xfaded/ellipse_demo
            // The associated blog post detailing the technique is described here: https://blog.chatfield.io/simple-method-for-distance-to-ellipse/
            // The code is licensed under the MIT license as follows:
            //
            // MIT License
            //
            // Copyright(c) 2017 Carl Chatfield
            //
            // Permission is hereby granted, free of charge, to any person obtaining a copy
            // of this software and associated documentation files(the "Software"), to deal
            // in the Software without restriction, including without limitation the rights
            // to use, copy, modify, merge, publish, distribute, sublicense, and/ or sell
            // copies of the Software, and to permit persons to whom the Software is
            // furnished to do so, subject to the following conditions:
            //
            //             The above copyright notice and this permission notice shall be included in all
            //             copies or substantial portions of the Software.
            //
            // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
            // IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
            // FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
            // AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
            // LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
            // OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
            // SOFTWARE.

            var    p = math.abs(testPoint);
            float2 t = 0.7071f;

            for (int i = 0; i < 3; i++)
            {
                var    xy            = halfExtents * t;
                var    extentsSq     = halfExtents * halfExtents;
                float2 extentsDiffSq = extentsSq.x - extentsSq.y;
                extentsDiffSq.y      = -extentsDiffSq.y;
                var evolute          = extentsDiffSq * t * t * t / halfExtents;
                var radiusVector     = xy - evolute;
                var targetVector     = p - evolute;

                var radiusSq  = math.lengthsq(radiusVector);
                var targetSq  = math.lengthsq(targetVector);
                t             = math.saturate((targetVector * math.sqrt(radiusSq / targetSq) + evolute) / halfExtents);
                t            /= math.length(t);
            }
            var result = math.abs(halfExtents * t);
            return math.select(result, -result, testPoint < 0f);
        }
    }
}

