using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class DestroyShadowHierarchiesSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var query = SystemAPI.QueryBuilder().WithAll<ShadowHierarchyReference>().WithOptions(
                EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build();
            int hashmapCapacity = query.CalculateEntityCountWithoutFiltering();
            var hashset         = new NativeHashSet<int>(hashmapCapacity, WorldUpdateAllocator);
            var list            = new NativeList<ShadowHierarchyReference>(hashmapCapacity, WorldUpdateAllocator);

            CompleteDependency();

            // Todo: Burst this?
            foreach (var reference in SystemAPI.Query<ShadowHierarchyReference>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                if (hashset.Add(reference.shadowHierarchyRoot.GetHashCode()))
                    list.Add(in reference);
            }

            foreach (var reference in list)
            {
                UnityEngine.GameObject shadow = reference.shadowHierarchyRoot;
                reference.keepAliveHandle.Free();
                UnityEngine.Object.DestroyImmediate(reference.shadowHierarchyRoot);
            }
        }
    }
}

