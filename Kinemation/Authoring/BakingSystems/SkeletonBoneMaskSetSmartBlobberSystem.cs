using Latios.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    public static class SkeletonBoneMaskSetBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a SkeletonBoneMaskSetBlob Blob Asset
        /// </summary>
        /// <param name="animator">An animator on which to map the mask onto.</param>
        /// <param name="masks">An array of masks which should be baked into the blob asset.
        /// This array can be temp-allocated.</param>
        /// <param name="enableRoots">An optional array to explicitly specify whether the root
        /// bone with root motion should be enabled in the mask. False by default.</param>
        public static SmartBlobberHandle<SkeletonBoneMaskSetBlob> RequestCreateBlobAsset(this IBaker baker,
                                                                                         Animator animator,
                                                                                         NativeArray<UnityObjectRef<AvatarMask> > masks,
                                                                                         NativeArray<bool>                        enableRoots = default)
        {
            return baker.RequestCreateBlobAsset<SkeletonBoneMaskSetBlob, SkeletonBoneMaskSetBakeData>(new SkeletonBoneMaskSetBakeData
            {
                animator    = animator,
                masks       = masks,
                enableRoots = enableRoots
            });
        }
    }

    public struct SkeletonBoneMaskSetBakeData : ISmartBlobberRequestFilter<SkeletonBoneMaskSetBlob>
    {
        /// <summary>
        /// The UnityEngine.Animator that should be used to bind the Avatar masks to runtime bone indices.
        /// The converted mask set will only work correctly with that GameObject's converted skeleton entity
        /// or another skeleton entity with an identical hierarchy.
        /// </summary>
        public Animator animator;
        /// <summary>
        /// The list of Avatar masks which should be baked into the mask set.
        /// </summary>
        public NativeArray<UnityObjectRef<AvatarMask> > masks;
        /// <summary>
        /// AvatarMask does not allow for directly specifying the enabled state of the skeleton root (Animator GameObject)
        /// via the visual editors. By default, it is assumed disabled, as typically root motion is handled in the base layer.
        /// This array can be used to override it.
        /// </summary>
        public NativeArray<bool> enableRoots;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (animator == null)
            {
                Debug.LogError($"Kinemation failed to bake clip set requested by a baker of {baker.GetAuthoringObjectForDebugDiagnostics().name}. The Animator was null.");
                return false;
            }
            if (!masks.IsCreated)
            {
                Debug.LogError($"Kinemation failed to bake mask set on animator {animator.gameObject.name}. The mask array was not allocated.");
                return false;
            }
            if (enableRoots.IsCreated && enableRoots.Length != masks.Length)
            {
                Debug.LogWarning(
                    $"The length of enableRoots {enableRoots.Length} does not match the length of masks {masks.Length} for animator {animator.gameObject.name}. Default will be applied for missing indices.");
            }

            int i = 0;
            foreach (var mask in masks)
            {
                if (mask.GetHashCode() == 0)
                {
                    Debug.LogError($"Kinemation failed to bake mask set set on animator {animator.gameObject.name}. Mask at index {i} was null.");
                }
                i++;
            }

            baker.DependsOn(animator.avatar);
            var masksBuffer = baker.AddBuffer<AvatarMaskToBake>(blobBakingEntity);
            i               = 0;
            foreach (var mask in masks)
            {
                baker.DependsOn(mask.Value);
                masksBuffer.Add(new AvatarMaskToBake
                {
                    mask       = mask,
                    enableRoot = enableRoots.IsCreated && i < enableRoots.Length && enableRoots[i]
                });
                i++;
            }
            var boneNames = baker.AddBuffer<SkeletonBoneNameInHierarchy>(blobBakingEntity);
            if (animator.hasTransformHierarchy)
            {
                var boneList = new NativeList<SkeletonBoneNameInHierarchy>(Allocator.Temp);
                baker.CreateBoneNamesForExposedSkeleton(animator, boneList);
                boneNames.AddRange(boneList.AsArray());
            }
            else
            {
                boneNames.Add(new SkeletonBoneNameInHierarchy { boneName = animator.gameObject.name, parentIndex = -1 });
                baker.AddComponent(blobBakingEntity, new ShadowHierarchyRequest
                {
                    animatorToBuildShadowFor = animator
                });
            }
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct AvatarMaskToBake : IBufferElementData
    {
        public UnityObjectRef<AvatarMask> mask;
        public bool                       enableRoot;
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct SkeletonBoneMaskSetSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<SkeletonBoneMaskSetBlob>().Register(state.World);
        }

        public void OnUpdate(ref SystemState state)
        {
            var map                                   = new NativeHashMap<UnityObjectRef<AvatarMask>, int>(64, state.WorldUpdateAllocator);
            new GatherUniqueMasksJob { maskToIndexMap = map }.Schedule();
            state.CompleteDependency();

            var maskStrings = CollectionHelper.CreateNativeArray<UnsafeList<UnsafeText> >(map.Count, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var maskNames   = CollectionHelper.CreateNativeArray<FixedString128Bytes>(maskStrings.Length, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            foreach (var pair in map)
            {
                var mask         = pair.Key.Value;
                var index        = pair.Value;
                maskNames[index] = mask.name;

                var boneCount = mask.transformCount;

                var stringList = new UnsafeList<UnsafeText>(boneCount, state.WorldUpdateAllocator);
                for (int i = 0; i < boneCount; i++)
                {
                    if (mask.GetTransformActive(i))
                    {
                        var s           = mask.GetTransformPath(i);
                        var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(s);
                        var text        = new UnsafeText(sizeInBytes + 1, state.WorldUpdateAllocator);
                        text.Append(s);
                        stringList.Add(text);
                    }
                }
                maskStrings[index] = stringList;
            }

            state.Dependency = new ReverseStringsJob { maskStrings = maskStrings }.ScheduleParallel(maskStrings.Length, 1, default);
            new BuildMaskBlobsJob
            {
                maskStrings    = maskStrings,
                maskNames      = maskNames,
                maskToIndexMap = map
            }.ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct GatherUniqueMasksJob : IJobEntity
        {
            public NativeHashMap<UnityObjectRef<AvatarMask>, int> maskToIndexMap;

            public void Execute(in DynamicBuffer<AvatarMaskToBake> buffer)
            {
                foreach (var mask in buffer)
                {
                    maskToIndexMap.TryAdd(mask.mask, maskToIndexMap.Count);
                }
            }
        }

        [BurstCompile]
        struct ReverseStringsJob : IJobFor
        {
            public NativeArray<UnsafeList<UnsafeText> > maskStrings;

            UnsafeText       temp;
            UnsafeList<int2> nameRange;

            public void Execute(int i)
            {
                if (!temp.IsCreated)
                {
                    temp      = new UnsafeText(256, Allocator.Temp);
                    nameRange = new UnsafeList<int2>(64, Allocator.Temp);
                }

                var maskList = maskStrings[i];
                for (int j = 0; j < maskList.Length; j++)
                {
                    temp.Clear();
                    temp.Append(maskList[j]);
                    nameRange.Clear();
                    nameRange.Add(new int2(0, 0));
                    for (int k = 0; k < temp.Length; )
                    {
                        var rune = temp.Peek(k);
                        if (rune == '/')
                        {
                            nameRange.ElementAt(nameRange.Length - 1).y = k;
                            temp.Read(ref k);
                            nameRange.Add(new int2(k, 0));
                        }
                        else
                            temp.Read(ref k);
                    }

                    // AvatarMask omits the root GameObject from paths, instead using an empty string for the root GameObject path.
                    // This root GameObject is always enabled.
                    if (nameRange.ElementAt(nameRange.Length - 1).y == 0)
                        nameRange.ElementAt(nameRange.Length - 1).y = temp.Length;

                    var mask = maskList[j];
                    mask.Clear();
                    for (int k = nameRange.Length - 1; k >= 0; k--)
                    {
                        var range = nameRange[k];
                        for (int m = range.x; m < range.y;)
                        {
                            mask.Append(temp.Read(ref m));
                        }
                        mask.Append('/');
                    }
                    maskList[j] = mask;
                }
            }
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct BuildMaskBlobsJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<UnityObjectRef<AvatarMask>, int> maskToIndexMap;
            [ReadOnly] public NativeArray<UnsafeList<UnsafeText> >           maskStrings;
            [ReadOnly] public NativeArray<FixedString128Bytes>               maskNames;

            UnsafeText temp;

            public void Execute(ref SmartBlobberResult result, in DynamicBuffer<AvatarMaskToBake> buffer, in DynamicBuffer<SkeletonBoneNameInHierarchy> bones)
            {
                if (!temp.IsCreated)
                    temp = new UnsafeText(256, Allocator.Temp);

                var maskSize = CollectionHelper.Align(bones.Length, 64) / 64;

                var     builder       = new BlobBuilder(Allocator.Temp);
                ref var root          = ref builder.ConstructRoot<SkeletonBoneMaskSetBlob>();
                var     masksArray    = builder.Allocate(ref root.masks, buffer.Length);
                var     maskNameArray = builder.Allocate(ref root.maskNames, buffer.Length);
                int     maskIndex     = 0;
                foreach (var mask in buffer)
                {
                    maskToIndexMap.TryGetValue(mask.mask, out var index);
                    maskNameArray[maskIndex] = maskNames[index];

                    var maskBits = builder.Allocate(ref masksArray[maskIndex], maskSize);
                    for (int i = 0; i < maskBits.Length; i++)
                        maskBits[i] = 0;

                    var strings  = maskStrings[index];
                    var boneSpan = bones.AsNativeArray().AsReadOnlySpan();
                    for (int i = math.select(1, 0, mask.enableRoot); i < bones.Length; i++)
                    {
                        temp.Clear();
                        temp.AppendBoneReversePath(boneSpan, i, false);

                        for (int j = 0; j < strings.Length; j++)
                        {
                            var s = strings[j];
                            if (s.StartsWith(temp))
                            {
                                int element        = i >> 6;
                                int bit            = i & 0x3f;
                                maskBits[element] |= 1ul << bit;
                                break;
                            }
                        }
                    }

                    maskIndex++;
                }

                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<SkeletonBoneMaskSetBlob>(Allocator.Persistent));
            }
        }
    }
}

