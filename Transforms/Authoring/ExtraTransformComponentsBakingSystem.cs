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
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct ExtraTransformComponentsBakingSystem : ISystem
    {
        EntityQuery m_addTickStartingQuery;
        EntityQuery m_removeTickStartingQuery;
        EntityQuery m_addPreviousTickStartingQuery;
        EntityQuery m_removePreviousTickStartingQuery;
        EntityQuery m_addIdentityQuery;
        EntityQuery m_removeIdentityQuery;

        static List<ComponentType> s_tickStartingRequestTypes         = null;
        static List<ComponentType> s_previousTickStartingRequestTypes = null;
        static List<ComponentType> s_identityRequestTypes             = null;

        public void OnCreate(ref SystemState state)
        {
            if (s_tickStartingRequestTypes == null)
            {
                var tickStartingType               = typeof(IRequestTickStartingTransform);
                var previousTickStartingType       = typeof(IRequestPreviousTickStartingTransform);
                var identityType                   = typeof(IRequestCopyParentTransform);
                s_tickStartingRequestTypes         = new List<ComponentType>();
                s_previousTickStartingRequestTypes = new List<ComponentType>();
                s_identityRequestTypes             = new List<ComponentType>();

                foreach (var type in TypeManager.AllTypes)
                {
                    if (!type.BakingOnlyType)
                        continue;
                    if (tickStartingType.IsAssignableFrom(type.Type))
                    {
                        s_tickStartingRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                    else if (previousTickStartingType.IsAssignableFrom(type.Type))
                    {
                        s_previousTickStartingRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                    else if (identityType.IsAssignableFrom(type.Type))
                    {
                        s_identityRequestTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                }
            }

            var ssrt  = s_tickStartingRequestTypes.ToNativeList(Allocator.Temp);
            var pssrt = s_previousTickStartingRequestTypes.ToNativeList(Allocator.Temp);
            var irt   = s_identityRequestTypes.ToNativeList(Allocator.Temp);

            m_addTickStartingQuery            = new EntityQueryBuilder(Allocator.Temp).WithAny(ref ssrt).WithNone<TickStartingTransform>().Build(ref state);
            m_removeTickStartingQuery         = new EntityQueryBuilder(Allocator.Temp).WithAll<TickStartingTransform>().WithNone(ref ssrt).Build(ref state);
            m_addPreviousTickStartingQuery    = new EntityQueryBuilder(Allocator.Temp).WithAny(ref pssrt).WithNone<PreviousTickStartingTransform>().Build(ref state);
            m_removePreviousTickStartingQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<PreviousTickStartingTransform>().WithNone(ref pssrt).Build(ref state);
            m_addIdentityQuery                =
                new EntityQueryBuilder(Allocator.Temp).WithAny(ref irt).WithNone<CopyParentWorldTransformTag>().Build(ref state);
            m_removeIdentityQuery =
                new EntityQueryBuilder(Allocator.Temp).WithAll<CopyParentWorldTransformTag>().WithNone(ref irt).Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            state.EntityManager.AddComponent<TickStartingTransform>(m_addTickStartingQuery);
            state.EntityManager.RemoveComponent<TickStartingTransform>(m_removeTickStartingQuery);
            state.EntityManager.AddComponent<PreviousTickStartingTransform>(m_addPreviousTickStartingQuery);
            state.EntityManager.RemoveComponent<PreviousTickStartingTransform>(m_removePreviousTickStartingQuery);
            state.EntityManager.AddComponent<CopyParentWorldTransformTag>(m_addIdentityQuery);
            state.EntityManager.RemoveComponent<CopyParentWorldTransformTag>(m_removeIdentityQuery);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

