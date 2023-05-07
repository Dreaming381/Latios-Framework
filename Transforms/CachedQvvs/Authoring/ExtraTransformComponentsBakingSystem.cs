#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System.Collections.Generic;
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Authoring.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    [UpdateBefore(typeof(TransformBakingSystem))]
    [UpdateAfter(typeof(UserPreTransformsBakingSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct ExtraTransformComponentsBakingSystem : ISystem
    {
        EntityQuery m_addPreviousQuery;
        EntityQuery m_removePreviousQuery;
        EntityQuery m_addTwoAgoQuery;
        EntityQuery m_removeTwoAgoQuery;
        EntityQuery m_addIdentityQuery;
        EntityQuery m_removeIdentityQuery;

        static List<ComponentType> s_previousRequestTypes = null;
        static List<ComponentType> s_twoAgoRequestTypes   = null;
        static List<ComponentType> s_identityRequestTypes = null;

        public void OnCreate(ref SystemState state)
        {
            if (s_previousRequestTypes == null)
            {
                var previousType       = typeof(IRequestPreviousTransform);
                var twoAgoType         = typeof(IRequestTwoAgoTransform);
                var identityType       = typeof(IRequestCopyParentTransform);
                s_previousRequestTypes = new List<ComponentType>();
                s_twoAgoRequestTypes   = new List<ComponentType>();
                s_identityRequestTypes = new List<ComponentType>();

                foreach (var type in TypeManager.AllTypes)
                {
                    if (!type.BakingOnlyType)
                        continue;
                    if (previousType.IsAssignableFrom(type.Type))
                    {
                        s_previousRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                    else if (twoAgoType.IsAssignableFrom(type.Type))
                    {
                        s_twoAgoRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                    else if (identityType.IsAssignableFrom(type.Type))
                    {
                        s_identityRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                }
            }

            var ssrt  = s_previousRequestTypes.ToNativeList(Allocator.Temp);
            var pssrt = s_twoAgoRequestTypes.ToNativeList(Allocator.Temp);
            var irt   = s_identityRequestTypes.ToNativeList(Allocator.Temp);

            m_addPreviousQuery = new EntityQueryBuilder(Allocator.Temp).WithAny(ref ssrt).WithNone<PreviousTransform>().WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            m_removePreviousQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<PreviousTransform>().WithNone(ref ssrt).WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            m_addTwoAgoQuery = new EntityQueryBuilder(Allocator.Temp).WithAny(ref pssrt).WithNone<TwoAgoTransform>().WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            m_removeTwoAgoQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<TwoAgoTransform>().WithNone(ref pssrt).WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            m_addIdentityQuery = new EntityQueryBuilder(Allocator.Temp).WithAny(ref irt).WithNone<CopyParentWorldTransformTag>().WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            m_removeIdentityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<CopyParentWorldTransformTag>().WithNone(ref irt).WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            state.EntityManager.AddComponent<PreviousTransform>(m_addPreviousQuery);
            state.EntityManager.RemoveComponent<PreviousTransform>(m_removePreviousQuery);
            state.EntityManager.AddComponent<TwoAgoTransform>(m_addTwoAgoQuery);
            state.EntityManager.RemoveComponent<TwoAgoTransform>(m_removeTwoAgoQuery);
            state.EntityManager.AddComponent<CopyParentWorldTransformTag>(m_addIdentityQuery);
            state.EntityManager.RemoveComponent<CopyParentWorldTransformTag>(m_removeIdentityQuery);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}
#endif

