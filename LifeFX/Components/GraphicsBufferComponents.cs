using System;
using System.Runtime.InteropServices;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

// Todo: Nothing in this file is ready yet.

namespace Latios.LifeFX
{
    public struct GraphicsSyncState
    {
        // Note: The user is allowed to replace the GraphicsSyncState to "refresh" the entity,
        // which basically kills the tracked instance and creates a new one. However, it is critical
        // that these four values stay in sync.
        internal int                              trackedEntityID;
        internal int                              trackedEntityVersion;
        internal int                              bufferIndex;
        internal UnityObjectRef<SpawnEventTunnel> spawnEventTunnel;

        public GraphicsSyncState(UnityObjectRef<SpawnEventTunnel> spawnEventTunnel)
        {
            trackedEntityID       = 0;
            trackedEntityVersion  = 0;
            bufferIndex           = 0;
            this.spawnEventTunnel = spawnEventTunnel;
        }
    }

    // If IEnableable, then enabled means "changed" and requires updates
    public interface IGraphicsSyncComponent<T> : IComponentData, IGraphicsSyncComponentBase where T : unmanaged
    {
        // Normally zero if you simply make GraphicsSyncState the first field.
        public abstract int GetGraphicsSyncStateFieldOffset();

        public abstract FixedString64Bytes GetGlobalShaderPropertyName();

        public abstract string GetUploadComputeShaderFromResourcesPath();

        // Assign the values of structs obtained by calls to GetCodeAssignedComponents() to fieldCodeAssignable to create byte offset bindings.
        // Use GetCodeAssignedBitPack() for bit-level granularity.
        // You can only fetch 256 bytes worth of source data in a pass (call to this function). Return true if you need an additional pass.
        // Otherwise, return false.
        public abstract bool Register(GraphicsSyncGatherRegistration registration, ref T fieldCodeAssignable, int passIndex);

        int IGraphicsSyncComponentBase.GetGraphicsSyncStateFieldOffsetBase() => GetGraphicsSyncStateFieldOffset();

        FixedString64Bytes IGraphicsSyncComponentBase.GetGlobalShaderPropertyNameBase() => GetGlobalShaderPropertyName();

        string IGraphicsSyncComponentBase.GetUploadComputeShaderFromResourcesPathBase() => GetUploadComputeShaderFromResourcesPath();

        int2 IGraphicsSyncComponentBase.GetTypeSizeAndAlignmentBase() => new int2(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>());

        bool IGraphicsSyncComponentBase.RegisterBase(GraphicsSyncGatherRegistration registration, ref Span<byte> fieldCodeAssignable, int passIndex)
        {
            var tSpan = MemoryMarshal.Cast<byte, T>(fieldCodeAssignable);
            return Register(registration, ref tSpan[0], passIndex);
        }
    }

    public class GraphicsSyncGatherRegistration
    {
        public unsafe T GetCodeAssignedComponent<T>() where T : unmanaged, IComponentData
        {
            if (nextByte + UnsafeUtility.SizeOf<T>() >= 256)
                throw new System.InvalidOperationException("Too many bytes used in a single registration pass.");
            T     result = default(T);
            byte* ptr    = (byte*)&result;
            var   range  = new AssignmentRange
            {
                type         = TypeManager.GetTypeIndex<T>(),
                start        = nextByte,
                count        = (byte)UnsafeUtility.SizeOf<T>(),
                bitPackStart = 0,
                bitPackCount = 0
            };
            for (int i = 0; i < range.count; i++)
            {
                ptr[i] = nextByte;
                nextByte++;
            }
            assignmentRanges.Add(range);
            return result;
        }

        public unsafe T GetCodeAssignedBitPack<T>(BitPackBuilder<T> builder) where T : unmanaged
        {
            if (nextByte + UnsafeUtility.SizeOf<T>() >= 256)
                throw new System.InvalidOperationException("Too many bytes used in a single registration pass.");

            T     result = default(T);
            byte* ptr    = (byte*)&result;
            var   range  = new AssignmentRange
            {
                type         = default,
                start        = nextByte,
                count        = UnsafeUtility.SizeOf<T>(),
                bitPackStart = bitPackRanges.Length,
                bitPackCount = builder.pendingRanges.Length
            };
            for (int i = 0; i < range.count; i++)
            {
                ptr[i] = nextByte;
                nextByte++;
            }
            assignmentRanges.Add(range);
            bitPackRanges.AddRange(builder.pendingRanges);
            return result;
        }

        public BitPackBuilder<TResult> GetBitPackBuilder<TResult>() where TResult : unmanaged
        {
            UnityEngine.Assertions.Assert.IsTrue(UnsafeUtility.SizeOf<TResult>() * 8 < ushort.MaxValue);
            return new BitPackBuilder<TResult>
            {
                pendingRanges = new UnsafeList<BitPackRange>(8, Allocator.Temp),
            };
        }

        public unsafe struct BitPackBuilder<TResult> where TResult : unmanaged
        {
            internal UnsafeList<BitPackRange> pendingRanges;
            internal fixed ulong              usedBits[32];

            public BitPackBuilder<TResult> AddEnabledBit<TComponent>(int bitIndex) where TComponent : unmanaged, IEnableableComponent
            {
                SetAndTestBits(bitIndex, 1);
                pendingRanges.Add(new BitPackRange
                {
                    type              = TypeManager.GetTypeIndex<TComponent>(),
                    componentBitStart = 0,
                    componentBitCount = 1,
                    isEnabledBit      = true,
                    isExistBit        = false,
                    isHasComponentBit = false,
                    packBitStart      = bitIndex
                });
                return this;
            }

            public BitPackBuilder<TResult> AddComponentExistBit<TComponent>(int bitIndex) where TComponent : unmanaged, IComponentData
            {
                SetAndTestBits(bitIndex, 1);
                pendingRanges.Add(new BitPackRange
                {
                    type              = TypeManager.GetTypeIndex<TComponent>(),
                    componentBitStart = 0,
                    componentBitCount = 1,
                    isEnabledBit      = false,
                    isExistBit        = false,
                    isHasComponentBit = true,
                    packBitStart      = bitIndex
                });
                return this;
            }

            public BitPackBuilder<TResult> AddBufferExistBit<TComponent>(int bitIndex) where TComponent : unmanaged, IBufferElementData
            {
                SetAndTestBits(bitIndex, 1);
                pendingRanges.Add(new BitPackRange
                {
                    type              = TypeManager.GetTypeIndex<TComponent>(),
                    componentBitStart = 0,
                    componentBitCount = 1,
                    isEnabledBit      = false,
                    isExistBit        = false,
                    isHasComponentBit = true,
                    packBitStart      = bitIndex
                });
                return this;
            }

            public BitPackBuilder<TResult> AddSharedComponentExistBit<TComponent>(int bitIndex) where TComponent : unmanaged, ISharedComponentData
            {
                SetAndTestBits(bitIndex, 1);
                pendingRanges.Add(new BitPackRange
                {
                    type              = TypeManager.GetTypeIndex<TComponent>(),
                    componentBitStart = 0,
                    componentBitCount = 1,
                    isEnabledBit      = false,
                    isExistBit        = false,
                    isHasComponentBit = true,
                    packBitStart      = bitIndex
                });
                return this;
            }

            public BitPackBuilder<TResult> AddSyncExistBit(int bitIndex)
            {
                SetAndTestBits(bitIndex, 1);
                pendingRanges.Add(new BitPackRange
                {
                    type              = default,
                    componentBitStart = 0,
                    componentBitCount = 1,
                    isEnabledBit      = false,
                    isExistBit        = true,
                    isHasComponentBit = false,
                    packBitStart      = bitIndex
                });
                return this;
            }

            public BitPackBuilder<TResult> AddBitsFromComponent<TComponent>(short componentBitStart, short bitCount, short packBitStart)
            {
                SetAndTestBits(packBitStart, bitCount);
                pendingRanges.Add(new BitPackRange
                {
                    type              = TypeManager.GetTypeIndex<TComponent>(),
                    componentBitStart = componentBitStart,
                    componentBitCount = bitCount,
                    isEnabledBit      = false,
                    packBitStart      = packBitStart
                });
                return this;
            }

            internal void SetAndTestBits(int firstBit, int bitCount)
            {
                if (firstBit + bitCount > 8 * UnsafeUtility.SizeOf<TResult>() || firstBit < 0 || bitCount < 1)
                    throw new System.ArgumentOutOfRangeException();

                for (int i = 0; i < bitCount; i++)
                {
                    var index      = firstBit + i;
                    var ulongIndex = index >> 6;
                    var bitIndex   = index & 0x3f;
                    var field      = new BitField64(usedBits[ulongIndex]);
                    if (field.IsSet(index))
                        throw new System.ArgumentException($"Bit {firstBit + i} is already used");
                    field.SetBits(index, true);
                    usedBits[ulongIndex] = field.Value;
                }
            }
        }

        internal struct AssignmentRange
        {
            public TypeIndex type;
            public int       start;
            public int       count;
            public int       bitPackStart;
            public int       bitPackCount;
        }

        internal struct BitPackRange
        {
            public TypeIndex type;
            public int       componentBitStart;
            public int       componentBitCount;
            public int       packBitStart;
            public bool      isEnabledBit;
            public bool      isHasComponentBit;
            public bool      isExistBit;
        }

        internal UnsafeList<AssignmentRange> assignmentRanges;
        internal UnsafeList<BitPackRange>    bitPackRanges;
        internal byte                        nextByte;
    }

    public interface IGraphicsSyncComponentBase
    {
        internal abstract int GetGraphicsSyncStateFieldOffsetBase();

        internal abstract FixedString64Bytes GetGlobalShaderPropertyNameBase();

        internal abstract string GetUploadComputeShaderFromResourcesPathBase();

        internal abstract int2 GetTypeSizeAndAlignmentBase();

        internal abstract bool RegisterBase(GraphicsSyncGatherRegistration registration, ref Span<byte> fieldCodeAssignable, int passIndex);
    }

    internal partial struct ShaderPropertyToGlobalBufferMap : ICollectionComponent
    {
        public NativeHashMap<int, GraphicsBufferUnmanaged> shaderPropertyToGlobalBufferMap;

        public JobHandle TryDispose(JobHandle inputDeps) => shaderPropertyToGlobalBufferMap.IsCreated ? shaderPropertyToGlobalBufferMap.Dispose(inputDeps) : inputDeps;
    }
}

