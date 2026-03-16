using Latios.Transforms;
using Latios.Transforms.Abstract;
using Latios.Transforms.Authoring;
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
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var componentsToAdd = new ComponentTypeSet(ComponentType.ReadWrite<Socket>(),
                                                       ComponentType.ReadWrite<BoneOwningSkeletonReference>(),
                                                       ComponentType.ReadWrite<BakedLocalTransformOverride>());

            new FindSocketByNameJob { socketLookup = GetComponentLookup<Socket>(false) }.ScheduleParallel();

            new ClearJob().ScheduleParallel();

            var ecbAdd = new EntityCommandBuffer(state.WorldUpdateAllocator);
            new ApplySkeletonsToImportedSocketsJob
            {
                componentTypesToAdd     = componentsToAdd,
                ecb                     = ecbAdd.AsParallelWriter(),
                skeletonReferenceLookup = GetComponentLookup<BoneOwningSkeletonReference>(false),
                socketLookup            = GetComponentLookup<Socket>(false),
                localTransformLookup    = GetComponentLookup<BakedLocalTransformOverride>(false)
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
        [BurstCompile]
        partial struct FindSocketByNameJob : IJobEntity
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<Socket> socketLookup;
            UnsafeText                                                           cache;

            public void Execute(in AuthoredSocketString s, in DynamicBuffer<SkeletonBoneNameInHierarchy> boneBuffer)
            {
                var bones  = boneBuffer.AsNativeArray().AsReadOnlySpan();
                var search = s.reversePathStart;
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i].boneName;
                    if (search.Length > bone.Length && search.StartsWith(bone))
                    {
                        if (!cache.IsCreated)
                            cache = new UnsafeText(search.Length * 2, Allocator.Temp);
                        cache.Clear();
                        cache.AppendBoneReversePath(bones, i);
                        if (cache.StartsWith(search))
                        {
                            socketLookup.GetRefRW(s.socket).ValueRW.boneIndex = (short)i;
                            return;
                        }
                    }
                    else if (search.Length <= bone.Length && bone.StartsWith(search))
                    {
                        socketLookup.GetRefRW(s.socket).ValueRW.boneIndex = (short)i;
                        return;
                    }
                }
            }
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
            [NativeDisableParallelForRestriction] public ComponentLookup<BakedLocalTransformOverride> localTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<Socket>                      socketLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<BoneOwningSkeletonReference> skeletonReferenceLookup;
            public EntityCommandBuffer.ParallelWriter                                                 ecb;
            public ComponentTypeSet                                                                   componentTypesToAdd;

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
                    var localTransform = ComputeRootTransformOfBone(socket.boneIndex, in boneTransforms);
                    if (socketLookup.HasComponent(socket.boneEntity))
                    {
                        skeletonReferenceLookup[socket.boneEntity] = new BoneOwningSkeletonReference { skeletonRoot = entity };
                        socketLookup[socket.boneEntity]                                                             = new Socket { boneIndex = (short)socket.boneIndex };
                        localTransformLookup[socket.boneEntity]                                                     = new BakedLocalTransformOverride {
                            localTransform                                                                          = localTransform
                        };
                    }
                    else
                    {
                        ecb.AddComponent(chunkIndexInQuery, socket.boneEntity, componentTypesToAdd);
                        ecb.SetComponent(chunkIndexInQuery, socket.boneEntity, new BoneOwningSkeletonReference { skeletonRoot   = entity });
                        ecb.SetComponent(chunkIndexInQuery, socket.boneEntity, new Socket { boneIndex                           = (short)socket.boneIndex });
                        ecb.SetComponent(chunkIndexInQuery, socket.boneEntity, new BakedLocalTransformOverride { localTransform = localTransform });
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

            public void Execute(ref BakedLocalTransformOverride localTransform, ref Socket socket, in BoneOwningSkeletonReference skeletonReference)
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
            var parent = result.context32;
            while (parent > 0)
            {
                var parentTransform = transforms[parent].boneTransform;
                parent              = parentTransform.context32;
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

