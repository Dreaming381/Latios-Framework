using System.Collections.Generic;
using Latios.Authoring;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Authoring.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    [BurstCompile]
    public partial struct TransformBakingSystem : ISystem
    {
        [BakingType]
        struct PreviousRuntimeFlags : IComponentData
        {
            public RuntimeTransformComponentFlags previousFlags;
        }

        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAll<TransformAuthoring>(true).IncludeDisabledEntities().IncludePrefabs().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            var job = new Job
            {
                ecb                          = ecb.AsParallelWriter(),
                entityHandle                 = GetEntityTypeHandle(),
                lastSystemVersion            = state.LastSystemVersion,
                localTransformHandle         = GetComponentTypeHandle<LocalTransform>(false),
                parentHandle                 = GetComponentTypeHandle<Parent>(false),
                parentToWorldTransformHandle = GetComponentTypeHandle<ParentToWorldTransform>(false),
                transformAuthoringHandle     = GetComponentTypeHandle<TransformAuthoring>(true),
                transformAuthoringLookup     = GetComponentLookup<TransformAuthoring>(true),
                worldTransformHandle         = GetComponentTypeHandle<WorldTransform>(false),
                previousFlagsHandle          = GetComponentTypeHandle<PreviousRuntimeFlags>(false)
            };

            state.Dependency = job.ScheduleParallelByRef(m_query, state.Dependency);
            state.CompleteDependency();

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                        entityHandle;
            [ReadOnly] public ComponentTypeHandle<TransformAuthoring> transformAuthoringHandle;
            [ReadOnly] public ComponentLookup<TransformAuthoring>     transformAuthoringLookup;
            public ComponentTypeHandle<WorldTransform>                worldTransformHandle;
            public ComponentTypeHandle<ParentToWorldTransform>        parentToWorldTransformHandle;
            public ComponentTypeHandle<LocalTransform>                localTransformHandle;
            public ComponentTypeHandle<Parent>                        parentHandle;
            public ComponentTypeHandle<PreviousRuntimeFlags>          previousFlagsHandle;

            public EntityCommandBuffer.ParallelWriter ecb;
            public uint                               lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!(chunk.DidChange(ref transformAuthoringHandle, lastSystemVersion) && chunk.DidOrderChange(lastSystemVersion)))
                    return;

                bool hasWorldTransform         = chunk.Has(ref worldTransformHandle);
                bool hasParent                 = chunk.Has(ref parentHandle);
                bool hasParentToWorldTransform = chunk.Has(ref parentToWorldTransformHandle);
                bool hasLocalTransform         = chunk.Has(ref localTransformHandle);
                bool hasStatic                 = chunk.Has<Unity.Transforms.Static>();  // Despite the namespace, this is in Unity.Entities assembly
                bool hasIdentityLocalToParent  = chunk.Has<CopyParentWorldTransformTag>();
                bool hasPreviousFlags          = chunk.Has(ref previousFlagsHandle);

                var entities                = chunk.GetNativeArray(entityHandle);
                var transformAuthoringArray = chunk.GetNativeArray(ref transformAuthoringHandle);

                var worldTransformArray         = chunk.GetNativeArray(ref worldTransformHandle);
                var parentToWorldTransformArray = chunk.GetNativeArray(ref parentToWorldTransformHandle);
                var localTransformArray         = chunk.GetNativeArray(ref localTransformHandle);
                var parentArray                 = chunk.GetNativeArray(ref parentHandle);
                var previousFlagsArray          = chunk.GetNativeArray(ref previousFlagsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var transformAuthoring = transformAuthoringArray[i];
                    if (!hasPreviousFlags)
                    {
                        ecb.AddComponent(unfilteredChunkIndex, entities[i], new PreviousRuntimeFlags { previousFlags = transformAuthoring.RuntimeTransformUsage });
                    }

                    if ((transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.ManualOverride) != 0)
                    {
                        if (!hasPreviousFlags || (previousFlagsArray[i].previousFlags & RuntimeTransformComponentFlags.ManualOverride) == 0)
                        {
                            var cts = new ComponentTypeSet(ComponentType.ReadOnly<WorldTransform>(),
                                                           ComponentType.ReadOnly<LocalTransform>(),
                                                           ComponentType.ReadOnly<ParentToWorldTransform>(),
                                                           ComponentType.ReadOnly<Parent>());

                            ecb.RemoveComponent(unfilteredChunkIndex, entities[i], cts);
                        }
                        continue;
                    }

                    bool needsWorldTransform = false;
                    bool needsLocalTransform = false;
                    if (transformAuthoring.RuntimeTransformUsage == RuntimeTransformComponentFlags.None)
                    {
                        // Do nothing
                    }
                    else if (hasStatic)
                    {
                        needsWorldTransform = true;
                    }
                    else
                    {
                        needsWorldTransform = (transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.LocalToWorld) != 0;
                        needsLocalTransform = (transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.RequestParent) != 0;
                    }

                    if (needsLocalTransform == false && needsWorldTransform == false &&
                        (hasLocalTransform || hasParent || hasParentToWorldTransform || hasWorldTransform))
                    {
                        var cts = new ComponentTypeSet(ComponentType.ReadOnly<WorldTransform>(),
                                                       ComponentType.ReadOnly<LocalTransform>(),
                                                       ComponentType.ReadOnly<ParentToWorldTransform>(),
                                                       ComponentType.ReadOnly<Parent>());
                        ecb.RemoveComponent(unfilteredChunkIndex, entities[i], cts);
                    }

                    if (needsWorldTransform == true && needsLocalTransform == false)
                    {
                        if (hasLocalTransform || hasParent || hasParentToWorldTransform)
                        {
                            var cts = new ComponentTypeSet(ComponentType.ReadOnly<LocalTransform>(),
                                                           ComponentType.ReadOnly<ParentToWorldTransform>(),
                                                           ComponentType.ReadOnly<Parent>());
                            ecb.RemoveComponent(unfilteredChunkIndex, entities[i], cts);
                        }

                        TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);

                        TransformQvvs worldTransform = new TransformQvvs
                        {
                            position   = transformAuthoring.LocalPosition,
                            rotation   = transformAuthoring.LocalRotation,
                            scale      = scale,
                            stretch    = stretch,
                            worldIndex = 0
                        };

                        if (hasWorldTransform)
                            worldTransformArray[i] = new WorldTransform { worldTransform = worldTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new WorldTransform { worldTransform = worldTransform });
                    }

                    if (needsWorldTransform && needsLocalTransform && hasIdentityLocalToParent)
                    {
                        var parentWorldTransform = GetWorldTransform(transformAuthoringLookup[transformAuthoring.RuntimeParent]);

                        if (hasWorldTransform)
                            worldTransformArray[i] = new WorldTransform { worldTransform = parentWorldTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new WorldTransform { worldTransform = parentWorldTransform });

                        if (hasParent)
                            parentArray[i] = new Parent { parent = transformAuthoring.RuntimeParent };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new Parent { parent = transformAuthoring.RuntimeParent });
                    }
                    else if (needsWorldTransform && needsLocalTransform)
                    {
                        var parentWorldTransform = GetWorldTransform(transformAuthoringLookup[transformAuthoring.RuntimeParent]);
                        TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);
                        var worldTransform = new TransformQvvs
                        {
                            stretch = stretch,
                        };
                        var localTransform = new TransformQvs
                        {
                            position = transformAuthoring.LocalPosition,
                            rotation = transformAuthoring.LocalRotation,
                            scale    = scale
                        };
                        qvvs.mul(ref worldTransform, parentWorldTransform, localTransform);

                        if (hasWorldTransform)
                            worldTransformArray[i] = new WorldTransform { worldTransform = worldTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new WorldTransform { worldTransform = worldTransform });

                        if (hasParentToWorldTransform)
                            parentToWorldTransformArray[i] = new ParentToWorldTransform { parentToWorldTransform = parentWorldTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new ParentToWorldTransform { parentToWorldTransform = parentWorldTransform });

                        if (hasLocalTransform)
                            localTransformArray[i] = new LocalTransform { localTransform = localTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new LocalTransform { localTransform = localTransform });

                        if (hasParent)
                            parentArray[i] = new Parent { parent = transformAuthoring.RuntimeParent };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new Parent { parent = transformAuthoring.RuntimeParent });
                    }
                }
            }

            TransformQvvs GetWorldTransform(in TransformAuthoring transformAuthoring)
            {
                if (transformAuthoring.RuntimeParent == Entity.Null)
                {
                    TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);

                    return new TransformQvvs
                    {
                        position   = transformAuthoring.LocalPosition,
                        rotation   = transformAuthoring.LocalRotation,
                        scale      = scale,
                        stretch    = stretch,
                        worldIndex = 0
                    };
                }
                else
                {
                    var parentWorldTransform = GetWorldTransform(transformAuthoringLookup[transformAuthoring.RuntimeParent]);
                    TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);
                    var worldTransform = new TransformQvvs
                    {
                        stretch = stretch,
                    };
                    var localTransform = new TransformQvs
                    {
                        position = transformAuthoring.LocalPosition,
                        rotation = transformAuthoring.LocalRotation,
                        scale    = scale
                    };
                    qvvs.mul(ref worldTransform, parentWorldTransform, localTransform);
                    return worldTransform;
                }
            }
        }
    }
}

