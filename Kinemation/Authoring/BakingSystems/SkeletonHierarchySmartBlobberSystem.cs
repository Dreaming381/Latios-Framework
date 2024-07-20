using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
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
                    $"Kinemation failed to bake optimized hierarchy requested by a baker of {baker.GetAuthoringObjectForDebugDiagnostics().name}. The Animator is not an optimized hierarchy.");
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
            public unsafe void Execute(ref SmartBlobberResult result, in DynamicBuffer<OptimizedSkeletonHierarchyParentIndex> parentIndices)
            {
                var builder = new BlobBuilder(Allocator.Temp);

                ref var root    = ref builder.ConstructRoot<OptimizedSkeletonHierarchyBlob>();
                var     indices = builder.Allocate(ref root.parentIndices, parentIndices.Length);

                var childBuffers = new NativeArray<UnsafeList<short> >(parentIndices.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);

                for (int i = 0; i < parentIndices.Length; i++)
                {
                    indices[i] = (short)parentIndices[i].parentIndex;
                    if (parentIndices[i].parentIndex < 0)
                        continue;

                    if (!childBuffers[parentIndices[i].parentIndex].IsCreated)
                        childBuffers[parentIndices[i].parentIndex] = new UnsafeList<short>(4, Allocator.Temp);
                    var buffer                                     = childBuffers[parentIndices[i].parentIndex];
                    buffer.Add((short)i);
                    childBuffers[parentIndices[i].parentIndex] = buffer;
                }

                var children = builder.Allocate(ref root.childrenIndices, parentIndices.Length);
                for (int i = 0; i < parentIndices.Length; i++)
                {
                    if (childBuffers[i].IsCreated)
                    {
                        builder.ConstructFromNativeArray(ref children[i], childBuffers[i].Ptr, childBuffers[i].Length);
                    }
                    else
                    {
                        builder.Allocate(ref children[i], 0);
                    }
                }

                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<OptimizedSkeletonHierarchyBlob>(Allocator.Persistent));
            }
        }
    }
}

