using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Jobs;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopyGameObjectTransformFromEntitySystem : ISystem
    {
        LatiosWorldUnmanaged                latiosWorld;
        WorldTransformReadOnlyAspect.Lookup transformLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            transformLookup = new WorldTransformReadOnlyAspect.Lookup(ref state);

            state.Fluent().With<GameObjectEntity.ExistComponent>(true).With<CopyTransformFromEntityTag>(true).With<CopyTransformFromEntityCleanupTag>(true)
            .WithWorldTransformReadOnly().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var mapping = latiosWorld.worldBlackboardEntity.GetCollectionComponent<CopyTransformFromEntityMapping>();
            transformLookup.Update(ref state);
            state.Dependency = new Job
            {
                indexToEntityMap = mapping.indexToEntityMap,
                transformLookup  = transformLookup
            }.Schedule(mapping.transformAccessArray, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobParallelForTransform
        {
            [ReadOnly] public NativeHashMap<int, Entity>          indexToEntityMap;
            [ReadOnly] public WorldTransformReadOnlyAspect.Lookup transformLookup;

            public void Execute(int index, TransformAccess transform)
            {
                var entityTransform = transformLookup[indexToEntityMap[index]];
                transform.SetPositionAndRotation(entityTransform.position, entityTransform.rotation);
                transform.localScale = entityTransform.worldTransformQvvs.scale * entityTransform.worldTransformQvvs.stretch;
            }
        }
    }
}

