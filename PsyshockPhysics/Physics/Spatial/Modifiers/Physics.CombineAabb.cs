using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Creates a new AABB by computing an AABB that encapsulates both inputs
        /// </summary>
        /// <param name="point">A point to encapsulate</param>
        /// <param name="aabb">An AABB to encapsulate</param>
        /// <returns>A new computed AABB</returns>
        public static Aabb CombineAabb(float3 point, Aabb aabb)
        {
            return new Aabb(math.min(point, aabb.min), math.max(point, aabb.max));
        }

        /// <summary>
        /// Creates a new AABB by computing an AABB that encapsulates both inputs
        /// </summary>
        /// <param name="a">An AABB to encapsulate</param>
        /// <param name="b">Another AABB to encapsulate</param>
        /// <returns></returns>
        public static Aabb CombineAabb(Aabb a, Aabb b)
        {
            return new Aabb(math.min(a.min, b.min), math.max(a.max, b.max));
        }
    }
}

