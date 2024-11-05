using Latios;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ValidateOptimizedSkeletonCacheSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        unsafe partial struct Job : IJobEntity
        {
            public void Execute(in DynamicBuffer<OptimizedSkeletonStructureCacheBoneValidation> validateBones,
                                in DynamicBuffer<OptimizedSkeletonStructureCachePathValidation> validatePaths,
                                in DynamicBuffer<OptimizedBoneTransform>                        bones,
                                in SkeletonBindingPathsBlobReference blobPaths,
                                in OptimizedSkeletonHierarchyBlobReference parents)
            {
                if (validateBones.Length == 0)
                    return;
                FixedString128Bytes firstName = default;
                var                 firstPath = validatePaths.Reinterpret<byte>().AsNativeArray().GetSubArray(0, validateBones[0].pathByteCount);
                firstName.AsFixedList().AddRange(firstPath.GetUnsafeReadOnlyPtr(), firstPath.Length);
                var boneCount = validateBones.Length;
                if (boneCount != bones.Length || boneCount != blobPaths.blob.Value.pathsInReversedNotation.Length || boneCount != parents.blob.Value.parentIndices.Length)
                {
                    UnityEngine.Debug.LogWarning(
                        $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. The bone counts do not match. Cached bones: {boneCount}, actual bones: {bones.Length}, actual paths: {blobPaths.blob.Value.pathsInReversedNotation.Length}, actual parents: {parents.blob.Value.parentIndices.Length}");
                    return;
                }

                var firstActualPathLength = blobPaths.blob.Value.pathsInReversedNotation[0].Length;
                for (int i = 0; i < boneCount; i++)
                {
                    var validateBone    = validateBones[i];
                    var actualTransform = bones[i].boneTransform;
                    if (!math.all(math.abs(validateBone.localTransform.rotation.value - actualTransform.rotation.value) < math.EPSILON) &&
                        !math.all(math.abs(validateBone.localTransform.rotation.value + actualTransform.rotation.value) < math.EPSILON))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Rotation at index {i} does not match. Cache: {validateBone.localTransform.rotation}, Actual: {actualTransform.rotation}");
                        return;
                    }
                    if (!math.all(math.abs(validateBone.localTransform.position - actualTransform.position) < math.EPSILON))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Position at index {i} does not match. Cache: {validateBone.localTransform.position}, Actual: {actualTransform.position}");
                        return;
                    }
                    if (!math.all(math.abs(validateBone.localTransform.stretch - actualTransform.stretch) < math.EPSILON))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Stretch at index {i} does not match. Cache: {validateBone.localTransform.stretch}, Actual: {actualTransform.stretch}");
                        return;
                    }
                    if (!(math.abs(validateBone.localTransform.scale - actualTransform.scale) < math.EPSILON))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Scale at index {i} does not match. Cache: {validateBone.localTransform.scale}, Actual: {actualTransform.scale}");
                        return;
                    }
                    if (validateBone.parentIndex != parents.blob.Value.parentIndices[i])
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Parent index at index {i} does not match. Cache: {validateBone.parentIndex}, Actual: {parents.blob.Value.parentIndices[i]}");
                        return;
                    }

                    var wrongBytes = validatePaths.Reinterpret<byte>().AsNativeArray().GetSubArray(validateBone.firstPathByteIndex,
                                                                                                   validateBone.pathByteCount - firstPath.Length);
                    FixedString4096Bytes wrongPath = default;
                    wrongPath.AsFixedList().AddRange(wrongBytes.GetUnsafeReadOnlyPtr(), validateBone.pathByteCount - firstPath.Length);
                    FixedString4096Bytes rightPath = default;
                    rightPath.AsFixedList().AddRange(blobPaths.blob.Value.pathsInReversedNotation[i].GetUnsafePtr(),
                                                     blobPaths.blob.Value.pathsInReversedNotation[i].Length - firstActualPathLength);
                    if (wrongPath != rightPath)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed Optimized Skeleton Structure Cache Validation for root {firstPath}. Path at index {i} does not match. Cache: {wrongPath}, Actual: {rightPath}");
                        return;
                    }
                }
            }
        }
    }
}

