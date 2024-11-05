using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Unika
{
    [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct SubsceneLoadDeserializeScriptsSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            AssemblyManager.Initialize();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
            state.CompleteDependency();

            var query = SystemAPI.QueryBuilder().WithAll<UnikaScripts>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build();
            var types = new ComponentTypeSet(ComponentType.ReadWrite<UnikaSerializedBlobReference>(),
                                             ComponentType.ReadWrite<UnikaSerializedAssetReference>(),
                                             ComponentType.ReadWrite<UnikaSerializedObjectReference>(),
                                             ComponentType.ReadWrite<UnikaSerializedTypeIds>());
            state.EntityManager.RemoveComponent(query, types);
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
            public void Execute(ref DynamicBuffer<UnikaScripts>                  scriptsBuffer,
                                in DynamicBuffer<UnikaSerializedBlobReference>   blobs,
                                in DynamicBuffer<UnikaSerializedAssetReference>  assets,
                                in DynamicBuffer<UnikaSerializedObjectReference> objects,
                                in UnikaSerializedTypeIds typeIds)
            {
                var scripts = scriptsBuffer.AsNativeArray();
                ScriptSerialization.DeserializeBlobs( ref scripts, blobs.AsNativeArray());
                ScriptSerialization.DeserializeAssets( ref scripts, assets.AsNativeArray());
                ScriptSerialization.DeserializeObjects( ref scripts, objects.AsNativeArray());
                ScriptSerialization.DeserializeScriptTypes(ref scripts, in typeIds);
            }
        }
    }
}

