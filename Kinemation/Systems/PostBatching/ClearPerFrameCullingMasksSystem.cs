using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class ClearPerFrameCullingMasksSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<ChunkPerFrameCullingMask>(false).WithAll<ChunkHeader>(true).Build();
        }

        protected override void OnUpdate()
        {
            Dependency = new ClearJob
            {
                handle            = GetComponentTypeHandle<ChunkPerFrameCullingMask>(false),
                lastSystemVersion = LastSystemVersion
            }.ScheduleParallel(m_metaQuery, Dependency);
        }

        [BurstCompile]
        struct ClearJob : IJobEntityBatch
        {
            public ComponentTypeHandle<ChunkPerFrameCullingMask> handle;
            public uint                                          lastSystemVersion;

            public unsafe void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.DidChange(handle, lastSystemVersion))
                {
                    var ptr = batchInChunk.GetComponentDataPtrRW(ref handle);
                    UnsafeUtility.MemClear(ptr, sizeof(ChunkPerFrameCullingMask) * batchInChunk.Count);
                }
            }
        }
    }
}

