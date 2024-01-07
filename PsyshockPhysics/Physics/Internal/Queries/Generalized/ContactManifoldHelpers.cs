using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ContactManifoldHelpers
    {
        public static UnitySim.ContactsBetweenResult GetSingleContactManifold(in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result = default;
            result.contactNormal                  = distanceResult.normalB;
            result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }
    }
}

