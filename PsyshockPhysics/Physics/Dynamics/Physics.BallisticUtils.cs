using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static float AccelerationFrom(float force, float inverseMass) => force * inverseMass;
        public static float2 AccelerationFrom(float2 force, float2 inverseMass) => force * inverseMass;
        public static float3 AccelerationFrom(float3 force, float3 inverseMass) => force * inverseMass;

        // Simplified model
        public static float DragFrom(float velocity, float k1, float k2) => - (k1 * velocity + k2 * velocity * velocity);
        public static float2 DragFrom(float2 velocity, float k1, float k2)
        {
            float speedSq = math.lengthsq(velocity);
            float speed   = math.sqrt(speedSq);
            var   unit    = velocity / speed;
            return -unit * (k1 * speed + k2 * speedSq);
        }
        public static float3 DragFrom(float3 velocity, float k1, float k2)
        {
            float speedSq = math.lengthsq(velocity);
            float speed   = math.sqrt(speedSq);
            var   unit    = velocity / speed;
            return -unit * (k1 * speed + k2 * speedSq);
        }

        // Source: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4786048/
        public static float3 DragFrom(float3 velocity, float objectRadius, float3 fluidVelocity, float fluidViscosity, float fluidDensity)
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

        public static float3 BouyancyFrom(float3 gravity, float objectRadius, float fluidDensity)
        {
            return fluidDensity * 4f / 3f * objectRadius * objectRadius * objectRadius * -gravity;
        }

        public static void WarpPropertiesForDesiredSpeed(float referenceSpeed, float desiredSpeed, out float massMultiplier, out float gravityMultiplier)
        {
            float factor      = referenceSpeed / desiredSpeed;
            massMultiplier    = factor * factor;
            gravityMultiplier = 1f / factor;
        }

        public static partial class Constants
        {
            public const float fluidViscosityOfAir = 0.000018f;  // Pa / s
            public const float densityOfAir        = 1.225f;  // kg / m^3
        }
    }
}

