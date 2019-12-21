using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, caches, scratchlists, and inflations
namespace Latios.PhysicsEngine
{
    public interface IFindPairsProcessor
    {
        void Execute(FindPairsResult result);
    }

    public struct FindPairsResult
    {
        public ColliderBody bodyA;
        public ColliderBody bodyB;
        public int          bodyAIndex;
        public int          bodyBIndex;
        //Todo: Shorthands for calling narrow phase distance and manifold queries
    }

    public struct FindPairsConfig<T> where T : struct, IFindPairsProcessor
    {
        internal T processor;

        internal CollisionLayer layerA;
        internal CollisionLayer layerB;

        internal bool isLayerLayer;
    }

    public static partial class Physics
    {
        public static FindPairsConfig<T> FindPairs<T>(CollisionLayer layer, T processor) where T : struct, IFindPairsProcessor
        {
            return new FindPairsConfig<T>
            {
                processor    = processor,
                layerA       = layer,
                isLayerLayer = false
            };
        }

        public static FindPairsConfig<T> FindPairs<T>(CollisionLayer layerA, CollisionLayer layerB, T processor) where T : struct, IFindPairsProcessor
        {
            return new FindPairsConfig<T>
            {
                processor    = processor,
                layerA       = layerA,
                layerB       = layerB,
                isLayerLayer = true
            };
        }

        #region Schedulers
        public static void RunImmediate<T>(this FindPairsConfig<T> config) where T : struct, IFindPairsProcessor
        {
            if (config.isLayerLayer)
            {
                FindPairsInternal.RunImmediate(config.layerA, config.layerB, config.processor);
            }
            else
            {
                FindPairsInternal.RunImmediate(config.layerA, config.processor);
            }
        }

        public static void Run<T>(this FindPairsConfig<T> config) where T : struct, IFindPairsProcessor
        {
            if (config.isLayerLayer)
            {
                new FindPairsInternal.LayerLayerSingle<T>
                {
                    layerA    = config.layerA,
                    layerB    = config.layerB,
                    processor = config.processor
                }.Run();
            }
            else
            {
                new FindPairsInternal.LayerSelfSingle<T>
                {
                    layer     = config.layerA,
                    processor = config.processor
                }.Run();
            }
        }

        public static JobHandle ScheduleSingle<T>(this FindPairsConfig<T> config, JobHandle inputDeps = default) where T : struct, IFindPairsProcessor
        {
            if (config.isLayerLayer)
            {
                return new FindPairsInternal.LayerLayerSingle<T>
                {
                    layerA    = config.layerA,
                    layerB    = config.layerB,
                    processor = config.processor
                }.Schedule(inputDeps);
            }
            else
            {
                return new FindPairsInternal.LayerSelfSingle<T>
                {
                    layer     = config.layerA,
                    processor = config.processor
                }.Schedule(inputDeps);
            }
        }

        public static JobHandle ScheduleParallel<T>(this FindPairsConfig<T> config, JobHandle inputDeps = default) where T : struct, IFindPairsProcessor
        {
            if (config.isLayerLayer)
            {
                JobHandle jh = new FindPairsInternal.LayerLayerPart1<T>
                {
                    layerA    = config.layerA,
                    layerB    = config.layerB,
                    processor = config.processor
                }.Schedule(config.layerB.BucketCount, 1, inputDeps);
                jh = new FindPairsInternal.LayerLayerPart2<T>
                {
                    layerA    = config.layerA,
                    layerB    = config.layerB,
                    processor = config.processor
                }.Schedule(2, 1, jh);
                return jh;
            }
            else
            {
                JobHandle jh = new FindPairsInternal.LayerSelfPart1<T>
                {
                    layer     = config.layerA,
                    processor = config.processor
                }.Schedule(config.layerA.BucketCount, 1, inputDeps);
                jh = new FindPairsInternal.LayerSelfPart2<T>
                {
                    layer     = config.layerA,
                    processor = config.processor
                }.Schedule(jh);
                return jh;
            }
        }
        #endregion Schedulers
    }
}

