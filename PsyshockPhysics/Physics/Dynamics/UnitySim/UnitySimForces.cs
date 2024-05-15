using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// Applies an impulse across the entire body
        /// </summary>
        /// <param name="velocity">The current velocity of the body to modify</param>
        /// <param name="mass">The mass properties of the body</param>
        /// <param name="impulse">A world-space vector representing the direction and magnitude of the impulse</param>
        public static void ApplyFieldImpulse(ref Velocity velocity, in Mass mass, float3 impulse)
        {
            velocity.linear += mass.inverseMass * impulse;
        }

        /// <summary>
        /// Applies an impulse at a given world-space point on the body
        /// </summary>
        /// <param name="velocity">The current velocity of the body to modify</param>
        /// <param name="mass">The mass properties of the body</param>
        /// <param name="inertialSpaceWorldTransform">The world transform of the center of mass and inertia tensor diagonal</param>
        /// <param name="point">The world-space location at which the impulse should be applied</param>
        /// <param name="impulse">A world-space vector representing the direction and magnitude of the impulse</param>
        public static void ApplyImpulseAtWorldPoint(ref Velocity velocity, in Mass mass, in RigidTransform inertialSpaceWorldTransform, float3 point, float3 impulse)
        {
            ApplyFieldImpulse(ref velocity, in mass, impulse);
            var angularImpulse  = math.cross(point - inertialSpaceWorldTransform.pos, impulse);
            velocity.angular   += math.InverseRotateFast(inertialSpaceWorldTransform.rot, angularImpulse) * mass.inverseInertia;
        }
    }
}

