using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring.Systems
{
    // This system is the old 0.5.x behavior. The results are usually identical to its replacement BuildOptimizedBoneToRootBufferSystem.
    // This system is currently not used.
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class BuildOptimizedBoneToRootSystem2 : SystemBase
    {
        Queue<(UnityEngine.Transform, int)> m_breadthQueue;

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<(UnityEngine.Transform, int)>();

            Entities.ForEach((ref DynamicBuffer<OptimizedBoneToRoot> btrBuffer, in ShadowHierarchyReference shadowRef) =>
            {
                m_breadthQueue.Clear();

                m_breadthQueue.Enqueue((shadowRef.shadowHierarchyRoot.Value.transform, -1));

                while (m_breadthQueue.Count > 0)
                {
                    var (bone, parentIndex) = m_breadthQueue.Dequeue();
                    int currentIndex        = btrBuffer.Length;
                    var ltp                 = float4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale);
                    if (parentIndex < 0)
                        btrBuffer.Add(new OptimizedBoneToRoot { boneToRoot = ltp });
                    else
                        btrBuffer.Add(new OptimizedBoneToRoot { boneToRoot = math.mul(btrBuffer[parentIndex].boneToRoot, ltp) });

                    for (int i = 0; i < bone.childCount; i++)
                    {
                        var child = bone.GetChild(i);
                        m_breadthQueue.Enqueue((child, currentIndex));
                    }
                }
            }).WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab).WithoutBurst().Run();
            m_breadthQueue.Clear();
        }
    }
}

