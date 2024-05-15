using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class GatherSkeletonBindingPathsFromShadowHierarchySystem : SystemBase
    {
        Queue<(UnityEngine.Transform, int)> m_breadthQueue;

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<(UnityEngine.Transform, int)>();

            var previousHierarchyCopyMap = new NativeHashMap<UnityObjectRef<UnityEngine.GameObject>, Entity>(128, Allocator.TempJob);

            CompleteDependency();
            foreach ((var boneNames, var shadowRef, var entity) in SystemAPI.Query<DynamicBuffer<SkeletonBoneNameInHierarchy>, ShadowHierarchyReference>()
                     .WithEntityAccess().WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab))
            {
                if (previousHierarchyCopyMap.TryGetValue(shadowRef.shadowHierarchyRoot, out var otherEntity))
                {
                    boneNames.Clear();
                    boneNames.AddRange(SystemAPI.GetBuffer<SkeletonBoneNameInHierarchy>(otherEntity).AsNativeArray());
                    continue;
                }

                m_breadthQueue.Clear();

                m_breadthQueue.Enqueue((shadowRef.shadowHierarchyRoot.Value.transform, -1));

                int currentIndex = 0;
                while (m_breadthQueue.Count > 0)
                {
                    var (bone, parentIndex) = m_breadthQueue.Dequeue();
                    if (currentIndex > 0)
                    {
                        boneNames.Add(new SkeletonBoneNameInHierarchy { parentIndex = parentIndex, boneName = bone.gameObject.name });
                    }

                    for (int i = 0; i < bone.childCount; i++)
                    {
                        var child = bone.GetChild(i);
                        m_breadthQueue.Enqueue((child, currentIndex));
                    }
                    currentIndex++;
                }
                previousHierarchyCopyMap.Add(shadowRef.shadowHierarchyRoot, entity);
            }
            m_breadthQueue.Clear();

            previousHierarchyCopyMap.Dispose();
        }
    }
}

