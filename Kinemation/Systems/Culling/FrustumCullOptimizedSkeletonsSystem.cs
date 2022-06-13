using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public class FrustumCullOptimizedSkeletonsSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<ChunkSkeletonWorldBounds>(true).WithAll<ChunkHeader>(true).WithAll<ChunkPerCameraSkeletonCullingMask>(false).Build();
        }

        protected override void OnUpdate()
        {
            var planesBuffer = worldBlackboardEntity.GetBuffer<CullingPlane>(true);
            var unmanaged    = World.Unmanaged;
            var planes       = CullingUtilities.BuildSOAPlanePackets(planesBuffer.Reinterpret<UnityEngine.Plane>().AsNativeArray(), ref unmanaged);

            Dependency = new SkeletonCullingJob
            {
                cullingPlanes     = planes,
                boundsHandle      = GetComponentTypeHandle<SkeletonWorldBounds>(true),
                chunkHeaderHandle = GetComponentTypeHandle<ChunkHeader>(true),
                chunkBoundsHandle = GetComponentTypeHandle<ChunkSkeletonWorldBounds>(true),
                chunkMaskHandle   = GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(false)
            }.ScheduleParallel(m_metaQuery, Dependency);
        }

        [BurstCompile]
        unsafe struct SkeletonCullingJob : IJobEntityBatch
        {
            [ReadOnly] public NativeArray<FrustumPlanes.PlanePacket4> cullingPlanes;

            [ReadOnly] public ComponentTypeHandle<SkeletonWorldBounds>      boundsHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>              chunkHeaderHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkSkeletonWorldBounds> chunkBoundsHandle;
            public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>   chunkMaskHandle;

            public void Execute(ArchetypeChunk archetypeChunk, int chunkIndex)
            {
                var chunkHeaderArray = archetypeChunk.GetNativeArray(chunkHeaderHandle);
                var chunkBoundsArray = archetypeChunk.GetNativeArray(chunkBoundsHandle);
                var chunkMaskArray   = archetypeChunk.GetNativeArray(chunkMaskHandle);

                for (var metaIndex = 0; metaIndex < archetypeChunk.Count; metaIndex++)
                {
                    var chunkHeader = chunkHeaderArray[metaIndex];
                    var chunkBounds = chunkBoundsArray[metaIndex];

                    var chunkIn = FrustumPlanes.Intersect2(cullingPlanes, chunkBounds.chunkBounds);

                    if (chunkIn == FrustumPlanes.IntersectResult.Partial)
                    {
                        var chunk = chunkHeader.ArchetypeChunk;

                        var chunkInstanceBounds = chunk.GetNativeArray(boundsHandle);

                        BitField64 maskWordLower;
                        maskWordLower.Value = 0;
                        for (int i = 0; i < math.min(64, chunk.Count); i++)
                        {
                            bool isIn            = FrustumPlanes.Intersect2NoPartial(cullingPlanes, chunkInstanceBounds[i].bounds) != FrustumPlanes.IntersectResult.Out;
                            maskWordLower.Value |= math.select(0ul, 1ul, isIn) << i;
                        }
                        BitField64 maskWordUpper;
                        maskWordUpper.Value = 0;
                        for (int i = 0; i < math.max(0, chunk.Count - 64); i++)
                        {
                            bool isIn            = FrustumPlanes.Intersect2NoPartial(cullingPlanes, chunkInstanceBounds[i + 64].bounds) != FrustumPlanes.IntersectResult.Out;
                            maskWordUpper.Value |= math.select(0ul, 1ul, isIn) << i;
                        }

                        chunkMaskArray[metaIndex] = new ChunkPerCameraSkeletonCullingMask { lower = maskWordLower, upper = maskWordUpper };
                    }
                    else if (chunkIn == FrustumPlanes.IntersectResult.In)
                    {
                        var chunk = chunkHeader.ArchetypeChunk;

                        BitField64 lodWordLower = default;
                        BitField64 lodWordUpper = default;
                        if (chunk.Count > 64)
                        {
                            lodWordUpper.SetBits(0, true, math.max(chunk.Count - 64, 0));
                            lodWordLower.Value = ~0UL;
                        }
                        else
                        {
                            lodWordLower.SetBits(0, true, chunk.Count);
                            lodWordUpper.Clear();
                        }
                        chunkMaskArray[metaIndex] = new ChunkPerCameraSkeletonCullingMask { lower = lodWordLower, upper = lodWordUpper };
                    }
                    else
                        chunkMaskArray[metaIndex] = default;
                }
            }
        }
    }
}

