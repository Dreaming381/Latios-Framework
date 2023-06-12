using System.Collections.Generic;
using Latios.Transforms;
using Latios.Transforms.Authoring;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class BuildOptimizedBoneTransformsSystem : SystemBase
    {
        Queue<(UnityEngine.Transform, int)> m_breadthQueue;

        protected override void OnUpdate()
        {
            if (m_breadthQueue == null)
                m_breadthQueue = new Queue<(UnityEngine.Transform, int)>();

            Entities.ForEach((ref DynamicBuffer<OptimizedBoneTransform> transformBuffer, in ShadowHierarchyReference shadowRef) =>
            {
                m_breadthQueue.Clear();

                m_breadthQueue.Enqueue((shadowRef.shadowHierarchyRoot.Value.transform, -1));

                while (m_breadthQueue.Count > 0)
                {
                    var (bone, parentIndex) = m_breadthQueue.Dequeue();
                    int currentIndex        = transformBuffer.Length;
                    TransformBakeUtils.GetScaleAndStretch(bone.localScale, out var scale, out var stretch);
                    transformBuffer.Add(new OptimizedBoneTransform
                    {
                        boneTransform = new Transforms.TransformQvvs
                        {
                            rotation   = bone.localRotation,
                            position   = bone.localPosition,
                            worldIndex = parentIndex,
                            stretch    = stretch,
                            scale      = scale
                        }
                    });

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

