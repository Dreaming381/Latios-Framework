using Latios.Kinemation.InternalSourceGen;
using Latios.Psyshock;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UpdateSkeletonBoundsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_exposedSkeletonsQuery;
        EntityQuery m_exposedBonesQuery;
        EntityQuery m_optimizedSkeletonsQuery;

        WorldTransformReadOnlyTypeHandle m_worldTransformHandle;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_exposedSkeletonsQuery = state.Fluent().With<SkeletonWorldBoundsOffsetsFromPosition>(false).With<ExposedSkeletonCullingIndex>(true)
                                      .WithWorldTransformReadOnly().Build();

            m_exposedBonesQuery = state.Fluent().With<BoneBounds>(true).With<BoneWorldBounds>(false).WithWorldTransformReadOnly().Build();

            m_optimizedSkeletonsQuery = state.Fluent().With<OptimizedBoneBounds, OptimizedSkeletonState>(true).With<OptimizedBoneTransform>(false)
                                        .With<SkeletonWorldBoundsOffsetsFromPosition>(false).WithWorldTransformReadOnly().Build();

            m_worldTransformHandle = new WorldTransformReadOnlyTypeHandle(ref state);
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var lastSystemVersion = state.LastSystemVersion;
            var allocator         = state.WorldUpdateAllocator;
            m_worldTransformHandle.Update(ref state);
            var exposedCullingIndexManager = latiosWorld.worldBlackboardEntity.GetCollectionComponent<ExposedCullingIndexManager>(true);
            var inputJh                    = state.Dependency;

            var                            exposedJh             = inputJh;
            bool                           hasExposed            = !m_exposedSkeletonsQuery.IsEmptyIgnoreFilter;
            NativeArray<UnsafeBitArray>    perThreadBitArrays    = default;
            NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays = default;
            if (hasExposed)
            {
                perThreadBitArrays = CollectionHelper.CreateNativeArray<UnsafeBitArray>(JobsUtility.ThreadIndexCount, allocator);
                exposedJh          = new UpdateExposedBoneWorldBoundsJob
                {
                    boneBoundsHandle      = GetComponentTypeHandle<BoneBounds>(true),
                    worldTransformHandle  = m_worldTransformHandle,
                    boneWorldBoundsHandle = GetComponentTypeHandle<BoneWorldBounds>(false),
                    allocator             = allocator,
                    perThreadBitArrays    = perThreadBitArrays,
                    indexHandle           = GetComponentTypeHandle<BoneCullingIndex>(true),
                    maxBitIndex           = exposedCullingIndexManager.maxIndex,
                    lastSystemVersion     = lastSystemVersion
                }.ScheduleParallel(m_exposedBonesQuery, inputJh);

                m_exposedSkeletonsQuery.AddWorldTranformChangeFilter();
                exposedJh = new FlagDirtyExposedSkeletonRootsJob
                {
                    allocator          = allocator,
                    indexHandle        = GetComponentTypeHandle<ExposedSkeletonCullingIndex>(true),
                    lastSystemVersion  = lastSystemVersion,
                    maxBitIndex        = exposedCullingIndexManager.maxIndex,
                    perThreadBitArrays = perThreadBitArrays
                }.ScheduleParallel(m_exposedSkeletonsQuery, exposedJh);

                var ulongCount = new NativeReference<int>(allocator);
                exposedJh      = new CollapseBitsJob
                {
                    perThreadBitArrays = perThreadBitArrays,
                    ulongCount         = ulongCount
                }.Schedule(exposedJh);

                perThreadBoundsArrays = CollectionHelper.CreateNativeArray<UnsafeList<Aabb> >(JobsUtility.ThreadIndexCount, allocator);
                exposedJh             = new CombineExposedBoneBoundsPerThreadJob
                {
                    allocator             = allocator,
                    boundsHandle          = GetComponentTypeHandle<BoneWorldBounds>(true),
                    indexHandle           = GetComponentTypeHandle<BoneCullingIndex>(true),
                    maxBitIndex           = exposedCullingIndexManager.maxIndex,
                    perThreadBitArrays    = perThreadBitArrays,
                    perThreadBoundsArrays = perThreadBoundsArrays
                }.ScheduleParallel(m_exposedBonesQuery, exposedJh);

                exposedJh = new MergeExposedSkeletonBoundsJob
                {
                    perThreadBitArrays    = perThreadBitArrays,
                    perThreadBoundsArrays = perThreadBoundsArrays,
                    ulongCount            = ulongCount
                }.Schedule(ulongCount.GetUnsafePtrWithoutChecks(), 1, exposedJh);
            }

            var optimizedJh = new OptimizedBoneBoundsJob
            {
                boneBoundsHandle     = GetBufferTypeHandle<OptimizedBoneBounds>(true),
                boneTransformHandle  = GetBufferTypeHandle<OptimizedBoneTransform>(true),
                stateHandle          = GetComponentTypeHandle<OptimizedSkeletonState>(true),
                worldTransformHandle = m_worldTransformHandle,
                skeletonBoundsHandle = GetComponentTypeHandle<SkeletonWorldBoundsOffsetsFromPosition>(false),
                lastSystemVersion    = lastSystemVersion
            }.ScheduleParallel(m_optimizedSkeletonsQuery, inputJh);

            if (hasExposed)
            {
                m_exposedSkeletonsQuery.ResetFilter();
                state.Dependency = new GatherExposedSkeletonBoundsJob
                {
                    indexHandle           = GetComponentTypeHandle<ExposedSkeletonCullingIndex>(true),
                    skeletonBoundsHandle  = GetComponentTypeHandle<SkeletonWorldBoundsOffsetsFromPosition>(false),
                    worldTransformHandle  = m_worldTransformHandle,
                    perThreadBitArrays    = perThreadBitArrays,
                    perThreadBoundsArrays = perThreadBoundsArrays
                }.ScheduleParallel(m_exposedSkeletonsQuery, JobHandle.CombineDependencies(optimizedJh, exposedJh));
            }
            else
                state.Dependency = optimizedJh;
        }

        [BurstCompile]
        struct UpdateExposedBoneWorldBoundsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneBounds>       boneBoundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex> indexHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle      worldTransformHandle;
            [ReadOnly] public NativeReference<int>                  maxBitIndex;

            public ComponentTypeHandle<BoneWorldBounds>                              boneWorldBoundsHandle;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public Allocator                                                         allocator;

            public uint lastSystemVersion;

            [NativeSetThreadIndex] int threadIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(ref boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= worldTransformHandle.DidChange(in chunk, lastSystemVersion);
                if (!needsUpdate)
                    return;

                var perThreadBitArray = perThreadBitArrays[threadIndex];
                if (!perThreadBitArray.IsCreated)
                {
                    perThreadBitArray = new UnsafeBitArray(CollectionHelper.Align(maxBitIndex.Value + 1, 64),
                                                           allocator,
                                                           NativeArrayOptions.ClearMemory);
                    perThreadBitArrays[threadIndex] = perThreadBitArray;
                }

                var boneBounds      = (BoneBounds*)chunk.GetRequiredComponentDataPtrRO(ref boneBoundsHandle);
                var skeletonIndices = (BoneCullingIndex*)chunk.GetRequiredComponentDataPtrRO(ref indexHandle);
                var worldTransforms = worldTransformHandle.Resolve(in chunk);
                var worldBounds     = (BoneWorldBounds*)chunk.GetRequiredComponentDataPtrRW(ref boneWorldBoundsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var newBounds  = ComputeBounds(boneBounds[i].radialOffsetInBoneSpace, worldTransforms[i]);
                    worldBounds[i] = new BoneWorldBounds { bounds = newBounds };
                    perThreadBitArray.Set(skeletonIndices[i].cullingIndex, true);
                }
            }
        }

        // Require change filter on WorldTransform
        [BurstCompile]
        struct FlagDirtyExposedSkeletonRootsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex>       indexHandle;
            [ReadOnly] public NativeReference<int>                                   maxBitIndex;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public Allocator                                                         allocator;

            public uint lastSystemVersion;

            [NativeSetThreadIndex] int threadIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var perThreadBitArray = perThreadBitArrays[threadIndex];
                if (!perThreadBitArray.IsCreated)
                {
                    perThreadBitArray = new UnsafeBitArray(CollectionHelper.Align(maxBitIndex.Value + 1, 64),
                                                           allocator,
                                                           NativeArrayOptions.ClearMemory);
                    perThreadBitArrays[threadIndex] = perThreadBitArray;
                }

                var indices = (ExposedSkeletonCullingIndex*)chunk.GetRequiredComponentDataPtrRO(ref indexHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    perThreadBitArray.Set(indices[i].cullingIndex, true);
                }
            }
        }

        [BurstCompile]
        unsafe struct CollapseBitsJob : IJob
        {
            public NativeArray<UnsafeBitArray> perThreadBitArrays;
            public NativeReference<int>        ulongCount;

            public void Execute()
            {
                int startFrom = -1;
                for (int i = 0; i < perThreadBitArrays.Length; i++)
                {
                    if (perThreadBitArrays[i].IsCreated)
                    {
                        startFrom             = i + 1;
                        perThreadBitArrays[0] = perThreadBitArrays[i];
                        perThreadBitArrays[i] = default;
                        break;
                    }
                }

                if (startFrom == -1)
                {
                    // This happens if no bones have changed. Unlikely but possible.
                    // In this case, we will need to check for this in future jobs.
                    return;
                }

                for (int arrayIndex = startFrom; arrayIndex < perThreadBitArrays.Length; arrayIndex++)
                {
                    if (!perThreadBitArrays[arrayIndex].IsCreated)
                        continue;
                    var dstArray    = perThreadBitArrays[0];
                    var dstArrayPtr = dstArray.Ptr;
                    var srcArrayPtr = perThreadBitArrays[arrayIndex].Ptr;

                    for (int i = 0, bitCount = 0; bitCount < dstArray.Length; i++, bitCount += 64)
                    {
                        dstArrayPtr[i] |= srcArrayPtr[i];
                    }
                }
            }
        }

        [BurstCompile]
        struct CombineExposedBoneBoundsPerThreadJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<BoneWorldBounds>                      boundsHandle;
            [ReadOnly] public ComponentTypeHandle<BoneCullingIndex>                     indexHandle;
            [ReadOnly] public NativeReference<int>                                      maxBitIndex;
            [ReadOnly] public NativeArray<UnsafeBitArray>                               perThreadBitArrays;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;
            public Allocator                                                            allocator;

            [NativeSetThreadIndex] int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!perThreadBitArrays[0].IsCreated)
                    return;

                var perThreadBoundsArray = perThreadBoundsArrays[threadIndex];
                if (!perThreadBoundsArray.IsCreated)
                {
                    perThreadBoundsArray = new UnsafeList<Aabb>(maxBitIndex.Value + 1, allocator, NativeArrayOptions.UninitializedMemory);
                    perThreadBoundsArray.Resize(maxBitIndex.Value + 1);
                    perThreadBoundsArrays[threadIndex] = perThreadBoundsArray;
                    for (int i = 0; i < maxBitIndex.Value + 1; i++)
                    {
                        perThreadBoundsArray[i] = new Aabb(float.MaxValue, float.MinValue);
                    }
                }

                var indices = chunk.GetNativeArray(ref indexHandle);
                var bounds  = chunk.GetNativeArray(ref boundsHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var index = indices[i].cullingIndex;
                    if (perThreadBitArrays[0].IsSet(index))
                    {
                        perThreadBoundsArray[index] = Physics.CombineAabb(perThreadBoundsArray[index], bounds[i].bounds);
                    }
                }
            }
        }

        [BurstCompile]
        struct MergeExposedSkeletonBoundsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeReference<int>                                      ulongCount;  // Just here for safety sanity.
            [ReadOnly] public NativeArray<UnsafeBitArray>                               perThreadBitArrays;
            [NativeDisableParallelForRestriction] public NativeArray<UnsafeList<Aabb> > perThreadBoundsArrays;

            UnsafeList<UnsafeList<Aabb> > validArraysCache;

            public unsafe void Execute(int index)
            {
                var bitArray = perThreadBitArrays[0];
                if (!bitArray.IsCreated)
                    return;
                var bits = bitArray.Ptr[index];
                if (bits == 0)
                    return;

                if (!validArraysCache.IsCreated)
                {
                    validArraysCache = new UnsafeList<UnsafeList<Aabb> >(Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount, Allocator.Temp);
                    foreach (var array in perThreadBoundsArrays)
                    {
                        if (array.IsCreated)
                            validArraysCache.Add(array);
                    }
                }
                if (validArraysCache.Length == 1)
                    return;

                var aabbIndexBase = index * 64;
                for (int i = math.tzcnt(bits); bits != 0; bits ^= 1ul << i, i = math.tzcnt(bits))
                {
                    var     aabbIndex = aabbIndexBase + i;
                    ref var aabb      = ref validArraysCache[0].ElementAt(aabbIndex);
                    for (int j = 1; j < validArraysCache.Length; j++)
                    {
                        aabb = Physics.CombineAabb(aabb, validArraysCache[j][aabbIndex]);
                    }
                }
            }
        }

        [BurstCompile]
        struct GatherExposedSkeletonBoundsJob : IJobChunk
        {
            [ReadOnly] public WorldTransformReadOnlyTypeHandle                 worldTransformHandle;
            [ReadOnly] public ComponentTypeHandle<ExposedSkeletonCullingIndex> indexHandle;
            [ReadOnly] public NativeArray<UnsafeBitArray>                      perThreadBitArrays;
            [ReadOnly] public NativeArray<UnsafeList<Aabb> >                   perThreadBoundsArrays;
            public ComponentTypeHandle<SkeletonWorldBoundsOffsetsFromPosition> skeletonBoundsHandle;

            int oneBelowBoundsArrayIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var bitArray = perThreadBitArrays[0];
                if (!bitArray.IsCreated)
                    return;

                var   indices = (ExposedSkeletonCullingIndex*)chunk.GetRequiredComponentDataPtrRO(ref indexHandle);
                ulong lower   = 0, upper = 0;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (bitArray.IsSet(indices[i].cullingIndex))
                    {
                        if (i < 64)
                            lower |= 1ul << i;
                        else
                            upper |= 1ul << (i - 64);
                    }
                }

                if ((lower | upper) == 0)
                    return;

                if (oneBelowBoundsArrayIndex == 0)
                {
                    for (int i = 0; i < perThreadBoundsArrays.Length; i++)
                    {
                        if (perThreadBoundsArrays[i].IsCreated)
                        {
                            oneBelowBoundsArrayIndex = i - 1;
                            break;
                        }
                    }
                }
                var boundsArray = perThreadBoundsArrays[oneBelowBoundsArrayIndex + 1];

                var boundsOffsets = (SkeletonWorldBoundsOffsetsFromPosition*)chunk.GetRequiredComponentDataPtrRW(ref skeletonBoundsHandle);
                var transforms    = worldTransformHandle.Resolve(in chunk);
                var enumerator    = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var position               = transforms[i].position;
                    var bounds                 = boundsArray[indices[i].cullingIndex];
                    boundsOffsets[i].minOffset = bounds.min - position;
                    boundsOffsets[i].maxOffset = bounds.max - position;
                }
            }
        }

        [BurstCompile]
        struct OptimizedBoneBoundsJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<OptimizedBoneBounds>            boneBoundsHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneTransform>         boneTransformHandle;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonState>      stateHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle                 worldTransformHandle;
            public ComponentTypeHandle<SkeletonWorldBoundsOffsetsFromPosition> skeletonBoundsHandle;

            public uint lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool needsUpdate  = chunk.DidChange(ref boneBoundsHandle, lastSystemVersion);
                needsUpdate      |= chunk.DidChange(ref boneTransformHandle, lastSystemVersion);
                needsUpdate      |= worldTransformHandle.DidChange(in chunk, lastSystemVersion);
                if (!needsUpdate)
                    return;

                var boneBounds      = chunk.GetBufferAccessor(ref boneBoundsHandle);
                var boneTransforms  = chunk.GetBufferAccessor(ref boneTransformHandle);
                var states          = (OptimizedSkeletonState*)chunk.GetRequiredComponentDataPtrRO(ref stateHandle);
                var worldTransforms = worldTransformHandle.Resolve(in chunk);
                var skeletonBounds  = (SkeletonWorldBoundsOffsetsFromPosition*)chunk.GetRequiredComponentDataPtrRW(ref skeletonBoundsHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var newBounds               = ComputeBufferBounds(boneBounds[i], boneTransforms[i], worldTransforms[i], states[i]);
                    var position                = worldTransforms[i].position;
                    skeletonBounds[i].minOffset = newBounds.min - position;
                    skeletonBounds[i].maxOffset = newBounds.max - position;
                }
            }

            Aabb ComputeBufferBounds(DynamicBuffer<OptimizedBoneBounds>    bounds,
                                     DynamicBuffer<OptimizedBoneTransform> boneTransforms,
                                     in WorldTransformReadOnlyAspect worldTransform,
                                     OptimizedSkeletonState state)
            {
                var boundsArray             = bounds.Reinterpret<float>().AsNativeArray();
                var boneTransformsArrayFull = boneTransforms.Reinterpret<TransformQvvs>().AsNativeArray();
                var mask                    = state.state & OptimizedSkeletonState.Flags.RotationMask;
                var currentRotation         = OptimizedSkeletonState.CurrentFromMask[(byte)mask];
                var previousRotation        = OptimizedSkeletonState.PreviousFromMask[(byte)mask];
                currentRotation             = (state.state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty ? currentRotation : previousRotation;
                var boneTransformsArray     = boneTransformsArrayFull.GetSubArray(boundsArray.Length * 2 * currentRotation, boundsArray.Length);

                // Todo: Assert that buffers are not empty?
                var aabb = new Aabb(float.MaxValue, float.MinValue);
                if (worldTransformHandle.isNativeQvvs)
                {
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        aabb = Physics.CombineAabb(aabb, ComputeBounds(boundsArray[i], qvvs.mul(worldTransform.worldTransformQvvs, boneTransformsArray[i])));
                    }
                }
                else
                {
                    for (int i = 0; i < boundsArray.Length; i++)
                    {
                        aabb = Physics.CombineAabb(aabb, ComputeBounds(boundsArray[i], math.mul(worldTransform.matrix4x4, boneTransformsArray[i].ToMatrix4x4())));
                    }
                }
                return aabb;
            }
        }

        static Aabb ComputeBounds(float radius, in WorldTransformReadOnlyAspect worldTransform)
        {
            var aabb = new Aabb(-radius, radius);
            return Physics.TransformAabb(in worldTransform, in aabb);
        }

        static Aabb ComputeBounds(float radius, in TransformQvvs worldTransform)
        {
            var aabb = new Aabb(-radius, radius);
            return Physics.TransformAabb(in worldTransform, in aabb);
        }

        static Aabb ComputeBounds(float radius, in float4x4 worldTransform)
        {
            var aabb = new Aabb(-radius, radius);
            Physics.GetCenterExtents(aabb, out var center, out var extents);
            return Physics.TransformAabb(worldTransform, center, extents);
        }
    }
}

