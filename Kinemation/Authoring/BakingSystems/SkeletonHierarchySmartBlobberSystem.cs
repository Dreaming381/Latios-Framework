using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring
{
    // Internal for now, will become public in the future
    [TemporaryBakingType]
    internal struct OptimizedSkeletonHierarchyParentIndex : IBufferElementData
    {
        public int parentIndex;
    }

    public static class OptimizedSkeletonHierarchyBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of an OptimizedSkeletonHierarchyBlob Blob Asset
        /// </summary>
        /// <param name="animator">An animator that was imported with "Optimize Game Objects" enabled</param>
        public static SmartBlobberHandle<OptimizedSkeletonHierarchyBlob> RequestCreateBlobAsset(this IBaker baker, UnityEngine.Animator animator)
        {
            return baker.RequestCreateBlobAsset<OptimizedSkeletonHierarchyBlob, OptimizedSkeletonHierarchyFromOptimizedAnimatorBakeData>(new OptimizedSkeletonHierarchyFromOptimizedAnimatorBakeData
            {
                animator = animator
            });
        }
    }

    public struct OptimizedSkeletonHierarchyFromOptimizedAnimatorBakeData : ISmartBlobberRequestFilter<OptimizedSkeletonHierarchyBlob>
    {
        public UnityEngine.Animator animator;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (animator == null)
                return false;
            if (animator.hasTransformHierarchy)
            {
                UnityEngine.Debug.LogError(
                    $"Kinemation failed to bake optimized hierarchy for {animator.gameObject.name}. The Animator is not an optimized hierarchy.");
                return false;
            }

            baker.AddComponent(blobBakingEntity, new ShadowHierarchyRequest
            {
                animatorToBuildShadowFor = animator
            });
            baker.AddBuffer<OptimizedSkeletonHierarchyParentIndex>(blobBakingEntity);
            baker.DependsOn(animator.avatar);
            return true;
        }
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SkeletonHierarchySmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<OptimizedSkeletonHierarchyBlob>().Register(state.World);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
            public void Execute(ref SmartBlobberResult result, in DynamicBuffer<OptimizedSkeletonHierarchyParentIndex> parentIndicess)
            {
                var builder = new BlobBuilder(Allocator.Temp);

                ref var root        = ref builder.ConstructRoot<OptimizedSkeletonHierarchyBlob>();
                var     indices     = builder.Allocate(ref root.parentIndices, parentIndicess.Length);
                var     hasPSI      = builder.Allocate(ref root.hasParentScaleInverseBitmask, (int)math.ceil(parentIndicess.Length / 64f));  // length is max 16 bits so this division is safe in float.
                var     hasChildPSI = builder.Allocate(ref root.hasChildWithParentScaleInverseBitmask, hasPSI.Length);

                root.hasAnyParentScaleInverseBone = false;
                for (int i = 0; i < hasPSI.Length; i++)
                {
                    hasPSI[i]      = new BitField64(0UL);
                    hasChildPSI[i] = new BitField64(0UL);
                }
                for (int i = 0; i < parentIndicess.Length; i++)
                {
                    indices[i] = (short)parentIndicess[i].parentIndex;
                }
                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<OptimizedSkeletonHierarchyBlob>(Allocator.Persistent));
            }
        }
    }
}

