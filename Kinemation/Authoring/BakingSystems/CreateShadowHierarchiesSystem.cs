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

        protected override void OnUpdate()
        {
            int hashmapCapacity = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap         = new NativeHashMap<ShadowHierarchyRequest, ShadowHierarchyReference>(hashmapCapacity, Allocator.TempJob);
            var list            = new NativeList<ShadowHierarchyRequest>(hashmapCapacity, Allocator.TempJob);

            CompleteDependency();

            Entities.ForEach((in ShadowHierarchyRequest request) =>
            {
                if (hashmap.TryAdd(request, default))
                    list.Add(request);
            }).WithStoreEntityQueryInField(ref m_query).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

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

            Entities.ForEach((ref ShadowHierarchyReference reference, in ShadowHierarchyRequest request) =>
            {
                reference = hashmap[request];
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

            hashmap.Dispose();
            list.Dispose();
        }
    }
}

