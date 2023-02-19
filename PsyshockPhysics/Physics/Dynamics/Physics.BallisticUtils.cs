using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Computes the acceleration given a force and an inverse mass the force is acting upon
        /// </summary>
        public static float AccelerationFrom(float force, float inverseMass) => force * inverseMass;
        /// <summary>
        /// Computes the acceleration given a force and an inverse mass the force is acting upon
        /// </summary>
        public static float2 AccelerationFrom(float2 force, float2 inverseMass) => force * inverseMass;
        /// <summary>
        /// Computes the acceleration given a force and an inverse mass the force is acting upon
        /// </summary>
        public static float3 AccelerationFrom(float3 force, float3 inverseMass) => force * inverseMass;

        // Simplified model
        /// <summary>
        /// Computes a drag force using simplified drag coefficients
        /// </summary>
        /// <param name="velocity">The speed and direction of travel</param>
        /// <param name="k1">The drag coefficient scaled by the velocity's magnitude</param>
        /// <param name="k2">The drag coefficient scaled by the velocity's magnitude squared</param>
        /// <returns>A force caused by the drag</returns>
        public static float DragForceFrom(float velocity, float k1, float k2) => - (k1 * velocity + k2 * velocity * velocity);
        /// <summary>
        /// Computes a drag force using simplified drag coefficients
        /// </summary>
        /// <param name="velocity">The speed and direction of travel</param>
        /// <param name="k1">The drag coefficient scaled by the velocity's magnitude</param>
        /// <param name="k2">The drag coefficient scaled by the velocity's magnitude squared</param>
        /// <returns>A force caused by the drag</returns>
        public static float2 DragForceFrom(float2 velocity, float k1, float k2)
        {
            float speedSq = math.lengthsq(velocity);
            float speed   = math.sqrt(speedSq);
            var   unit    = velocity / speed;
            return -unit * (k1 * speed + k2 * speedSq);
        }
        /// <summary>
        /// Computes a drag force using simplified drag coefficients
        /// </summary>
        /// <param name="velocity">The speed and direction of travel</param>
        /// <param name="k1">The drag coefficient scaled by the velocity's magnitude</param>
        /// <param name="k2">The drag coefficient scaled by the velocity's magnitude squared</param>
        /// <returns>A force caused by the drag</returns>
        public static float3 DragForceFrom(float3 velocity, float k1, float k2)
        {
            float speedSq = math.lengthsq(velocity);
            float speed   = math.sqrt(speedSq);
            var   unit    = velocity / speed;
            return -unit * (k1 * speed + k2 * speedSq);
        }

        // Source: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4786048/
        /// <summary>
        /// Computes a drag force using an approximation of a sphere inside a flowing fluid (such as wind or water)
        /// </summary>
        /// <param name="velocity">The velocity of the spherical object</param>
        /// <param name="objectRadius">The spherical object's radius</param>
        /// <param name="fluidVelocity">The velocity of the flowing fluid</param>
        /// <param name="fluidViscosity">The viscosity of the fluid</param>
        /// <param name="fluidDensity">The density of the fluid</param>
        /// <returns>A drag force to be applied to the spherical object</returns>
        public static float3 DragForceFrom(float3 velocity, float objectRadius, float3 fluidVelocity, float fluidViscosity, float fluidDensity)
        {
            float3 combinedVelocity = fluidVelocity - velocity;
            float  speed            = math.length(combinedVelocity);
            float  re               = 2f * objectRadius * fluidDensity * speed / fluidViscosity;  // Reynold's Number
            float  cd               = 0.18f;  // coefficient of drag
            if (re <= 0f)
                cd = 0f;
            else if (re < 1f)
                cd = 24f / re;
            else if (re < 400f)
                cd = 24 / math.pow(re, 0.646f);
            else if (re < 300000f)
                cd = 0.5f;
            else if (re < 2000000f)
                cd = 0.000366f * math.pow(re, 0.4275f);
            return 0.5f * fluidDensity * cd * speed * math.PI * objectRadius * objectRadius * combinedVelocity;
        }

        /// <summary>
        /// Computes a bouyancy force of a fluid applied to a fully submersed spherical object
        /// </summary>
        /// <param name="gravity">The acceleration vector of gravity</param>
        /// <param name="objectRadius">The spherical object's radius</param>
        /// <param name="fluidDensity">The density of the fluid</param>
        /// <returns>A bouyancy force to be applied to the spherical object</returns>
        public static float3 BouyancyForceFrom(float3 gravity, float objectRadius, float fluidDensity)
        {
            return fluidDensity * 4f / 3f * objectRadius * objectRadius * objectRadius * -gravity;
        }

        /// <summary>
        /// Computes the required mass and gravity multipliers to preserve the same kinetic energy and ballistic trajectory
        /// of an object moving significantly slower in simulation relative to real-world velocities
        /// </summary>
        /// <param name="referenceSpeed">The speed the object would move in the real world</param>
        /// <param name="desiredSpeed">The speed the object should move in the simulation</param>
        /// <param name="massMultiplier">A multiplier to be applied to the mass</param>
        /// <param name="gravityMultiplier">A multiplier to be applied to gravity</param>
        public static void WarpPropertiesForDesiredSpeed(float referenceSpeed, float desiredSpeed, out float massMultiplier, out float gravityMultiplier)
        {
            float factor      = referenceSpeed / desiredSpeed;
            massMultiplier    = factor * factor;
            gravityMultiplier = 1f / factor;
        }

        public static partial class Constants
        {
            /// <summary>
            /// Approximate real-world fluid viscosity of air.
            /// Units: Pa / s
            /// </summary>
            public const float fluidViscosityOfAir = 0.000018f;  // Pa / s
            /// <summary>
            /// Approximate real-world density of air.
            /// Units: kg / m^3
            /// </summary>
            public const float densityOfAir = 1.225f;  // kg / m^3
        }
    }
}

