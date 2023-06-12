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
        EntityQuery m_query;

        protected override void OnUpdate()
        {
            int hashmapCapacity = m_query.CalculateEntityCountWithoutFiltering();
            var hashset         = new NativeHashSet<int>(hashmapCapacity, Allocator.TempJob);
            var list            = new NativeList<ShadowHierarchyReference>(hashmapCapacity, Allocator.TempJob);

            CompleteDependency();

            Entities.ForEach((in ShadowHierarchyReference reference) =>
            {
                if (hashset.Add(reference.shadowHierarchyRoot.GetHashCode()))
                    list.Add(in reference);
            }).WithStoreEntityQueryInField(ref m_query).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

            foreach (var reference in list)
            {
                UnityEngine.GameObject shadow = reference.shadowHierarchyRoot;
                reference.keepAliveHandle.Free();
                UnityEngine.Object.DestroyImmediate(reference.shadowHierarchyRoot);
            }

            hashset.Dispose();
            list.Dispose();
        }
    }
}

