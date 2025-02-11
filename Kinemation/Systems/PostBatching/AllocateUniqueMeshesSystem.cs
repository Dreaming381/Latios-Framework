using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AllocateUniqueMeshesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_newMeshesQuery;
        EntityQuery m_deadMeshesQuery;
        EntityQuery m_liveBakedQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_newMeshesQuery  = state.Fluent().With<UniqueMeshConfig>(true).Without<TrackedUniqueMesh>().Build();
            m_deadMeshesQuery = state.Fluent().With<TrackedUniqueMesh>(true).Without<UniqueMeshConfig>().Build();

            if ((state.WorldUnmanaged.Flags & WorldFlags.Editor) != WorldFlags.None)
            {
                m_liveBakedQuery = state.Fluent().With<TrackedUniqueMesh>(true).With<MaterialMeshInfo>(false).Build();
            }

            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new UniqueMeshPool
            {
                allMeshes                   = new NativeList<UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent),
                unusedMeshes                = new NativeList<UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent),
                invalidMeshesToCull         = new NativeHashSet<BatchMeshID>(64, Allocator.Persistent),
                meshesPrevalidatedThisFrame = new NativeHashSet<BatchMeshID>(64, Allocator.Persistent),
                meshToIdMap                 = new NativeHashMap<UnityObjectRef<UnityEngine.Mesh>, BatchMeshID>(64, Allocator.Persistent),
                idToMeshMap                 = new NativeHashMap<BatchMeshID, UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent)
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var newMeshesCount  = m_newMeshesQuery.CalculateEntityCountWithoutFiltering();
            var deadMeshesCount = m_deadMeshesQuery.CalculateEntityCountWithoutFiltering();
            var neededMeshes    = newMeshesCount - deadMeshesCount;
            var meshPool        = latiosWorld.worldBlackboardEntity.GetCollectionComponent<UniqueMeshPool>(false);
            if (neededMeshes > 0)
            {
                var newAllocatedMeshes = CollectionHelper.CreateNativeArray<UnityObjectRef<UnityEngine.Mesh> >(neededMeshes,
                                                                                                               state.WorldUpdateAllocator,
                                                                                                               NativeArrayOptions.UninitializedMemory);
                var newNames = CollectionHelper.CreateNativeArray<FixedString128Bytes>(neededMeshes, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
                var newIDs   = CollectionHelper.CreateNativeArray<BatchMeshID>(neededMeshes, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < neededMeshes; i++)
                {
                    newNames[i] = $"Kinemation Unique Mesh {meshPool.allMeshes.Length + i}";
                }

                GraphicsUnmanaged.CreateMeshes(newAllocatedMeshes, newNames);
                GraphicsUnmanaged.RegisterMeshes(newAllocatedMeshes, newIDs, state.WorldUnmanaged);

                state.Dependency = new AddMeshesToPoolJob
                {
                    meshes   = newAllocatedMeshes,
                    ids      = newIDs,
                    meshPool = meshPool,
                }.Schedule(state.Dependency);
            }

            if (deadMeshesCount > 0)
            {
                state.Dependency = new DeadMeshesJob
                {
                    ecb           = latiosWorld.syncPoint.CreateEntityCommandBuffer(),
                    entityHandle  = GetEntityTypeHandle(),
                    meshPool      = meshPool,
                    trackedHandle = GetComponentTypeHandle<TrackedUniqueMesh>(true)
                }.Schedule(m_deadMeshesQuery, state.Dependency);
            }
            if (newMeshesCount > 0)
            {
                state.Dependency = new NewMeshesJob
                {
                    accb         = latiosWorld.syncPoint.CreateAddComponentsCommandBuffer<TrackedUniqueMesh>(AddComponentsDestroyedEntityResolution.AddToNewEntityAndDestroy),
                    entityHandle = GetEntityTypeHandle(),
                    meshPool     = meshPool,
                    mmiHandle    = GetComponentTypeHandle<MaterialMeshInfo>(false)
                }.Schedule(m_newMeshesQuery, state.Dependency);
            }
            if ((state.WorldUnmanaged.Flags & WorldFlags.Editor) != WorldFlags.None)
            {
                state.Dependency = new PatchLiveBakedMeshesJob
                {
                    meshPool      = meshPool,
                    mmiHandle     = GetComponentTypeHandle<MaterialMeshInfo>(false),
                    trackedHandle = GetComponentTypeHandle<TrackedUniqueMesh>(true)
                }.ScheduleParallel(m_liveBakedQuery, state.Dependency);
            }
        }

        [BurstCompile]
        struct AddMeshesToPoolJob : IJob
        {
            [ReadOnly] public NativeArray<UnityObjectRef<UnityEngine.Mesh> > meshes;
            [ReadOnly] public NativeArray<BatchMeshID>                       ids;
            public UniqueMeshPool                                            meshPool;

            public void Execute()
            {
                meshPool.allMeshes.AddRange(meshes);
                meshPool.unusedMeshes.AddRange(meshes);
                for (int i = 0; i < meshes.Length; i++)
                {
                    meshPool.meshToIdMap.Add(meshes[i], ids[i]);
                    meshPool.idToMeshMap.Add(ids[i], meshes[i]);
                }
            }
        }

        [BurstCompile]
        struct DeadMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            public EntityCommandBuffer                               ecb;
            public UniqueMeshPool                                    meshPool;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                ecb.RemoveComponent<TrackedUniqueMesh>(entities);
                var tracked = (TrackedUniqueMesh*)chunk.GetRequiredComponentDataPtrRO(ref trackedHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var mesh = tracked[i].mesh;
                    meshPool.unusedMeshes.Add(mesh);
                    meshPool.invalidMeshesToCull.Remove(meshPool.meshToIdMap[mesh]);
                }
            }
        }

        [BurstCompile]
        struct NewMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                   entityHandle;
            public ComponentTypeHandle<MaterialMeshInfo>         mmiHandle;
            public AddComponentsCommandBuffer<TrackedUniqueMesh> accb;
            public UniqueMeshPool                                meshPool;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetEntityDataPtrRO(entityHandle);
                var mmis     = chunk.GetComponentDataPtrRW(ref mmiHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var mesh = meshPool.unusedMeshes[meshPool.unusedMeshes.Length - 1];
                    meshPool.unusedMeshes.Length--;
                    accb.Add(entities[i], new TrackedUniqueMesh { mesh = mesh });
                    var id                                             = meshPool.meshToIdMap[mesh];
                    meshPool.invalidMeshesToCull.Add(id);
                    if (mmis != null)
                        mmis[i].MeshID = id;
                }
            }
        }

        [BurstCompile]
        struct PatchLiveBakedMeshesJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            public ComponentTypeHandle<MaterialMeshInfo>             mmiHandle;
            [ReadOnly] public UniqueMeshPool                         meshPool;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var tracked = chunk.GetComponentDataPtrRO(ref trackedHandle);
                var mmis    = chunk.GetComponentDataPtrRW(ref mmiHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    mmis[i].MeshID = meshPool.meshToIdMap[tracked[i].mesh];
                }
            }
        }
    }
}

