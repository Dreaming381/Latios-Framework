using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        public static DrawLayerConfig DrawLayer(CollisionLayer layer)
        {
            var colors                         = new FixedList512<Color>();
            colors.Length                      = 7;
            colors[0]                          = Color.red;
            colors[1]                          = Color.green;
            colors[2]                          = Color.blue;
            colors[3]                          = Color.cyan;
            colors[4]                          = Color.yellow;
            colors[5]                          = Color.magenta;
            colors[6]                          = Color.black;
            var crossColor                     = Color.white;
            return new DrawLayerConfig { layer = layer, colors = colors, crossColor = crossColor };
        }

        public struct DrawLayerConfig
        {
            internal CollisionLayer      layer;
            internal FixedList512<Color> colors;
            internal Color               crossColor;

            public DrawLayerConfig WithColors(FixedList512<Color> colors, Color crossBucketColor)
            {
                this.colors     = colors;
                this.crossColor = crossBucketColor;
                return this;
            }

            public void RunImmediate()
            {
                var job = new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                };
                for (int i = 0; i < layer.BucketCount; i++)
                {
                    job.Execute(i);
                }
            }

            public void Run()
            {
                new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.Run(layer.BucketCount);
            }

            public JobHandle ScheduleSingle(JobHandle inputDeps = default)
            {
                return new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.Schedule(layer.BucketCount, inputDeps);
            }

            public JobHandle ScheduleParallel(JobHandle inputDeps = default)
            {
                return new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.ScheduleParallel(layer.BucketCount, 1, inputDeps);
            }
        }

        public static DrawFindPairsConfig DrawFindPairs(CollisionLayer layer)
        {
            return new DrawFindPairsConfig
            {
                layerA       = layer,
                hitColor     = Color.red,
                missColor    = Color.green,
                drawMisses   = true,
                isLayerLayer = false
            };
        }

        public static DrawFindPairsConfig DrawFindPairs(CollisionLayer layerA, CollisionLayer layerB)
        {
            return new DrawFindPairsConfig
            {
                layerA       = layerA,
                layerB       = layerB,
                hitColor     = Color.red,
                missColor    = Color.green,
                drawMisses   = true,
                isLayerLayer = true
            };
        }

        public struct DrawFindPairsConfig
        {
            internal CollisionLayer layerA;
            internal CollisionLayer layerB;
            internal Color          hitColor;
            internal Color          missColor;
            internal bool           drawMisses;
            internal bool           isLayerLayer;

            public DrawFindPairsConfig WithColors(Color overlapColor, Color nonOverlapColor, bool drawNonOverlapping = true)
            {
                hitColor   = overlapColor;
                missColor  = nonOverlapColor;
                drawMisses = drawNonOverlapping;
                return this;
            }

            public void RunImmediate()
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.Count, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.Count, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerLayerProcessor { hitArrayA = hitArrayA, hitArrayB = hitArrayB };
                    Physics.FindPairs(layerA, layerB, processor).RunImmediate();
                    var job = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArrayA,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    };
                    for (int i = 0; i < layerA.Count; i++)
                    {
                        job.Execute(i);
                    }
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    for (int i = 0; i < layerB.Count; i++)
                    {
                        job.Execute(i);
                    }
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.Count, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    Physics.FindPairs(layerA, processor).RunImmediate();
                    var job = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    };
                    for (int i = 0; i < layerA.Count; i++)
                    {
                        job.Execute(i);
                    }
                }
            }

            public void Run()
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerLayerProcessor { hitArrayA = hitArrayA, hitArrayB = hitArrayB };
                    Physics.FindPairs(layerA, layerB, processor).Run();
                    var job = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArrayA,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    };
                    job.Run(layerA.Count);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    job.Run(layerB.Count);
                    hitArrayA.Dispose();
                    hitArrayB.Dispose();
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    Physics.FindPairs(layerA, processor).Run();
                    new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.Run(layerA.Count);
                    hitArray.Dispose();
                }
            }

            public JobHandle ScheduleSingle(JobHandle inputDeps = default)
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerLayerProcessor { hitArrayA = hitArrayA, hitArrayB = hitArrayB };
                    var jh        = Physics.FindPairs(layerA, layerB, processor).ScheduleSingle(inputDeps);
                    var job       = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArrayA,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    };
                    jh           = job.Schedule(layerA.Count, jh);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    jh           = job.Schedule(layerB.Count, jh);
                    jh           = hitArrayA.Dispose(jh);
                    return hitArrayB.Dispose(jh);
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    var jh        = Physics.FindPairs(layerA, processor).ScheduleSingle(inputDeps);
                    jh            = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.Schedule(layerA.Count, jh);
                    return hitArray.Dispose(jh);
                }
            }

            public JobHandle ScheduleParallel(JobHandle inputDeps = default)
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerLayerProcessor { hitArrayA = hitArrayA, hitArrayB = hitArrayB };
                    var jh        = Physics.FindPairs(layerA, layerB, processor).ScheduleSingle(inputDeps);
                    var job       = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArrayA,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    };
                    jh           = job.ScheduleParallel(layerA.Count, 64, jh);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    jh           = job.ScheduleParallel(layerB.Count, 64, jh);
                    jh           = hitArrayA.Dispose(jh);
                    return hitArrayB.Dispose(jh);
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.Count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    var jh        = Physics.FindPairs(layerA, processor).ScheduleSingle(inputDeps);
                    jh            = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.ScheduleParallel(layerA.Count, 64, jh);
                    return hitArray.Dispose(jh);
                }
            }
        }

        public static void DrawAabb(Aabb aabb, Color color)
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

        #region DrawLayerUtils
        [BurstCompile]
        private struct DebugDrawLayerJob : IJobFor
        {
            [ReadOnly] public CollisionLayer layer;
            public FixedList512<Color>       colors;
            public Color                     crossColor;

            public void Execute(int index)
            {
                if (index < layer.BucketCount - 1)
                {
                    Color color  = colors[index % colors.Length];
                    var   slices = layer.GetBucketSlices(index);
                    for (int i = 0; i < slices.count; i++)
                    {
                        Aabb aabb = new Aabb(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], slices.yzminmaxs[i].zw));
                        DrawAabb(aabb, color);
                    }
                }
                else
                {
                    Color color  = crossColor;
                    var   slices = layer.GetBucketSlices(index);
                    for (int i = 0; i < slices.count; i++)
                    {
                        Aabb aabb = new Aabb(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], slices.yzminmaxs[i].zw));
                        DrawAabb(aabb, color);
                    }
                }
            }
        }
        #endregion

        #region DrawFindPairsUtils
        internal struct DebugFindPairsLayerSelfProcessor : IFindPairsProcessor
        {
            public NativeBitArray hitArray;

            public void Execute(FindPairsResult result)
            {
                hitArray.Set(result.bodyAIndex, true);
                hitArray.Set(result.bodyBIndex, true);
            }
        }

        private struct DebugFindPairsLayerLayerProcessor : IFindPairsProcessor
        {
            public NativeBitArray hitArrayA;
            public NativeBitArray hitArrayB;

            public void Execute(FindPairsResult result)
            {
                hitArrayA.Set(result.bodyAIndex, true);
                hitArrayB.Set(result.bodyBIndex, true);
            }
        }

        [BurstCompile]
        private struct DebugFindPairsDrawJob : IJobFor
        {
            [ReadOnly] public CollisionLayer layer;
            [ReadOnly] public NativeBitArray hitArray;
            public Color                     hitColor;
            public Color                     missColor;
            public bool                      drawMisses;

            public void Execute(int i)
            {
                float3 min  = new float3(layer.xmins[i], layer.yzminmaxs[i].xy);
                float3 max  = new float3(layer.xmaxs[i], layer.yzminmaxs[i].zw);
                var    aabb = new Aabb(min, max);
                if (hitArray.IsSet(i))
                {
                    DrawAabb(aabb, hitColor);
                }
                else if (drawMisses)
                {
                    DrawAabb(aabb, missColor);
                }
            }
        }
        #endregion
    }
}

