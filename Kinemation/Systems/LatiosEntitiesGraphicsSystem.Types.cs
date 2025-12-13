using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    public partial class LatiosEntitiesGraphicsSystem
    {
        partial struct Unmanaged
        {
            struct SetBatchChunkDataArgs
            {
                public int                          ChunkMetadataBegin;
                public int                          ChunkOffsetInBatch;
                public NativeArray<BatchCreateInfo> BatchChunks;
                public int                          BatchIndex;
                public int                          NumProperties;
                public BatchCreationTypeHandles     TypeHandles;
                public EntityManager                EntityManager;
                public NativeArray<ChunkProperty>   ChunkProperties;
                public NativeArray<int>             OverrideStreamBegin;
            }

            internal struct BatchCreationTypeHandles
            {
                public ComponentTypeHandle<LodHeightPercentages>                     lodHeightPercentagesHandle;
                public ComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins> lodHeightPercentagesWithCrossfadeMarginsHandle;
                public ComponentTypeHandle<PerInstanceCullingTag>                    perInstanceCullingHandle;
            }
        }

        internal struct EntitiesGraphicsChunkUpdater
        {
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>         postProcessMatrixHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousPostProcessMatrix> previousPostProcessMatrixHandle;

            public ComponentTypeCache.BurstCompatibleTypeArray         materialTypeHandleArray;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask> chunkMaterialPropertyDirtyMask;

            [NativeDisableParallelForRestriction]
            public NativeArray<long> unreferencedBatchIndices;

            [NativeDisableParallelForRestriction]
            [ReadOnly]
            public NativeArray<ChunkProperty> chunkProperties;

            public uint lastSystemVersion;

            public int worldToLocalType;
            public int prevWorldToLocalType;

            public int worldTransformType;
            public int previousTransformType;

            unsafe void MarkBatchAsReferenced(int batchIndex)
            {
                // If the batch is referenced, remove it from the unreferenced bitfield

                AtomicHelpers.IndexToQwIndexAndMask(batchIndex, out int qw, out long mask);

                Assert.IsTrue(qw < unreferencedBatchIndices.Length, "Batch index out of bounds");

                AtomicHelpers.AtomicAnd(
                    (long*)unreferencedBatchIndices.GetUnsafePtr(),
                    qw,
                    ~mask);
            }

            public void ProcessChunk(in EntitiesGraphicsChunkInfo chunkInfo, in ArchetypeChunk chunk)
            {
                if (chunkInfo.Valid)
                    ProcessValidChunk(in chunkInfo, chunk, false);
            }

            public unsafe void ProcessValidChunk(in EntitiesGraphicsChunkInfo chunkInfo, in ArchetypeChunk chunk, bool isNewChunk)
            {
                if (!isNewChunk)
                    MarkBatchAsReferenced(chunkInfo.BatchIndex);

                bool structuralChanges = chunk.DidOrderChange(lastSystemVersion);

                ref var mask = ref chunk.GetChunkComponentRefRW(ref chunkMaterialPropertyDirtyMask);

                fixed (DynamicComponentTypeHandle* fixedT0 = &materialTypeHandleArray.t0)
                {
                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = chunkProperties[i];
                        var type          = chunkProperty.ComponentTypeIndex;
                    }

                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = chunkProperties[i];
                        var type          = materialTypeHandleArray.Type(fixedT0, chunkProperty.ComponentTypeIndex);
                        var typeIndex     = materialTypeHandleArray.TypeIndexToArrayIndex[ComponentTypeCache.GetArrayIndex(chunkProperty.ComponentTypeIndex)];
                        var chunkType     = chunkProperty.ComponentTypeIndex;
                        if (chunkType == worldToLocalType || chunkType == prevWorldToLocalType)
                            continue;

                        bool componentChanged = chunk.DidChange(ref type, lastSystemVersion);
                        if (chunkType == worldTransformType)
                            componentChanged |= chunk.DidChange(ref postProcessMatrixHandle, lastSystemVersion);
                        if (chunkType == previousTransformType)
                            componentChanged |= chunk.DidChange(ref previousPostProcessMatrixHandle, lastSystemVersion);

                        if (isNewChunk || structuralChanges || componentChanged)
                        {
                            if (typeIndex >= 64)
                                mask.upper.SetBits(typeIndex - 64, true);
                            else
                                mask.lower.SetBits(typeIndex, true);
                        }
                    }
                }
            }
        }

        struct SortedSetUnmanaged
        {
            UnsafeHashSet<int> m_set;
            int                m_cachedMax;

            public SortedSetUnmanaged(int initialCapacity)
            {
                m_set       = new UnsafeHashSet<int>(initialCapacity, Allocator.Persistent);
                m_cachedMax = -1;
            }

            public bool isCreated => m_set.IsCreated;

            public void Dispose() => m_set.Dispose();

            public void Add(int index)
            {
                m_cachedMax = math.max(index, Max);
                m_set.Add(index);
            }

            public void Remove(int index)
            {
                m_set.Remove(index);
                if (m_cachedMax == index)
                    m_cachedMax = -1;
            }

            public bool Contains(int index) => m_set.Contains(index);

            public int Max
            {
                get
                {
                    if (m_cachedMax < 0)
                    {
                        foreach (var i in m_set)
                            m_cachedMax = math.max(m_cachedMax, i);
                    }
                    return math.max(0, m_cachedMax);
                }
            }
        }
    }
}

