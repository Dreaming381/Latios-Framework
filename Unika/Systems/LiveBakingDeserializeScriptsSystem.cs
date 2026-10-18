using Latios.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Unika.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(AfterLiveBakingSuperSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LiveBakingDeserializeScriptsSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<UnikaLiveBakedScriptsState>(false).With<UnikaScripts>(false).With<UnikaSerializedEntityReference>(true)
                      .IncludePrefabs().IncludeDisabledEntities().Build();
            m_query.AddChangedVersionFilter(ComponentType.ReadWrite<UnikaLiveBakedScriptsState>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api = this.GetApi(ref state);
            m_query.SetOverrideChangeFilterVersion(api.worldBlackboardEntity.GetComponentData<SystemVersionBeforeLiveBake>().version);
            state.Dependency = new Job().Inject(api).ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IInjectable
        {
            [Inject] ComponentTypeHandle<UnikaLiveBakedScriptsState>            liveStateHandle;
            [Inject] BufferTypeHandle<UnikaScripts>                             scriptsHandle;
            [Inject, ReadOnly] BufferTypeHandle<UnikaSerializedEntityReference> entityRefsHandle;
            [Inject, ReadOnly] BufferTypeHandle<UnikaSerializedBlobReference>   blobRefsHandle;
            [Inject, ReadOnly] BufferTypeHandle<UnikaSerializedAssetReference>  assetRefsHandle;
            [Inject, ReadOnly] BufferTypeHandle<UnikaSerializedObjectReference> objectRefsHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var liveStates       = chunk.GetNativeArray(ref liveStateHandle);
                var scriptsAccessor  = chunk.GetBufferAccessor(ref scriptsHandle);
                var entityRefsAccess = chunk.GetBufferAccessor(ref entityRefsHandle);
                var blobRefsAccess   = chunk.GetBufferAccessor(ref blobRefsHandle);
                var assetRefsAccess  = chunk.GetBufferAccessor(ref assetRefsHandle);
                var objectRefsAccess = chunk.GetBufferAccessor(ref objectRefsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var liveState = liveStates[i];
                    if (!liveState.deserializationRequested)
                        continue;

                    var scripts = scriptsAccessor[i].AsNativeArray();
                    ScriptSerialization.DeserializeEntities(ref scripts, entityRefsAccess[i].AsNativeArray());
                    if (blobRefsAccess.Length > 0)
                        ScriptSerialization.DeserializeBlobs(ref scripts, blobRefsAccess[i].AsNativeArray());
                    if (assetRefsAccess.Length > 0)
                        ScriptSerialization.DeserializeAssets(ref scripts, assetRefsAccess[i].AsNativeArray());
                    if (objectRefsAccess.Length > 0)
                        ScriptSerialization.DeserializeObjects(ref scripts, objectRefsAccess[i].AsNativeArray());

                    liveState.deserializationRequested = false;
                    liveStates[i]                      = liveState;
                }
            }
        }
    }
}

