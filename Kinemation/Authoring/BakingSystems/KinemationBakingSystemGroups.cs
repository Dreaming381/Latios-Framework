using Latios.Authoring.Systems;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    [DisableAutoCreation]
    public class KinemationSmartBlobberBakingGroup : ComponentSystemGroup
    {
        void GetOrCreateAndAddSystem<T>() where T : unmanaged, ISystem
        {
            AddSystemToUpdateList(World.GetOrCreateSystem<T>());
        }

        void GetOrCreateAndAddManagedSystem<T>() where T : ComponentSystemBase
        {
            AddSystemToUpdateList(World.GetOrCreateSystemManaged<T>());
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<CreateShadowHierarchiesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherMeshBindingPathsFromShadowHierarchySystem>();  // sync
            GetOrCreateAndAddManagedSystem<PruneShadowHierarchiesSystem>();  // sync
            //GetOrCreateAndAddManagedSystem<BuildOptimizedBoneToRootSystem2>();  // sync
            GetOrCreateAndAddManagedSystem<BuildOptimizedBoneToRootBufferSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherSkeletonBindingPathsFromShadowHierarchySystem>();
            GetOrCreateAndAddManagedSystem<AssignExportedBoneIndicesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherOptimizedHierarchyFromShadowHierarchySystem>();  // sync -> async

            GetOrCreateAndAddSystem<BindSkinnedMeshesToSkeletonsSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<MeshSkinningSmartBlobberSystem>();  // sync -> async
            GetOrCreateAndAddSystem<FindExposedBonesBakingSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<SkeletonClipSetSmartBlobberSystem>();  // sync -> async

            GetOrCreateAndAddSystem<MeshPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonHierarchySmartBlobberSystem>();  // async

            GetOrCreateAndAddSystem<SetupExportedBonesSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<DestroyShadowHierarchiesSystem>();  // sync
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBakerBakingGroup))]
    public class KinemationSmartBlobberResolverBakingGroup : ComponentSystemGroup
    {
        void GetOrCreateAndAddSystem<T>() where T : unmanaged, ISystem
        {
            AddSystemToUpdateList(World.GetOrCreateSystem<T>());
        }

        void GetOrCreateAndAddManagedSystem<T>() where T : ComponentSystemBase
        {
            AddSystemToUpdateList(World.GetOrCreateSystemManaged<T>());
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            EnableSystemSorting = false;

            GetOrCreateAndAddSystem<ResolveSkeletonAndSkinnedMeshBlobsSystem>();  // async
        }
    }
}

