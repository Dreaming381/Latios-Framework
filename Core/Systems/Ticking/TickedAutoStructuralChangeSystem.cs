using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

// Todo: This system's implementation currently performs structural changes one component at a time.
// This will be improved once setting archetypes on arrays of entities is supported.

namespace Latios.Systems
{
    [UpdateInGroup(typeof(TickedArchetypeCorrectionSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct TickedAutoStructuralChangeSystem : ISystem
    {
        NativeList<TypePairState> typePairStates;

        public void OnCreate(ref SystemState state)
        {
            typePairStates = new NativeList<TypePairState>(Allocator.Persistent);
            foreach (var typeInfo in TypeManager.AllTypes)
            {
                if (typeInfo.BakingOnlyType || typeInfo.TemporaryBakingType)
                    continue;
                TickedAutoAddAttribute attribute = null;
                try
                {
                    attribute = typeInfo.Type.GetCustomAttribute<TickedAutoAddAttribute>();
                }
                catch
                {
                }
                if (attribute == null)
                    continue;
                if (attribute.referenceType == null)
                    continue;

                TypeIndex referenceTypeIndex = default;
                try
                {
                    referenceTypeIndex = TypeManager.GetTypeIndex(attribute.referenceType);
                }
                catch
                {
                    throw new System.InvalidOperationException(
                        $"On {typeInfo.Type.FullName}, the TickedAutoAdd attribute specifies a non-ticked type {attribute.referenceType.FullName} which is not a known component type.");
                }

                var referenceType = ComponentType.FromTypeIndex(referenceTypeIndex);
                var tickingType   = ComponentType.FromTypeIndex(typeInfo.TypeIndex);
                if (attribute.copyData)
                {
                    var nonTickedInfo = TypeManager.GetTypeInfo(referenceTypeIndex);
                    if (typeInfo.ElementSize != nonTickedInfo.ElementSize || typeInfo.TypeIndex.IsBuffer != referenceTypeIndex.IsBuffer ||
                        typeInfo.TypeIndex.IsEnableable != referenceTypeIndex.IsEnableable || typeInfo.TypeIndex.IsSharedComponentType || typeInfo.TypeIndex.IsManagedType)
                    {
                        throw new System.InvalidOperationException(
                            $"On {typeInfo.Type.FullName}, the TickedAutoAdd attribute specifies a non-ticked type {attribute.referenceType.FullName} which is not compatible for data copying either due to wrong component type or mismatched size.");
                    }
                }
                if (attribute.removeReferenceForTickedOnly)
                {
                    typePairStates.Add(new TypePairState
                    {
                        referenceType       = referenceType,
                        tickingType         = tickingType,
                        referenceHandle     = attribute.copyData ? state.GetDynamicComponentTypeHandle(referenceType) : default,
                        tickingHandle       = attribute.copyData ? state.GetDynamicComponentTypeHandle(tickingType) : default,
                        missingTickingQuery = state.Fluent().With<TickedEntityTag>(true).With(referenceTypeIndex, !attribute.copyData).Without(typeInfo.TypeIndex)
                                              .IncludeDisabledEntities().IncludePrefabs().Build(),
                        missingReferenceQuery = state.Fluent().Without<TickingOnlyEntityTag>().With(typeInfo.TypeIndex, !attribute.copyData).Without(referenceTypeIndex)
                                                .IncludeDisabledEntities().IncludePrefabs().Build(),
                        removeTickingQuery   = state.Fluent().Without<TickedEntityTag>().With(typeInfo.TypeIndex, true).IncludeDisabledEntities().IncludePrefabs().Build(),
                        removeReferenceQuery = state.Fluent().With<TickingOnlyEntityTag>().With(referenceTypeIndex,
                                                                                                true).IncludeDisabledEntities().IncludePrefabs().Build(),
                        copyData                     = attribute.copyData,
                        removeReferenceInTickingOnly = true,
                    });
                }
                else
                {
                    typePairStates.Add(new TypePairState
                    {
                        referenceType       = referenceType,
                        tickingType         = tickingType,
                        referenceHandle     = attribute.copyData ? state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly(referenceTypeIndex)) : default,
                        tickingHandle       = attribute.copyData ? state.GetDynamicComponentTypeHandle(tickingType) : default,
                        missingTickingQuery = state.Fluent().With<TickedEntityTag>(true).With(referenceTypeIndex, !attribute.copyData).Without(typeInfo.TypeIndex)
                                              .IncludeDisabledEntities().IncludePrefabs().Build(),
                        missingReferenceQuery        = state.Fluent().Without(referenceTypeIndex).With(typeInfo.TypeIndex, true).IncludeDisabledEntities().IncludePrefabs().Build(),
                        removeTickingQuery           = state.Fluent().Without<TickedEntityTag>().With(typeInfo.TypeIndex, true).IncludeDisabledEntities().IncludePrefabs().Build(),
                        removeReferenceQuery         = default,
                        copyData                     = attribute.copyData,
                        removeReferenceInTickingOnly = false
                    });
                }
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            typePairStates.Dispose();
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            foreach (var typePairState in typePairStates)
            {
                if (!typePairState.missingTickingQuery.IsEmptyIgnoreFilter)
                {
                    if (typePairState.copyData)
                    {
                        var entities = typePairState.missingTickingQuery.ToEntityArray(state.WorldUpdateAllocator);
                        state.EntityManager.AddComponent(typePairState.missingTickingQuery, typePairState.tickingType);
                        var writeHandle = typePairState.tickingHandle;
                        var readHandle  = typePairState.removeReferenceInTickingOnly ? typePairState.referenceHandle.CopyToReadOnly() : typePairState.referenceHandle;
                        writeHandle.Update(ref state);
                        readHandle.Update(ref state);
                        var typeSize = TypeManager.GetTypeInfo(typePairState.tickingType.TypeIndex).ElementSize;
                        if (typePairState.tickingType.IsBuffer)
                        {
                            foreach (var entity in entities)
                            {
                                var esi           = state.EntityManager.GetStorageInfo(entity);
                                var readAccessor  = esi.Chunk.GetUntypedBufferAccessor(ref readHandle);
                                var writeAccessor = esi.Chunk.GetUntypedBufferAccessor(ref writeHandle);
                                var readPtr       = readAccessor.GetUnsafeReadOnlyPtrAndLength(esi.IndexInChunk, out var length);
                                writeAccessor.ResizeUninitialized(esi.IndexInChunk, length);
                                var writePtr = writeAccessor.GetUnsafePtr(esi.IndexInChunk);
                                UnsafeUtility.MemCpy(writePtr, readPtr, length * typeSize);
                            }
                        }
                        else if (!typePairState.tickingType.IsZeroSized)
                        {
                            foreach (var entity in entities)
                            {
                                var esi      = state.EntityManager.GetStorageInfo(entity);
                                var readPtr  = (byte*)esi.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref readHandle, typeSize).GetUnsafeReadOnlyPtr();
                                var writePtr = (byte*)esi.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref writeHandle, typeSize).GetUnsafePtr();
                                UnsafeUtility.MemCpy(writePtr + typeSize * esi.IndexInChunk, readPtr + typeSize * esi.IndexInChunk, typeSize);
                            }
                        }
                        if (typePairState.tickingType.IsEnableable)
                        {
                            foreach (var entity in entities)
                            {
                                bool enabled = state.EntityManager.IsComponentEnabled(entity, typePairState.referenceType);
                                state.EntityManager.SetComponentEnabled(entity, typePairState.tickingType, enabled);
                            }
                        }
                    }
                    else
                    {
                        state.EntityManager.AddComponent(typePairState.missingTickingQuery, typePairState.tickingType);
                    }
                }
                if (!typePairState.missingReferenceQuery.IsEmptyIgnoreFilter)
                {
                    if (!typePairState.removeReferenceInTickingOnly)
                    {
                        state.EntityManager.RemoveComponent(typePairState.missingReferenceQuery, typePairState.tickingType);
                    }
                    else if (typePairState.copyData)
                    {
                        var entities = typePairState.missingReferenceQuery.ToEntityArray(state.WorldUpdateAllocator);
                        state.EntityManager.AddComponent(typePairState.missingReferenceQuery, typePairState.referenceType);
                        typePairState.referenceHandle.Update(ref state);
                        typePairState.tickingHandle.Update(ref state);
                        var writeHandle = typePairState.referenceHandle;
                        var readHandle  = typePairState.tickingHandle.CopyToReadOnly();
                        var typeSize    = TypeManager.GetTypeInfo(typePairState.tickingType.TypeIndex).ElementSize;
                        if (typePairState.tickingType.IsBuffer)
                        {
                            foreach (var entity in entities)
                            {
                                var esi           = state.EntityManager.GetStorageInfo(entity);
                                var readAccessor  = esi.Chunk.GetUntypedBufferAccessor(ref readHandle);
                                var writeAccessor = esi.Chunk.GetUntypedBufferAccessor(ref writeHandle);
                                var readPtr       = readAccessor.GetUnsafeReadOnlyPtrAndLength(esi.IndexInChunk, out var length);
                                writeAccessor.ResizeUninitialized(esi.IndexInChunk, length);
                                var writePtr = writeAccessor.GetUnsafePtr(esi.IndexInChunk);
                                UnsafeUtility.MemCpy(writePtr, readPtr, length * typeSize);
                            }
                        }
                        else if (!typePairState.tickingType.IsZeroSized)
                        {
                            foreach (var entity in entities)
                            {
                                var esi      = state.EntityManager.GetStorageInfo(entity);
                                var readPtr  = (byte*)esi.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref readHandle, typeSize).GetUnsafeReadOnlyPtr();
                                var writePtr = (byte*)esi.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref writeHandle, typeSize).GetUnsafePtr();
                                UnsafeUtility.MemCpy(writePtr + typeSize * esi.IndexInChunk, readPtr + typeSize * esi.IndexInChunk, typeSize);
                            }
                        }
                        if (typePairState.tickingType.IsEnableable)
                        {
                            foreach (var entity in entities)
                            {
                                bool enabled = state.EntityManager.IsComponentEnabled(entity, typePairState.tickingType);
                                state.EntityManager.SetComponentEnabled(entity, typePairState.referenceType, enabled);
                            }
                        }
                    }
                    else
                    {
                        state.EntityManager.AddComponent(typePairState.missingReferenceQuery, typePairState.referenceType);
                    }
                }
                if (!typePairState.removeTickingQuery.IsEmptyIgnoreFilter)
                {
                    state.EntityManager.RemoveComponent(typePairState.removeTickingQuery, typePairState.tickingType);
                }
                if (typePairState.removeReferenceInTickingOnly && !typePairState.removeReferenceQuery.IsEmptyIgnoreFilter)
                {
                    state.EntityManager.RemoveComponent(typePairState.removeReferenceQuery, typePairState.referenceType);
                }
            }
        }

        struct TypePairState
        {
            public ComponentType              referenceType;
            public ComponentType              tickingType;
            public EntityQuery                missingTickingQuery;
            public EntityQuery                missingReferenceQuery;
            public EntityQuery                removeTickingQuery;
            public EntityQuery                removeReferenceQuery;
            public DynamicComponentTypeHandle referenceHandle;
            public DynamicComponentTypeHandle tickingHandle;
            public bool                       copyData;
            public bool                       removeReferenceInTickingOnly;
        }
    }
}

