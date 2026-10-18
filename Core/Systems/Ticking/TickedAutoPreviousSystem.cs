using System.Reflection;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;

namespace Latios.Systems
{
    [UpdateInGroup(typeof(TickedUpdateHistorySuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct TickedAutoPreviousSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        NativeList<TypePairState> typePairStates;
        uint                      twoAgoVersion;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            twoAgoVersion  = 0;
            typePairStates = new NativeList<TypePairState>(Allocator.Persistent);
            foreach (var typeInfo in TypeManager.AllTypes)
            {
                if (typeInfo.BakingOnlyType || typeInfo.TemporaryBakingType)
                    continue;
                TickedAutoPreviousAttribute attribute = null;
                try
                {
                    attribute = typeInfo.Type.GetCustomAttribute<TickedAutoPreviousAttribute>();
                }
                catch
                {
                }
                if (attribute == null)
                    continue;
                if (attribute.currentTickedType == null)
                    continue;

                TypeIndex currentTypeIndex = default;
                try
                {
                    currentTypeIndex = TypeManager.GetTypeIndex(attribute.currentTickedType);
                }
                catch
                {
                    throw new System.InvalidOperationException(
                        $"On {typeInfo.Type.FullName}, the TickedAutoPrevious attribute specifies a current type {attribute.currentTickedType.FullName} which is not a known component type.");
                }

                var currentType   = ComponentType.FromTypeIndex(currentTypeIndex);
                var previousType  = ComponentType.FromTypeIndex(typeInfo.TypeIndex);
                var nonTickedInfo = TypeManager.GetTypeInfo(currentTypeIndex);
                if (typeInfo.ElementSize != nonTickedInfo.ElementSize || typeInfo.TypeIndex.IsBuffer != currentTypeIndex.IsBuffer ||
                    typeInfo.TypeIndex.IsEnableable != currentTypeIndex.IsEnableable || typeInfo.TypeIndex.IsSharedComponentType || typeInfo.TypeIndex.IsManagedType)
                {
                    throw new System.InvalidOperationException(
                        $"On {typeInfo.Type.FullName}, the TickedAutoPrevious attribute specifies a current type {attribute.currentTickedType.FullName} which is not compatible for data copying either due to wrong component type or mismatched size.");
                }
                var query = state.Fluent().With(currentTypeIndex, false).With(typeInfo.TypeIndex, false).Build();
                query.AddChangedVersionFilter(currentType);
                typePairStates.Add(new TypePairState
                {
                    currentType    = currentType,
                    previousType   = previousType,
                    currentHandle  = state.GetDynamicComponentTypeHandle(currentType),
                    previousHandle = state.GetDynamicComponentTypeHandle(previousType),
                    query          = query,
                });
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            typePairStates.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var advance = !latiosWorld.worldBlackboardEntity.GetComponentData<TickingState>().discardPreviousTick;
            var jhs     = new NativeList<JobHandle>(typePairStates.Length, state.WorldUpdateAllocator);
            foreach (var typePairState in typePairStates)
            {
                if (!typePairState.query.IsEmptyIgnoreFilter)
                {
                    var srcType   = advance ? typePairState.currentType : typePairState.previousType;
                    var srcHandle = advance ? typePairState.currentHandle : typePairState.previousHandle;
                    srcHandle     = srcHandle.CopyToReadOnly();
                    var dstHandle = advance ? typePairState.previousHandle : typePairState.currentHandle;
                    if (advance)
                        typePairState.query.SetOverrideChangeFilterVersion(twoAgoVersion);
                    srcHandle.Update(ref state);
                    dstHandle.Update(ref state);
                    jhs.Add(new Job
                    {
                        srcHandle = srcHandle,
                        dstHandle = dstHandle,
                        srcType   = srcType,
                    }.Schedule(typePairState.query, state.Dependency));
                }
            }
            if (!jhs.IsEmpty)
            {
                state.Dependency = JobHandle.CombineDependencies(jhs.AsArray());
            }
            twoAgoVersion = state.LastSystemVersion;
        }

        struct TypePairState
        {
            public ComponentType              currentType;
            public ComponentType              previousType;
            public EntityQuery                query;
            public DynamicComponentTypeHandle currentHandle;
            public DynamicComponentTypeHandle previousHandle;
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public DynamicComponentTypeHandle srcHandle;
            public DynamicComponentTypeHandle            dstHandle;
            public ComponentType                         srcType;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var size = TypeManager.GetTypeInfo(srcType.TypeIndex).ElementSize;
                if (srcType.IsBuffer)
                {
                    var srcAccess = chunk.GetUntypedBufferAccessor(ref srcHandle);
                    var dstAccess = chunk.GetUntypedBufferAccessor(ref dstHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var srcPtr = srcAccess.GetUnsafeReadOnlyPtrAndLength(i, out var length);
                        dstAccess.ResizeUninitialized(i, length);
                        var dstPtr = dstAccess.GetUnsafePtr(i);
                        UnsafeUtility.MemCpy(dstPtr, srcPtr, length * (long)size);
                    }
                }
                else if (!srcType.IsZeroSized)
                {
                    var src = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref srcHandle, size);
                    var dst = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref dstHandle, size);
                    dst.CopyFrom(src);
                }
                if (srcType.IsEnableable)
                {
                    var srcBits = chunk.GetEnabledMask(ref srcHandle);
                    var dstBits = chunk.GetEnabledMask(ref dstHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        dstBits[i] = srcBits[i];
                    }
                }
            }
        }
    }
}

