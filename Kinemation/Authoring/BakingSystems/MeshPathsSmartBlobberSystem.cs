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
    public static class MeshBindingPathsBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a MeshBindingPathsBlob Blob Asset
        /// </summary>
        /// <param name="packedPaths">A flattened string of all the paths. Can be temp-allocated.</param>
        /// <param name="pathStartingByteOffsets">An array containing the starting byte index of each path in <paramref name="packedPaths"/>. Can be temp-allocated.</param>
        public static SmartBlobberHandle<MeshBindingPathsBlob> RequestCreateBlobAsset(this IBaker baker, NativeText packedPaths, NativeArray<int> pathStartingByteOffsets)
        {
            return baker.RequestCreateBlobAsset<MeshBindingPathsBlob, MeshBindingPathsBakeData>(new MeshBindingPathsBakeData
            {
                packedPaths             = packedPaths,
                pathStartingByteOffsets = pathStartingByteOffsets
            });
        }

        /// <summary>
        /// Requests the creation of a MeshBindingPathsBlob Blob Asset
        /// </summary>
        /// <param name="paths">The list of paths</param>
        public static SmartBlobberHandle<MeshBindingPathsBlob> RequestCreateBlobAsset(this IBaker baker, List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                UnityEngine.Debug.LogError($"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The paths list is empty or null.");
                // Run default algorithm to trigger filtering on result.
                return baker.RequestCreateBlobAsset<MeshBindingPathsBlob, MeshBindingPathsBakeData>(default);
            }

            var packedPaths             = new NativeText(Allocator.Temp);
            var pathStartingByteOffsets = new NativeArray<int>(paths.Count, Allocator.Temp);
            int index                   = 0;
            foreach (var path in paths)
            {
                if (path == null)
                {
                    UnityEngine.Debug.LogError($"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The path at index {index} is null.");
                    packedPaths.Clear();
                    // Run default algorithm to trigger filtering on result.
                    return baker.RequestCreateBlobAsset<MeshBindingPathsBlob, MeshBindingPathsBakeData>(default);
                }

                pathStartingByteOffsets[index] = packedPaths.Length;
                packedPaths.Append(path);
                index++;
            }

            return baker.RequestCreateBlobAsset<MeshBindingPathsBlob, MeshBindingPathsBakeData>(new MeshBindingPathsBakeData
            {
                packedPaths             = packedPaths,
                pathStartingByteOffsets = pathStartingByteOffsets
            });
        }

        /// <summary>
        /// Requests the creation of a MeshBindingPathsBlob Blob Asset
        /// </summary>
        /// <param name="animator">An Animator that was imported with "Optimize Game Objects" enabled and that the SkinnedMeshRenderer was imported to use.</param>
        /// <param name="skinnedMeshRenderer">The SkinnedMeshRenderer whose bones array is empty because the transform hierarchy is optimized.</param>
        public static SmartBlobberHandle<MeshBindingPathsBlob> RequestCreateBlobAsset(this IBaker baker,
                                                                                      UnityEngine.Animator animator,
                                                                                      UnityEngine.SkinnedMeshRenderer skinnedMeshRenderer)
        {
            return baker.RequestCreateBlobAsset<MeshBindingPathsBlob, MeshBindingPathsFromOptimizedAnimatorBakeData>(new MeshBindingPathsFromOptimizedAnimatorBakeData
            {
                animator            = animator,
                skinnedMeshRenderer = skinnedMeshRenderer
            });
        }
    }

    public struct MeshBindingPathsBakeData : ISmartBlobberRequestFilter<MeshBindingPathsBlob>
    {
        public NativeText       packedPaths;
        public NativeArray<int> pathStartingByteOffsets;

        public unsafe bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (!packedPaths.IsCreated)
            {
                UnityEngine.Debug.LogError($"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The packed paths is not allocated.");
                return false;
            }
            if (!pathStartingByteOffsets.IsCreated)
            {
                UnityEngine.Debug.LogError($"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The path starting byte offsets array is not allocated.");
                return false;
            }

            int lastOffset = -1;
            foreach (var offset in pathStartingByteOffsets)
            {
                if (offset <= lastOffset)
                {
                    UnityEngine.Debug.LogError(
                        $"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The path starting byte offsets array must be ordered with increasing values. Violation at offset {offset}.");
                    return false;
                }
                if (offset >= packedPaths.Length)
                {
                    UnityEngine.Debug.LogError(
                        $"Kinemation failed to bake mesh binding paths for {baker.GetName()}. The path starting byte offset {offset} is greater or equal to the length of packedPaths {packedPaths.Length}.");
                    return false;
                }
                lastOffset = offset;
            }

            var bytes = baker.AddBuffer<MeshPathByte>(blobBakingEntity).Reinterpret<byte>();
            bytes.ResizeUninitialized(packedPaths.Length);
            UnsafeUtility.MemCpy(bytes.GetUnsafePtr(), packedPaths.GetUnsafePtr(), packedPaths.Length);
            baker.AddBuffer<MeshPathByteOffsetForPath>(blobBakingEntity).Reinterpret<int>().AddRange(pathStartingByteOffsets);
            return true;
        }
    }

    public struct MeshBindingPathsFromOptimizedAnimatorBakeData : ISmartBlobberRequestFilter<MeshBindingPathsBlob>
    {
        public UnityEngine.Animator            animator;
        public UnityEngine.SkinnedMeshRenderer skinnedMeshRenderer;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (animator == null)
            {
                UnityEngine.Debug.LogError(
                    $"Kinemation failed to bake mesh binding paths requested by a baker of {baker.GetAuthoringObjectForDebugDiagnostics().name}. The Animator is null.");
                return false;
            }
            if (animator.hasTransformHierarchy)
            {
                UnityEngine.Debug.LogError(
                    $"Kinemation failed to bake binding paths for {animator.gameObject.name}. The Animator is not an optimized hierarchy but was passed to a filter expecting one.");
                return false;
            }
            if (skinnedMeshRenderer == null)
            {
                UnityEngine.Debug.LogError($"Kinemation failed to bake mesh binding paths for {animator.gameObject.name}. The SkinnedMeshRenderer is null.");
                return false;
            }

            baker.AddBuffer<MeshPathByte>(             blobBakingEntity);
            baker.AddBuffer<MeshPathByteOffsetForPath>(blobBakingEntity);
            baker.AddComponent(blobBakingEntity, new SkinnedMeshRenderererReferenceForMeshPaths
            {
                skinnedMeshRenderer = skinnedMeshRenderer
            });
            baker.AddComponent(blobBakingEntity, new ShadowHierarchyRequest
            {
                animatorToBuildShadowFor = animator
            });
            baker.DependsOn(animator.avatar);
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct MeshPathByte : IBufferElementData
    {
        public byte pathByte;
    }

    [TemporaryBakingType]
    internal struct MeshPathByteOffsetForPath : IBufferElementData
    {
        public int offset;
    }

    [TemporaryBakingType]
    internal struct SkinnedMeshRenderererReferenceForMeshPaths : IComponentData
    {
        public UnityObjectRef<UnityEngine.SkinnedMeshRenderer> skinnedMeshRenderer;
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct MeshPathsSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<MeshBindingPathsBlob>().Register(state.World);
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
            public void Execute(ref SmartBlobberResult result, in DynamicBuffer<MeshPathByte> bytes, in DynamicBuffer<MeshPathByteOffsetForPath> offsets)
            {
                if (bytes.IsEmpty)
                    return;

                var builder = new BlobBuilder(Allocator.Temp);

                ref var root       = ref builder.ConstructRoot<MeshBindingPathsBlob>();
                var     pathsOuter = builder.Allocate(ref root.pathsInReversedNotation, offsets.Length);
                var     bytesArray = bytes.Reinterpret<byte>().AsNativeArray();
                for (int i = 0; i < offsets.Length; i++)
                {
                    int pathLength;
                    if (i + 1 == offsets.Length)
                        pathLength = bytesArray.Length - offsets[i].offset;
                    else
                        pathLength = offsets[i + 1].offset - offsets[i].offset;
                    builder.ConstructFromNativeArray(ref pathsOuter[i], bytesArray.GetSubArray(offsets[i].offset, pathLength));
                }
                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<MeshBindingPathsBlob>(Allocator.Persistent));
            }
        }
    }
}

