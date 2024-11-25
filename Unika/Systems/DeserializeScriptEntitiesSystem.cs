using Latios.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Unika.Systems
{
    [UpdateInGroup(typeof(PostSyncPointGroup))]
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct DeserializeScriptEntitiesSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<UnikaScripts>(false).WithEnabled<UnikaEntitySerializationController>(false).With<UnikaSerializedEntityReference>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                entityHandle     = GetEntityTypeHandle(),
                scriptsHandle    = GetBufferTypeHandle<UnikaScripts>(false),
                controllerHandle = GetComponentTypeHandle<UnikaEntitySerializationController>(false),
                referencesHandle = GetBufferTypeHandle<UnikaSerializedEntityReference>(true)
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                 entityHandle;
            public BufferTypeHandle<UnikaScripts>                              scriptsHandle;
            public ComponentTypeHandle<UnikaEntitySerializationController>     controllerHandle;
            [ReadOnly] public BufferTypeHandle<UnikaSerializedEntityReference> referencesHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities           = chunk.GetNativeArray(entityHandle);
                var scriptsAccessor    = chunk.GetBufferAccessor(ref scriptsHandle);
                var referencesAccessor = chunk.GetBufferAccessor(ref referencesHandle);
                var controllers        = chunk.GetNativeArray(ref controllerHandle);
                chunk.SetComponentEnabledForAll(ref controllerHandle, false);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var entity     = entities[i];
                    var controller = controllers[i];
                    if (controller.IsOriginalEntity(entity))
                        continue;

                    controllers[i] = new UnikaEntitySerializationController
                    {
                        originalIndex   = entity.Index,
                        originalVersion = entity.Version
                    };
                    var scripts    = scriptsAccessor[i].AsNativeArray();
                    var references = referencesAccessor[i].AsNativeArray();
                    ScriptSerialization.DeserializeEntities(ref scripts, in references);
                }
            }
        }
    }
}

