using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal struct PointDistanceResultInternal
    {
        public float3 hitpoint;
        public float  distance;  // Negative if inside the collider
        public float3 normal;
    }

    public struct ColliderDistanceResultInternal
    {
        public float3 hitpointA;
        public float3 hitpointB;
        public float3 normalA;
        public float3 normalB;
        public float  distance;
    }

    internal struct SupportPoint
    {
        public float3 pos;
        public uint   id;

        public int idA => (int)(id >> 16);
        public int idB => (int)(id & 0xffff);
    }

    internal static class InternalQueryTypeUtilities
    {
        public static ColliderDistanceResult BinAResultToWorld(in ColliderDistanceResultInternal BinAResult, in RigidTransform aTransform)
        {
            return new ColliderDistanceResult
            {
                hitpointA = math.transform(aTransform, BinAResult.hitpointA),
                hitpointB = math.transform(aTransform, BinAResult.hitpointB),
                normalA   = math.rotate(aTransform, BinAResult.normalA),
                normalB   = math.rotate(aTransform, BinAResult.normalB),
                distance  = BinAResult.distance
            };
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckMprResolved(bool somethingWentWrong)
        {
            if (somethingWentWrong)
                UnityEngine.Debug.LogWarning("MPR failed to resolve within the allotted number of iterations. If you see this, please report a bug.");
        }
    }
}

