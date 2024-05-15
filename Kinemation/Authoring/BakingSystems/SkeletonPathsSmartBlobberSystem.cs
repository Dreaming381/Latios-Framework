using System;
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
    // Do not add to entity directly.
    [TemporaryBakingType]
    public struct SkeletonBoneNameInHierarchy : IBufferElementData
    {
        public FixedString64Bytes boneName;
        public int                parentIndex;  // -1 to ignore
    }

    public static class SkeletonBindingPathsBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a SkeletonBindingPathsBlob Blob Asset
        /// </summary>
        /// <param name="boneNamesInHierarchy">An array containing each bone's name as well as its parent index in the same array.
        /// This array can be temp-allocated.</param>
        public static SmartBlobberHandle<SkeletonBindingPathsBlob> RequestCreateBlobAsset(this IBaker baker, NativeArray<SkeletonBoneNameInHierarchy> boneNamesInHierarchy)
        {
            return baker.RequestCreateBlobAsset<SkeletonBindingPathsBlob, SkeletonBindingPathsBakeData>(new SkeletonBindingPathsBakeData {
                boneNamesInHierarchy = boneNamesInHierarchy
            });
        }

        /// <summary>
        /// Requests the creation of a SkeletonBindingPathsBlob Blob Asset
        /// </summary>
        /// <param name="animator">An animator that was imported with "Optimize Game Objects" enabled</param>
        /// <param name="rootName">The name to use for the root bone in the hiearchy</param>
        public static SmartBlobberHandle<SkeletonBindingPathsBlob> RequestCreateBlobAsset(this IBaker baker, UnityEngine.Animator animator, FixedString64Bytes rootName)
        {
            return baker.RequestCreateBlobAsset<SkeletonBindingPathsBlob, SkeletonBindingPathsFromOptimizedAnimatorBakeData>(new SkeletonBindingPathsFromOptimizedAnimatorBakeData
            {
                animator = animator,
                rootName = rootName
            });
        }

        /// <summary>
        /// Requests the creation of a temporary baking entity with a DynamicBuffer<SkeletonBoneNameInHierarchy> allowing for generation of bone index
        /// references at bake time inside an ISmartPostProcessItem.
        /// </summary>
        /// <param name="animator">An animator that was imported with "Optimize Game Objects" enabled</param>
        /// <param name="rootName">The name to use for the root bone in the hiearchy</param>
        /// <returns>A handle which can be resolved in ISmartPostProcessItem.PostProcessBlobRequests() to read bone names and parent indices</returns>
        public static BoneNamesRequestHandle RequestBoneNames(this IBaker baker, UnityEngine.Animator animator, FixedString64Bytes rootName)
        {
            var entity                                                                                          = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            baker.AddComponent(entity, new ShadowHierarchyRequest { animatorToBuildShadowFor                    = animator });
            baker.AddBuffer<SkeletonBoneNameInHierarchy>(entity).Add(new SkeletonBoneNameInHierarchy { boneName = rootName, parentIndex = -1 });
            baker.DependsOn(animator.avatar);
            return new BoneNamesRequestHandle { boneNamesTempEntity = entity };
        }
    }

    /// <summary>
    /// A handle to a temporary entity used to bake bone names which can be resolved in an ISmartPostProcessItem.
    /// This is typically used to bake indices into optimized skeletons.
    /// </summary>
    public struct BoneNamesRequestHandle
    {
        internal EntityWithBuffer<SkeletonBoneNameInHierarchy> boneNamesTempEntity;

        /// <summary>
        /// Retrieves the optimized skeleton's bone names. Call this in ISmartPostProcessItem.PostProcessBlobRequests().
        /// </summary>
        public ReadOnlySpan<SkeletonBoneNameInHierarchy> Resolve(EntityManager entityManager)
        {
            return entityManager.GetBuffer<SkeletonBoneNameInHierarchy>(boneNamesTempEntity, true).AsNativeArray().AsReadOnlySpan();
        }
    }

    public struct SkeletonBindingPathsBakeData : ISmartBlobberRequestFilter<SkeletonBindingPathsBlob>
    {
        public NativeArray<SkeletonBoneNameInHierarchy> boneNamesInHierarchy;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (boneNamesInHierarchy.IsCreated && boneNamesInHierarchy.Length > 0)
            {
                for (int i = 0; i < boneNamesInHierarchy.Length; i++)
                {
                    if (boneNamesInHierarchy[i].parentIndex >= i)
                    {
                        UnityEngine.Debug.LogError(
                            $"Kinemation failed to bake binding paths for {baker.GetName()}. Bone {boneNamesInHierarchy[i].boneName} had a parent with an index {boneNamesInHierarchy[i].parentIndex} greater or equal to its own {i}.");
                        return false;
                    }
                }

                baker.AddBuffer<SkeletonBoneNameInHierarchy>(blobBakingEntity).AddRange(boneNamesInHierarchy);
                return true;
            }
            return false;
        }
    }

    public struct SkeletonBindingPathsFromOptimizedAnimatorBakeData : ISmartBlobberRequestFilter<SkeletonBindingPathsBlob>
    {
        public UnityEngine.Animator animator;
        public FixedString64Bytes   rootName;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (animator == null)
                return false;
            if (animator.hasTransformHierarchy)
            {
                UnityEngine.Debug.LogError(
                    $"Kinemation failed to bake binding paths for {animator.gameObject.name}. The Animator is not an optimized hierarchy but was passed to a filter expecting one.");
                return false;
            }
            if (rootName.IsEmpty)
            {
                UnityEngine.Debug.LogError($"Kinemation failed to bake binding paths for {animator.gameObject.name}. No root name was provided.");
                return false;
            }

            baker.AddComponent(blobBakingEntity, new ShadowHierarchyRequest
            {
                animatorToBuildShadowFor = animator
            });
            baker.AddBuffer<SkeletonBoneNameInHierarchy>(blobBakingEntity).Add(new SkeletonBoneNameInHierarchy { boneName = rootName, parentIndex = -1 });
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
    public partial struct SkeletonPathsSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<SkeletonBindingPathsBlob>().Register(state.World);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
            public unsafe void Execute(ref SmartBlobberResult blob, in DynamicBuffer<SkeletonBoneNameInHierarchy> bones)
            {
                var builder = new BlobBuilder(Allocator.Temp);

                NativeText path = new NativeText(Allocator.Temp);

                ref var root       = ref builder.ConstructRoot<SkeletonBindingPathsBlob>();
                var     pathsOuter = builder.Allocate(ref root.pathsInReversedNotation, bones.Length);
                for (int i = 0; i < bones.Length; i++)
                {
                    path.Clear();
                    for (int j = i; j >= 0; j = bones[j].parentIndex)
                    {
                        path.Append(bones[j].boneName);
                        path.Append('/');
                    }

                    builder.ConstructFromNativeArray(ref pathsOuter[i], path.GetUnsafePtr(), path.Length);
                }
                blob.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<SkeletonBindingPathsBlob>(Allocator.Persistent));
            }
        }
    }
}

