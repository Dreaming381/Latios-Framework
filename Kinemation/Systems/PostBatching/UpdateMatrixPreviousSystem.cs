using Latios;
using Unity.Burst;
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
    [DisableAutoCreation]
    public partial class UpdateMatrixPreviousSystem : SubSystem
    {
        EntityQuery m_query;
        uint        m_secondLastSystemVersion;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<LocalToWorld>(true).WithAll<MatrixPreviousCache>().WithAll<BuiltinMaterialPropertyUnity_MatrixPreviousM>()
                      .WithAll<ChunkMaterialPropertyDirtyMask>(false, true).Build();
        }

        protected override void OnUpdate()
        {
            int matrixPrevIndex = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray()
                                  .IndexOf(ComponentType.ReadOnly<BuiltinMaterialPropertyUnity_MatrixPreviousM>());
            ulong matrixPrevMaterialMaskLower = (ulong)matrixPrevIndex >= 64UL ? 0UL : (1UL << matrixPrevIndex);
            ulong matrixPrevMaterialMaskUpper = (ulong)matrixPrevIndex >= 64UL ? (1UL << (matrixPrevIndex - 64)) : 0UL;

            Dependency = new UpdateMatricesJob
            {
                ltwHandle                   = GetComponentTypeHandle<LocalToWorld>(true),
                cacheHandle                 = GetComponentTypeHandle<MatrixPreviousCache>(false),
                prevHandle                  = GetComponentTypeHandle<BuiltinMaterialPropertyUnity_MatrixPreviousM>(false),
                maskHandle                  = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                secondLastSystemVersion     = m_secondLastSystemVersion,
                matrixPrevMaterialMaskLower = matrixPrevMaterialMaskLower,
                matrixPrevMaterialMaskUpper = matrixPrevMaterialMaskUpper
            }.ScheduleParallel(m_query, Dependency);
            m_secondLastSystemVersion = LastSystemVersion;
        }

        [BurstCompile]
        struct UpdateMatricesJob : IJobEntityBatch
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

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (!batchInChunk.DidChange(ltwHandle, secondLastSystemVersion))
                    return;

                var mask          = batchInChunk.GetChunkComponentData(maskHandle);
                mask.lower.Value |= matrixPrevMaterialMaskLower;
                mask.upper.Value |= matrixPrevMaterialMaskUpper;
                batchInChunk.SetChunkComponentData(maskHandle, mask);

                var ltws   = batchInChunk.GetNativeArray(ltwHandle).Reinterpret<float4x4>();
                var caches = batchInChunk.GetNativeArray(cacheHandle).Reinterpret<float2x4>();
                // Even if this is the first frame that LTW moves, we still dirty prev.
                // Todo: Is there an efficient way to fix this, and is it even worth it?
                var prevs = batchInChunk.GetNativeArray(prevHandle).Reinterpret<float4x4>();

                for (int i = 0; i < batchInChunk.Count; i++)
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

