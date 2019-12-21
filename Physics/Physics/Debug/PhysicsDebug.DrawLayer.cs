using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.PhysicsEngine
{
    public static partial class PhysicsDebug
    {
        public static void DrawLayer(CollisionLayer layer)
        {
            var colors     = new NativeArray<Color>(7, Allocator.TempJob);
            colors[0]      = Color.red;
            colors[1]      = Color.green;
            colors[2]      = Color.blue;
            colors[3]      = Color.cyan;
            colors[4]      = Color.yellow;
            colors[5]      = Color.magenta;
            colors[6]      = Color.black;
            var crossColor = Color.white;
            for (int bucketI = 0; bucketI < layer.BucketCount - 1; bucketI++)
            {
                Color choiceColor = colors[bucketI % 7];
                var   slices      = layer.GetBucketSlices(bucketI);
                for (int i = 0; i < slices.count; i++)
                {
                    AABB aabb = new AABB(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], slices.yzminmaxs[i].zw));
                    DrawAabb(aabb, choiceColor);
                }
            }
            {
                var slices = layer.GetBucketSlices(layer.BucketCount - 1);
                for (int i = 0; i < slices.count; i++)
                {
                    AABB aabb = new AABB(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], slices.yzminmaxs[i].zw));
                    DrawAabb(aabb, crossColor);
                }
            }
            colors.Dispose();
        }

        public static void DrawFindPairs(CollisionLayer layer)
        {
            NativeHashMap<Entity, bool>          hitmap  = new NativeHashMap<Entity, bool>(layer.Count * 2, Allocator.TempJob);
            NativeArray<DebugFindPairsHitResult> results = new NativeArray<DebugFindPairsHitResult>(layer.Count, Allocator.TempJob);
            DebugFindPairsProcessor              job     = new DebugFindPairsProcessor { hits = hitmap };
            new DebugFindPairsJob { processor            = job, layer = layer }.Run();
            new DebugFindPairsProcessResultsJob { layer  = layer, hits = hitmap, results = results }.Run();

            for (int i = 0; i < results.Length; i++)
            {
                DrawAabb(results[i].aabb, results[i].hit ? Color.red : Color.green);
            }
            hitmap.Dispose();
            results.Dispose();
        }

        private static void DrawAabb(AABB aabb, Color color)
        {
            float3 leftTopFront     = new float3(aabb.min.x, aabb.max.y, aabb.min.z);
            float3 rightTopFront    = new float3(aabb.max.x, aabb.max.y, aabb.min.z);
            float3 leftBottomFront  = new float3(aabb.min.x, aabb.min.y, aabb.min.z);
            float3 rightBottomFront = new float3(aabb.max.x, aabb.min.y, aabb.min.z);
            float3 leftTopBack      = new float3(aabb.min.x, aabb.max.y, aabb.max.z);
            float3 rightTopBack     = new float3(aabb.max.x, aabb.max.y, aabb.max.z);
            float3 leftBottomBack   = new float3(aabb.min.x, aabb.min.y, aabb.max.z);
            float3 rightBottomBack  = new float3(aabb.max.x, aabb.min.y, aabb.max.z);

            Debug.DrawLine(leftTopFront,     rightTopFront,    color);
            Debug.DrawLine(rightTopFront,    rightBottomFront, color);
            Debug.DrawLine(rightBottomFront, leftBottomFront,  color);
            Debug.DrawLine(leftBottomFront,  leftTopFront,     color);

            Debug.DrawLine(leftTopBack,      rightTopBack,     color);
            Debug.DrawLine(rightTopBack,     rightBottomBack,  color);
            Debug.DrawLine(rightBottomBack,  leftBottomBack,   color);
            Debug.DrawLine(leftBottomBack,   leftTopBack,      color);

            Debug.DrawLine(leftTopFront,     leftTopBack,      color);
            Debug.DrawLine(rightTopFront,    rightTopBack,     color);
            Debug.DrawLine(leftBottomFront,  leftBottomBack,   color);
            Debug.DrawLine(rightBottomFront, rightBottomBack,  color);
        }

        #region DrawFindPairsUtils
        [BurstCompile]
        internal struct DebugFindPairsProcessor : IFindPairsProcessor
        {
            public NativeHashMap<Entity, bool> hits;

            public void Execute(FindPairsResult result)
            {
                hits.TryAdd(result.bodyA.entity, true);
                hits.TryAdd(result.bodyB.entity, true);
            }
        }

        [BurstCompile]
        private struct DebugFindPairsJob : IJob
        {
            public DebugFindPairsProcessor processor;
            public CollisionLayer          layer;

            public void Execute()
            {
                Physics.FindPairs(layer, processor).RunImmediate();
            }
        }

        private struct DebugFindPairsHitResult
        {
            public AABB aabb;
            public bool hit;
        }

        [BurstCompile]
        private struct DebugFindPairsProcessResultsJob : IJob
        {
            [ReadOnly] public CollisionLayer              layer;
            [ReadOnly] public NativeHashMap<Entity, bool> hits;
            public NativeArray<DebugFindPairsHitResult>   results;

            public void Execute()
            {
                for (int i = 0; i < layer.Count; i++)
                {
                    var res = new DebugFindPairsHitResult
                    {
                        aabb = new AABB(new float3(layer.xmins[i], layer.yzminmaxs[i].xy), new float3(layer.xmaxs[i], layer.yzminmaxs[i].zw)),
                        hit  = hits.ContainsKey(layer.bodies[i].entity),
                    };
                    results[i] = res;
                }
            }
        }
        #endregion
    }
}

