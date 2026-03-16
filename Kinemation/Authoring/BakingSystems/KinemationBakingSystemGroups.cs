using Latios.Authoring.Systems;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class KinemationPreTransformsBakingGroup : ComponentSystemGroup
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

            GetOrCreateAndAddSystem<BindSkinnedMeshesToSkeletonsSystem>();  // async -> sync
            GetOrCreateAndAddSystem<FindExposedBonesBakingSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<CreateShadowHierarchiesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherMeshBindingPathsFromShadowHierarchySystem>();  // sync
            GetOrCreateAndAddManagedSystem<PruneShadowHierarchiesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<BuildOptimizedBoneTransformsSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherSkeletonBindingPathsFromShadowHierarchySystem>();  // sync
            GetOrCreateAndAddManagedSystem<AssignSocketIndicesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherOptimizedHierarchyFromShadowHierarchySystem>();  // sync -> async
            GetOrCreateAndAddSystem<SetupSocketsSystem>();  // async -> sync
            // Todo: How do we set LTWs correctly for sockets in Unity Transforms?
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    [DisableAutoCreation]
    public partial class KinemationSmartBlobberBakingGroup : ComponentSystemGroup
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

#if UNITY_EDITOR

            // Todo: Need new async -> sync
            GetOrCreateAndAddManagedSystem<MeshDeformDataSmartBlobberSystem>();  // sync -> async

            GetOrCreateAndAddSystem<MeshPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonHierarchySmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonBoneMaskSetSmartBlobberSystem>();  // async -> sync -> async

#if !LATIOS_DISABLE_ACL
            GetOrCreateAndAddManagedSystem<SkeletonClipSetSmartBlobberSystem>();  // sync -> async
            GetOrCreateAndAddSystem<ParameterClipSetSmartBlobberSystem>();  // async
#endif
            GetOrCreateAndAddManagedSystem<DestroyShadowHierarchiesSystem>();  // sync
#endif
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBakerBakingGroup))]
    public partial class KinemationSmartBlobberResolverBakingGroup : ComponentSystemGroup
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
            GetOrCreateAndAddSystem<ValidateOptimizedSkeletonCacheSystem>();  // async
        }
    }
}

