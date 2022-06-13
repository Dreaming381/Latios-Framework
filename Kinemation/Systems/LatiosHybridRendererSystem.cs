#region Header
// #define DISABLE_HYBRID_V2_SRP_LOGS
// #define DEBUG_LOG_HYBRID_V2
// #define DEBUG_LOG_CHUNK_CHANGES
// #define DEBUG_LOG_TOP_LEVEL
// #define DEBUG_LOG_BATCHES
// #define DEBUG_LOG_BATCH_UPDATES
// #define DEBUG_LOG_CHUNKS
// #define DEBUG_LOG_INVALID_CHUNKS
// #define DEBUG_LOG_UPLOADS
// #define DEBUG_LOG_PROPERTIES
// #define DEBUG_LOG_OVERRIDES
// #define DEBUG_LOG_VISIBLE_INSTANCES
// #define DEBUG_LOG_MATERIAL_PROPERTIES
// #define DEBUG_LOG_MEMORY_USAGE
// #define DEBUG_LOG_AMBIENT_PROBE
// #define PROFILE_BURST_JOB_INTERNALS

#if UNITY_EDITOR || DEBUG_LOG_OVERRIDES
#define USE_PROPERTY_ASSERTS
#endif

#if UNITY_EDITOR
#define USE_PICKING_MATRICES
#endif

// Define this to remove the performance overhead of error material usage
// #define DISABLE_HYBRID_ERROR_MATERIAL

// Assert that V2 requirements are met if it's enabled

#if !UNITY_2020_1_OR_NEWER
#error Hybrid Renderer V2 requires Unity 2020.1 or newer.
#endif
// Hybrid Renderer is disabled if SRP 9 is not found, unless an override define is present
// It is also disabled if -nographics is given from the command line.
#if !(HDRP_9_0_0_OR_NEWER || URP_9_0_0_OR_NEWER || HYBRID_RENDERER_ENABLE_WITHOUT_SRP)
#define HYBRID_RENDERER_DISABLED
#endif

#if ENABLE_UNITY_OCCLUSION && UNITY_2020_2_OR_NEWER && (!HYBRID_RENDERER_DISABLED)
#define USE_UNITY_OCCLUSION
#endif

// TODO:
// - Minimize struct sizes to improve memory footprint and cache usage
// - What to do with FrozenRenderSceneTag / ForceLowLOD?
// - Precompute and optimize material property + chunk component matching as much as possible
// - Integrate new occlusion culling
// - PickableObject?

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
#if UNITY_2020_1_OR_NEWER
// This API only exists since 2020.1
using Unity.Rendering.HybridV2;
#endif

#if USE_UNITY_OCCLUSION
using Unity.Rendering.Occlusion;
#endif

using Unity.Rendering;

#endregion

namespace Latios.Kinemation.Systems
{
    /// <summary>
    /// Renders all Entities containing both RenderMesh and LocalToWorld components.
    /// </summary>

    [ExecuteAlways]
    //@TODO: Necessary due to empty component group. When Component group and archetype chunks are unified this should be removed
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdatePresentationSystemGroup))]
    [UpdateAfter(typeof(HybridRendererSystem))]
    [DisableAutoCreation]
    public unsafe partial class LatiosHybridRendererSystem : SubSystem
    {
        #region UtilityTypes
        // Contains the immutable properties that are set
        // upon batch creation. Only chunks with identical BatchCreateInfo
        // can be combined in a single batch.
        private struct BatchCreateInfo : IEquatable<BatchCreateInfo>
        {
            public static readonly Bounds BigBounds =
                new Bounds(new Vector3(0, 0, 0), new Vector3(1048576.0f, 1048576.0f, 1048576.0f));

            public RenderMesh       RenderMesh;
            public EditorRenderData EditorRenderData;
            public Bounds           Bounds;
            public bool             FlippedWinding;
            public ulong            PartitionValue;

            public bool Valid => RenderMesh.mesh != null &&
            RenderMesh.material != null &&
            RenderMesh.material.shader != null;

            public bool Equals(BatchCreateInfo other)
            {
                return RenderMesh.Equals(other.RenderMesh) &&
                       EditorRenderData.Equals(other.EditorRenderData) &&
                       Bounds.Equals(other.Bounds) &&
                       FlippedWinding == other.FlippedWinding &&
                       PartitionValue == other.PartitionValue;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchCreateInfo other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = RenderMesh.GetHashCode();
                    hashCode     = (hashCode * 397) ^ EditorRenderData.GetHashCode();
                    hashCode     = (hashCode * 397) ^ Bounds.GetHashCode();
                    hashCode     = (hashCode * 397) ^ FlippedWinding.GetHashCode();
                    hashCode     = (hashCode * 397) ^ PartitionValue.GetHashCode();
                    return hashCode;
                }
            }
        }

        private class BatchCreateInfoFactory
        {
            public EntityManager                                    EntityManager;
            public SharedComponentTypeHandle<RenderMesh>            RenderMeshTypeHandle;
            public SharedComponentTypeHandle<EditorRenderData>      EditorRenderDataTypeHandle;
            public SharedComponentTypeHandle<HybridBatchPartition>  HybridBatchPartitionHandle;
            public ComponentTypeHandle<RenderMeshFlippedWindingTag> RenderMeshFlippedWindingTagTypeHandle;
            public EditorRenderData                                 DefaultEditorRenderData;

            public BatchCreateInfo CreateInfoForChunk(ArchetypeChunk chunk)
            {
                return new BatchCreateInfo
                {
                    RenderMesh       = chunk.GetSharedComponentData(RenderMeshTypeHandle, EntityManager),
                    EditorRenderData = chunk.Has(EditorRenderDataTypeHandle) ?
                                       chunk.GetSharedComponentData(EditorRenderDataTypeHandle, EntityManager) :
                                       DefaultEditorRenderData,
                    Bounds         = BatchCreateInfo.BigBounds,
                    FlippedWinding = chunk.Has(RenderMeshFlippedWindingTagTypeHandle),
                    PartitionValue = chunk.Has(HybridBatchPartitionHandle) ?
                                     chunk.GetSharedComponentData(HybridBatchPartitionHandle, EntityManager).PartitionValue :
                                     0,
                };
            }
        }

        private struct BatchCompatibility : IComparer<int>, IDisposable
        {
            public struct BatchSortEntry
            {
                // We can't store BatchCreateInfo itself here easily, because it's a managed
                // type and those cannot be stored in native collections, so we store just the
                // hash and recreate the actual CreateInfo just-in-time when needed.
                public int                          CreateInfoHash;
                public bool                         CreateInfoValid;
                public ArchetypeChunk               Chunk;
                public SharedComponentOverridesInfo SharedOverrides;
                public ulong                        SortKey;

                public void CalculateSortKey()
                {
                    SortKey = ((ulong)(CreateInfoHash * 397)) ^ SharedOverrides.SharedOverrideHash;
                }

                public bool IsCompatibleWith(EntityManager entityManager,
                                             LatiosHybridRendererSystem system,
                                             BatchCreateInfoFactory createInfoFactory,
                                             BatchSortEntry e)
                {
                    // NOTE: This archetype property does not seem to hold, do more expensive testing for now
                    // bool sameArchetype = Chunk.Archetype == e.Chunk.Archetype;
                    // // If the archetypes are the same, all values of all shared components must be the same.
                    // if (sameArchetype)
                    //     return true;

                    // If the batch settings are not the same, the chunks are never compatible
                    // We can test the hash first to avoid instantiating the CreateInfo.
                    bool sameBatchHash = CreateInfoHash == e.CreateInfoHash;
                    if (!sameBatchHash)
                        return false;

                    var  ci0                     = createInfoFactory.CreateInfoForChunk(Chunk);
                    var  ci1                     = createInfoFactory.CreateInfoForChunk(e.Chunk);
                    bool compatibleBatchSettings = ci0.Equals(ci1);
                    if (!compatibleBatchSettings)
                        return false;

                    // If the batch settings are compatible but the archetypes are different, the
                    // chunks are compatible if and only if they have identical shared component overrides.

                    // If the override hash is different, the overrides cannot be the same.
                    bool sameOverrideHash = SharedOverrides.SharedOverrideHash == e.SharedOverrides.SharedOverrideHash;
                    if (!sameOverrideHash)
                        return false;

                    // If the override hash is the same, then either the overrides are the same or
                    // there is a hash collision. Must do full comparison of shared component values to
                    // know for sure.

                    // NOTE: This test is slightly overconservative. It would be enough that the overrides
                    // used by the shader are the same, but the mapping between shader properties and components
                    // is only done later.

                    var os0 = SharedOverrides.SharedOverrideTypeIndices;
                    var os1 = e.SharedOverrides.SharedOverrideTypeIndices;

                    // If either has no overrides, we are done
                    if (!os0.IsCreated || !os1.IsCreated)
                        return os0.IsCreated == os1.IsCreated;

                    if (os0.Length != os1.Length)
                        return false;

                    for (int i = 0; i < os0.Length; ++i)
                    {
                        int typeIndex = ((int*)os0.Ptr)[i];
                        int ti1       = ((int*)os1.Ptr)[i];
                        if (typeIndex != ti1)
                            return false;

                        var handle = system.GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly(typeIndex));
                        var c0     = Chunk.GetSharedComponentDataBoxed(handle, entityManager) as IHybridSharedComponentFloat4Override;
                        var c1     = e.Chunk.GetSharedComponentDataBoxed(handle, entityManager) as IHybridSharedComponentFloat4Override;
                        var v0     = c0.GetFloat4OverrideData();
                        var v1     = c1.GetFloat4OverrideData();
                        if (!math.all(v0 == v1))
                            return false;
                    }

                    return true;
                }
            }

            private BatchCreateInfoFactory      m_CreateInfoFactory;
            private NativeArray<ArchetypeChunk> m_Chunks;
            private NativeArray<int>            m_SortIndices;
            private NativeArray<BatchSortEntry> m_SortEntries;

            public BatchCompatibility(
                LatiosHybridRendererSystem system,
                BatchCreateInfoFactory createInfoFactory,
                NativeArray<ArchetypeChunk> chunks)
            {
                m_CreateInfoFactory = createInfoFactory;
                m_Chunks            = chunks;
                m_SortIndices       = new NativeArray<int>(
                    m_Chunks.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                m_SortEntries = new NativeArray<BatchSortEntry>(
                    m_Chunks.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < m_Chunks.Length; ++i)
                {
                    m_SortIndices[i] = i;
                    m_SortEntries[i] = CreateSortEntry(system, m_Chunks[i]);
                }
            }

            private BatchSortEntry CreateSortEntry(
                LatiosHybridRendererSystem system,
                ArchetypeChunk chunk)
            {
                var ci    = m_CreateInfoFactory.CreateInfoForChunk(chunk);
                var entry = new BatchSortEntry
                {
                    CreateInfoHash  = ci.GetHashCode(),
                    CreateInfoValid = ci.Valid,
                    Chunk           = chunk,
                    SharedOverrides = system.SharedComponentOverridesForChunk(chunk),
                };
                entry.CalculateSortKey();
                return entry;
            }

            public NativeArray<ArchetypeChunk> SortChunks()
            {
                // Key-value sort all chunks according to compatibility
                m_SortIndices.Sort(this);
                var sortedChunks = new NativeArray<ArchetypeChunk>(
                    m_Chunks.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < m_Chunks.Length; ++i)
                    sortedChunks[i] = m_Chunks[SortedIndex(i)];

                return sortedChunks;
            }

            public int SortedIndex(int i) => m_SortIndices[i];

            public BatchCreateInfo CreateInfoFor(int sortedIndex) =>
            m_CreateInfoFactory.CreateInfoForChunk(m_Chunks[SortedIndex(sortedIndex)]);

            public bool IsCompatible(EntityManager entityManager, LatiosHybridRendererSystem system, int sortedI, int sortedJ)
            {
                return m_SortEntries[SortedIndex(sortedI)].IsCompatibleWith(entityManager, system, m_CreateInfoFactory, m_SortEntries[SortedIndex(sortedJ)]);
            }

            public int Compare(int x, int y)
            {
                // Sort according to CreateInfo validity and hash based sort key.
                // Hash collisions can result in bad batching (consecutive incompatible chunks),
                // but not incorrect rendering (compatibility is always checked explicitly).

                var sx = m_SortEntries[x];
                var sy = m_SortEntries[y];

                bool vx = sx.CreateInfoValid;
                bool vy = sy.CreateInfoValid;

                // Always sort invalid chunks last, so they can be skipped by shortening the array.
                if (!vx || !vy)
                {
                    if (vx)
                        return -1;
                    else if (vy)
                        return 1;
                    else
                        return 0;
                }

                var hx = sx.SortKey;
                var hy = sy.SortKey;

                if (hx < hy)
                    return -1;
                else if (hx > hy)
                    return 1;
                else
                    return 0;
            }

            public void Dispose()
            {
                m_SortIndices.Dispose();
                m_SortEntries.Dispose();
            }
        }

        private enum BatchFlags
        {
            NeedMotionVectorPassFlag = 0x1
        };

        enum DefaultValueKind
        {
            ZeroDefault,
            NonzeroDefault,
            SharedBuiltin,
        }

        struct MaterialPropertyType
        {
            public int              TypeIndex;
            public short            SizeBytesCPU;
            public short            SizeBytesGPU;
            public DefaultValueKind DefaultValueKind;
        };

        struct PropertyMapping
        {
            public string                       Name;
            public short                        SizeCPU;
            public short                        SizeGPU;
            public MaterialPropertyDefaultValue DefaultValue;
            public bool                         IsShared;
        }

        private struct BatchInfo
        {
            // There is one BatchProperty per shader property, which can be different from
            // the amount of overriding components.
            // TODO: Most of this data is no longer needed after the batch has been created, and could be
            // allocated from temp memory and freed after the batch has been created.

#pragma warning disable CS0649
            // CS0649: Field is never assigned to, and will always have its default value 0
            internal struct BatchProperty
            {
                public int   MetadataOffset;
                public short SizeBytesCPU;
                public short SizeBytesGPU;
                public int   CbufferIndex;
                public int   OverrideComponentsIndex;
                public int   OverrideSharedComponentsIndex;
                public int   SharedBuiltinOffset;
#if USE_PROPERTY_ASSERTS
                public int NameID;
#endif
                public BatchPropertyOverrideStatus OverrideStatus;
                public HeapBlock                   GPUAllocation;
                public float4x4                    DefaultValue;
            }

            // There is one BatchOverrideComponent for each component type that can possibly
            // override any of the BatchProperty entries. Some entries might have zero,
            // some entries might have multiples. Each chunk is only allowed a single overriding component.
            // This list is allocated from temporary memory and is freed after the batch has been fully created.
            internal struct BatchOverrideComponent
            {
                public int BatchPropertyIndex;
                public int TypeIndex;
            }
#pragma warning restore CS0649

            public UnsafeList<BatchProperty>          Properties;
            public UnsafeList<BatchOverrideComponent> OverrideComponents;
            public UnsafeList<BatchOverrideComponent> OverrideSharedComponents;
            public UnsafeList<HeapBlock>              ChunkMetadataAllocations;

            public void Dispose()
            {
                if (Properties.IsCreated)
                    Properties.Dispose();
                if (OverrideComponents.IsCreated)
                    OverrideComponents.Dispose();
                if (OverrideSharedComponents.IsCreated)
                    OverrideSharedComponents.Dispose();
                if (ChunkMetadataAllocations.IsCreated)
                    ChunkMetadataAllocations.Dispose();
            }
        }

        public struct SharedComponentOverridesInfo
        {
            public UnsafeList<int> SharedOverrideTypeIndices;
            public ulong           SharedOverrideHash;
        }

        // Status enums are ordered so that numerically larger enums always take priority
        // if at least one chunk in the batch requires that.
        public enum BatchPropertyOverrideStatus
        {
            Uninitialized,  // Override status has not been initialized
            SharedZeroDefault,  // Property uses a zero bit pattern value for all entities
            SharedNonzeroDefault,  // Property uses a shared value for all entities, but it's not zero
            SharedComponentOverride,  // Property uses a shared value that is read from a shared component
            SharedBuiltin,  // Property uses a shared value that is set by special fixed function code
            PerEntityOverride,  // Property uses per-entity unique values
        }

        struct BatchUpdateStatistics
        {
            // Ifdef struct contents to avoid warnings when ifdef is disabled.
#if DEBUG_LOG_BATCH_UPDATES
            public int BatchesWithChangedBounds;
            public int BatchesNeedingMotionVectors;
            public int BatchesWithoutMotionVectors;

            public bool NonZero =>
            BatchesWithChangedBounds > 0 ||
            BatchesNeedingMotionVectors > 0 ||
            BatchesWithoutMotionVectors > 0;
#endif
        }

        private struct CullingComponentTypes
        {
            public ComponentTypeHandle<RootLODRange>               RootLODRanges;
            public ComponentTypeHandle<RootLODWorldReferencePoint> RootLODWorldReferencePoints;
            public ComponentTypeHandle<LODRange>                   LODRanges;
            public ComponentTypeHandle<LODWorldReferencePoint>     LODWorldReferencePoints;
            public ComponentTypeHandle<PerInstanceCullingTag>      PerInstanceCullingTag;
        }

        [BurstCompile]
        internal struct HybridChunkUpdater
        {
            public const uint kFloatsPerAABB = 6;
            public const int  kMinX          = 0;
            public const int  kMinY          = 1;
            public const int  kMinZ          = 2;
            public const int  kMaxX          = 3;
            public const int  kMaxY          = 4;
            public const int  kMaxZ          = 5;

            public ComponentTypeCache.BurstCompatibleTypeArray ComponentTypes;

            [NativeDisableParallelForRestriction]
            public NativeArray<long> UnreferencedInternalIndices;
            [NativeDisableParallelForRestriction]
            public NativeArray<long> BatchRequiresUpdates;
            [NativeDisableParallelForRestriction]
            public NativeArray<long> BatchHadMovingEntities;

            [NativeDisableParallelForRestriction]
            [ReadOnly]
            public NativeArray<ChunkProperty> ChunkProperties;

            [NativeDisableParallelForRestriction]
            [ReadOnly]
            public NativeList<BatchMotionInfo> BatchMotionInfos;

            [NativeDisableParallelForRestriction]
            public NativeList<float> BatchAABBs;
            public MinMaxAABB        ThreadLocalAABB;

#if USE_PICKING_MATRICES
            [NativeDisableParallelForRestriction]
            [ReadOnly]
            public NativeList<IntPtr> BatchPickingMatrices;
#endif

            public uint LastSystemVersion;
            public uint lastSystemVersionForProperties;
            public int  PreviousBatchIndex;

            public int LocalToWorldType;
            public int WorldToLocalType;
            public int PrevWorldToLocalType;

#if PROFILE_BURST_JOB_INTERNALS
            public ProfilerMarker ProfileAddUpload;
            public ProfilerMarker ProfilePickingMatrices;
#endif

            public unsafe void MarkBatchForUpdates(int internalIndex, bool entitiesMoved)
            {
                AtomicHelpers.IndexToQwIndexAndMask(internalIndex, out int qw, out long mask);
                Debug.Assert(qw < BatchRequiresUpdates.Length && qw < BatchHadMovingEntities.Length,
                             "Batch index out of bounds");

                var  motionInfo               = BatchMotionInfos[internalIndex];
                bool mustDisableMotionVectors = motionInfo.MotionVectorFlagSet && !entitiesMoved;

                // If entities moved, we always update the batch since bounds must be updated.
                // If no entities moved, we only update the batch if it requires motion vector disable.
                if (entitiesMoved || mustDisableMotionVectors)
                    AtomicHelpers.AtomicOr((long*)BatchRequiresUpdates.GetUnsafePtr(), qw, mask);

                if (entitiesMoved)
                    AtomicHelpers.AtomicOr((long*)BatchHadMovingEntities.GetUnsafePtr(), qw, mask);
            }

            unsafe void MarkBatchAsReferenced(int internalIndex)
            {
                // If the batch is referenced, remove it from the unreferenced bitfield

                AtomicHelpers.IndexToQwIndexAndMask(internalIndex, out int qw, out long mask);

                Debug.Assert(qw < UnreferencedInternalIndices.Length, "Batch index out of bounds");

                AtomicHelpers.AtomicAnd(
                    (long*)UnreferencedInternalIndices.GetUnsafePtr(),
                    qw,
                    ~mask);
            }

            public void ProcessChunk(ref HybridChunkInfo chunkInfo, ref ChunkMaterialPropertyDirtyMask mask, ArchetypeChunk chunk, ChunkWorldRenderBounds chunkBounds)
            {
#if DEBUG_LOG_CHUNKS
                Debug.Log(
                    $"HybridChunkUpdater.ProcessChunk(internalBatchIndex: {chunkInfo.InternalIndex}, valid: {chunkInfo.Valid}, count: {chunk.Count}, chunk: {chunk.GetHashCode()})");
#endif

                if (chunkInfo.Valid)
                    ProcessValidChunk(ref chunkInfo, ref mask, chunk, chunkBounds.Value, false);
            }

            public unsafe void ProcessValidChunk(ref HybridChunkInfo chunkInfo, ref ChunkMaterialPropertyDirtyMask mask, ArchetypeChunk chunk,
                                                 MinMaxAABB chunkAABB, bool isNewChunk)
            {
                if (!isNewChunk)
                    MarkBatchAsReferenced(chunkInfo.InternalIndex);

                int internalIndex = chunkInfo.InternalIndex;
                UpdateBatchAABB(internalIndex, chunkAABB);

                bool structuralChanges = chunk.DidOrderChange(LastSystemVersion);

                fixed (DynamicComponentTypeHandle* fixedT0 = &ComponentTypes.t0)
                {
                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = ChunkProperties[i];
                        var type          = chunkProperty.ComponentTypeIndex;
                    }

                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = ChunkProperties[i];
                        var type          = ComponentTypes.Type(fixedT0, chunkProperty.ComponentTypeIndex);
                        var typeIndex     = ComponentTypes.TypeIndexToArrayIndex[ComponentTypeCache.GetArrayIndex(chunkProperty.ComponentTypeIndex)];

                        var chunkType          = chunkProperty.ComponentTypeIndex;
                        var isLocalToWorld     = chunkType == LocalToWorldType;
                        var isWorldToLocal     = chunkType == WorldToLocalType;
                        var isPrevWorldToLocal = chunkType == PrevWorldToLocalType;

                        var skipComponent = isWorldToLocal || isPrevWorldToLocal;

                        bool componentChanged  = chunk.DidChange(type, lastSystemVersionForProperties);
                        bool copyComponentData = (isNewChunk || structuralChanges || componentChanged) && !skipComponent;

                        if (copyComponentData)
                        {
                            if (typeIndex >= 64)
                                mask.upper.SetBits(typeIndex - 64, true);
                            else
                                mask.lower.SetBits(typeIndex, true);
#if DEBUG_LOG_PROPERTIES
                            Debug.Log($"UpdateChunkProperty(internalBatchIndex: {chunkInfo.InternalIndex}, property: {i}, elementSize: {chunkProperty.ValueSizeBytesCPU})");
#endif

                            var src = chunk.GetDynamicComponentDataArrayReinterpret<int>(type,
                                                                                         chunkProperty.ValueSizeBytesCPU);

#if PROFILE_BURST_JOB_INTERNALS
                            ProfileAddUpload.Begin();
#endif

                            int sizeBytes = (int)((uint)chunk.Count * (uint)chunkProperty.ValueSizeBytesCPU);
                            var srcPtr    = src.GetUnsafeReadOnlyPtr();
                            var dstOffset = chunkProperty.GPUDataBegin;
                            if (isLocalToWorld)
                            {
                                var numMatrices = sizeBytes / sizeof(float4x4);

#if USE_PICKING_MATRICES
                                // If picking support is enabled, also copy the LocalToWorld matrices
                                // to the traditional instancing matrix array. This should be thread safe
                                // because the related Burst jobs run during DOTS system execution, and
                                // are guaranteed to have finished before rendering starts.
#if PROFILE_BURST_JOB_INTERNALS
                                ProfilePickingMatrices.Begin();
#endif
                                float4x4* batchPickingMatrices = (float4x4*)BatchPickingMatrices[internalIndex];
                                int chunkOffsetInBatch   = chunkInfo.CullingData.BatchOffset;
                                UnsafeUtility.MemCpy(
                                    batchPickingMatrices + chunkOffsetInBatch,
                                    srcPtr,
                                    sizeBytes);
#if PROFILE_BURST_JOB_INTERNALS
                                ProfilePickingMatrices.End();
#endif
#endif
                            }

#if PROFILE_BURST_JOB_INTERNALS
                            ProfileAddUpload.End();
#endif
                        }
                    }
                }
            }

            private void UpdateBatchAABB(int internalIndex, MinMaxAABB chunkAABB)
            {
                // As long as we keep processing chunks that belong to the same batch,
                // we can keep accumulating a thread local AABB cheaply.
                // Once we encounter a different batch, we need to "flush" the thread
                // local version to the global one with atomics.
                bool sameBatchAsPrevious = internalIndex == PreviousBatchIndex;

                if (sameBatchAsPrevious)
                {
                    ThreadLocalAABB.Encapsulate(chunkAABB);
                }
                else
                {
                    CommitBatchAABB();
                    ThreadLocalAABB    = chunkAABB;
                    PreviousBatchIndex = internalIndex;
                }
            }

            private unsafe void CommitBatchAABB()
            {
                bool validThreadLocalAABB = PreviousBatchIndex >= 0;
                if (!validThreadLocalAABB)
                    return;

                int internalIndex = PreviousBatchIndex;
                var aabb          = ThreadLocalAABB;

                int    aabbIndex  = (int)(((uint)internalIndex) * kFloatsPerAABB);
                float* aabbFloats = (float*)BatchAABBs.GetUnsafePtr();
                AtomicHelpers.AtomicMin(aabbFloats, aabbIndex + kMinX, aabb.Min.x);
                AtomicHelpers.AtomicMin(aabbFloats, aabbIndex + kMinY, aabb.Min.y);
                AtomicHelpers.AtomicMin(aabbFloats, aabbIndex + kMinZ, aabb.Min.z);
                AtomicHelpers.AtomicMax(aabbFloats, aabbIndex + kMaxX, aabb.Max.x);
                AtomicHelpers.AtomicMax(aabbFloats, aabbIndex + kMaxY, aabb.Max.y);
                AtomicHelpers.AtomicMax(aabbFloats, aabbIndex + kMaxZ, aabb.Max.z);

                PreviousBatchIndex = -1;
            }

            public void FinishExecute()
            {
                CommitBatchAABB();
            }
        }

        #endregion

        #region Variables
        static private bool s_HybridRendererEnabled = true;
        public static bool HybridRendererEnabled => s_HybridRendererEnabled;

        private ulong m_PersistentInstanceDataSize;

        private EntityQuery m_HybridRenderedQuery;

#if UNITY_EDITOR
        private EditorRenderData m_DefaultEditorRenderData = new EditorRenderData
        { SceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask };
#else
        private EditorRenderData m_DefaultEditorRenderData = new EditorRenderData { SceneCullingMask = ~0UL };
#endif

        const int   kInitialMaxBatchCount         = 1 * 1024;
        const float kMaxBatchGrowFactor           = 2f;
        const int   kMaxEntitiesPerBatch          = 1023;  // C++ code is restricted to a certain maximum size
        const int   kNumNewChunksPerThread        = 1;  // TODO: Tune this
        const int   kNumScatteredIndicesPerThread = 8;  // TODO: Tune this
        const int   kNumGatheredIndicesPerThread  = 128 * 8;  // Two cache lines per thread
        const int   kBuiltinCbufferIndex          = 0;

        const int   kMaxChunkMetadata      = 1 * 1024 * 1024;
        const ulong kMaxGPUAllocatorMemory = 1024 * 1024 * 1024;  // 1GiB of potential memory space
        const ulong kGPUBufferSizeInitial  = 32 * 1024 * 1024;
        const ulong kGPUBufferSizeMax      = 1023 * 1024 * 1024;

        private BatchRendererGroup m_BatchRendererGroup;

        private HeapAllocator m_GPUPersistentAllocator;
        private HeapBlock     m_SharedZeroAllocation;
        private HeapBlock     m_SharedAmbientProbeAllocation;

        private HeapAllocator m_ChunkMetadataAllocator;

        private NativeList<BatchInfo>       m_BatchInfos;
        private NativeList<BatchMotionInfo> m_BatchMotionInfos;
#if USE_PICKING_MATRICES
        private NativeList<IntPtr> m_BatchPickingMatrices;
#endif
        private NativeArray<ChunkProperty> m_ChunkProperties;
        private NativeHashMap<int, int>    m_ExistingBatchInternalIndices;
        private ComponentTypeCache         m_ComponentTypeCache;

        private NativeList<float> m_BatchAABBs;

        private NativeList<int> m_InternalToExternalIds;
        private NativeList<int> m_ExternalToInternalIds;
        private NativeList<int> m_InternalIdFreelist;
        private int             m_ExternalBatchCount;
        private SortedSet<int>  m_SortedInternalIds;

        private EntityQuery m_MetaEntitiesForHybridRenderableChunks;

        private NativeList<DefaultValueBlitDescriptor> m_DefaultValueBlits;

        private JobHandle m_AABBsCleared;
        private bool      m_AABBClearKicked;

        NativeMultiHashMap<int, MaterialPropertyType> m_MaterialPropertyTypes;
        NativeMultiHashMap<int, MaterialPropertyType> m_MaterialPropertyTypesShared;
        NativeHashSet<int>                            m_SharedComponentOverrideTypeIndices;

        // When extra debugging is enabled, store mappings from NameIDs to property names,
        // and from type indices to type names.
        Dictionary<int, string>                  m_MaterialPropertyNames;
        Dictionary<int, string>                  m_MaterialPropertyTypeNames;
        Dictionary<int, float4x4>                m_MaterialPropertyDefaultValues;
        Dictionary<int, int>                     m_MaterialPropertySharedBuiltins;
        static Dictionary<Type, PropertyMapping> s_TypeToPropertyMappings = new Dictionary<Type, PropertyMapping>();

#if USE_UNITY_OCCLUSION
        private OcclusionCulling m_OcclusionCulling;
#endif

        private bool     m_FirstFrameAfterInit;
        private Shader   m_BuiltinErrorShader;
        private Material m_ErrorMaterial;
#if UNITY_EDITOR
        private TrackShaderReflectionChangesSystem m_ShaderReflectionChangesSystem;
        private Dictionary<Shader, bool> m_ShaderHasCompileErrors;
#endif

        NativeHashMap<EntityArchetype, SharedComponentOverridesInfo> m_ArchetypeSharedOverrideInfos;
        private SHProperties                                         m_GlobalAmbientProbe;
        private bool                                                 m_GlobalAmbientProbeDirty;

        int                          m_cullIndexThisFrame             = 0;
        uint                         m_lastSystemVersionForProperties = 0;
        KinemationCullingSuperSystem m_cullingSuperSystem;
        #endregion

        #region UtilityFunctions

        private SharedComponentOverridesInfo SharedComponentOverridesForChunk(ArchetypeChunk chunk)
        {
            // Since archetypes are immutable, we can memoize the override infos which are a bit
            // costly to create.
            if (m_ArchetypeSharedOverrideInfos.TryGetValue(chunk.Archetype, out var existingInfo))
                return existingInfo;

            var componentTypes = chunk.Archetype.GetComponentTypes();
            int numOverrides   = 0;

            // First, count the amount of overrides so we know how much to allocate
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                int typeIndex = componentTypes[i].TypeIndex;

                // If the type is not in this hash map, it's not a shared component override
                if (!m_SharedComponentOverrideTypeIndices.Contains(typeIndex))
                    continue;

                ++numOverrides;
            }

            // If there are no overrides, no need to allocate or hash anything
            if (numOverrides == 0)
            {
                var nullInfo = new SharedComponentOverridesInfo
                {
                    SharedOverrideTypeIndices = default,
                    SharedOverrideHash        = 0,
                };
                m_ArchetypeSharedOverrideInfos[chunk.Archetype] = nullInfo;
                return nullInfo;
            }

            var overridesArray = new UnsafeList<int>(
                numOverrides,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            overridesArray.Resize(numOverrides, NativeArrayOptions.UninitializedMemory);
            var overrides = overridesArray.Ptr;

            int j = 0;
            for (int i = 0; i < componentTypes.Length; ++i)
            {
                int typeIndex = componentTypes[i].TypeIndex;

                // If the type is not in this hash map, it's not a shared component override
                if (!m_SharedComponentOverrideTypeIndices.Contains(typeIndex))
                    continue;

                overrides[j] = typeIndex;
                ++j;
            }
            componentTypes.Dispose();

            // Sort the type indices so every archetype has them in the same order
            overridesArray.Sort<int>();

            // Finally, hash the actual contents
            xxHash3.StreamingState hash = new xxHash3.StreamingState(true);
            for (int i = 0; i < overridesArray.Length; ++i)
            {
                int typeIndex = overrides[i];
                var handle    = GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly(typeIndex));
                var c         = chunk.GetSharedComponentDataBoxed(handle, EntityManager) as IHybridSharedComponentFloat4Override;
                var v         = c.GetFloat4OverrideData();
                hash.Update(&v, UnsafeUtility.SizeOf<float4>());
            }
            uint2 h64 = hash.DigestHash64();

            var info = new SharedComponentOverridesInfo
            {
                SharedOverrideTypeIndices = overridesArray,
                SharedOverrideHash        = (ulong)h64.x | (((ulong)h64.y) << 32),
            };
            m_ArchetypeSharedOverrideInfos[chunk.Archetype] = info;
            return info;
        }

        private static bool PropertyIsZero(BatchPropertyOverrideStatus status) =>
        status == BatchPropertyOverrideStatus.SharedZeroDefault;

        private static bool PropertyRequiresAllocation(BatchPropertyOverrideStatus status) =>
        !PropertyIsZero(status) && status != BatchPropertyOverrideStatus.SharedBuiltin;

        private static bool PropertyRequiresBlit(BatchPropertyOverrideStatus status) =>
        status == BatchPropertyOverrideStatus.SharedNonzeroDefault ||
        status == BatchPropertyOverrideStatus.SharedComponentOverride;

        private static bool PropertyHasPerEntityValues(BatchPropertyOverrideStatus status) =>
        status == BatchPropertyOverrideStatus.PerEntityOverride;

        private static int PropertyValuesPerAllocation(BatchPropertyOverrideStatus status, int numInstances) =>
        PropertyHasPerEntityValues(status) ?
        numInstances :
        1;

        private void RegisterSharedBuiltin(string propertyName, int sharedBuiltinOffset)
        {
            int nameID                               = Shader.PropertyToID(propertyName);
            m_MaterialPropertySharedBuiltins[nameID] = sharedBuiltinOffset;
        }

        private void BlitBytes(HeapBlock heapBlock, void* bytes, int sizeBytes)
        {
            int size4x4 = UnsafeUtility.SizeOf<float4x4>();
            int offset  = 0;

            // Since each default blit can only copy a float4x4, split to multiple blits
            while (sizeBytes > 0)
            {
                int blitSize = math.min(sizeBytes, size4x4);
                var desc     = new DefaultValueBlitDescriptor
                {
                    DestinationOffset = (uint)heapBlock.begin + (uint)offset,
                    ValueSizeBytes    = (uint)blitSize,
                    Count             = 1,
                };
                UnsafeUtility.MemCpy(&desc.DefaultValue, (byte*)bytes + offset, blitSize);
                m_DefaultValueBlits.Add(desc);

                sizeBytes -= blitSize;
                offset    += blitSize;
            }
        }

        public static void RegisterMaterialPropertyType(Type type, string propertyName, short overrideTypeSizeGPU = -1, MaterialPropertyDefaultValue defaultValue = default)
        {
            Debug.Assert(type != null,                        "type must be non-null");
            Debug.Assert(!string.IsNullOrEmpty(propertyName), "Property name must be valid");

            short typeSizeCPU = (short)UnsafeUtility.SizeOf(type);
            if (overrideTypeSizeGPU == -1)
                overrideTypeSizeGPU = typeSizeCPU;

            // For now, we only support overriding one material property with one type.
            // Several types can override one property, but not the other way around.
            // If necessary, this restriction can be lifted in the future.
            if (s_TypeToPropertyMappings.ContainsKey(type))
            {
                string prevPropertyName = s_TypeToPropertyMappings[type].Name;
                Debug.Assert(propertyName.Equals(
                                 prevPropertyName),
                             $"Attempted to register type {type.Name} with multiple different property names. Registered with \"{propertyName}\", previously registered with \"{prevPropertyName}\".");
            }
            else
            {
                var pm                         = new PropertyMapping();
                pm.Name                        = propertyName;
                pm.SizeCPU                     = typeSizeCPU;
                pm.SizeGPU                     = overrideTypeSizeGPU;
                pm.DefaultValue                = defaultValue;
                pm.IsShared                    = typeof(ISharedComponentData).IsAssignableFrom(type);
                s_TypeToPropertyMappings[type] = pm;
            }
        }

        public static void RegisterMaterialPropertyType<T>(string propertyName, short overrideTypeSizeGPU = -1, MaterialPropertyDefaultValue defaultValue = default)
            where T : IComponentData
        {
            RegisterMaterialPropertyType(typeof(T), propertyName, overrideTypeSizeGPU, defaultValue);
        }

        private void InitializeMaterialProperties()
        {
            m_MaterialPropertyTypes.Clear();
            m_MaterialPropertyTypesShared.Clear();
            m_MaterialPropertyDefaultValues.Clear();

            foreach (var kv in s_TypeToPropertyMappings)
            {
                Type   type         = kv.Key;
                string propertyName = kv.Value.Name;

                short sizeBytesCPU = kv.Value.SizeCPU;
                short sizeBytesGPU = kv.Value.SizeGPU;
                int   typeIndex    = TypeManager.GetTypeIndex(type);
                int   nameID       = Shader.PropertyToID(propertyName);
                var   defaultValue = kv.Value.DefaultValue;
                bool  isShared     = kv.Value.IsShared;

                DefaultValueKind defaultKind;
                if (m_MaterialPropertySharedBuiltins.ContainsKey(nameID))
                    defaultKind = DefaultValueKind.SharedBuiltin;
                else
                    defaultKind = defaultValue.Nonzero ?
                                  DefaultValueKind.NonzeroDefault :
                                  DefaultValueKind.ZeroDefault;

                if (isShared)
                {
                    m_MaterialPropertyTypesShared.Add(nameID,
                                                      new MaterialPropertyType
                    {
                        TypeIndex        = typeIndex,
                        SizeBytesCPU     = sizeBytesCPU,
                        SizeBytesGPU     = sizeBytesGPU,
                        DefaultValueKind = defaultKind,
                    });
                    m_SharedComponentOverrideTypeIndices.Add(typeIndex);
                }
                else
                {
                    m_MaterialPropertyTypes.Add(nameID,
                                                new MaterialPropertyType
                    {
                        TypeIndex        = typeIndex,
                        SizeBytesCPU     = sizeBytesCPU,
                        SizeBytesGPU     = sizeBytesGPU,
                        DefaultValueKind = defaultKind,
                    });
                }

                if (defaultValue.Nonzero)
                    m_MaterialPropertyDefaultValues[typeIndex] = defaultValue.Value;

#if USE_PROPERTY_ASSERTS
                m_MaterialPropertyNames[nameID]        = propertyName;
                m_MaterialPropertyTypeNames[typeIndex] = type.Name;
#endif

#if DEBUG_LOG_MATERIAL_PROPERTIES
                Debug.Log(
                    $"Type \"{type.Name}\" ({sizeBytesCPU} bytes) overrides material property \"{propertyName}\" (nameID: {nameID}, typeIndex: {typeIndex}, defaultKind: {defaultKind})");
#endif

                // We cache all IComponentData types that we know are capable of overriding properties
                if (!isShared)
                    m_ComponentTypeCache.UseType(typeIndex);
            }

            // UsedTypes values are the ComponentType values while the keys are the same
            // except with the bit flags in the high bits masked off.
            // The HybridRenderer packs ComponentTypeHandles by the order they show up
            // in the value array from the hashmap.
            var types  = m_ComponentTypeCache.UsedTypes.GetValueArray(Allocator.Temp);
            var ctypes = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>().Reinterpret<ComponentType>();
            ctypes.ResizeUninitialized(types.Length);
            for (int i = 0; i < types.Length; i++)
                ctypes[i] = ComponentType.ReadOnly(types[i]);

            s_TypeToPropertyMappings.Clear();
        }

        bool HasShaderReflectionChanged
        {
            get
            {
#if UNITY_EDITOR
                return m_ShaderReflectionChangesSystem.HasReflectionChanged;
#else
                return false;
#endif
            }
        }

        void AlignWithShaderReflectionChanges()
        {
#if UNITY_EDITOR
            // Reflection changing can imply that shader compile error status
            // has also changed, so flush our cache.
            if (HasShaderReflectionChanged)
            {
                m_ShaderHasCompileErrors = new Dictionary<Shader, bool>();
            }
#endif
        }

        void UpdateHybridV2Batches(out int totalChunks)
        {
            if (m_FirstFrameAfterInit)
            {
                OnFirstFrame();
                m_FirstFrameAfterInit = false;
            }

            AlignWithShaderReflectionChanges();

            Profiler.BeginSample("UpdateAllBatches");
            using (var hybridChunks =
                       m_HybridRenderedQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                UpdateAllBatches(out totalChunks);
            }

            Profiler.EndSample();
        }

        private void OnFirstFrame()
        {
#if UNITY_EDITOR
            m_ShaderReflectionChangesSystem = World.GetExistingSystem<TrackShaderReflectionChangesSystem>();
#endif

            InitializeMaterialProperties();

#if DEBUG_LOG_HYBRID_V2
            Debug.Log(
                $"Latios Hybrid Renderer V2 active, MaterialProperty component type count {m_ComponentTypeCache.UsedTypeCount} / {ComponentTypeCache.BurstCompatibleTypeArray.kMaxTypes}");
#endif
        }

        private void ResetIds()
        {
            if (m_InternalToExternalIds.IsCreated)
                m_InternalToExternalIds.Dispose();
            m_InternalToExternalIds = new NativeList<int>(kInitialMaxBatchCount, Allocator.Persistent);
            ResizeWithMinusOne(m_InternalToExternalIds, kInitialMaxBatchCount);

            if (m_ExternalToInternalIds.IsCreated)
                m_ExternalToInternalIds.Dispose();
            m_ExternalToInternalIds = new NativeList<int>(kInitialMaxBatchCount, Allocator.Persistent);
            ResizeWithMinusOne(m_ExternalToInternalIds, kInitialMaxBatchCount);

            m_ExternalBatchCount = 0;
            m_SortedInternalIds  = new SortedSet<int>();

            if (m_InternalIdFreelist.IsCreated)
                m_InternalIdFreelist.Dispose();
            m_InternalIdFreelist = new NativeList<int>(kInitialMaxBatchCount, Allocator.Persistent);

            for (int i = kInitialMaxBatchCount - 1; i >= 0; --i)
                m_InternalIdFreelist.Add(i);
        }

        private void EnsureHaveSpaceForNewBatch()
        {
            if (m_InternalIdFreelist.Length > 0)
                return;

            Debug.Assert(m_ExternalBatchCount > 0);
            Debug.Assert(kMaxBatchGrowFactor >= 1f,
                         "Grow factor should always be greater or equal to 1");

            var newCapacity               = (int)(kMaxBatchGrowFactor * m_ExternalBatchCount);
            m_InternalIdFreelist.Capacity = newCapacity;

            for (int i = newCapacity - 1; i >= m_ExternalBatchCount; --i)
                m_InternalIdFreelist.Add(i);

            ResizeWithMinusOne(m_ExternalToInternalIds, newCapacity);
            ResizeWithMinusOne(m_InternalToExternalIds, newCapacity);
            m_BatchAABBs.Resize(newCapacity * (int)HybridChunkUpdater.kFloatsPerAABB, NativeArrayOptions.ClearMemory);
            m_BatchInfos.Resize(newCapacity, NativeArrayOptions.ClearMemory);
            m_BatchMotionInfos.Resize(newCapacity, NativeArrayOptions.ClearMemory);

#if USE_PICKING_MATRICES
            m_BatchPickingMatrices.Resize(newCapacity, NativeArrayOptions.ClearMemory);
#endif
        }

        private int AllocateInternalId()
        {
            EnsureHaveSpaceForNewBatch();

            int id = m_InternalIdFreelist[m_InternalIdFreelist.Length - 1];
            m_InternalIdFreelist.Resize(m_InternalIdFreelist.Length - 1, NativeArrayOptions.UninitializedMemory);
            Debug.Assert(!m_SortedInternalIds.Contains(id), "Freshly allocated batch id found in list of used ids");
            m_SortedInternalIds.Add(id);
            return id;
        }

        private void ReleaseInternalId(int id)
        {
            if (!(id >= 0 && id < m_InternalToExternalIds.Length))
                Debug.Assert(false, $"Attempted to release invalid batch id {id}");
            if (!m_SortedInternalIds.Contains(id))
                Debug.Assert(false, $"Attempted to release an unused id {id}");
            m_SortedInternalIds.Remove(id);
            m_InternalIdFreelist.Add(id);
        }

        private void RemoveExternalIdSwapWithBack(int externalId)
        {
            // Mimic the swap back and erase that BatchRendererGroup does

            int internalIdOfRemoved = m_ExternalToInternalIds[externalId];
            int lastExternalId      = m_ExternalBatchCount - 1;

            if (lastExternalId != externalId)
            {
                int internalIdOfLast    = m_ExternalToInternalIds[lastExternalId];
                int newExternalIdOfLast = externalId;

                m_InternalToExternalIds[internalIdOfLast]    = newExternalIdOfLast;
                m_ExternalToInternalIds[newExternalIdOfLast] = internalIdOfLast;

                m_InternalToExternalIds[internalIdOfRemoved] = -1;
                m_ExternalToInternalIds[lastExternalId]      = -1;
            }
            else
            {
                m_InternalToExternalIds[internalIdOfRemoved] = -1;
                m_ExternalToInternalIds[externalId]          = -1;
            }
        }

        private int AddBatchIndex(int externalId)
        {
            int internalId                             = AllocateInternalId();
            m_InternalToExternalIds[internalId]        = externalId;
            m_ExternalToInternalIds[externalId]        = internalId;
            m_ExistingBatchInternalIndices[internalId] = internalId;
            ++m_ExternalBatchCount;
            return internalId;
        }

        private void RemoveBatchIndex(int internalId, int externalId)
        {
            if (!(m_ExternalBatchCount > 0))
                Debug.Assert(false, $"Attempted to release an invalid BatchRendererGroup id {externalId}");
            m_ExistingBatchInternalIndices.Remove(internalId);
            RemoveExternalIdSwapWithBack(externalId);
            ReleaseInternalId(internalId);
            --m_ExternalBatchCount;
        }

        private int InternalIndexRange => m_SortedInternalIds.Max + 1;

        private void Dispose()
        {
            m_BatchRendererGroup.Dispose();
            m_MaterialPropertyTypes.Dispose();
            m_MaterialPropertyTypesShared.Dispose();
            m_SharedComponentOverrideTypeIndices.Dispose();
            m_GPUPersistentAllocator.Dispose();
            m_ChunkMetadataAllocator.Dispose();

            m_BatchInfos.Dispose();
            m_BatchMotionInfos.Dispose();
#if USE_PICKING_MATRICES
            m_BatchPickingMatrices.Dispose();
#endif
            m_ChunkProperties.Dispose();
            m_ExistingBatchInternalIndices.Dispose();
            m_DefaultValueBlits.Dispose();
            m_ComponentTypeCache.Dispose();

            m_BatchAABBs.Dispose();

            if (m_InternalToExternalIds.IsCreated)
                m_InternalToExternalIds.Dispose();
            if (m_ExternalToInternalIds.IsCreated)
                m_ExternalToInternalIds.Dispose();
            if (m_InternalIdFreelist.IsCreated)
                m_InternalIdFreelist.Dispose();
            m_ExternalBatchCount = 0;
            m_SortedInternalIds  = null;

#if USE_UNITY_OCCLUSION
            m_OcclusionCulling.Dispose();
#endif

            DisposeSharedOverrideCache();

            m_AABBsCleared    = new JobHandle();
            m_AABBClearKicked = false;
        }

        private void DisposeSharedOverrideCache()
        {
            var infos = m_ArchetypeSharedOverrideInfos.GetValueArray(Allocator.Temp);
            for (int i = 0; i < infos.Length; ++i)
            {
                var indices = infos[i].SharedOverrideTypeIndices;
                if (indices.IsCreated)
                    indices.Dispose();
            }

            infos.Dispose();
            m_ArchetypeSharedOverrideInfos.Dispose();
        }

#if UNITY_ANDROID && !UNITY_64
        // There is a crash bug on ARMv7 potentially related to a compiler bug in the tool chain.
        // We will have to leave this function without optimizations on that platform.
        [MethodImpl(MethodImplOptions.NoOptimization)]
#endif
        private void UpdateAllBatches(out int totalChunks)
        {
            Profiler.BeginSample("GetComponentTypes");

            var hybridRenderedChunkTypeRO = GetComponentTypeHandle<HybridChunkInfo>(true);
            var hybridRenderedChunkType   = GetComponentTypeHandle<HybridChunkInfo>(false);
            var chunkHeadersRO            = GetComponentTypeHandle<ChunkHeader>(true);
            var chunkWorldRenderBoundsRO  = GetComponentTypeHandle<ChunkWorldRenderBounds>(true);
            var localToWorldsRO           = GetComponentTypeHandle<LocalToWorld>(true);
            var lodRangesRO               = GetComponentTypeHandle<LODRange>(true);
            var rootLodRangesRO           = GetComponentTypeHandle<RootLODRange>(true);
            var chunkPropertyDirtyMask    = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false);

            m_ComponentTypeCache.FetchTypeHandles(this);
            Profiler.EndSample();

            var numNewChunksArray = new NativeArray<int>(1, Allocator.TempJob);
            totalChunks           = m_HybridRenderedQuery.CalculateChunkCount();
            var newChunks         = new NativeArray<ArchetypeChunk>(
                totalChunks,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            Dependency = new ClassifyNewChunksJob
            {
                HybridChunkInfo = hybridRenderedChunkTypeRO,
                ChunkHeader     = chunkHeadersRO,
                NumNewChunks    = numNewChunksArray,
                NewChunks       = newChunks
            }
            .Schedule(m_MetaEntitiesForHybridRenderableChunks, Dependency);

            const int kNumBitsPerLong             = sizeof(long) * 8;
            var       unreferencedInternalIndices = new NativeArray<long>(
                (InternalIndexRange + kNumBitsPerLong) / kNumBitsPerLong,

                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            JobHandle initializedUnreferenced = default;
            var       existingKeys            = m_ExistingBatchInternalIndices.GetKeyArray(Allocator.TempJob);
            initializedUnreferenced           = new InitializeUnreferencedIndicesScatterJob
            {
                ExistingInternalIndices     = existingKeys,
                UnreferencedInternalIndices = unreferencedInternalIndices,
            }.Schedule(existingKeys.Length, kNumScatteredIndicesPerThread);
            existingKeys.Dispose(initializedUnreferenced);

            Dependency = JobHandle.CombineDependencies(Dependency, initializedUnreferenced);

            uint lastSystemVersion = LastSystemVersion;

            if (HybridEditorTools.DebugSettings.ForceInstanceDataUpload)
            {
                Debug.Log("Reuploading all Hybrid Renderer instance data to GPU");
                lastSystemVersion = 0;
            }

            CompleteDependency();
            int numNewChunks = numNewChunksArray[0];

            var maxBatchCount = math.max(kInitialMaxBatchCount, InternalIndexRange + numNewChunks);

            // Integer division with round up
            var maxBatchLongCount = (maxBatchCount + kNumBitsPerLong - 1) / kNumBitsPerLong;

            var batchRequiresUpdates = new NativeArray<long>(
                maxBatchLongCount,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            var batchHadMovingEntities = new NativeArray<long>(
                maxBatchLongCount,
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            var hybridChunkUpdater = new HybridChunkUpdater
            {
                ComponentTypes                 = m_ComponentTypeCache.ToBurstCompatible(Allocator.TempJob),
                UnreferencedInternalIndices    = unreferencedInternalIndices,
                BatchRequiresUpdates           = batchRequiresUpdates,
                BatchHadMovingEntities         = batchHadMovingEntities,
                ChunkProperties                = m_ChunkProperties,
                BatchMotionInfos               = m_BatchMotionInfos,
                BatchAABBs                     = m_BatchAABBs,
                LastSystemVersion              = lastSystemVersion,
                lastSystemVersionForProperties = m_lastSystemVersionForProperties,
                PreviousBatchIndex             = -1,

                LocalToWorldType     = TypeManager.GetTypeIndex<LocalToWorld>(),
                WorldToLocalType     = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
                PrevWorldToLocalType = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),

#if PROFILE_BURST_JOB_INTERNALS
                ProfileAddUpload       = new ProfilerMarker("AddUpload"),
                ProfilePickingMatrices = new ProfilerMarker("EditorPickingMatrices"),
#endif
#if USE_PICKING_MATRICES
                BatchPickingMatrices = m_BatchPickingMatrices,
#endif
            };

            var updateOldJob = new UpdateOldHybridChunksJob
            {
                HybridChunkInfo              = hybridRenderedChunkType,
                ChunkWorldRenderBounds       = chunkWorldRenderBoundsRO,
                ChunkHeader                  = chunkHeadersRO,
                LocalToWorld                 = localToWorldsRO,
                LodRange                     = lodRangesRO,
                RootLodRange                 = rootLodRangesRO,
                HybridChunkUpdater           = hybridChunkUpdater,
                chunkPropertyDirtyMaskHandle = chunkPropertyDirtyMask
            };

            Dependency = JobHandle.CombineDependencies(Dependency, EnsureAABBsCleared());

            // We need to wait for the job to complete here so we can process the new chunks
            Dependency = updateOldJob.Schedule(m_MetaEntitiesForHybridRenderableChunks, Dependency);
            CompleteDependency();

            // Garbage collect deleted batches before adding new ones to minimize peak memory use.
            Profiler.BeginSample("GarbageCollectUnreferencedBatches");
            int numRemoved = GarbageCollectUnreferencedBatches(unreferencedInternalIndices);
            Profiler.EndSample();

            if (numNewChunks > 0)
            {
                Profiler.BeginSample("AddNewChunks");
                int numValidNewChunks = AddNewChunks(newChunks.GetSubArray(0, numNewChunks));
                Profiler.EndSample();

                hybridChunkUpdater.PreviousBatchIndex = -1;

                var updateNewChunksJob = new UpdateNewHybridChunksJob
                {
                    NewChunks                    = newChunks,
                    HybridChunkInfo              = hybridRenderedChunkType,
                    ChunkWorldRenderBounds       = chunkWorldRenderBoundsRO,
                    HybridChunkUpdater           = hybridChunkUpdater,
                    chunkPropertyDirtyMaskHandle = chunkPropertyDirtyMask
                };

#if DEBUG_LOG_INVALID_CHUNKS
                if (numValidNewChunks != numNewChunks)
                    Debug.Log($"Tried to add {numNewChunks} new chunks, but only {numValidNewChunks} were valid, {numNewChunks - numValidNewChunks} were invalid");
#endif

                Dependency = updateNewChunksJob.Schedule(numValidNewChunks, kNumNewChunksPerThread, Dependency);
            }

            hybridChunkUpdater.ComponentTypes.Dispose(Dependency);
            newChunks.Dispose(Dependency);
            numNewChunksArray.Dispose(Dependency);

            // TODO: Need to wait for new chunk updating to complete, so there are no more jobs writing to the bitfields.
            // This could be optimized by splitting the memcpy (time consuming part) out from the jobs, because this
            // part would only need to wait for the metadata checking, not the memcpys.
            CompleteDependency();

            BlitGlobalAmbientProbe();

            StartUpdate();

            Profiler.BeginSample("UpdateBatchProperties");
            UpdateBatchProperties(batchRequiresUpdates, batchHadMovingEntities);
            Profiler.EndSample();

#if DEBUG_LOG_CHUNK_CHANGES
            if (numNewChunks > 0 || numRemoved > 0)
                Debug.Log(
                    $"Chunks changed, new chunks: {numNewChunks}, removed batches: {numRemoved}, batch count: {m_ExistingBatchInternalIndices.Count()}, chunk count: {m_MetaEntitiesForHybridRenderableChunks.CalculateEntityCount()}");
#endif

            // Kick the job that clears the batch AABBs for the next frame, so it
            // will be done by the time we update on the next frame.
            KickAABBClear();

            unreferencedInternalIndices.Dispose();
            batchRequiresUpdates.Dispose();
            batchHadMovingEntities.Dispose();
        }

        private int GarbageCollectUnreferencedBatches(NativeArray<long> unreferencedInternalIndices)
        {
            int numRemoved = 0;

            int firstInQw = 0;
            for (int i = 0; i < unreferencedInternalIndices.Length; ++i)
            {
                long qw = unreferencedInternalIndices[i];
                while (qw != 0)
                {
                    int  setBit        = math.tzcnt(qw);
                    long mask          = ~(1L << setBit);
                    int  internalIndex = firstInQw + setBit;

                    RemoveBatch(internalIndex);
                    ++numRemoved;

                    qw &= mask;
                }

                firstInQw += (int)AtomicHelpers.kNumBitsInLong;
            }

#if DEBUG_LOG_TOP_LEVEL
            Debug.Log($"GarbageCollectUnreferencedBatches(removed: {numRemoved})");
#endif

            return numRemoved;
        }

        private void UpdateBatchProperties(
            NativeArray<long> batchRequiresUpdates,
            NativeArray<long> batchHadMovingEntities)
        {
            BatchUpdateStatistics updateStatistics = default;

            int firstInQw = 0;
            for (int i = 0; i < batchRequiresUpdates.Length; ++i)
            {
                long qw = batchRequiresUpdates[i];
                while (qw != 0)
                {
                    int  setBit        = math.tzcnt(qw);
                    long mask          = (1L << setBit);
                    int  internalIndex = firstInQw + setBit;

                    bool entitiesMoved      = (batchHadMovingEntities[i] & mask) != 0;
                    var  batchMotionInfo    = (BatchMotionInfo*)m_BatchMotionInfos.GetUnsafePtr() + internalIndex;
                    int  externalBatchIndex = m_InternalToExternalIds[internalIndex];

                    UpdateBatchMotionVectors(externalBatchIndex, batchMotionInfo, entitiesMoved, ref updateStatistics);

                    if (entitiesMoved)
                        UpdateBatchBounds(internalIndex, externalBatchIndex, ref updateStatistics);

                    qw &= ~mask;
                }

                firstInQw += (int)AtomicHelpers.kNumBitsInLong;
            }

#if DEBUG_LOG_BATCH_UPDATES
            if (updateStatistics.NonZero)
                Debug.Log(
                    $"Updating batch properties. Enabled motion vectors: {updateStatistics.BatchesNeedingMotionVectors}, disabled motion vectors: {updateStatistics.BatchesWithoutMotionVectors}, updated bounds: {updateStatistics.BatchesWithChangedBounds}");
#endif
        }

        private void UpdateBatchBounds(int internalIndex, int externalBatchIndex, ref BatchUpdateStatistics updateStatistics)
        {
            int   aabbIndex = (int)(((uint)internalIndex) * HybridChunkUpdater.kFloatsPerAABB);
            float minX      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMinX];
            float minY      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMinY];
            float minZ      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMinZ];
            float maxX      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxX];
            float maxY      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxY];
            float maxZ      = m_BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxZ];

            var aabb = new MinMaxAABB
            {
                Min = new float3(minX, minY, minZ),
                Max = new float3(maxX, maxY, maxZ),
            };

            var batchBounds = (AABB)aabb;
            var batchCenter = batchBounds.Center;
            var batchSize   = batchBounds.Size;

            m_BatchRendererGroup.SetBatchBounds(
                externalBatchIndex,
                new Bounds(
                    new Vector3(batchCenter.x, batchCenter.y, batchCenter.z),
                    new Vector3(batchSize.x, batchSize.y, batchSize.z)));

#if DEBUG_LOG_BATCH_UPDATES
            ++updateStatistics.BatchesWithChangedBounds;
#endif
        }

        private void UpdateBatchMotionVectors(int externalBatchIndex,
                                              BatchMotionInfo*          batchMotionInfo,
                                              bool entitiesMoved,
                                              ref BatchUpdateStatistics updateStatistics)
        {
            if (batchMotionInfo->RequiresMotionVectorUpdates &&
                entitiesMoved != batchMotionInfo->MotionVectorFlagSet)
            {
#if UNITY_2020_1_OR_NEWER
                if (entitiesMoved)
                {
#if DEBUG_LOG_BATCH_UPDATES
                    ++updateStatistics.BatchesNeedingMotionVectors;
#endif
                    m_BatchRendererGroup.SetBatchFlags(
                        externalBatchIndex,
                        (int)BatchFlags.NeedMotionVectorPassFlag);

                    batchMotionInfo->MotionVectorFlagSet = true;
                }
                else
                {
#if DEBUG_LOG_BATCH_UPDATES
                    ++updateStatistics.BatchesWithoutMotionVectors;
#endif
                    m_BatchRendererGroup.SetBatchFlags(
                        externalBatchIndex,
                        0);

                    batchMotionInfo->MotionVectorFlagSet = false;
                }
#endif
            }
        }

        private void RemoveBatch(int internalBatchIndex)
        {
            int externalBatchIndex = m_InternalToExternalIds[internalBatchIndex];

            var batchInfo                          = m_BatchInfos[internalBatchIndex];
            m_BatchInfos[internalBatchIndex]       = default;
            m_BatchMotionInfos[internalBatchIndex] = default;

#if USE_PICKING_MATRICES
            m_BatchPickingMatrices[internalBatchIndex] = IntPtr.Zero;
#endif

#if DEBUG_LOG_BATCHES
            Debug.Log($"RemoveBatch(internalBatchIndex: {internalBatchIndex}, externalBatchIndex: {externalBatchIndex})");
#endif

            m_BatchRendererGroup.RemoveBatch(externalBatchIndex);
            RemoveBatchIndex(internalBatchIndex, externalBatchIndex);

            ref var properties = ref batchInfo.Properties;
            for (int i = 0; i < properties.Length; ++i)
            {
                var gpuAllocation = (properties.Ptr + i)->GPUAllocation;
                if (!gpuAllocation.Empty)
                {
                    m_GPUPersistentAllocator.Release(gpuAllocation);
#if DEBUG_LOG_MEMORY_USAGE
                    Debug.Log($"RELEASE; {gpuAllocation.Length}");
#endif
                }
            }

            ref var metadataAllocations = ref batchInfo.ChunkMetadataAllocations;
            for (int i = 0; i < metadataAllocations.Length; ++i)
            {
                var metadataAllocation = metadataAllocations.Ptr[i];
                if (!metadataAllocation.Empty)
                {
                    for (ulong j = metadataAllocation.begin; j < metadataAllocation.end; ++j)
                        m_ChunkProperties[(int)j] = default;

                    m_ChunkMetadataAllocator.Release(metadataAllocation);
                }
            }

            batchInfo.Dispose();
        }

        private int AddNewChunks(NativeArray<ArchetypeChunk> newChunksX)
        {
            int numValidNewChunks = 0;

            Debug.Assert(newChunksX.Length > 0, "Attempted to add new chunks, but list of new chunks was empty");

            var hybridChunkInfoType = GetComponentTypeHandle<HybridChunkInfo>();
            // Sort new chunks by RenderMesh so we can put
            // all compatible chunks inside one batch.
            var batchCreateInfoFactory = new BatchCreateInfoFactory
            {
                EntityManager                         = EntityManager,
                RenderMeshTypeHandle                  = GetSharedComponentTypeHandle<RenderMesh>(),
                EditorRenderDataTypeHandle            = GetSharedComponentTypeHandle<EditorRenderData>(),
                HybridBatchPartitionHandle            = GetSharedComponentTypeHandle<HybridBatchPartition>(),
                RenderMeshFlippedWindingTagTypeHandle = GetComponentTypeHandle<RenderMeshFlippedWindingTag>(),
#if UNITY_EDITOR
                DefaultEditorRenderData = new EditorRenderData
                { SceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask },
#else
                DefaultEditorRenderData = new EditorRenderData { SceneCullingMask = ~0UL },
#endif
            };
            var batchCompatibility = new BatchCompatibility(this, batchCreateInfoFactory, newChunksX);
            // This also sorts invalid chunks to the back.
            var sortedNewChunks = batchCompatibility.SortChunks();

            int batchBegin   = 0;
            int numInstances = sortedNewChunks[0].Capacity;

            for (int i = 1; i <= sortedNewChunks.Length; ++i)
            {
                int  instancesInChunk = 0;
                bool breakBatch       = false;

                if (i < sortedNewChunks.Length)
                {
                    var chunk        = sortedNewChunks[i];
                    breakBatch       = !batchCompatibility.IsCompatible(EntityManager, this, batchBegin, i);
                    instancesInChunk = chunk.Capacity;
                }
                else
                {
                    breakBatch = true;
                }

                if (numInstances + instancesInChunk > kMaxEntitiesPerBatch)
                    breakBatch = true;

                if (breakBatch)
                {
                    int numChunks = i - batchBegin;

                    var  createInfo = batchCompatibility.CreateInfoFor(batchBegin);
                    bool valid      = AddNewBatch(ref createInfo, ref hybridChunkInfoType,
                                                  sortedNewChunks.GetSubArray(batchBegin, numChunks), numInstances);

                    // As soon as we encounter an invalid chunk, we know that all the rest are invalid
                    // too.
                    if (valid)
                        numValidNewChunks += numChunks;
                    else
                    {
                        NativeArray<ArchetypeChunk>.Copy(sortedNewChunks, newChunksX, sortedNewChunks.Length);
                        return numValidNewChunks;
                    }

                    batchBegin   = i;
                    numInstances = instancesInChunk;
                }
                else
                {
                    numInstances += instancesInChunk;
                }
            }

            batchCompatibility.Dispose();
            sortedNewChunks.Dispose();

            return numValidNewChunks;
        }

        private BatchInfo CreateBatchInfo(ref BatchCreateInfo createInfo, NativeArray<ArchetypeChunk> chunks,
                                          int numInstances, Material material = null)
        {
            BatchInfo batchInfo = default;

            if (material == null)
                material = createInfo.RenderMesh.material;

            if (material == null || material.shader == null)
                return batchInfo;

#if UNITY_2020_1_OR_NEWER
            var shaderProperties = HybridV2ShaderReflection.GetDOTSInstancingProperties(material.shader);

            ref var properties               = ref batchInfo.Properties;
            ref var overrideComponents       = ref batchInfo.OverrideComponents;
            ref var sharedOverrideComponents = ref batchInfo.OverrideSharedComponents;
            // TODO: This can be made a Temp allocation if the to-be-released GPU allocations are stored separately
            // and preferably batched into one allocation
            properties = new UnsafeList<BatchInfo.BatchProperty>(
                shaderProperties.Length,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);
            overrideComponents = new UnsafeList<BatchInfo.BatchOverrideComponent>(
                shaderProperties.Length,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            sharedOverrideComponents = new UnsafeList<BatchInfo.BatchOverrideComponent>(
                shaderProperties.Length,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);
            batchInfo.ChunkMetadataAllocations = new UnsafeList<HeapBlock>(
                shaderProperties.Length,
                Allocator.Persistent,
                NativeArrayOptions.ClearMemory);

            float4x4 defaultValue = default;

            for (int i = 0; i < shaderProperties.Length; ++i)
            {
                var shaderProperty = shaderProperties[i];
                int nameID         = shaderProperty.ConstantNameID;
                BatchPropertyOverrideStatus overrideStatus = BatchPropertyOverrideStatus.Uninitialized;

                bool isBuiltin = shaderProperty.CbufferIndex == kBuiltinCbufferIndex;

                short sizeCPU                   = 0;
                int overridesStartIndex       = -1;
                int sharedOverridesStartIndex = -1;

                {
                    bool foundMaterialPropertyType = m_MaterialPropertyTypes.TryGetFirstValue(
                        nameID,
                        out var materialPropertyType,
                        out var it);

                    while (foundMaterialPropertyType)
                    {
                        // There can be multiple components that override some particular NameID, so add
                        // entries for all of them.
                        if (materialPropertyType.SizeBytesGPU == shaderProperty.SizeBytes ||
                            materialPropertyType.SizeBytesCPU == shaderProperty.SizeBytes
                            )  // TODO: hack to work around the property being the real size after load
                        {
                            if (overridesStartIndex < 0)
                                overridesStartIndex = overrideComponents.Length;

                            overrideComponents.Add(new BatchInfo.BatchOverrideComponent
                            {
                                BatchPropertyIndex = i,
                                TypeIndex          = materialPropertyType.TypeIndex,
                            });

                            sizeCPU = materialPropertyType.SizeBytesCPU;

                            // We cannot ask default values for builtins from the material, that causes errors.
                            // Instead, check whether one was registered manually when the overriding type
                            // was registered. In case there are several overriding types, we use the first
                            // one with a registered value.
                            if (isBuiltin && overrideStatus == BatchPropertyOverrideStatus.Uninitialized)
                            {
                                switch (materialPropertyType.DefaultValueKind)
                                {
                                    case DefaultValueKind.ZeroDefault:
                                    default:
                                        break;
                                    case DefaultValueKind.NonzeroDefault:
                                        defaultValue   = m_MaterialPropertyDefaultValues[materialPropertyType.TypeIndex];
                                        overrideStatus = BatchPropertyOverrideStatus.SharedNonzeroDefault;
                                        break;
                                    case DefaultValueKind.SharedBuiltin:
                                        overrideStatus = BatchPropertyOverrideStatus.SharedBuiltin;
                                        break;
                                }
                            }
                        }
                        else
                        {
#if USE_PROPERTY_ASSERTS
                            Debug.Log(
                                $"Shader \"{material.shader.name}\" expects property \"{m_MaterialPropertyNames[nameID]}\" to have size {shaderProperty.SizeBytes}, but overriding component \"{m_MaterialPropertyTypeNames[materialPropertyType.TypeIndex]}\" has size {materialPropertyType.SizeBytesGPU} instead.");
#endif
                        }

                        foundMaterialPropertyType =
                            m_MaterialPropertyTypes.TryGetNextValue(out materialPropertyType, ref it);
                    }
                }

                {
                    bool foundSharedMaterialPropertyType = m_MaterialPropertyTypesShared.TryGetFirstValue(
                        nameID,
                        out var sharedMaterialPropertyType,
                        out var it);

                    while (foundSharedMaterialPropertyType)
                    {
                        if (sharedMaterialPropertyType.SizeBytesGPU == shaderProperty.SizeBytes ||
                            sharedMaterialPropertyType.SizeBytesCPU == shaderProperty.SizeBytes
                            )  // TODO: hack to work around the property being the real size after load
                        {
                            if (sharedOverridesStartIndex < 0)
                                sharedOverridesStartIndex = sharedOverrideComponents.Length;

                            if (sizeCPU == 0)
                            {
                                sizeCPU = sharedMaterialPropertyType.SizeBytesCPU;
                            }
                            else if (sizeCPU == sharedMaterialPropertyType.SizeBytesCPU)
                            {
                                sharedOverrideComponents.Add(new BatchInfo.BatchOverrideComponent
                                {
                                    BatchPropertyIndex = i,
                                    TypeIndex          = sharedMaterialPropertyType.TypeIndex,
                                });
                            }
                            else
                            {
#if USE_PROPERTY_ASSERTS
                                Debug.Log(
                                    $"Shader \"{material.shader.name}\" expects property \"{m_MaterialPropertyNames[nameID]}\" to have size {sizeCPU}, but overriding shared component \"{m_MaterialPropertyTypeNames[sharedMaterialPropertyType.TypeIndex]}\" has size {sharedMaterialPropertyType.SizeBytesGPU} instead.");
#endif
                            }
                        }
                        else
                        {
#if USE_PROPERTY_ASSERTS
                            Debug.Log(
                                $"Shader \"{material.shader.name}\" expects property \"{m_MaterialPropertyNames[nameID]}\" to have size {shaderProperty.SizeBytes}, but overriding component \"{m_MaterialPropertyTypeNames[sharedMaterialPropertyType.TypeIndex]}\" has size {sharedMaterialPropertyType.SizeBytesGPU} instead.");
#endif
                        }

                        foundSharedMaterialPropertyType =
                            m_MaterialPropertyTypesShared.TryGetNextValue(out sharedMaterialPropertyType, ref it);
                    }
                }

                int sharedBuiltinOffset = -1;
                // For non-builtin properties, we can always ask the material for defaults.
                if (!isBuiltin)
                {
                    var propertyDefault = DefaultValueFromMaterial(material, nameID, shaderProperty.SizeBytes);
                    defaultValue   = propertyDefault.Value;
                    overrideStatus = propertyDefault.Nonzero ?
                                     BatchPropertyOverrideStatus.SharedNonzeroDefault :
                                     BatchPropertyOverrideStatus.SharedZeroDefault;
                }
                // Builtins for which nothing special has been found use a zero default
                else if (overrideStatus == BatchPropertyOverrideStatus.Uninitialized)
                {
                    overrideStatus = BatchPropertyOverrideStatus.SharedZeroDefault;
                }
                else if (overrideStatus == BatchPropertyOverrideStatus.SharedBuiltin)
                {
                    if (!m_MaterialPropertySharedBuiltins.TryGetValue(nameID, out sharedBuiltinOffset))
                    {
#if USE_PROPERTY_ASSERTS
                        Debug.Log($"Shader property \"{m_MaterialPropertyNames[nameID]}\" is configured to use a shared built-in value, but it was not found.");
#endif

                        // Fall back to zero
                        overrideStatus = BatchPropertyOverrideStatus.SharedZeroDefault;
                    }
                }

                properties.Add(new BatchInfo.BatchProperty
                {
                    MetadataOffset          = shaderProperty.MetadataOffset,
                    SizeBytesCPU            = sizeCPU,
                    SizeBytesGPU            = (short)shaderProperty.SizeBytes,
                    CbufferIndex            = shaderProperty.CbufferIndex,
                    OverrideComponentsIndex = overridesStartIndex,
                    SharedBuiltinOffset     = sharedBuiltinOffset,
                    OverrideStatus          = overrideStatus,
                    DefaultValue            = defaultValue,
#if USE_PROPERTY_ASSERTS
                    NameID = nameID,
#endif
                });
            }

            // Check which properties have static component overrides in at least one chunk.
            for (int i = 0; i < sharedOverrideComponents.Length; ++i)
            {
                var componentType = sharedOverrideComponents.Ptr + i;
                var property      = properties.Ptr + componentType->BatchPropertyIndex;

                var type = GetDynamicSharedComponentTypeHandle(ComponentType.ReadOnly(componentType->TypeIndex));

                for (int j = 0; j < chunks.Length; ++j)
                {
                    if (chunks[j].Has(type))
                    {
                        property->OverrideStatus = BatchPropertyOverrideStatus.SharedComponentOverride;
                        var propertyDefault = DefaultValueFromSharedComponent(chunks[j], type);
                        property->DefaultValue = propertyDefault.Value;
                        break;
                    }
                }
            }

            // Check which properties have overrides in at least one chunk.
            for (int i = 0; i < overrideComponents.Length; ++i)
            {
                var componentType = overrideComponents.Ptr + i;
                var property      = properties.Ptr + componentType->BatchPropertyIndex;

                var type = m_ComponentTypeCache.Type(componentType->TypeIndex);

                for (int j = 0; j < chunks.Length; ++j)
                {
                    if (chunks[j].Has(type))
                    {
                        property->OverrideStatus = BatchPropertyOverrideStatus.PerEntityOverride;
                        break;
                    }
                }
            }

            for (int i = 0; i < properties.Length; ++i)
            {
                var property = properties.Ptr + i;

                if (property->OverrideStatus == BatchPropertyOverrideStatus.Uninitialized)
                    Debug.Assert(false, "Batch property override status not initialized");

                // If the property has a default value of all zeros and isn't overridden,
                // we can use the global offset which contains zero bytes, so we don't need
                // to upload a huge amount of unnecessary zeros.
                bool needsDedicatedAllocation = PropertyRequiresAllocation(property->OverrideStatus);
                if (needsDedicatedAllocation)
                {
                    // If the property is not overridden, we only need space for a single element, the default value.
                    uint sizeBytes = (uint)PropertyValuesPerAllocation(property->OverrideStatus, numInstances) * (uint)property->SizeBytesGPU;

#if DEBUG_LOG_MEMORY_USAGE
                    Debug.Log(
                        $"ALLOCATE; {m_MaterialPropertyNames[property->NameID]}; {PropertyValuesPerAllocation(property->OverrideStatus, numInstances)}; {property->SizeBytesGPU}; {sizeBytes}");
#endif

                    property->GPUAllocation = m_GPUPersistentAllocator.Allocate(sizeBytes);
                    if (property->GPUAllocation.Empty)
                        Debug.Assert(false,
                                     $"Out of memory in the Hybrid Renderer GPU instance data buffer. Attempted to allocate {sizeBytes}, buffer size: {m_GPUPersistentAllocator.Size}, free size left: {m_GPUPersistentAllocator.FreeSpace}.");
                }
            }
#endif

            return batchInfo;
        }

        private MaterialPropertyDefaultValue DefaultValueFromSharedComponent(ArchetypeChunk chunk, DynamicSharedComponentTypeHandle type)
        {
            return new MaterialPropertyDefaultValue(
                (chunk.GetSharedComponentDataBoxed(type, EntityManager) as IHybridSharedComponentFloat4Override)
                .GetFloat4OverrideData());
        }

        private int FindPropertyFromNameID(Shader shader, int nameID)
        {
            // TODO: this linear search should go away, but serialized property in shader is all string based so we can't use regular nameID sadly
            var count = shader.GetPropertyCount();
            for (int i = 0; i < count; ++i)
            {
                var id = shader.GetPropertyNameId(i);
                if (id == nameID)
                    return i;
            }

            return -1;
        }

        private MaterialPropertyDefaultValue DefaultValueFromMaterial(
            Material material, int nameID, int sizeBytes)
        {
            MaterialPropertyDefaultValue propertyDefaultValue = default;

            switch (sizeBytes)
            {
                case 4:
                    propertyDefaultValue = new MaterialPropertyDefaultValue(material.GetFloat(nameID));
                    break;
                // float2 and float3 are handled as float4 here
                case 8:
                case 12:
                case 16:
                    var shader = material.shader;
                    var i      = FindPropertyFromNameID(shader, nameID);
                    Debug.Assert(i != -1, "Could not find property in shader");
                    var type  = shader.GetPropertyType(i);
                    var flags = shader.GetPropertyFlags(i);
                    // HDR colors should never be converted, they are always linear
                    if (type == ShaderPropertyType.Color && (flags & ShaderPropertyFlags.HDR) == 0)
                    {
                        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                            propertyDefaultValue =
                                new MaterialPropertyDefaultValue((Vector4)material.GetColor(nameID).linear);
                        else
                            propertyDefaultValue =
                                new MaterialPropertyDefaultValue((Vector4)material.GetColor(nameID).gamma);
                    }
                    else
                    {
                        propertyDefaultValue = new MaterialPropertyDefaultValue(material.GetVector(nameID));
                    }
                    break;
                case 64:
                    propertyDefaultValue = float4x4.identity;  // matrix4x4 can't have default value in unity (you can't edit a matrix in a material inspector) (case 1339072)
                    break;
                default:
                    Debug.LogWarning($"Unsupported size for a material property with nameID {nameID}");
                    break;
            }

            return propertyDefaultValue;
        }

        private NativeList<ChunkProperty> ChunkOverriddenProperties(ref BatchInfo batchInfo, ArchetypeChunk chunk,
                                                                    int chunkStart, Allocator allocator)
        {
            ref var properties         = ref batchInfo.Properties;
            ref var overrideComponents = ref batchInfo.OverrideComponents;

            var overriddenProperties = new NativeList<ChunkProperty>(properties.Length, allocator);

            int prevPropertyIndex       = -1;
            int numOverridesForProperty = 0;
            int overrideIsFromIndex     = -1;

            for (int i = 0; i < overrideComponents.Length; ++i)
            {
                var componentType = overrideComponents.Ptr + i;
                int propertyIndex = componentType->BatchPropertyIndex;
                var property      = properties.Ptr + propertyIndex;

                if (property->OverrideStatus != BatchPropertyOverrideStatus.PerEntityOverride)
                    continue;

                if (propertyIndex != prevPropertyIndex)
                    numOverridesForProperty = 0;

                prevPropertyIndex = propertyIndex;

                if (property->GPUAllocation.Empty)
                {
                    Debug.Assert(false,
#if USE_PROPERTY_ASSERTS
                                 $"No valid GPU instance data buffer allocation for property {m_MaterialPropertyNames[property->NameID]}");
#else
                                 "No valid GPU instance data buffer allocation for property");
#endif
                }

                int typeIndex = componentType->TypeIndex;
                var type      = m_ComponentTypeCache.Type(typeIndex);

                if (chunk.Has(type))
                {
                    // If a chunk has multiple separate overrides for a property, it is not
                    // well defined and we ignore all but one of them and possibly issue an error.
                    if (numOverridesForProperty == 0)
                    {
                        uint sizeBytes        = (uint)property->SizeBytesGPU;
                        uint batchBeginOffset = (uint)property->GPUAllocation.begin;
                        uint chunkBeginOffset = batchBeginOffset + (uint)chunkStart * sizeBytes;

                        overriddenProperties.Add(new ChunkProperty
                        {
                            ComponentTypeIndex = typeIndex,
                            ValueSizeBytesCPU  = property->SizeBytesCPU,
                            ValueSizeBytesGPU  = property->SizeBytesGPU,
                            GPUDataBegin       = (int)chunkBeginOffset,
                        });

                        overrideIsFromIndex = i;

#if DEBUG_LOG_OVERRIDES
                        Debug.Log($"Property {m_MaterialPropertyNames[property->NameID]} overridden by component {m_MaterialPropertyTypeNames[componentType->TypeIndex]}");
#endif
                    }
                    else
                    {
#if USE_PROPERTY_ASSERTS
                        Debug.Log(
                            $"Chunk has multiple overriding components for property \"{m_MaterialPropertyNames[property->NameID]}\". Override from component \"{m_MaterialPropertyTypeNames[overrideComponents.Ptr[overrideIsFromIndex].TypeIndex]}\" used, value from component \"{m_MaterialPropertyTypeNames[componentType->TypeIndex]}\" ignored.");
#endif
                    }

                    ++numOverridesForProperty;
                }
            }

            return overriddenProperties;
        }

        private bool UseErrorMaterial(ref BatchCreateInfo createInfo)
        {
#if DISABLE_HYBRID_ERROR_MATERIAL
            // If the error material is disabled, skip all checking.
            return false;
#endif

            ref var renderMesh = ref createInfo.RenderMesh;
            var     material   = renderMesh.material;

            // If there is no mesh, we can't use an error material at all
            if (renderMesh.mesh == null)
                return false;

            // If there is a mesh, but there is no material, then we use the error material
            if (material == null)
                return true;

            // If there is a material, and it somehow doesn't have a shader, use the error material.
            if (material.shader == null)
                return true;

            // If the shader being used has compile errors, always use the error material.
            if (ShaderHasError(material.shader))
                return true;

            // If there is a material, check whether it's using the internal error shader,
            // in that case we also use the error material.
            if (material.shader == m_BuiltinErrorShader)
                return true;

            // Otherwise, don't use the error material.
            return false;
        }

        private bool ShaderHasError(Shader shader)
        {
#if UNITY_EDITOR
            bool hasErrors = false;

            // ShaderUtil.ShaderHasError is very expensive, check if we have already called
            // it for this shader.
            if (m_ShaderHasCompileErrors.TryGetValue(shader, out hasErrors))
                return hasErrors;

            // If not, we check that status once and cache the result.
            hasErrors                        = ShaderUtil.ShaderHasError(shader);
            m_ShaderHasCompileErrors[shader] = hasErrors;
            return hasErrors;
#else
            // ShaderUtil is an Editor only API
            return false;
#endif
        }

        private bool AddNewBatch(ref BatchCreateInfo createInfo,
                                 ref ComponentTypeHandle<HybridChunkInfo> hybridChunkInfoTypeHandle,
                                 NativeArray<ArchetypeChunk>              batchChunks,
                                 int numInstances)
        {
            var material       = createInfo.RenderMesh.material;
            var cachedMaterial = material;

            if (UseErrorMaterial(ref createInfo))
            {
                material = m_ErrorMaterial;

                if (material != null)
                    Debug.LogWarning("WARNING: Hybrid Renderer using error shader for batch with an erroneous material: " + cachedMaterial, cachedMaterial);
            }
            else if (!createInfo.Valid)
            {
                Debug.LogWarning("WARNING: Hybrid Renderer skipping a batch due to invalid RenderMesh.");
                return false;
            }

            // Double check for null material, this can happen if there is a problem with
            // the error material.
            if (material == null)
            {
                Debug.LogWarning("WARNING: Hybrid Renderer skipping a batch due to no valid material.");
                return false;
            }

            ref var renderMesh = ref createInfo.RenderMesh;

            int externalBatchIndex = m_BatchRendererGroup.AddBatch(
                renderMesh.mesh,
                renderMesh.subMesh,
                material,
                renderMesh.layer,
                renderMesh.castShadows,
                renderMesh.receiveShadows,
                createInfo.FlippedWinding,
                createInfo.Bounds,
                numInstances,
                null,
                createInfo.EditorRenderData.PickableObject,
                createInfo.EditorRenderData.SceneCullingMask,
                renderMesh.layerMask);
            int internalBatchIndex = AddBatchIndex(externalBatchIndex);

#if UNITY_2020_1_OR_NEWER
            if (renderMesh.needMotionVectorPass)
                m_BatchRendererGroup.SetBatchFlags(externalBatchIndex, (int)BatchFlags.NeedMotionVectorPassFlag);
#endif

            var batchInfo       = CreateBatchInfo(ref createInfo, batchChunks, numInstances, material);
            var batchMotionInfo = new BatchMotionInfo
            {
                RequiresMotionVectorUpdates = renderMesh.needMotionVectorPass,
                MotionVectorFlagSet         = renderMesh.needMotionVectorPass,
            };

#if DEBUG_LOG_BATCHES
            Debug.Log(
                $"AddBatch(internalBatchIndex: {internalBatchIndex}, externalBatchIndex: {externalBatchIndex}, properties: {batchInfo.Properties.Length}, chunks: {batchChunks.Length}, numInstances: {numInstances}, mesh: {renderMesh.mesh}, material: {material})");
#endif

            SetBatchMetadata(externalBatchIndex, ref batchInfo, material);
            AddBlitsForSharedDefaults(ref batchInfo);

#if USE_PICKING_MATRICES
            // Picking currently uses a built-in shader that renders using traditional instancing,
            // and expects matrices in an instancing array, which is how Hybrid V1 always works.
            // To support picking, we cache a pointer into the instancing matrix array of each
            // batch, and refresh the contents whenever the DOTS side matrices change.
            // This approach relies on the instancing matrices being permanently allocated (i.e.
            // not temp allocated), which is the case at the time of writing.
            var matrixArray = m_BatchRendererGroup.GetBatchMatrices(externalBatchIndex);
            m_BatchPickingMatrices[internalBatchIndex] = (IntPtr)matrixArray.GetUnsafePtr();
#endif

            CullingComponentTypes batchCullingComponentTypes = new CullingComponentTypes
            {
                RootLODRanges               = GetComponentTypeHandle<RootLODRange>(true),
                RootLODWorldReferencePoints = GetComponentTypeHandle<RootLODWorldReferencePoint>(true),
                LODRanges                   = GetComponentTypeHandle<LODRange>(true),
                LODWorldReferencePoints     = GetComponentTypeHandle<LODWorldReferencePoint>(true),
                PerInstanceCullingTag       = GetComponentTypeHandle<PerInstanceCullingTag>(true)
            };

            ref var metadataAllocations = ref batchInfo.ChunkMetadataAllocations;

            int chunkStart = 0;
            for (int i = 0; i < batchChunks.Length; ++i)
            {
                var chunk = batchChunks[i];
                AddBlitsForNotOverriddenProperties(ref batchInfo, chunk, chunkStart);
                var       overriddenProperties = ChunkOverriddenProperties(ref batchInfo, chunk, chunkStart, Allocator.Temp);
                HeapBlock metadataAllocation   = default;
                if (overriddenProperties.Length > 0)
                {
                    metadataAllocation = m_ChunkMetadataAllocator.Allocate((ulong)overriddenProperties.Length);
                    Debug.Assert(!metadataAllocation.Empty, "Failed to allocate space for chunk property metadata");
                    metadataAllocations.Add(metadataAllocation);
                }

                var chunkInfo = new HybridChunkInfo
                {
                    InternalIndex   = internalBatchIndex,
                    ChunkTypesBegin = (int)metadataAllocation.begin,
                    ChunkTypesEnd   = (int)metadataAllocation.end,
                    CullingData     = ComputeChunkCullingData(ref batchCullingComponentTypes, chunk, chunkStart),
                    Valid           = true,
                };

                if (overriddenProperties.Length > 0)
                {
                    UnsafeUtility.MemCpy(
                        (ChunkProperty*)m_ChunkProperties.GetUnsafePtr() + chunkInfo.ChunkTypesBegin,
                        overriddenProperties.GetUnsafeReadOnlyPtr(),
                        overriddenProperties.Length * sizeof(ChunkProperty));
                }

                chunk.SetChunkComponentData(hybridChunkInfoTypeHandle, chunkInfo);

#if DEBUG_LOG_CHUNKS
                Debug.Log($"AddChunk(chunk: {chunk.Count}, chunkStart: {chunkStart}, overriddenProperties: {overriddenProperties.Length})");
#endif

                chunkStart += chunk.Capacity;
            }

            batchInfo.OverrideComponents.Dispose();
            batchInfo.OverrideComponents = default;
            batchInfo.OverrideSharedComponents.Dispose();
            batchInfo.OverrideSharedComponents = default;

            m_BatchInfos[internalBatchIndex]       = batchInfo;
            m_BatchMotionInfos[internalBatchIndex] = batchMotionInfo;

            return true;
        }

        private void SetBatchMetadata(int externalBatchIndex, ref BatchInfo batchInfo, Material material)
        {
#if UNITY_2020_1_OR_NEWER
            var metadataCbuffers = HybridV2ShaderReflection.GetDOTSInstancingCbuffers(material.shader);

            var metadataCbufferStarts = new NativeArray<int>(
                metadataCbuffers.Length,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var metadataCbufferLengths = new NativeArray<int>(
                metadataCbuffers.Length,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            int totalSizeInts = 0;

            for (int i = 0; i < metadataCbuffers.Length; ++i)
            {
                int sizeInts = (int)((uint)metadataCbuffers[i].SizeBytes / sizeof(int));
                metadataCbufferStarts[i]  = totalSizeInts;
                metadataCbufferLengths[i] = sizeInts;
                totalSizeInts            += sizeInts;
            }

            var metadataCbufferStorage = new NativeArray<int>(
                totalSizeInts,
                Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            ref var properties = ref batchInfo.Properties;
            for (int i = 0; i < properties.Length; ++i)
            {
                var property      = properties.Ptr + i;
                int offsetInInts  = property->MetadataOffset / sizeof(int);
                int metadataIndex = metadataCbufferStarts[property->CbufferIndex] + offsetInInts;

                HeapBlock allocation = property->GPUAllocation;
                if (PropertyIsZero(property->OverrideStatus))
                {
                    allocation = m_SharedZeroAllocation;
                }
                else if (property->OverrideStatus == BatchPropertyOverrideStatus.SharedBuiltin)
                {
                    allocation.begin = (ulong)property->SharedBuiltinOffset;
                }

                uint metadataForProperty = PropertyHasPerEntityValues(property->OverrideStatus) ?
                                           0x80000000 :
                                           0;
                metadataForProperty                  |= (uint)allocation.begin & 0x7fffffff;
                metadataCbufferStorage[metadataIndex] = (int)metadataForProperty;

#if DEBUG_LOG_PROPERTIES
                Debug.Log(
                    $"Property(internalBatchIndex: {m_ExternalToInternalIds[externalBatchIndex]}, externalBatchIndex: {externalBatchIndex}, property: {i}, elementSize: {property->SizeBytesCPU}, cbuffer: {property->CbufferIndex}, metadataOffset: {property->MetadataOffset}, metadata: {metadataForProperty:x8})");
#endif
            }

#if DEBUG_LOG_BATCHES
            Debug.Log(
                $"SetBatchPropertyMetadata(internalBatchIndex: {m_ExternalToInternalIds[externalBatchIndex]}, externalBatchIndex: {externalBatchIndex}, numCbuffers: {metadataCbufferLengths.Length}, numMetadataInts: {metadataCbufferStorage.Length})");
#endif

            m_BatchRendererGroup.SetBatchPropertyMetadata(externalBatchIndex, metadataCbufferLengths,
                                                          metadataCbufferStorage);
#endif
        }

        private HybridChunkCullingData ComputeChunkCullingData(
            ref CullingComponentTypes cullingComponentTypes,
            ArchetypeChunk chunk, int chunkStart)
        {
            var hasLodData = chunk.Has(cullingComponentTypes.RootLODRanges) &&
                             chunk.Has(cullingComponentTypes.LODRanges);
            var hasPerInstanceCulling = !hasLodData || chunk.Has(cullingComponentTypes.PerInstanceCullingTag);

            return new HybridChunkCullingData
            {
                Flags = (byte)
                        ((hasLodData ? HybridChunkCullingData.kFlagHasLodData : 0) |
                         (hasPerInstanceCulling ? HybridChunkCullingData.kFlagInstanceCulling : 0)),
                BatchOffset         = (short)chunkStart,
                InstanceLodEnableds = default
            };
        }

        private void AddBlitsForSharedDefaults(ref BatchInfo batchInfo)
        {
            ref var properties = ref batchInfo.Properties;
            for (int i = 0; i < properties.Length; ++i)
            {
                var property = properties.Ptr + i;

                // If the property is overridden, the batch cannot use a single shared default
                // value, as there is only a single pointer for the entire batch.
                // If the default value can be shared, but is known to be zero, we will use the
                // global offset zero, so no need to upload separately for each property.
                if (!PropertyRequiresBlit(property->OverrideStatus))
                    continue;

#if DEBUG_LOG_OVERRIDES
                Debug.Log($"Property {m_MaterialPropertyNames[property->NameID]} not overridden in batch, OverrideStatus: {property->OverrideStatus}");
#endif

                uint sizeBytes        = (uint)property->SizeBytesGPU;
                uint batchBeginOffset = (uint)property->GPUAllocation.begin;

                m_DefaultValueBlits.Add(new DefaultValueBlitDescriptor
                {
                    DefaultValue      = property->DefaultValue,
                    DestinationOffset = batchBeginOffset,
                    Count             = 1,
                    ValueSizeBytes    = sizeBytes,
                });
            }
        }

        private void AddBlitsForNotOverriddenProperties(ref BatchInfo batchInfo, ArchetypeChunk chunk, int chunkStart)
        {
            ref var properties         = ref batchInfo.Properties;
            ref var overrideComponents = ref batchInfo.OverrideComponents;

            for (int i = 0; i < properties.Length; ++i)
            {
                var property = properties.Ptr + i;

                // If the property is not overridden in the batch at all, it is handled by
                // AddBlitsForSharedDefaults().
                if (property->OverrideStatus != BatchPropertyOverrideStatus.PerEntityOverride)
                    continue;

                // Loop through all components that could potentially override this property, which
                // are guaranteed to be contiguous in the array.
                int  overrideIndex = property->OverrideComponentsIndex;
                bool isOverridden  = false;

                if (property->GPUAllocation.Empty)
                {
                    Debug.Assert(false,
#if USE_PROPERTY_ASSERTS
                                 $"No valid GPU instance data buffer allocation for property {m_MaterialPropertyNames[property->NameID]}");
#else
                                 "No valid GPU instance data buffer allocation for property");
#endif
                }

                Debug.Assert(overrideIndex >= 0, "Expected a valid array index");

                while (overrideIndex < overrideComponents.Length)
                {
                    var componentType = overrideComponents.Ptr + overrideIndex;
                    if (componentType->BatchPropertyIndex != i)
                        break;

                    int typeIndex = componentType->TypeIndex;
                    var type      = m_ComponentTypeCache.Type(typeIndex);

                    if (chunk.Has(type))
                    {
#if DEBUG_LOG_OVERRIDES
                        Debug.Log($"Property {m_MaterialPropertyNames[property->NameID]} IS overridden in chunk, NOT uploading default");
#endif

                        isOverridden = true;
                        break;
                    }

                    ++overrideIndex;
                }

                if (!isOverridden)
                {
#if DEBUG_LOG_OVERRIDES
                    Debug.Log($"Property {m_MaterialPropertyNames[property->NameID]} NOT overridden in chunk, uploading default");
#endif

                    uint sizeBytes        = (uint)property->SizeBytesGPU;
                    uint batchBeginOffset = (uint)property->GPUAllocation.begin;
                    uint chunkBeginOffset = (uint)chunkStart * sizeBytes;

                    m_DefaultValueBlits.Add(new DefaultValueBlitDescriptor
                    {
                        DefaultValue      = property->DefaultValue,
                        DestinationOffset = batchBeginOffset + chunkBeginOffset,
                        Count             = (uint)chunk.Count,
                        ValueSizeBytes    = sizeBytes,
                    });
                }
            }
        }

        // Return a JobHandle that completes when the AABBs are clear. If the job
        // hasn't been kicked (i.e. it's the first frame), then do it now.
        private JobHandle EnsureAABBsCleared()
        {
            if (!m_AABBClearKicked)
                KickAABBClear();

            return m_AABBsCleared;
        }

        private void KickAABBClear()
        {
            m_AABBsCleared = new AABBClearJob
            {
                BatchAABBs = m_BatchAABBs,
            }.Schedule(m_ExternalBatchCount, 64);

            m_AABBClearKicked = true;
        }

        private void CompleteJobs()
        {
            m_AABBsCleared.Complete();
        }

        private void StartUpdate()
        {
            var persistanceBytes = m_GPUPersistentAllocator.OnePastHighestUsedAddress;
            if (persistanceBytes > m_PersistentInstanceDataSize)
            {
                while (m_PersistentInstanceDataSize < persistanceBytes)
                {
                    m_PersistentInstanceDataSize *= 2;
                }

                if (m_PersistentInstanceDataSize > kGPUBufferSizeMax)
                {
                    m_PersistentInstanceDataSize = kGPUBufferSizeMax;  // Some backends fails at loading 1024 MiB, but 1023 is fine... This should ideally be a device cap.
                }

                if (persistanceBytes > kGPUBufferSizeMax)
                    Debug.LogError(
                        "Hybrid Renderer: Current loaded scenes need more than 1GiB of persistent GPU memory. This is more than some GPU backends can allocate. Try to reduce amount of loaded data.");
            }
        }

        internal void ResizeWithMinusOne(NativeList<int> list, int newLength)
        {
            Debug.Assert(newLength > 0, "Invalid newLength argument");

            var currentLength = list.Length;

            if (newLength > currentLength)
            {
                list.Resize(newLength, NativeArrayOptions.ClearMemory);

                for (int i = currentLength; i < newLength; ++i)
                {
                    list[i] = -1;
                }
            }
        }

        static NativeList<T> NewNativeListResized<T>(int length, Allocator allocator, NativeArrayOptions resizeOptions = NativeArrayOptions.ClearMemory) where T : unmanaged
        {
            var list = new NativeList<T>(length, allocator);
            list.Resize(length, resizeOptions);

            return list;
        }

        private void BlitGlobalAmbientProbe()
        {
            if (m_GlobalAmbientProbeDirty)
            {
                var probe = m_GlobalAmbientProbe;
                BlitBytes(m_SharedAmbientProbeAllocation, &probe, UnsafeUtility.SizeOf<SHProperties>());
                m_GlobalAmbientProbeDirty = false;
            }
        }

        private void UpdateGlobalAmbientProbe(SHProperties globalAmbientProbe)
        {
            if (!s_HybridRendererEnabled)
                return;
            m_GlobalAmbientProbeDirty = globalAmbientProbe != m_GlobalAmbientProbe;
#if DEBUG_LOG_AMBIENT_PROBE
            if (m_GlobalAmbientProbeDirty)
            {
                Debug.Log(
                    $"Global Ambient probe: {globalAmbientProbe.SHAr} {globalAmbientProbe.SHAg} {globalAmbientProbe.SHAb} {globalAmbientProbe.SHBr} {globalAmbientProbe.SHBg} {globalAmbientProbe.SHBb} {globalAmbientProbe.SHC}");
            }
#endif
            m_GlobalAmbientProbe = globalAmbientProbe;
        }

        #endregion

        #region Callbacks
        protected override void OnCreate()
        {
            // If all graphics rendering has been disabled, early out from all HR functionality
#if HYBRID_RENDERER_DISABLED
            s_HybridRendererEnabled = false;
#else
            s_HybridRendererEnabled = HybridUtils.IsHybridSupportedOnSystem();
#endif
            if (!s_HybridRendererEnabled)
            {
#if !DISABLE_HYBRID_V2_SRP_LOGS
                Debug.Log("No SRP present, no compute shader support, or running with -nographics. Hybrid Renderer disabled");
#endif
                return;
            }
            m_cullingSuperSystem = World.GetOrCreateSystem<KinemationCullingSuperSystem>();
            worldBlackboardEntity.AddComponent<CullingContext>();
            worldBlackboardEntity.AddBuffer<CullingPlane>();
            worldBlackboardEntity.AddCollectionComponent(new BrgCullingContext());
            worldBlackboardEntity.AddBuffer<MaterialPropertyComponentType>();
            worldBlackboardEntity.AddCollectionComponent(new MaterialPropertiesUploadContext());

            m_PersistentInstanceDataSize = kGPUBufferSizeInitial;

            m_HybridRenderedQuery = GetEntityQuery(HybridUtils.GetHybridRenderedQueryDesc());

            m_BatchRendererGroup = new BatchRendererGroup(OnPerformCulling);

            m_GPUPersistentAllocator = new HeapAllocator(kMaxGPUAllocatorMemory, 16);
            m_ChunkMetadataAllocator = new HeapAllocator(kMaxChunkMetadata);

            m_BatchInfos       = NewNativeListResized<BatchInfo>(kInitialMaxBatchCount, Allocator.Persistent);
            m_BatchMotionInfos = NewNativeListResized<BatchMotionInfo>(kInitialMaxBatchCount, Allocator.Persistent);
#if USE_PICKING_MATRICES
            m_BatchPickingMatrices = NewNativeListResized<IntPtr>(kInitialMaxBatchCount, Allocator.Persistent);
#endif
            m_ChunkProperties              = new NativeArray<ChunkProperty>(kMaxChunkMetadata, Allocator.Persistent);
            m_ExistingBatchInternalIndices = new NativeHashMap<int, int>(128, Allocator.Persistent);
            m_ComponentTypeCache           = new ComponentTypeCache(128);

            m_BatchAABBs = NewNativeListResized<float>(kInitialMaxBatchCount * (int)HybridChunkUpdater.kFloatsPerAABB, Allocator.Persistent);

            m_DefaultValueBlits = new NativeList<DefaultValueBlitDescriptor>(Allocator.Persistent);

            m_AABBsCleared    = new JobHandle();
            m_AABBClearKicked = false;

            // Globally allocate a single zero matrix and reuse that for all default values that are pure zero
            m_SharedZeroAllocation = m_GPUPersistentAllocator.Allocate((ulong)sizeof(float4x4));
            Debug.Assert(!m_SharedZeroAllocation.Empty, "Allocation of constant-zero data failed");
            // Make sure the global zero is actually zero.
            m_DefaultValueBlits.Add(new DefaultValueBlitDescriptor
            {
                DefaultValue      = float4x4.zero,
                DestinationOffset = (uint)m_SharedZeroAllocation.begin,
                ValueSizeBytes    = (uint)sizeof(float4x4),
                Count             = 1,
            });

            m_SharedAmbientProbeAllocation = m_GPUPersistentAllocator.Allocate((ulong)UnsafeUtility.SizeOf<SHProperties>());
            Debug.Assert(!m_SharedAmbientProbeAllocation.Empty, "Allocation of the global ambient probe failed");
            UpdateGlobalAmbientProbe(new SHProperties());

            ResetIds();

            m_MetaEntitiesForHybridRenderableChunks = GetEntityQuery(
                new EntityQueryDesc

            {
                All = new[]
                {
                    ComponentType.ReadWrite<HybridChunkInfo>(),
                    ComponentType.ReadOnly<ChunkHeader>(),
                },
            });

            // Collect all components with [MaterialProperty] attribute
            m_MaterialPropertyTypes              = new NativeMultiHashMap<int, MaterialPropertyType>(256, Allocator.Persistent);
            m_MaterialPropertyTypesShared        = new NativeMultiHashMap<int, MaterialPropertyType>(256, Allocator.Persistent);
            m_SharedComponentOverrideTypeIndices = new NativeHashSet<int>(256, Allocator.Persistent);
            m_MaterialPropertyNames              = new Dictionary<int, string>();
            m_MaterialPropertyTypeNames          = new Dictionary<int, string>();
            m_MaterialPropertyDefaultValues      = new Dictionary<int, float4x4>();
            m_MaterialPropertySharedBuiltins     = new Dictionary<int, int>();
            m_ArchetypeSharedOverrideInfos       =
                new NativeHashMap<EntityArchetype, SharedComponentOverridesInfo>(256, Allocator.Persistent);

            // Some hardcoded mappings to avoid dependencies to Hybrid from DOTS
#if SRP_10_0_0_OR_NEWER
            RegisterMaterialPropertyType<LocalToWorld>(    "unity_ObjectToWorld", 4 * 4 * 3);
            RegisterMaterialPropertyType<WorldToLocal_Tag>("unity_WorldToObject", overrideTypeSizeGPU: 4 * 4 * 3);
#else
            RegisterMaterialPropertyType<LocalToWorld>(    "unity_ObjectToWorld", 4 * 4 * 4);
            RegisterMaterialPropertyType<WorldToLocal_Tag>("unity_WorldToObject", 4 * 4 * 4);
#endif

            // Explicitly use a default of all ones for probe occlusion, so stuff doesn't render as black if this isn't set.
            RegisterMaterialPropertyType<BuiltinMaterialPropertyUnity_ProbesOcclusion>(
                "unity_ProbesOcclusion",
                defaultValue: new float4(1, 1, 1, 1));
            RegisterSharedBuiltin("unity_SHAr", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHAr);
            RegisterSharedBuiltin("unity_SHAg", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHAg);
            RegisterSharedBuiltin("unity_SHAb", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHAb);
            RegisterSharedBuiltin("unity_SHBr", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHBr);
            RegisterSharedBuiltin("unity_SHBg", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHBg);
            RegisterSharedBuiltin("unity_SHBb", (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHBb);
            RegisterSharedBuiltin("unity_SHC",  (int) m_SharedAmbientProbeAllocation.begin + SHProperties.kOffsetOfSHC);

            foreach (var typeInfo in TypeManager.AllTypes)
            {
                var type = typeInfo.Type;

                bool isComponent       = typeof(IComponentData).IsAssignableFrom(type);
                bool isSharedComponent = typeof(ISharedComponentData).IsAssignableFrom(type);
                if (isComponent || isSharedComponent)
                {
                    var attributes = type.GetCustomAttributes(typeof(MaterialPropertyAttribute), false);
                    if (attributes.Length > 0)
                    {
                        var propertyAttr = (MaterialPropertyAttribute)attributes[0];

                        if (isSharedComponent)
                        {
                            Debug.Assert(typeof(IHybridSharedComponentFloat4Override).IsAssignableFrom(type),
                                         $"Hybrid Renderer ISharedComponentData overrides must implement IHybridSharedComponentOverride. Type \"{type.Name}\" does not.");
                            Debug.Assert(propertyAttr.Format == MaterialPropertyFormat.Float4,
                                         $"Hybrid Renderer ISharedComponentData overrides must have format Float4. Type \"{type.Name}\" had format {propertyAttr.Format} instead.");
                        }

                        RegisterMaterialPropertyType(type, propertyAttr.Name, propertyAttr.OverrideSizeGPU);
                    }
                }
            }

#if USE_UNITY_OCCLUSION
            m_OcclusionCulling = new OcclusionCulling();
            m_OcclusionCulling.Create(EntityManager);
#endif

            m_FirstFrameAfterInit = true;

            // Hybrid Renderer cannot use the internal error shader because it doesn't support
            // DOTS instancing, but we can check if it's set as a fallback, and use a supported
            // error shader instead.

            m_BuiltinErrorShader = Shader.Find("Hidden/InternalErrorShader");
#if UNITY_EDITOR
            m_ShaderHasCompileErrors = new Dictionary<Shader, bool>();
#endif

            Shader hybridErrorShader = null;

#if HDRP_9_0_0_OR_NEWER
            hybridErrorShader = Shader.Find("Hidden/HDRP/MaterialError");
#endif

#if URP_9_0_0_OR_NEWER
            hybridErrorShader = Shader.Find("Hidden/Universal Render Pipeline/MaterialError");
#endif

            // TODO: What about custom SRPs? Is it enough to just throw an error, or should
            // we search for a custom shader with a specific name?

            if (hybridErrorShader != null)
                m_ErrorMaterial = new Material(hybridErrorShader);
        }

        protected override void OnDestroy()
        {
            if (!Enabled)
                return;
            CompleteJobs();
            Dispose();
        }

        protected override void OnUpdate()
        {
            if (!s_HybridRendererEnabled)
                return;

            m_cullIndexThisFrame = 0;

            UpdateGlobalAmbientProbe(new SHProperties(RenderSettings.ambientProbe));

            Profiler.BeginSample("CompleteJobs");
            CompleteDependency();  // #todo
            CompleteJobs();
            Profiler.EndSample();

            int totalChunks;

            try
            {
                Profiler.BeginSample("UpdateHybridV2Batches");
                UpdateHybridV2Batches(out totalChunks);
                Profiler.EndSample();
            }
            finally
            {
            }

            CompleteDependency();

            worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new MaterialPropertiesUploadContext
            {
                chunkProperties              = m_ChunkProperties,
                componentTypeCache           = m_ComponentTypeCache,
                defaultValueBlits            = m_DefaultValueBlits,
                hybridRenderedChunkCount     = totalChunks,
                requiredPersistentBufferSize = (int)m_PersistentInstanceDataSize
            });

            HybridEditorTools.EndFrame();

            CompleteDependency();
        }

        public JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext batchCullingContext)
        {
            var batchCount = batchCullingContext.batchVisibility.Length;
            if (batchCount == 0)
                return new JobHandle();

            var exposedCullingContext = new CullingContext
            {
                cullingMatrix      = batchCullingContext.cullingMatrix,
                cullIndexThisFrame = m_cullIndexThisFrame,
                lodParameters      = batchCullingContext.lodParameters,
                nearPlane          = batchCullingContext.nearPlane
            };
            worldBlackboardEntity.SetComponentData(exposedCullingContext);
            worldBlackboardEntity.GetBuffer<CullingPlane>().Reinterpret<Plane>().CopyFrom(batchCullingContext.cullingPlanes);

            var brgCullingContext = new BrgCullingContext
            {
                cullingContext               = batchCullingContext,
                internalToExternalMappingIds = m_InternalToExternalIds
            };

            worldBlackboardEntity.SetCollectionComponentAndDisposeOld(brgCullingContext);
            m_cullingSuperSystem.Update();

            brgCullingContext = m_cullingSuperSystem.worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(false, out var finalHandle);
            // Clear the dependency since we are not reading a container.
            m_cullingSuperSystem.worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(finalHandle, true);

            m_cullIndexThisFrame++;
            m_lastSystemVersionForProperties = GlobalSystemVersion;
            return finalHandle;
        }

        #endregion

        #region Jobs
        [BurstCompile]
        internal struct ClassifyNewChunksJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>     ChunkHeader;
            [ReadOnly] public ComponentTypeHandle<HybridChunkInfo> HybridChunkInfo;

            [NativeDisableParallelForRestriction]
            public NativeArray<ArchetypeChunk> NewChunks;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> NumNewChunks;

            public void Execute(ArchetypeChunk metaChunk, int chunkIndex, int firstEntityIndex)
            {
                var chunkHeaders     = metaChunk.GetNativeArray(ChunkHeader);
                var hybridChunkInfos = metaChunk.GetNativeArray(HybridChunkInfo);

                for (int i = 0; i < metaChunk.Count; ++i)
                {
                    var chunkInfo   = hybridChunkInfos[i];
                    var chunkHeader = chunkHeaders[i];

                    if (ShouldCountAsNewChunk(chunkInfo, chunkHeader.ArchetypeChunk))
                    {
                        ClassifyNewChunk(chunkHeader.ArchetypeChunk);
                    }
                }
            }

            bool ShouldCountAsNewChunk(in HybridChunkInfo chunkInfo, in ArchetypeChunk chunk)
            {
                return !chunkInfo.Valid && !chunk.Archetype.Prefab && !chunk.Archetype.Disabled;
            }

            public unsafe void ClassifyNewChunk(ArchetypeChunk chunk)
            {
                int* numNewChunks = (int*)NumNewChunks.GetUnsafePtr();
                int  iPlus1       = System.Threading.Interlocked.Add(ref numNewChunks[0], 1);
                int  i            = iPlus1 - 1;  // C# Interlocked semantics are weird
                Debug.Assert(i < NewChunks.Length, "Out of space in the NewChunks buffer");
                NewChunks[i] = chunk;
            }
        }

        [BurstCompile]
        private struct UpdateOldHybridChunksJob : IJobChunk
        {
            public ComponentTypeHandle<HybridChunkInfo>                   HybridChunkInfo;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>    chunkPropertyDirtyMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> ChunkWorldRenderBounds;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>            ChunkHeader;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>           LocalToWorld;
            [ReadOnly] public ComponentTypeHandle<LODRange>               LodRange;
            [ReadOnly] public ComponentTypeHandle<RootLODRange>           RootLodRange;
            public HybridChunkUpdater                                     HybridChunkUpdater;

            public void Execute(ArchetypeChunk metaChunk, int chunkIndex, int firstEntityIndex)
            {
                // metaChunk is the chunk which contains the meta entities (= entities holding the chunk components) for the actual chunks

                var hybridChunkInfos = metaChunk.GetNativeArray(HybridChunkInfo);
                var chunkHeaders     = metaChunk.GetNativeArray(ChunkHeader);
                var chunkBoundsArray = metaChunk.GetNativeArray(ChunkWorldRenderBounds);
                var chunkDirtyMasks  = metaChunk.GetNativeArray(chunkPropertyDirtyMaskHandle);

                for (int i = 0; i < metaChunk.Count; ++i)
                {
                    var chunkInfo   = hybridChunkInfos[i];
                    var dirtyMask   = chunkDirtyMasks[i];
                    var chunkHeader = chunkHeaders[i];

                    var chunk = chunkHeader.ArchetypeChunk;

                    ChunkWorldRenderBounds chunkBounds = chunkBoundsArray[i];

                    bool isNewChunk         = !chunkInfo.Valid;
                    bool localToWorldChange = chunk.DidChange(LocalToWorld, HybridChunkUpdater.LastSystemVersion);

                    // When LOD ranges change, we must reset the movement grace to avoid using stale data
                    bool lodRangeChange =
                        chunk.DidOrderChange(HybridChunkUpdater.LastSystemVersion) |
                        chunk.DidChange(LodRange, HybridChunkUpdater.LastSystemVersion) |
                        chunk.DidChange(RootLodRange, HybridChunkUpdater.LastSystemVersion);

                    if (lodRangeChange)
                        chunkInfo.CullingData.MovementGraceFixed16 = 0;

                    // Don't mark new chunks for updates here, they will be handled later when they have valid batch indices.
                    if (!isNewChunk)
                        HybridChunkUpdater.MarkBatchForUpdates(chunkInfo.InternalIndex, localToWorldChange);

                    HybridChunkUpdater.ProcessChunk(ref chunkInfo, ref dirtyMask, chunk, chunkBounds);
                    hybridChunkInfos[i] = chunkInfo;
                    chunkDirtyMasks[i]  = dirtyMask;
                }

                HybridChunkUpdater.FinishExecute();
            }
        }

        [BurstCompile]
        private struct UpdateNewHybridChunksJob : IJobParallelFor
        {
            public ComponentTypeHandle<HybridChunkInfo>                   HybridChunkInfo;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>    chunkPropertyDirtyMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkWorldRenderBounds> ChunkWorldRenderBounds;

            public NativeArray<ArchetypeChunk> NewChunks;
            public HybridChunkUpdater          HybridChunkUpdater;

            public void Execute(int index)
            {
                var chunk     = NewChunks[index];
                var chunkInfo = chunk.GetChunkComponentData(HybridChunkInfo);
                var dirtyMask = chunk.GetChunkComponentData(chunkPropertyDirtyMaskHandle);

                ChunkWorldRenderBounds chunkBounds = chunk.GetChunkComponentData(ChunkWorldRenderBounds);

                Debug.Assert(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");
                HybridChunkUpdater.MarkBatchForUpdates(chunkInfo.InternalIndex, true);
                HybridChunkUpdater.ProcessValidChunk(ref chunkInfo, ref dirtyMask, chunk, chunkBounds.Value, true);
                chunk.SetChunkComponentData(HybridChunkInfo,              chunkInfo);
                chunk.SetChunkComponentData(chunkPropertyDirtyMaskHandle, dirtyMask);
                HybridChunkUpdater.FinishExecute();
            }
        }

        [BurstCompile]
        private struct AABBClearJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction] public NativeList<float> BatchAABBs;
            public void Execute(int index)
            {
                int aabbIndex = (int)(((uint)index) * HybridChunkUpdater.kFloatsPerAABB);
                Debug.Assert(aabbIndex < BatchAABBs.Length, "AABBIndex is out of BatchAABBs bounds");

                BatchAABBs[aabbIndex + HybridChunkUpdater.kMinX] = float.MaxValue;
                BatchAABBs[aabbIndex + HybridChunkUpdater.kMinY] = float.MaxValue;
                BatchAABBs[aabbIndex + HybridChunkUpdater.kMinZ] = float.MaxValue;
                BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxX] = float.MinValue;
                BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxY] = float.MinValue;
                BatchAABBs[aabbIndex + HybridChunkUpdater.kMaxZ] = float.MinValue;
            }
        }
        #endregion
    }
}

