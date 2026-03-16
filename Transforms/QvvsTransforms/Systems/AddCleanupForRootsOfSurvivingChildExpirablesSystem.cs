#if !LATIOS_TRANSFORMS_UNITY
using Latios.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(SyncPointPlaybackSystemDispatch))]
    [UpdateAfter(typeof(AutoDestroyExpirablesSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddCleanupForRootsOfSurvivingChildExpirablesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var journal = latiosWorld.worldBlackboardEntity.GetCollectionComponent<AutoDestroyExpirationJournal>(true);
            state.CompleteDependency();
            if (!journal.removedFromLinkedEntityGroupStream.IsCreated || journal.removedFromLinkedEntityGroupStream.IsEmpty())
                return;

            var hierarchyChecker = new HasChecker<EntityInHierarchy>();
            var cleanupChecker   = new HasChecker<EntityInHierarchyCleanup>();

            Entity             lastLegOwner                      = Entity.Null;
            bool               lastLegOwnerIsAddCleanupCandidate = false;
            bool               lastLegOwnerGetsCleanupAdded      = false;
            NativeList<Entity> entitiesToAddCleanupTo            = new NativeList<Entity>(state.WorldUpdateAllocator);

            var reader      = journal.removedFromLinkedEntityGroupStream.AsReader();
            var streamCount = reader.ForEachCount;
            for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
            {
                int count = reader.BeginForEachIndex(streamIndex);
                for (int i = 0; i < count; i++)
                {
                    var op = reader.Read<AutoDestroyExpirationJournal.RemovedFromLinkedEntityGroup>();
                    if (op.linkedEntityGroupOwner != lastLegOwner)
                    {
                        var chunk                         = state.EntityManager.GetChunk(op.linkedEntityGroupOwner);
                        lastLegOwner                      = op.linkedEntityGroupOwner;
                        lastLegOwnerIsAddCleanupCandidate = hierarchyChecker[chunk] && !cleanupChecker[chunk];
                        lastLegOwnerGetsCleanupAdded      = false;
                    }
                    if (lastLegOwnerIsAddCleanupCandidate && !lastLegOwnerGetsCleanupAdded)
                    {
                        if (SystemAPI.HasComponent<RootReference>(op.entityRemoved))
                        {
                            var rr = SystemAPI.GetComponentRO<RootReference>(op.entityRemoved).ValueRO;
                            if (rr.rootEntity == lastLegOwner)
                            {
                                entitiesToAddCleanupTo.Add(lastLegOwner);
                                lastLegOwnerGetsCleanupAdded = true;
                            }
                        }
                    }
                }
                reader.EndForEachIndex();
            }

            state.EntityManager.AddComponent<EntityInHierarchyCleanup>(entitiesToAddCleanupTo.AsArray());
            foreach (var entity in entitiesToAddCleanupTo)
            {
                var src = SystemAPI.GetBuffer<EntityInHierarchy>(entity);
                var dst = SystemAPI.GetBuffer<EntityInHierarchyCleanup>(entity);
                TreeKernels.CopyHierarchyToCleanup(in src, ref dst);
            }
        }
    }
}
#endif

