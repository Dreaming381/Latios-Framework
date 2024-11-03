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

            GetOrCreateAndAddSystem<FindExposedBonesBakingSystem>();  // async -> sync
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
            GetOrCreateAndAddManagedSystem<CreateShadowHierarchiesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherMeshBindingPathsFromShadowHierarchySystem>();  // sync
            GetOrCreateAndAddManagedSystem<PruneShadowHierarchiesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<BuildOptimizedBoneTransformsSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherSkeletonBindingPathsFromShadowHierarchySystem>();  // sync
            GetOrCreateAndAddManagedSystem<AssignExportedBoneIndicesSystem>();  // sync
            GetOrCreateAndAddManagedSystem<GatherOptimizedHierarchyFromShadowHierarchySystem>();  // sync -> async

            GetOrCreateAndAddSystem<BindSkinnedMeshesToSkeletonsSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<MeshDeformDataSmartBlobberSystem>();  // sync -> async

            GetOrCreateAndAddSystem<MeshPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonPathsSmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<SkeletonHierarchySmartBlobberSystem>();  // async
            GetOrCreateAndAddSystem<ParameterClipSetSmartBlobberSystem>();  // async

            GetOrCreateAndAddSystem<SetupExportedBonesSystem>();  // async -> sync
            GetOrCreateAndAddManagedSystem<SkeletonClipSetSmartBlobberSystem>();  // sync -> async
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            GetOrCreateAndAddSystem<Latios.Transforms.Authoring.Systems.TransformHierarchySyncBakingSystem>();  // async | Needed for correcting children of exported bones.
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            // Todo: How do we set LTWs correctly for exported bones in Unity Transforms?
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
        }
    }
}

