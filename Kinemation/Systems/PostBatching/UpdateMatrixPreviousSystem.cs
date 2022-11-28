using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    // Unity's Hybrid Renderer uploads all MatrixPrevious and then updates them to LocalToWorld in the very next system.
    // However, we upload material properties later, so we would have to wait until all culling is complete before updating.
    // That would be fragile, so instead, we use an intermediate buffer cache component.
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateMatrixPreviousSystem : ISystem
    {
        EntityQuery          m_query;
        uint                 m_secondLastSystemVersion;
        LatiosWorldUnmanaged latiosWorld;

        UpdateMatricesJob m_job;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().WithAll<LocalToWorld>(true).WithAll<MatrixPreviousCache>().WithAll<BuiltinMaterialPropertyUnity_MatrixPreviousM>()
                          .WithAll<ChunkMaterialPropertyDirtyMask>(false, true).Build();

            m_job = new UpdateMatricesJob
            {
                ltwHandle   = state.GetComponentTypeHandle<LocalToWorld>(true),
                cacheHandle = state.GetComponentTypeHandle<MatrixPreviousCache>(false),
                prevHandle  = state.GetComponentTypeHandle<BuiltinMaterialPropertyUnity_MatrixPreviousM>(false),
                maskHandle  = state.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int matrixPrevIndex = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>()
                                  .AsNativeArray().IndexOf(ComponentType.ReadOnly<BuiltinMaterialPropertyUnity_MatrixPreviousM>());
            ulong matrixPrevMaterialMaskLower = (ulong)matrixPrevIndex >= 64UL ? 0UL : (1UL << matrixPrevIndex);
            ulong matrixPrevMaterialMaskUpper = (ulong)matrixPrevIndex >= 64UL ? (1UL << (matrixPrevIndex - 64)) : 0UL;

            m_job.ltwHandle.Update(ref state);
            m_job.cacheHandle.Update(ref state);
            m_job.prevHandle.Update(ref state);
            m_job.maskHandle.Update(ref state);
            m_job.secondLastSystemVersion     = m_secondLastSystemVersion;
            m_job.matrixPrevMaterialMaskLower = matrixPrevMaterialMaskLower;
            m_job.matrixPrevMaterialMaskUpper = matrixPrevMaterialMaskUpper;

            state.Dependency          = m_job.ScheduleParallel(m_query, state.Dependency);
            m_secondLastSystemVersion = state.LastSystemVersion;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) {
        }

        [BurstCompile]
        struct UpdateMatricesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>                      ltwHandle;
            public ComponentTypeHandle<MatrixPreviousCache>                          cacheHandle;
            public ComponentTypeHandle<BuiltinMaterialPropertyUnity_MatrixPreviousM> prevHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>               maskHandle;

            // Even if LTW stops moving, we still need to update for one more frame for the prev
            // to catch up.
            public uint secondLastSystemVersion;

            // We update after LatiosHybridRendererSystem, which means property changes don't get
            // seen since this is before the end of culling. So we update the mask instead.
            public ulong matrixPrevMaterialMaskLower;
            public ulong matrixPrevMaterialMaskUpper;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref ltwHandle, secondLastSystemVersion))
                    return;

                var mask          = chunk.GetChunkComponentData(ref maskHandle);
                mask.lower.Value |= matrixPrevMaterialMaskLower;
                mask.upper.Value |= matrixPrevMaterialMaskUpper;
                chunk.SetChunkComponentData(ref maskHandle, mask);

                var ltws   = chunk.GetNativeArray(ref ltwHandle).Reinterpret<float4x4>();
                var caches = chunk.GetNativeArray(ref cacheHandle).Reinterpret<float2x4>();
                // Even if this is the first frame that LTW moves, we still dirty prev.
                // Todo: Is there an efficient way to fix this, and is it even worth it?
                var prevs = chunk.GetNativeArray(ref prevHandle).Reinterpret<float4x4>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    // We pack the 3rd row of the cache in the 4th row of prev since that row
                    // isn't used by the uploader. This reduces memory footprint and hopefully
                    // increases chunk capacity a little.
                    var cache = caches[i];
                    var ltw   = ltws[i];
                    var prev  = prevs[i];
                    prevs[i]  = new float4x4(new float4(cache.c0.xy, prev.c0.w, ltw.c0.z),
                                             new float4(cache.c1.xy, prev.c1.w, ltw.c1.z),
                                             new float4(cache.c2.xy, prev.c2.w, ltw.c2.z),
                                             new float4(cache.c3.xy, prev.c3.w, ltw.c3.z));
                    caches[i] = new float2x4(ltw.c0.xy, ltw.c1.xy, ltw.c2.xy, ltw.c3.xy);
                }
            }
        }
    }
}

