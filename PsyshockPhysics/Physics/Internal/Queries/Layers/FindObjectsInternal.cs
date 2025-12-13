using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.Psyshock
{
    public partial struct FindObjectsConfig<T> where T : struct, IFindObjectsProcessor
    {
        internal static class FindObjectsInternal
        {
            #region Jobs
            [BurstCompile]
            public struct SingleJob : IJob
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;
                public Aabb                      aabb;

                public void Execute()
                {
                    LayerQuerySweepMethods.AabbSweep(in aabb, in layer, ref processor);
                }

                [Preserve]
                void RequireEarlyJobInit()
                {
                    new InitJobsForProcessors.FindObjectsIniter<T>().Init();
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

    public partial struct FindObjectsWorldConfig<T> where T : struct, IFindObjectsProcessor
    {
        internal static class FindObjectsInternal
        {
            #region Jobs
            [BurstCompile]
            public struct SingleJob : IJob
            {
                [ReadOnly] public CollisionWorld world;
                public EntityQueryMask           queryMask;
                public T                         processor;
                public Aabb                      aabb;

                public void Execute()
                {
                    var mask = world.CreateMask(queryMask);
                    WorldQuerySweepMethods.AabbSweep(in aabb, in world, in mask, ref processor);
                }

                [Preserve]
                void RequireEarlyJobInit()
                {
                    new InitJobsForProcessors.FindObjectsIniter<T>().Init();
                }
            }
            #endregion

            #region ImmediateMethods
            public static void RunImmediate(in Aabb aabb, in CollisionWorld world, in CollisionWorld.Mask mask, ref T processor)
            {
                WorldQuerySweepMethods.AabbSweep(in aabb, in world, in mask, ref processor);
            }
            #endregion
        }
    }
}

