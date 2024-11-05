using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Mimic.Shuriken
{
    public struct EmissionModule
    {
        // Some fun facts about the emission module.
        // First, rate over distance as a curve still treats that curve over simulation time.
        // Second, if you have more Burst cycles than what fits in the remainder of the simulation time,
        // those cycles get dropped.
        // Third, rate over distance and rate over time use a shared accumulator. So if you spawn a whole bunch
        // over distance and then stop, it will take the full time duration before the next particle spawns after the last spawn.
        // Fourth, Burst spawns are totally independent, being integers. Though we could still put them in the accumulator for convenience.

        internal byte rateOverTime;  // half k, rk, c, rc
        internal byte rateOverDistance;  // half k, rk, c, rc

        internal struct Burst
        {
            public int  count;
            public half time;
            public half probability;
        }
    }
}

