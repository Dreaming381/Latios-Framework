using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TRSToLocalToParentSystem))]
    [UpdateBefore(typeof(TRSToLocalToWorldSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopyTransformFromBoneSystem : ISystem
    {
        EntityQuery m_query;

        ComponentTypeHandle<CopyLocalToParentFromBone>   m_fromBoneHandle;
        ComponentTypeHandle<BoneOwningSkeletonReference> m_skeletonHandle;
        ComponentTypeHandle<LocalToParent>               m_ltpHandle;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAll<LocalToParent>(false).WithAll<CopyLocalToParentFromBone>(true).WithAll<BoneOwningSkeletonReference>(true).Build();

            m_fromBoneHandle = state.GetComponentTypeHandle<CopyLocalToParentFromBone>(true);
            m_skeletonHandle = state.GetComponentTypeHandle<BoneOwningSkeletonReference>(true);
            m_ltpHandle      = state.GetComponentTypeHandle<LocalToParent>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_fromBoneHandle.Update(ref state);
            m_skeletonHandle.Update(ref state);
            m_ltpHandle.Update(ref state);

            state.Dependency = new CopyFromBoneJob
            {
                fromBoneHandle    = m_fromBoneHandle,
                skeletonHandle    = m_skeletonHandle,
                btrLookup         = GetBufferLookup<OptimizedBoneToRoot>(true),
                ltpHandle         = m_ltpHandle,
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state) {
        }

        [BurstCompile]
        struct CopyFromBoneJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<CopyLocalToParentFromBone>   fromBoneHandle;
            [ReadOnly] public ComponentTypeHandle<BoneOwningSkeletonReference> skeletonHandle;
            [ReadOnly] public BufferLookup<OptimizedBoneToRoot>                btrLookup;
            public ComponentTypeHandle<LocalToParent>                          ltpHandle;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var skeletons = chunk.GetNativeArray(skeletonHandle);

                if (!chunk.DidChange(fromBoneHandle, lastSystemVersion) && !chunk.DidChange(skeletonHandle, lastSystemVersion))
                {
                    bool needsCopy = false;
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        if (btrLookup.DidChange(skeletons[i].skeletonRoot, lastSystemVersion))
                        {
                            needsCopy = true;
                            break;;
                        }
                    }

                    if (!needsCopy)
                        return;
                }

                var bones = chunk.GetNativeArray(fromBoneHandle);
                var ltps  = chunk.GetNativeArray(ltpHandle).Reinterpret<float4x4>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    var buffer = btrLookup[skeletons[i].skeletonRoot];
                    ltps[i]    = buffer[bones[i].boneIndex].boneToRoot;
                }
            }
        }
    }
}

