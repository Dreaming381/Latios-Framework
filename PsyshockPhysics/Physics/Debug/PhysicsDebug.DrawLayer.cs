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
        /// <summary>
        /// Draws the bounding boxes of a CollisionLayer using UnityEngine.Debug.DrawLine calls.
        /// The boxes are color-coded by the cell they exist in.
        /// This is the start of a Fluent chain.
        /// </summary>
        /// <param name="layer">The collision layer to draw.</param>
        /// <returns>A context object from which a scheduler should be invoked.</returns>
        public static DrawLayerConfig DrawLayer(CollisionLayer layer)
        {
            var colors                         = new FixedList512Bytes<Color>();
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
            internal CollisionLayer           layer;
            internal FixedList512Bytes<Color> colors;
            internal Color                    crossColor;

            public DrawLayerConfig WithColors(FixedList512Bytes<Color> colors, Color crossBucketColor)
            {
                this.colors     = colors;
                this.crossColor = crossBucketColor;
                return this;
            }

            /// <summary>
            /// Run immediately without using the job system.
            /// </summary>
            public void RunImmediate()
            {
                var job = new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                };
                for (int i = 0; i < layer.bucketCount; i++)
                {
                    job.Execute(i);
                }
            }

            /// <summary>
            /// Run on the same thread using a Burst job.
            /// </summary>
            public void Run()
            {
                new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.Run(layer.bucketCount);
            }

            /// <summary>
            /// Schedule a single-threaded job to perform the drawing operations
            /// </summary>
            /// <param name="inputDeps">The JobHandle that this job should wait for before executing</param>
            /// <returns>A job handle associated with the scheduled job</returns>
            public JobHandle ScheduleSingle(JobHandle inputDeps = default)
            {
                return new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.Schedule(inputDeps);
            }

            /// <summary>
            /// Schedule a multi-threaded job to perform the drawing operations
            /// </summary>
            /// <param name="inputDeps">The JobHandle that this job should wait for before executing</param>
            /// <returns>A job handle associated with the scheduled job</returns>
            public JobHandle ScheduleParallel(JobHandle inputDeps = default)
            {
                return new DebugDrawLayerJob
                {
                    layer      = layer,
                    colors     = colors,
                    crossColor = crossColor
                }.Schedule(layer.bucketCount, 1, inputDeps);
            }
        }

        /// <summary>
        /// Draws overlapping AABBs within the layer using UnityEngine.Debug.DrawLine calls
        /// This is the start of a Fluent chain.
        /// </summary>
        /// <param name="layer">The layer to draw</param>
        /// <returns>A config object from which a scheduler should be called</returns>
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

        /// <summary>
        /// Draws overlapping AABBs between two layers using UnityEngine.Debug.DrawLine calls
        /// This is the start of a Fluent chain.
        /// </summary>
        /// <param name="layerA">The first layer to draw</param>
        /// <param name="layerB">The second layer to draw</param>
        /// <returns>A config object from which a scheduler should be called</returns>
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

            /// <summary>
            /// Override the default colors drawn and set whether or not to draw non-overlapping
            /// </summary>
            /// <param name="overlapColor">The color for when two AABBs overlap</param>
            /// <param name="nonOverlapColor">The color for when an AABB does not overlap with anything</param>
            /// <param name="drawNonOverlapping">If true, AABBs which do not overlap with anything will be drawn using the nonOverlapColor</param>
            /// <returns>A config object from which a scheduler should be called</returns>
            public DrawFindPairsConfig WithColors(Color overlapColor, Color nonOverlapColor, bool drawNonOverlapping = true)
            {
                hitColor   = overlapColor;
                missColor  = nonOverlapColor;
                drawMisses = drawNonOverlapping;
                return this;
            }

            /// <summary>
            /// Run immediately without using the job system.
            /// </summary>
            public void RunImmediate()
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.count, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.count, Allocator.Temp, NativeArrayOptions.ClearMemory);
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
                    for (int i = 0; i < layerA.count; i++)
                    {
                        job.Execute(i);
                    }
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    for (int i = 0; i < layerB.count; i++)
                    {
                        job.Execute(i);
                    }
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.count, Allocator.Temp, NativeArrayOptions.ClearMemory);
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
                    for (int i = 0; i < layerA.count; i++)
                    {
                        job.Execute(i);
                    }
                }
            }

            /// <summary>
            /// Run on the same thread using Burst jobs
            /// </summary>
            public void Run()
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
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
                    job.Run(layerA.count);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    job.Run(layerB.count);
                    hitArrayA.Dispose();
                    hitArrayB.Dispose();
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    Physics.FindPairs(layerA, processor).Run();
                    new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.Run(layerA.count);
                    hitArray.Dispose();
                }
            }

            /// <summary>
            /// Schedule single-threadeds job to perform the drawing operations
            /// </summary>
            /// <param name="inputDeps">The JobHandle that these jobs should wait for before executing</param>
            /// <returns>A job handle associated with the scheduled jobs</returns>
            public JobHandle ScheduleSingle(JobHandle inputDeps = default)
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
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
                    jh           = job.Schedule(jh);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    jh           = job.Schedule(jh);
                    jh           = hitArrayA.Dispose(jh);
                    return hitArrayB.Dispose(jh);
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    var jh        = Physics.FindPairs(layerA, processor).ScheduleSingle(inputDeps);
                    jh            = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.Schedule(jh);
                    return hitArray.Dispose(jh);
                }
            }

            /// <summary>
            /// Schedule multi-threaded jobs to perform the drawing operations
            /// </summary>
            /// <param name="inputDeps">The JobHandle that these jobs should wait for before executing</param>
            /// <returns>A job handle associated with the scheduled jobs</returns>
            public JobHandle ScheduleParallel(JobHandle inputDeps = default)
            {
                if (isLayerLayer)
                {
                    var hitArrayA = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var hitArrayB = new NativeBitArray(layerB.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
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
                    jh           = job.Schedule(layerA.count, 64, jh);
                    job.hitArray = hitArrayB;
                    job.layer    = layerB;
                    jh           = job.Schedule(layerB.count, 64, jh);
                    jh           = hitArrayA.Dispose(jh);
                    return hitArrayB.Dispose(jh);
                }
                else
                {
                    var hitArray  = new NativeBitArray(layerA.count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    var processor = new DebugFindPairsLayerSelfProcessor { hitArray = hitArray };
                    var jh        = Physics.FindPairs(layerA, processor).ScheduleSingle(inputDeps);
                    jh            = new DebugFindPairsDrawJob
                    {
                        layer      = layerA,
                        hitArray   = hitArray,
                        hitColor   = hitColor,
                        missColor  = missColor,
                        drawMisses = drawMisses
                    }.Schedule(layerA.count, 64, jh);
                    return hitArray.Dispose(jh);
                }
            }
        }

        /// <summary>
        /// Draw an AABB wireframe using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="aabb">The AABB to draw</param>
        /// <param name="color">The color of the wireframe</param>
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
        private struct DebugDrawLayerJob : IJob, IJobParallelFor
        {
            [ReadOnly] public CollisionLayer layer;
            public FixedList512Bytes<Color>  colors;
            public Color                     crossColor;

            public void Execute()
            {
                for (int i = 0; i < layer.bucketCount; i++)
                    Execute(i);
            }

            public void Execute(int index)
            {
                if (index < layer.bucketCount - 1)
                {
                    Color color  = colors[index % colors.Length];
                    var   slices = layer.GetBucketSlices(index);
                    for (int i = 0; i < slices.count; i++)
                    {
                        Aabb aabb = new Aabb(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], -slices.yzminmaxs[i].zw));
                        DrawAabb(aabb, color);
                    }
                }
                else
                {
                    Color color  = crossColor;
                    var   slices = layer.GetBucketSlices(index);
                    for (int i = 0; i < slices.count; i++)
                    {
                        Aabb aabb = new Aabb(new float3(slices.xmins[i], slices.yzminmaxs[i].xy), new float3(slices.xmaxs[i], -slices.yzminmaxs[i].zw));
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

            public void Execute(in FindPairsResult result)
            {
                hitArray.Set(result.bodyIndexA, true);
                hitArray.Set(result.bodyIndexB, true);
            }
        }

        private struct DebugFindPairsLayerLayerProcessor : IFindPairsProcessor
        {
            public NativeBitArray hitArrayA;
            public NativeBitArray hitArrayB;

            public void Execute(in FindPairsResult result)
            {
                hitArrayA.Set(result.bodyIndexA, true);
                hitArrayB.Set(result.bodyIndexB, true);
            }
        }

        [BurstCompile]
        private struct DebugFindPairsDrawJob : IJob, IJobParallelFor
        {
            [ReadOnly] public CollisionLayer layer;
            [ReadOnly] public NativeBitArray hitArray;
            public Color                     hitColor;
            public Color                     missColor;
            public bool                      drawMisses;

            public void Execute()
            {
                for (int i = 0; i < layer.count; i++)
                    Execute(i);
            }

            public void Execute(int i)
            {
                float3 min  = new float3(layer.xmins[i], layer.yzminmaxs[i].xy);
                float3 max  = new float3(layer.xmaxs[i], -layer.yzminmaxs[i].zw);
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

