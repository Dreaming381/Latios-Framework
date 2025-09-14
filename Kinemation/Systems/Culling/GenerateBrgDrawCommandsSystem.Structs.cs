using System;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Rendering;
using UnityEngine.Rendering;

namespace Latios.Kinemation.Systems
{
    public partial struct GenerateBrgDrawCommandsSystem
    {
        unsafe struct DrawCommandSettings : IEquatable<DrawCommandSettings>
        {
            v256 packed;

            int hash
            {
                get => packed.SInt0;
                set => packed.SInt0 = value;
            }
            public BatchID batch
            {
                get => new BatchID { value = packed.UInt1 };
                set => packed.UInt1        = value.value;
            }
            public ushort splitMask
            {
                get => packed.UShort4;
                set => packed.UShort4 = value;
            }
            public ushort meshLod
            {
                get => packed.UShort5;
                set => packed.UShort5 = value;
            }
            public ushort submesh
            {
                get => packed.UShort6;
                set => packed.UShort6 = value;
            }
            public BatchMeshID mesh
            {
                // Need to flip the bit because SIMD uses signed integer compare. Todo: Does this actually help BRG at all?
                get
                {
                    var flipped                     = packed.UShort7 ^ 0x00008000u;
                    flipped                        |= (uint)packed.UShort8 << 16;
                    return new BatchMeshID { value  = flipped };
                }
                set
                {
                    var flipped    = value.value ^ 0x00008000;
                    packed.UShort7 = (ushort)(flipped & 0xffff);
                    packed.UShort8 = (ushort)(flipped >> 16);
                }
            }
            public BatchMaterialID material
            {
                get => new BatchMaterialID { value = packed.UShort9 | ((uint)packed.UShort10 << 16) };
                set
                {
                    packed.UShort9  = (ushort)(value.value & 0xffff);
                    packed.UShort10 = (ushort)(value.value >> 16);
                }
            }
            public BatchDrawCommandFlags flags
            {
                get => (BatchDrawCommandFlags)packed.UShort11;
                set => packed.UShort11 = (ushort)value;
            }
            public int filterIndex
            {
                get => packed.SInt6;
                set => packed.SInt6 = value;
            }
            public int renderingPriority
            {
                get => packed.SInt7;
                set => packed.SInt7 = value;
            }

            uint4x2 asUint4x2 => new uint4x2(new uint4(packed.UInt0, packed.UInt1, packed.UInt2, packed.UInt3), new uint4(packed.UInt4, packed.UInt5, packed.UInt6, packed.UInt7));

            public bool Equals(DrawCommandSettings other)
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    var ones = X86.Avx2.mm256_cmpeq_epi8(packed, packed);
                    var eq   = X86.Avx2.mm256_cmpeq_epi8(packed, other.packed);
                    return X86.Avx.mm256_testc_si256(eq, ones) != 0;
                }
                else
                {
                    return asUint4x2.Equals(other.asUint4x2);
                }
            }

            public int CompareTo(DrawCommandSettings other)
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    var a     = packed;
                    var b     = other.packed;
                    var aGt   = X86.Avx2.mm256_cmpgt_epi64(a, b);
                    var bGt   = X86.Avx2.mm256_cmpgt_epi64(b, a);
                    var aMask = math.asuint(X86.Avx2.mm256_movemask_epi8(aGt));
                    var bMask = math.asuint(X86.Avx2.mm256_movemask_epi8(bGt));
                    return aMask.CompareTo(bMask);
                }
                else if (Arm.Neon.IsNeonSupported)
                {
                    var a           = packed;
                    var b           = other.packed;
                    var ac0         = a.Lo128;
                    var bc0         = b.Lo128;
                    var ac1         = a.Hi128;
                    var bc1         = b.Hi128;
                    var a0          = Arm.Neon.vcgtq_s64(ac0, bc0);
                    var b0          = Arm.Neon.vcgtq_s64(bc0, ac0);
                    var a1          = Arm.Neon.vcgtq_s64(ac1, bc1);
                    var b1          = Arm.Neon.vcgtq_s64(bc1, ac1);
                    var aLower      = Arm.Neon.vshrn_n_u16(a0, 4);
                    var bLower      = Arm.Neon.vshrn_n_u16(b0, 4);
                    var aLowerUpper = Arm.Neon.vshrn_high_n_u16(aLower, a1, 4);
                    var bLowerUpper = Arm.Neon.vshrn_high_n_u16(bLower, b1, 4);
                    var aMask       = Arm.Neon.vshrn_n_u16(aLowerUpper, 4).ULong0;
                    var bMask       = Arm.Neon.vshrn_n_u16(bLowerUpper, 4).ULong0;
                    return aMask.CompareTo(bMask);
                }
                else
                {
                    var a     = asUint4x2;
                    var b     = other.asUint4x2;
                    var a0    = a.c0 > b.c0;
                    var b0    = b.c0 > a.c0;
                    var a1    = a.c1 > b.c1;
                    var b1    = b.c1 > a.c1;
                    var aMask = math.bitmask(a0) | (math.bitmask(a1) << 16);
                    var bMask = math.bitmask(b0) | (math.bitmask(b1) << 16);
                    return aMask.CompareTo(bMask);
                }
            }

            public override int GetHashCode() => hash;
            public void ComputeHashCode()
            {
                hash = 0;
                hash = asUint4x2.GetHashCode();
            }
            public bool hasSortingPosition => (flags & BatchDrawCommandFlags.HasSortingPosition) != 0;
        }

        unsafe struct EntityDrawSettings
        {
            public int           entityQword;
            public int           entityBit;
            public int           chunkStartIndex;
            public bool          complementLodCrossfade;
            public LodCrossfade* lodCrossfades;
            public float*        chunkTransforms;
            public int           transformStrideInFloats;
            public int           positionOffsetInFloats;
        }

        unsafe struct EntityDrawSettingsBatched
        {
            public ulong  lower;
            public ulong  upper;
            public int    chunkStartIndex;
            public int    instancesCount;
            public float* chunkTransforms;
            public int    transformStrideInFloats;
            public int    positionOffsetInFloats;
        }

        struct ChunkDrawCommand : IComparable<ChunkDrawCommand>
        {
            public DrawCommandSettings   Settings;
            public DrawCommandVisibility Visibility;

            public int CompareTo(ChunkDrawCommand other) => Settings.CompareTo(other.Settings);
        }

        unsafe struct DrawCommandWorkItem
        {
            public DrawStream<DrawCommandVisibility>.Header* Arrays;
            public int                                       BinIndex;
            public int                                       PrefixSumNumInstances;
        }

        unsafe struct DrawCommandVisibility
        {
            public fixed ulong   visibleInstances[2];
            public fixed ulong   crossfadeComplements[2];
            public float*        transformsPtr;
            public LodCrossfade* crossfadesPtr;
            public int           chunkStartIndex;
            public int           transformStrideInFloats;
            public int           positionOffsetInFloats;

            public DrawCommandVisibility(in EntityDrawSettings entitySettings)
            {
                chunkStartIndex         = entitySettings.chunkStartIndex;
                visibleInstances[0]     = 0;
                visibleInstances[1]     = 0;
                crossfadeComplements[0] = 0;
                crossfadeComplements[1] = 0;
                transformsPtr           = entitySettings.chunkTransforms;
                crossfadesPtr           = entitySettings.lodCrossfades;
                transformStrideInFloats = entitySettings.transformStrideInFloats;
                positionOffsetInFloats  = entitySettings.positionOffsetInFloats;
            }

            public DrawCommandVisibility(in EntityDrawSettingsBatched entitySettingsBatched)
            {
                chunkStartIndex         = entitySettingsBatched.chunkStartIndex;
                visibleInstances[0]     = 0;
                visibleInstances[1]     = 0;
                crossfadeComplements[0] = 0;
                crossfadeComplements[1] = 0;
                transformsPtr           = entitySettingsBatched.chunkTransforms;
                crossfadesPtr           = null;
                transformStrideInFloats = entitySettingsBatched.transformStrideInFloats;
                positionOffsetInFloats  = entitySettingsBatched.positionOffsetInFloats;
            }
        }

        [NoAlias]
        unsafe struct DrawCommandStream
        {
            private DrawStream<DrawCommandVisibility> m_Stream;
            private int                               m_PrevChunkStartIndex;
            [NoAlias]
            private DrawCommandVisibility* m_PrevVisibility;

            public DrawCommandStream(RewindableAllocator* allocator)
            {
                m_Stream              = new DrawStream<DrawCommandVisibility>(allocator);
                m_PrevChunkStartIndex = -1;
                m_PrevVisibility      = null;
            }

            public void Emit(RewindableAllocator* allocator, in EntityDrawSettings entitySettings)
            {
                DrawCommandVisibility* visibility;

                if (entitySettings.chunkStartIndex == m_PrevChunkStartIndex)
                {
                    visibility = m_PrevVisibility;
                }
                else
                {
                    visibility  = m_Stream.AppendElement(allocator);
                    *visibility = new DrawCommandVisibility(in entitySettings);
                }

                var bit                                                   = 1ul << entitySettings.entityBit;
                visibility->visibleInstances[entitySettings.entityQword] |= bit;
                if (entitySettings.complementLodCrossfade)
                    visibility->crossfadeComplements[entitySettings.entityQword] |= bit;

                m_PrevChunkStartIndex = entitySettings.chunkStartIndex;
                m_PrevVisibility      = visibility;
                m_Stream.AddInstances(1);
            }

            public void Emit(RewindableAllocator* allocator, in EntityDrawSettingsBatched entitySettingsBatched)
            {
                DrawCommandVisibility* visibility;

                if (entitySettingsBatched.chunkStartIndex == m_PrevChunkStartIndex)
                {
                    visibility = m_PrevVisibility;
                }
                else
                {
                    visibility  = m_Stream.AppendElement(allocator);
                    *visibility = new DrawCommandVisibility(in entitySettingsBatched);
                }

                visibility->visibleInstances[0] |= entitySettingsBatched.lower;
                visibility->visibleInstances[1] |= entitySettingsBatched.upper;

                m_PrevChunkStartIndex = entitySettingsBatched.chunkStartIndex;
                m_PrevVisibility      = visibility;
                m_Stream.AddInstances(entitySettingsBatched.instancesCount);
            }

            public DrawStream<DrawCommandVisibility> Stream => m_Stream;
        }

        [StructLayout(LayoutKind.Sequential, Size = 128)]  // Force instances on separate cache lines
        unsafe struct ThreadLocalDrawCommands
        {
            // Store the actual streams in a separate array so we can mutate them in place,
            // the hash map only supports a get/set API.
            public UnsafeHashMap<DrawCommandSettings, int> DrawCommandStreamIndices;
            public UnsafeList<DrawCommandStream>           DrawCommands;
            public ThreadLocalAllocator                    ThreadLocalAllocator;

            public ThreadLocalDrawCommands(int capacity, ThreadLocalAllocator tlAllocator, int threadIndex)
            {
                var allocator            = tlAllocator.ThreadAllocator(threadIndex)->Handle;
                DrawCommandStreamIndices = new UnsafeHashMap<DrawCommandSettings, int>(capacity, allocator);
                DrawCommands             = new UnsafeList<DrawCommandStream>(capacity, allocator);
                ThreadLocalAllocator     = tlAllocator;
            }

            public bool IsCreated => DrawCommandStreamIndices.IsCreated;

            public bool Emit(in DrawCommandSettings commandSettings, in EntityDrawSettings entitySettings, int threadIndex)
            {
                var allocator = ThreadLocalAllocator.ThreadAllocator(threadIndex);

                if (DrawCommandStreamIndices.TryGetValue(commandSettings, out int streamIndex))
                {
                    DrawCommandStream* stream = DrawCommands.Ptr + streamIndex;
                    stream->Emit(allocator, in entitySettings);
                    return false;
                }
                else
                {
                    streamIndex = DrawCommands.Length;
                    DrawCommands.Add(new DrawCommandStream(allocator));
                    DrawCommandStreamIndices.Add(commandSettings, streamIndex);

                    DrawCommandStream* stream = DrawCommands.Ptr + streamIndex;
                    stream->Emit(allocator, in entitySettings);

                    return true;
                }
            }

            public bool Emit(in DrawCommandSettings commandSettings, in EntityDrawSettingsBatched entitySettingsBatched, int threadIndex)
            {
                var allocator = ThreadLocalAllocator.ThreadAllocator(threadIndex);

                if (DrawCommandStreamIndices.TryGetValue(commandSettings, out int streamIndex))
                {
                    DrawCommandStream* stream = DrawCommands.Ptr + streamIndex;
                    stream->Emit(allocator, in entitySettingsBatched);
                    return false;
                }
                else
                {
                    streamIndex = DrawCommands.Length;
                    DrawCommands.Add(new DrawCommandStream(allocator));
                    DrawCommandStreamIndices.Add(commandSettings, streamIndex);

                    DrawCommandStream* stream = DrawCommands.Ptr + streamIndex;
                    stream->Emit(allocator, in entitySettingsBatched);

                    return true;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]  // Force instances on separate cache lines
        unsafe struct ThreadLocalCollectBuffer
        {
            public static readonly int kCollectBufferSize = ChunkDrawCommandOutput.NumThreads;

            public UnsafeList<DrawCommandWorkItem> WorkItems;

            public void EnsureCapacity(UnsafeList<DrawCommandWorkItem>.ParallelWriter dst, int count, ThreadLocalAllocator tlAllocator, int threadIndex)
            {
                Assert.IsTrue(count <= kCollectBufferSize);

                if (!WorkItems.IsCreated)
                {
                    var allocator = tlAllocator.ThreadAllocator(threadIndex)->Handle;
                    WorkItems     = new UnsafeList<DrawCommandWorkItem>(
                        kCollectBufferSize,
                        allocator,
                        NativeArrayOptions.UninitializedMemory);
                }

                if (WorkItems.Length + count > WorkItems.Capacity)
                    Flush(dst);
            }

            public void Flush(UnsafeList<DrawCommandWorkItem>.ParallelWriter dst)
            {
                dst.AddRangeNoResize(WorkItems.Ptr, WorkItems.Length);
                WorkItems.Clear();
            }

            public void Add(DrawCommandWorkItem workItem) => WorkItems.Add(workItem);
        }

        unsafe struct DrawBinCollector
        {
            public static readonly int NumThreads = ChunkDrawCommandOutput.NumThreads;

            public IndirectList<DrawCommandSettings>           Bins;
            private UnsafeParallelHashSet<DrawCommandSettings> m_BinSet;
            private UnsafeList<ThreadLocalDrawCommands>        m_ThreadLocalDrawCommands;

            public DrawBinCollector(UnsafeList<ThreadLocalDrawCommands> tlDrawCommands, RewindableAllocator* allocator)
            {
                Bins                      = new IndirectList<DrawCommandSettings>(0, allocator);
                m_BinSet                  = new UnsafeParallelHashSet<DrawCommandSettings>(0, allocator->Handle);
                m_ThreadLocalDrawCommands = tlDrawCommands;
            }

            [BurstCompile]
            struct AllocateBinsJob : IJob
            {
                public IndirectList<DrawCommandSettings>          Bins;
                public UnsafeParallelHashSet<DrawCommandSettings> BinSet;
                public UnsafeList<ThreadLocalDrawCommands>        ThreadLocalDrawCommands;

                public void Execute()
                {
                    int numBinsUpperBound = 0;

                    for (int i = 0; i < NumThreads; ++i)
                        numBinsUpperBound += ThreadLocalDrawCommands.ElementAt(i).DrawCommands.Length;

                    Bins.SetCapacity(numBinsUpperBound);
                    BinSet.Capacity = numBinsUpperBound;
                }
            }

            [BurstCompile]
            struct CollectBinsJob : IJobParallelFor
            {
                public const int ThreadLocalArraySize = 256;

                public IndirectList<DrawCommandSettings>                         Bins;
                public UnsafeParallelHashSet<DrawCommandSettings>.ParallelWriter BinSet;
                public UnsafeList<ThreadLocalDrawCommands>                       ThreadLocalDrawCommands;

                private UnsafeList<DrawCommandSettings>.ParallelWriter m_BinsParallel;

                public void Execute(int index)
                {
                    ref var drawCommands = ref ThreadLocalDrawCommands.ElementAt(index);
                    if (!drawCommands.IsCreated)
                        return;

                    m_BinsParallel = Bins.List->AsParallelWriter();

                    var uniqueSettings = new NativeArray<DrawCommandSettings>(
                        ThreadLocalArraySize,
                        Allocator.Temp,
                        NativeArrayOptions.UninitializedMemory);
                    int numSettings = 0;

                    var keys = drawCommands.DrawCommandStreamIndices.GetEnumerator();
                    while (keys.MoveNext())
                    {
                        var settings = keys.Current.Key;
                        if (BinSet.Add(settings))
                            AddBin(uniqueSettings, ref numSettings, settings);
                    }
                    keys.Dispose();

                    Flush(uniqueSettings, numSettings);
                }

                private void AddBin(
                    NativeArray<DrawCommandSettings> uniqueSettings,
                    ref int numSettings,
                    DrawCommandSettings settings)
                {
                    if (numSettings >= ThreadLocalArraySize)
                    {
                        Flush(uniqueSettings, numSettings);
                        numSettings = 0;
                    }

                    uniqueSettings[numSettings] = settings;
                    ++numSettings;
                }

                private void Flush(
                    NativeArray<DrawCommandSettings> uniqueSettings,
                    int numSettings)
                {
                    if (numSettings <= 0)
                        return;

                    m_BinsParallel.AddRangeNoResize(
                        uniqueSettings.GetUnsafeReadOnlyPtr(),
                        numSettings);
                }
            }

            public JobHandle ScheduleFinalize(JobHandle dependency)
            {
                var allocateDependency = new AllocateBinsJob
                {
                    Bins                    = Bins,
                    BinSet                  = m_BinSet,
                    ThreadLocalDrawCommands = m_ThreadLocalDrawCommands,
                }.Schedule(dependency);

                return new CollectBinsJob
                {
                    Bins                    = Bins,
                    BinSet                  = m_BinSet.AsParallelWriter(),
                    ThreadLocalDrawCommands = m_ThreadLocalDrawCommands,
                }.Schedule(NumThreads, 1, allocateDependency);
            }

            public void RunFinalizeImmediate()
            {
                var allocateJob = new AllocateBinsJob
                {
                    Bins                    = Bins,
                    BinSet                  = m_BinSet,
                    ThreadLocalDrawCommands = m_ThreadLocalDrawCommands,
                };
                allocateJob.Execute();
                var collectJob = new CollectBinsJob
                {
                    Bins                    = Bins,
                    BinSet                  = m_BinSet.AsParallelWriter(),
                    ThreadLocalDrawCommands = m_ThreadLocalDrawCommands,
                };
                for (int i = 0; i < NumThreads; i++)
                {
                    collectJob.Execute(i);
                }
            }
        }

        [NoAlias]
        unsafe struct ChunkDrawCommandOutput
        {
            public const Allocator kAllocator = Allocator.TempJob;

#if UNITY_2022_2_14F1_OR_NEWER
            public static readonly int NumThreads = JobsUtility.ThreadIndexCount;
#else
            public static readonly int NumThreads = JobsUtility.MaxJobThreadCount;
#endif

            public static readonly int kNumThreadsBitfieldLength = (NumThreads + 63) / 64;
            public const int           kBinPresentFilterSize     = 1 << 10;

            public UnsafeList<ThreadLocalDrawCommands>  ThreadLocalDrawCommands;
            public UnsafeList<ThreadLocalCollectBuffer> ThreadLocalCollectBuffers;

            public UnsafeList<long> BinPresentFilter;

            public DrawBinCollector BinCollector;
            public IndirectList<DrawCommandSettings> UnsortedBins => BinCollector.Bins;

            [NativeDisableUnsafePtrRestriction]
            public IndirectList<int> SortedBins;

            [NativeDisableUnsafePtrRestriction]
            public IndirectList<DrawCommandBin> BinIndices;

            [NativeDisableUnsafePtrRestriction]
            public IndirectList<DrawCommandWorkItem> WorkItems;

            [NativeDisableParallelForRestriction]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<BatchCullingOutputDrawCommands> CullingOutput;

            public int BinCapacity;

            public ThreadLocalAllocator ThreadLocalAllocator;

            public ProfilerMarker ProfilerEmit;

#pragma warning disable 649
            [NativeSetThreadIndex] public int ThreadIndex;
#pragma warning restore 649

            public ChunkDrawCommandOutput(
                int initialBinCapacity,
                ThreadLocalAllocator tlAllocator,
                BatchCullingOutput cullingOutput)
            {
                BinCapacity   = initialBinCapacity;
                CullingOutput = cullingOutput.drawCommands;

                ThreadLocalAllocator = tlAllocator;
                var generalAllocator = ThreadLocalAllocator.GeneralAllocator;

                ThreadLocalDrawCommands = new UnsafeList<ThreadLocalDrawCommands>(
                    NumThreads,
                    generalAllocator->Handle,
                    NativeArrayOptions.ClearMemory);
                ThreadLocalDrawCommands.Resize(ThreadLocalDrawCommands.Capacity);
                ThreadLocalCollectBuffers = new UnsafeList<ThreadLocalCollectBuffer>(
                    NumThreads,
                    generalAllocator->Handle,
                    NativeArrayOptions.ClearMemory);
                ThreadLocalCollectBuffers.Resize(ThreadLocalCollectBuffers.Capacity);
                BinPresentFilter = new UnsafeList<long>(
                    kBinPresentFilterSize * kNumThreadsBitfieldLength,
                    generalAllocator->Handle,
                    NativeArrayOptions.ClearMemory);
                BinPresentFilter.Resize(BinPresentFilter.Capacity);

                BinCollector = new DrawBinCollector(ThreadLocalDrawCommands, generalAllocator);
                SortedBins   = new IndirectList<int>(0, generalAllocator);
                BinIndices   = new IndirectList<DrawCommandBin>(0, generalAllocator);
                WorkItems    = new IndirectList<DrawCommandWorkItem>(0, generalAllocator);

                // Initialized by job system
                ThreadIndex = 0;

                ProfilerEmit = new ProfilerMarker("Emit");
            }

            public void InitializeForEmitThread()
            {
                // First to use the thread local initializes is, but don't double init
                if (!ThreadLocalDrawCommands[ThreadIndex].IsCreated)
                    ThreadLocalDrawCommands[ThreadIndex] = new ThreadLocalDrawCommands(BinCapacity, ThreadLocalAllocator, ThreadIndex);
            }

            public BatchCullingOutputDrawCommands* CullingOutputDrawCommands =>
            (BatchCullingOutputDrawCommands*)CullingOutput.GetUnsafePtr();

            public static T* Malloc<T>(int count) where T : unmanaged
            {
                return (T*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<T>() * count,
                    UnsafeUtility.AlignOf<T>(),
                    kAllocator);
            }

            private ThreadLocalDrawCommands* DrawCommands
            {
                [return : NoAlias]
                get => ThreadLocalDrawCommands.Ptr + ThreadIndex;
            }

            public ThreadLocalCollectBuffer* CollectBuffer
            {
                [return : NoAlias]
                get => ThreadLocalCollectBuffers.Ptr + ThreadIndex;
            }

            public void Emit(ref DrawCommandSettings commandSettings, in EntityDrawSettings entitySettings)
            {
                // Update the cached hash code here, so all processing after this can just use the cached value
                // without recomputing the hash each time.
                commandSettings.ComputeHashCode();

                bool newBinAdded = DrawCommands->Emit(in commandSettings, in entitySettings, ThreadIndex);
                if (newBinAdded)
                {
                    MarkBinPresentInThread(in commandSettings, ThreadIndex);
                }
            }

            public void Emit(ref DrawCommandSettings commandSettings, in EntityDrawSettingsBatched entitySettingsBatched)
            {
                // Update the cached hash code here, so all processing after this can just use the cached value
                // without recomputing the hash each time.
                commandSettings.ComputeHashCode();

                bool newBinAdded = DrawCommands->Emit(in commandSettings, in entitySettingsBatched, ThreadIndex);
                if (newBinAdded)
                {
                    MarkBinPresentInThread(in commandSettings, ThreadIndex);
                }
            }

            [return : NoAlias]
            public long* BinPresentFilterForSettings(in DrawCommandSettings settings)
            {
                uint hash  = (uint)settings.GetHashCode();
                uint index = hash % (uint)kBinPresentFilterSize;
                return BinPresentFilter.Ptr + index * kNumThreadsBitfieldLength;
            }

            private void MarkBinPresentInThread(in DrawCommandSettings settings, int threadIndex)
            {
                long* settingsFilter = BinPresentFilterForSettings(in settings);

                uint threadQword = (uint)threadIndex / 64;
                uint threadBit   = (uint)threadIndex % 64;

                AtomicHelpers.AtomicOr(
                    settingsFilter,
                    (int)threadQword,
                    1L << (int)threadBit);
            }

            public static int FastHash<T>(T value) where T : struct
            {
                // TODO: Replace with hardware CRC32?
                return (int)xxHash3.Hash64(UnsafeUtility.AddressOf(ref value), UnsafeUtility.SizeOf<T>()).x;
            }
        }
    }
}

