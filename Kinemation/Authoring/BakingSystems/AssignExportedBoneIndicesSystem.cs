using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class AssignExportedBoneIndicesSystem : SystemBase
    {
        Queue<UnityEngine.Transform> m_breadthQueue;

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<UnityEngine.Transform>();

            Entities.ForEach((ref DynamicBuffer<OptimizedSkeletonExportedBone> exportedBones, in DynamicBuffer<ExportedBoneGameObjectRef> gameObjectRefs,
                              in ShadowHierarchyReference shadowRef) =>
            {
                m_breadthQueue.Clear();

                m_breadthQueue.Enqueue(shadowRef.shadowHierarchyRoot.Value.transform);

                int currentIndex = 0;
                while (m_breadthQueue.Count > 0)
                {
                    var bone = m_breadthQueue.Dequeue();
                    var link = bone.GetComponent<HideThis.ShadowCloneTracker>();
                    if (link != null)
                    {
                        var id = link.source.gameObject.GetInstanceID();

                        int i = 0;
                        foreach (var go in gameObjectRefs)
                        {
                            if (go.authoringGameObjectForBone.GetHashCode() == id)
                            {
                                exportedBones.ElementAt(i).boneIndex = currentIndex;
                                break;
                            }
                            i++;
                        }
                    }
                    currentIndex++;

                    for (int i = 0; i < bone.childCount; i++)
                    {
                        var child = bone.GetChild(i);
                        m_breadthQueue.Enqueue(child);
                    }
                }
            }).WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab).WithoutBurst().Run();
            m_breadthQueue.Clear();
        }
    }
}

