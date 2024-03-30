using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal struct PointDistanceResultInternal
    {
        public float3 hitpoint;
        public float  distance;  // Negative if inside the collider
        public float3 normal;
        public ushort featureCode;
    }

    public struct ColliderDistanceResultInternal
    {
        public float3   hitpointA;
        public float3   hitpointB;
        public float3   normalA;
        public float3   normalB;
        public float    distance;
        internal ushort featureCodeA;
        internal ushort featureCodeB;
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
        public static ColliderDistanceResult BinAResultToWorld(in ColliderDistanceResultInternal bInAResult, in RigidTransform aTransform)
        {
            return new ColliderDistanceResult
            {
                hitpointA         = math.transform(aTransform, bInAResult.hitpointA),
                hitpointB         = math.transform(aTransform, bInAResult.hitpointB),
                normalA           = math.rotate(aTransform, bInAResult.normalA),
                normalB           = math.rotate(aTransform, bInAResult.normalB),
                distance          = bInAResult.distance,
                subColliderIndexA = 0,
                subColliderIndexB = 0,
                featureCodeA      = bInAResult.featureCodeA,
                featureCodeB      = bInAResult.featureCodeB
            };
        }

        public static int GetSubcolliders(in Collider collider)
        {
            switch(collider.type)
            {
                case ColliderType.TriMesh: return collider.m_triMesh().triMeshColliderBlob.Value.triangles.Length;
                case ColliderType.Compound: return collider.m_compound().compoundColliderBlob.Value.blobColliders.Length;
                default: return 1;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckMprResolved(bool somethingWentWrong)
        {
            if (somethingWentWrong)
                UnityEngine.Debug.LogWarning("MPR failed to resolve within the allotted number of iterations. If you see this, please report a bug.");
        }
    }
}

