using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.Psyshock
{
    public partial struct FindObjectsConfig<T> where T : struct, IFindObjectsProcessor
    {
        internal static class FindObjectsInternal
        {
            #region Jobs
            [BurstCompile]
            public struct Single : IJob
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;
                public Aabb                      aabb;

                public void Execute()
                {
                    LayerQuerySweepMethods.AabbSweep(in aabb, in layer, ref processor);
                }
            }
            #endregion

            #region ImmediateMethods
            public static T RunImmediate(in Aabb aabb, in CollisionLayer layer, T processor)
            {
                LayerQuerySweepMethods.AabbSweep(in aabb, in layer, ref processor);
                return processor;
            }
            #endregion
        }
    }
}

