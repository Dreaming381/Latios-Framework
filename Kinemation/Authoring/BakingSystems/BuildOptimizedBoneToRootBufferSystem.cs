using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class BuildOptimizedBoneToRootBufferSystem : SystemBase
    {
        Queue<UnityEngine.Transform> m_breadthQueue;

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<UnityEngine.Transform>();

            Entities.ForEach((Entity entity, ref DynamicBuffer<OptimizedBoneToRoot> btrs, in ShadowHierarchyReference shadowRef) =>
            {
                m_breadthQueue.Clear();
                btrs.Clear();
                var matrices = btrs.Reinterpret<float4x4>();

                var      root        = shadowRef.shadowHierarchyRoot.Value;
                float4x4 worldToRoot = root.transform.worldToLocalMatrix.inverse;
                m_breadthQueue.Enqueue(root.transform);

                while (m_breadthQueue.Count > 0)
                {
                    var bone = m_breadthQueue.Dequeue();
                    matrices.Add(math.mul(worldToRoot, bone.localToWorldMatrix));

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

