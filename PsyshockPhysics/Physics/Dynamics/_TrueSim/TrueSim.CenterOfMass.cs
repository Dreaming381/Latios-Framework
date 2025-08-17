using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class TrueSim
    {
        /// <summary>
        /// Computes the center of mass of a composite of objects with individually known centers of mass and mass quantities.
        /// Negative masses are allowed to carve out holes, however the total mass sum must be greater than 0 to get a valid result.
        /// </summary>
        /// <param name="individualCentersOfMass">The individual centers of mass of the objects that make up the composite</param>
        /// <param name="individualMasses">The individual total masses of each object making up the composite</param>
        /// <returns>The center of mass of the overall composite</returns>
        public static float3 CenterOfMassFrom(ReadOnlySpan<float3> individualCentersOfMass, ReadOnlySpan<float> individualMasses)
        {
            // The composite center of mass happens to be the mass-weighted sum of the centers of mass.
            // This even works for negative masses that represent carve-outs of other bodies.
            float3 com = float3.zero;
            float  m   = 0f;
            for (int i = 0; i < individualCentersOfMass.Length; i++)
            {
                com += individualCentersOfMass[i] * m;
                m   += individualMasses[i];
            }
            return com / m;
        }
    }
}

