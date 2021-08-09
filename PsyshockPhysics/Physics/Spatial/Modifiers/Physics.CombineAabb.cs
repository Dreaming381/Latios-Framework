using Latios;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static Aabb CombineAabb(float3 point, Aabb aabb)
        {
            return new Aabb(math.min(point, aabb.min), math.max(point, aabb.max));
        }

        public static Aabb CombineAabb(Aabb a, Aabb b)
        {
            return new Aabb(math.min(a.min, b.min), math.max(a.max, b.max));
        }
    }
}

