using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    [UpdateBefore(typeof(TRSToLocalToWorldSystem))]
    [DisableAutoCreation]
    public partial class CopyTransformFromBoneSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<LocalToParent>(false).WithAll<CopyLocalToParentFromBone>(true).WithAll<BoneOwningSkeletonReference>(true).Build();
        }

        protected override void OnUpdate()
        {
            Dependency = new CopyFromBoneJob
            {
                fromBoneHandle    = GetComponentTypeHandle<CopyLocalToParentFromBone>(true),
                skeletonHandle    = GetComponentTypeHandle<BoneOwningSkeletonReference>(true),
                btrBfe            = GetBufferFromEntity<OptimizedBoneToRoot>(true),
                ltpHandle         = GetComponentTypeHandle<LocalToParent>(false),
                lastSystemVersion = LastSystemVersion
            }.ScheduleParallel(m_query, Dependency);
        }

        [BurstCompile]
        struct CopyFromBoneJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<CopyLocalToParentFromBone>   fromBoneHandle;
            [ReadOnly] public ComponentTypeHandle<BoneOwningSkeletonReference> skeletonHandle;
            [ReadOnly] public BufferFromEntity<OptimizedBoneToRoot>            btrBfe;
            public ComponentTypeHandle<LocalToParent>                          ltpHandle;

            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var skeletons = batchInChunk.GetNativeArray(skeletonHandle);

                if (!batchInChunk.DidChange(fromBoneHandle, lastSystemVersion) && !batchInChunk.DidChange(skeletonHandle, lastSystemVersion))
                {
                    bool needsCopy = false;
                    for (int i = 0; i < batchInChunk.Count; i++)
                    {
                        if (btrBfe.DidChange(skeletons[i].skeletonRoot, lastSystemVersion))
                        {
                            needsCopy = true;
                            break;;
                        }
                    }

                    if (!needsCopy)
                        return;
                }

                var bones = batchInChunk.GetNativeArray(fromBoneHandle);
                var ltps  = batchInChunk.GetNativeArray(ltpHandle).Reinterpret<float4x4>();

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var buffer = btrBfe[skeletons[i].skeletonRoot];
                    ltps[i]    = buffer[bones[i].boneIndex].boneToRoot;
                }
            }
        }
    }
}

