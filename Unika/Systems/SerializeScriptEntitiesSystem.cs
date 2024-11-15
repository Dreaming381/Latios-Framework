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
    [UpdateInGroup(typeof(PreSyncPointGroup), OrderLast = true)]
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct SerializeScriptEntitiesSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<UnikaScripts>(true).WithEnabled<UnikaEntitySerializationController>(false).With<UnikaSerializedEntityReference>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                entityHandle     = GetEntityTypeHandle(),
                scriptsHandle    = GetBufferTypeHandle<UnikaScripts>(true),
                controllerHandle = GetComponentTypeHandle<UnikaEntitySerializationController>(false),
                referencesHandle = GetBufferTypeHandle<UnikaSerializedEntityReference>(false)
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                             entityHandle;
            [ReadOnly] public BufferTypeHandle<UnikaScripts>               scriptsHandle;
            public ComponentTypeHandle<UnikaEntitySerializationController> controllerHandle;
            public BufferTypeHandle<UnikaSerializedEntityReference>        referencesHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities           = chunk.GetNativeArray(entityHandle);
                var scriptsAccessor    = chunk.GetBufferAccessor(ref scriptsHandle);
                var referencesAccessor = chunk.GetBufferAccessor(ref referencesHandle);
                var controllers        = chunk.GetNativeArray(ref controllerHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var entity     = entities[i];
                    controllers[i] = new UnikaEntitySerializationController
                    {
                        originalIndex   = entity.Index,
                        originalVersion = entity.Version
                    };
                    var scripts    = scriptsAccessor[i].AsNativeArray();
                    var references = referencesAccessor[i];
                    ScriptSerialization.SerializeEntities(in scripts, ref references);
                }
            }
        }
    }
}

