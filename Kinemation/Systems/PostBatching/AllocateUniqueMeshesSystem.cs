using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AllocateUniqueMeshesSystem : ISystem, ILatiosApi
    {
        EntityQuery m_newMeshesQuery;
        EntityQuery m_deadMeshesQuery;
        EntityQuery m_deadMeshesQuery2;
        EntityQuery m_liveBakedQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var api = this.OnCreateForLatios(ref state);

            m_newMeshesQuery   = state.Fluent().With<UniqueMeshConfig, MaterialMeshInfo>(true).Without<TrackedUniqueMesh>().Build();
            m_deadMeshesQuery  = state.Fluent().With<TrackedUniqueMesh>(true).Without<UniqueMeshConfig>().Build();
            m_deadMeshesQuery2 = state.Fluent().With<TrackedUniqueMesh, UniqueMeshConfig>(true).Without<MaterialMeshInfo>().Build();
            m_liveBakedQuery   = state.Fluent().With<TrackedUniqueMesh, LiveBakedTag>(true).With<MaterialMeshInfo>(false).IncludeDisabledEntities().Build();
            m_liveBakedQuery.AddChangedVersionFilter(ComponentType.ReadOnly<MaterialMeshInfo>());

            api.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new UniqueMeshPool
            {
                allMeshes    = new NativeList<UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent),
                unusedMeshes = new NativeList<UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent),
                meshToIdMap  = new NativeHashMap<UnityObjectRef<UnityEngine.Mesh>, int>(64, Allocator.Persistent),
                idToMeshMap  = new NativeHashMap<int, UnityObjectRef<UnityEngine.Mesh> >(64, Allocator.Persistent)
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api              = this.GetApi(ref state);
            var newMeshesCount   = m_newMeshesQuery.CalculateEntityCountWithoutFiltering();
            var deadMeshesCount  = m_deadMeshesQuery.CalculateEntityCountWithoutFiltering();
            var deadMeshesCount2 = m_deadMeshesQuery2.CalculateEntityCountWithoutFiltering();
            var meshPool         = api.worldBlackboardEntity.GetCollectionComponent<UniqueMeshPool>(false);
            var neededMeshes     = newMeshesCount - deadMeshesCount - deadMeshesCount2 - meshPool.unusedMeshes.Length;
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

            if (deadMeshesCount + deadMeshesCount2 > 0)
            {
                var job = new DeadMeshesJob
                {
                    ecb      = api.syncPoint.CreateEntityCommandBuffer(),
                    meshPool = meshPool,
                }.Inject(api);
                if (deadMeshesCount > 0)
                    state.Dependency = job.Schedule(m_deadMeshesQuery, state.Dependency);
                if (deadMeshesCount2 > 0)
                    state.Dependency = job.Schedule(m_deadMeshesQuery2, state.Dependency);
            }
            if (newMeshesCount > 0)
            {
                state.Dependency = new NewMeshesJob
                {
                    accb     = api.syncPoint.CreateAddComponentsCommandBuffer<TrackedUniqueMesh>(AddComponentsDestroyedEntityResolution.AddToNewEntityAndDestroy),
                    meshPool = meshPool,
                }.Inject(api).Schedule(m_newMeshesQuery, state.Dependency);
            }
            if (!m_liveBakedQuery.IsEmptyIgnoreFilter)
            {
                state.Dependency = new PatchLiveBakedMeshesJob
                {
                    meshPool = meshPool,
                }.Inject(api).ScheduleParallel(m_liveBakedQuery, state.Dependency);
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
                    MaterialMeshInfo mmi = default;
                    mmi.MeshID           = ids[i];
                    meshPool.meshToIdMap.Add(meshes[i], mmi.Mesh);
                    meshPool.idToMeshMap.Add(mmi.Mesh, meshes[i]);
                }
            }
        }

        [BurstCompile]
        partial struct DeadMeshesJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] EntityTypeHandle                       entityHandle;
            [ReadOnly, Inject] ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            [Inject] ComponentTypeHandle<MaterialMeshInfo>            mmiHandle;
            public EntityCommandBuffer                                ecb;
            public UniqueMeshPool                                     meshPool;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(entityHandle);
                ecb.RemoveComponent<TrackedUniqueMesh>(entities);
                var tracked = (TrackedUniqueMesh*)chunk.GetRequiredComponentDataPtrRO(ref trackedHandle);
                var mmis    = chunk.GetComponentDataPtrRW(ref mmiHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var mesh = tracked[i].mesh;
                    meshPool.unusedMeshes.Add(mesh);
                    if (mmis != null)
                        mmis[i].Mesh = 0;
                }
            }
        }

        [BurstCompile]
        partial struct NewMeshesJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] EntityTypeHandle                  entityHandle;
            [Inject] ComponentTypeHandle<MaterialMeshInfo>       mmiHandle;
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
                    if (mmis != null)
                    {
                        mmis[i].Mesh = id;
                    }
                }
            }
        }

        [BurstCompile]
        partial struct PatchLiveBakedMeshesJob : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            [Inject] ComponentTypeHandle<MaterialMeshInfo>            mmiHandle;
            [ReadOnly] public UniqueMeshPool                          meshPool;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var tracked = chunk.GetComponentDataPtrRO(ref trackedHandle);
                var mmis    = chunk.GetComponentDataPtrRW(ref mmiHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (meshPool.meshToIdMap.TryGetValue(tracked[i].mesh, out var id))
                        mmis[i].Mesh = id;
                }
            }
        }
    }
}

