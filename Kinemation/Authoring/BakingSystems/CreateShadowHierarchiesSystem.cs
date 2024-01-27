using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class CreateShadowHierarchiesSystem : SystemBase
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = CheckedStateRef.Fluent().With<ShadowHierarchyRequest>(true).IncludePrefabs().IncludeDisabledEntities().Build();
        }

        protected override void OnUpdate()
        {
            int hashmapCapacity = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap         = new NativeHashMap<ShadowHierarchyRequest, ShadowHierarchyReference>(hashmapCapacity, Allocator.TempJob);
            var list            = new NativeList<ShadowHierarchyRequest>(hashmapCapacity, Allocator.TempJob);

            CompleteDependency();

            new AddToMapJob { hashmap = hashmap, list = list }.Run();

            foreach (var request in list)
            {
                var sourceAnimator = request.animatorToBuildShadowFor.Value;
                var shadow         = ShadowHierarchyBuilder.BuildShadowHierarchy(sourceAnimator.gameObject, !sourceAnimator.hasTransformHierarchy);
                hashmap[request]   = new ShadowHierarchyReference
                {
                    shadowHierarchyRoot = shadow,
                    keepAliveHandle     = GCHandle.Alloc(shadow, GCHandleType.Normal)
                };
            }

            EntityManager.AddComponent<ShadowHierarchyReference>(m_query);

            new ApplyJob { hashmap = hashmap }.Run();

            hashmap.Dispose();
            list.Dispose();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct AddToMapJob : IJobEntity
        {
            public NativeHashMap<ShadowHierarchyRequest, ShadowHierarchyReference> hashmap;
            public NativeList<ShadowHierarchyRequest>                              list;

            public void Execute(in ShadowHierarchyRequest request)
            {
                if (hashmap.TryAdd(request, default))
                    list.Add(request);
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct ApplyJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<ShadowHierarchyRequest, ShadowHierarchyReference> hashmap;

            public void Execute(ref ShadowHierarchyReference reference, in ShadowHierarchyRequest request)
            {
                reference = hashmap[request];
            }
        }
    }
}

