using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopyGameObjectTransformToEntitySystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        TransformAspect.Lookup transformLookup;
#endif

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            transformLookup = new TransformAspect.Lookup(ref state);
#endif
            state.Fluent().With<GameObjectEntity.ExistComponent>(true).With<CopyTransformToEntity>(true).With<CopyTransformToEntityCleanupTag>(true)
            .WithWorldTransformReadOnly().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var mapping = latiosWorld.worldBlackboardEntity.GetCollectionComponent<CopyTransformToEntityMapping>();
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            transformLookup.Update(ref state);
#endif
            state.Dependency = new Job
            {
                indexToEntityMap = mapping.indexToEntityMap,
                copyLookup       = GetComponentLookup<CopyTransformToEntity>(true),
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                transformLookup = transformLookup,
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                ltwLookup = GetComponentLookup<LocalToWorld>(false),
#endif
            }.Schedule(mapping.transformAccessArray, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobParallelForTransform
        {
            [ReadOnly] public NativeHashMap<int, Entity>             indexToEntityMap;
            [ReadOnly] public ComponentLookup<CopyTransformToEntity> copyLookup;
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            [NativeDisableParallelForRestriction] public TransformAspect.Lookup transformLookup;

            public void Execute(int index, TransformAccess transform)
            {
                var entity          = indexToEntityMap[index];
                var entityTransform = transformLookup[entity];
                transform.GetPositionAndRotation(out var position, out var rotation);
                if (copyLookup[entity].useUniformScale)
                    entityTransform.worldTransform = new TransformQvvs(position,
                                                                       rotation,
                                                                       math.cmax(((float4x4)transform.localToWorldMatrix).Scale()),
                                                                       entityTransform.stretch,
                                                                       entityTransform.worldIndex);
                else
                    entityTransform.worldTransform = new TransformQvvs(position, rotation, entityTransform.worldScale, transform.localScale, entityTransform.worldIndex);
            }
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            public ComponentLookup<LocalToWorld> ltwLookup;

            public void Execute(int index, TransformAccess transform)
            {
                var entity    = indexToEntityMap[index];
                ref var entityLtw = ref ltwLookup.GetRefRW(entity).ValueRW;

                if (copyLookup[entity].useUniformScale)
                {
                    transform.GetPositionAndRotation(out var position, out var rotation);
                    entityLtw.Value = float4x4.TRS(position, rotation, math.cmax(((float4x4)transform.localToWorldMatrix).Scale()));
                }
                else
                    entityLtw.Value = transform.localToWorldMatrix;
            }
#endif
        }
    }
}

