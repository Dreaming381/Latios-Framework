using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SetupSocketsSystem : ISystem
    {
        LocalTransformQvvsReadWriteAspect.Lookup m_localTransformLookup;
        ParentReadOnlyAspect.Lookup              m_parentROLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_localTransformLookup = new LocalTransformQvvsReadWriteAspect.Lookup(ref state);
            m_parentROLookup       = new ParentReadOnlyAspect.Lookup(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_localTransformLookup.Update(ref state);
            m_parentROLookup.Update(ref state);

            var componentsToAdd = new ComponentTypeSet(ComponentType.ReadWrite<Socket>(),
                                                       ComponentType.ReadWrite<BoneOwningSkeletonReference>());

            new ClearJob().ScheduleParallel();

            var ecbAdd = new EntityCommandBuffer(state.WorldUpdateAllocator);
            new ApplySkeletonsToImportedSocketsJob
            {
                componentTypesToAdd      = componentsToAdd,
                ecb                      = ecbAdd.AsParallelWriter(),
                skeletonReferenceLookup  = GetComponentLookup<BoneOwningSkeletonReference>(false),
                socketLookup             = GetComponentLookup<Socket>(false),
                parentLookup             = m_parentROLookup,
                transformAuthoringLookup = GetComponentLookup<TransformAuthoring>(true),
                localTransformLookup     = m_localTransformLookup
            }.ScheduleParallel();
            new ApplySkeletonsToAuthoredSocketsJob
            {
                skeletonLookup = GetBufferLookup<OptimizedBoneTransform>(true)
            }.ScheduleParallel();

            var ecbRemove                                  = new EntityCommandBuffer(state.WorldUpdateAllocator);
            new RemoveDisconnectedImportedSocketsJob { ecb = ecbRemove.AsParallelWriter(), componentTypesToRemove = componentsToAdd }.ScheduleParallel();

            state.CompleteDependency();

            ecbAdd.Playback(state.EntityManager);
            ecbRemove.Playback(state.EntityManager);
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(Socket))]
        [WithNone(typeof(AuthoredSocket))]
        [BurstCompile]
        partial struct ClearJob : IJobEntity
        {
            public void Execute(ref BoneOwningSkeletonReference boneReference)
            {
                boneReference.skeletonRoot = Entity.Null;
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ApplySkeletonsToImportedSocketsJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public LocalTransformQvvsReadWriteAspect.Lookup     localTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Socket>                      socketLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            [ReadOnly] public ParentReadOnlyAspect.Lookup                                             parentLookup;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;

            [ReadOnly] public ComponentLookup<TransformAuthoring> transformAuthoringLookup;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref DynamicBuffer<ImportedSocket> importedSockets,
                                in DynamicBuffer<OptimizedBoneTransform> boneTransforms)
            {
                for (int i = 0; i < importedSockets.Length; i++)
                {
                    if (importedSockets[i].boneIndex == 0)
                    {
                        // If the socket is still parented to the root, it is not actually an imported socket.
                        importedSockets.RemoveAt(i);
                        i--;
                    }
                }

                foreach (var socket in importedSockets)
                {
                    var localTransformAspect            = localTransformLookup[socket.boneEntity];
                    localTransformAspect.localTransform = ComputeRootTransformOfBone(socket.boneIndex, in boneTransforms);
                    if (socketLookup.HasComponent(socket.boneEntity))
                    {
                        skeletonReferenceLookup[socket.boneEntity] = new BoneOwningSkeletonReference { skeletonRoot = entity };
                        socketLookup[socket.boneEntity]                                                             = new Socket { boneIndex = (short)socket.boneIndex };
                    }
                    else
                    {
                        ecb.AddComponent(chunkIndexInQuery, socket.boneEntity, componentTypesToAdd);
                        ecb.SetComponent(chunkIndexInQuery, socket.boneEntity, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(chunkIndexInQuery, socket.boneEntity, new Socket { boneIndex                         = (short)socket.boneIndex });
                    }
                }
            }
        }

        [WithAll(typeof(AuthoredSocket))]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct ApplySkeletonsToAuthoredSocketsJob : IJobEntity
        {
            [ReadOnly] public BufferLookup<OptimizedBoneTransform> skeletonLookup;

            public void Execute(LocalTransformQvvsReadWriteAspect localTransform, ref Socket socket, in BoneOwningSkeletonReference skeletonReference)
            {
                if (!skeletonLookup.TryGetBuffer(skeletonReference.skeletonRoot, out var bones))
                {
                    UnityEngine.Debug.LogError($"A socket targets {skeletonReference.skeletonRoot.entity.ToFixedString()} which does not have an OptimizedBoneTransform buffer.");
                    return;
                }
                if (bones.Length <= socket.boneIndex)
                {
                    UnityEngine.Debug.LogError(
                        $"A socket targets index {socket.boneIndex} of skeleton {skeletonReference.skeletonRoot.entity.ToFixedString()} which has only {bones.Length} bones.");
                    socket.boneIndex = 0;
                }
                localTransform.localTransform = ComputeRootTransformOfBone(socket.boneIndex, in bones);
            }
        }

        static TransformQvvs ComputeRootTransformOfBone(int index, in DynamicBuffer<OptimizedBoneTransform> transforms)
        {
            var result = transforms[index].boneTransform;
            var parent = result.worldIndex;
            while (parent > 0)
            {
                var parentTransform = transforms[parent].boneTransform;
                parent              = parentTransform.worldIndex;
                result              = qvvs.mul(parentTransform, result);
            }
            return result;
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [WithAll(typeof(Socket))]
        [WithNone(typeof(AuthoredSocket))]
        [BurstCompile]
        partial struct RemoveDisconnectedImportedSocketsJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter ecb;
            public ComponentTypeSet                   componentTypesToRemove;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref BoneOwningSkeletonReference skeletonReference)
            {
                if (skeletonReference.skeletonRoot == Entity.Null)
                    ecb.RemoveComponent(chunkIndexInQuery, entity, componentTypesToRemove);
            }
        }
    }
}

