#region Header
#if UNITY_EDITOR && !DISABLE_HYBRID_RENDERER_PICKING
#define ENABLE_PICKING
#endif

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

using MaterialPropertyType = Unity.Rendering.MaterialPropertyType;
#endregion

namespace Latios.Kinemation.Systems
{
    public unsafe partial class LatiosEntitiesGraphicsSystem
    {
        #region Managed Impl
        static Dictionary<Type, NamedPropertyMapping> s_TypeToPropertyMappings = new Dictionary<Type, NamedPropertyMapping>();

        internal static readonly bool UseConstantBuffers       = EntitiesGraphicsUtils.UseHybridConstantBufferMode();
        internal static readonly int  MaxBytesPerCBuffer       = EntitiesGraphicsUtils.MaxBytesPerCBuffer;
        internal static readonly uint BatchAllocationAlignment = (uint)EntitiesGraphicsUtils.BatchAllocationAlignment;

        internal const int kMaxBytesPerBatchRawBuffer = 16 * 1024 * 1024;

        // Reuse Lists used for GetAllUniqueSharedComponentData to avoid GC allocs every frame
        private List<RenderFilterSettings> m_RenderFilterSettings   = new List<RenderFilterSettings>();
        private List<int>                  m_SharedComponentIndices = new List<int>();

        private BatchRendererGroup m_BatchRendererGroup;

#if ENABLE_PICKING
        Material m_PickingMaterial;
#endif

        Material m_LoadingMaterial;
        Material m_ErrorMaterial;

        RegisterMaterialsAndMeshesSystem m_registerMaterialsAndMeshesSystem;
        EntitiesGraphicsSystem           m_unityEntitiesGraphicsSystem;
        KinemationCullingSuperSystem     m_cullingSuperSystem;

        private Unmanaged m_unmanaged;

        private static bool ErrorShaderEnabled => true;

#if UNITY_EDITOR
        private static bool LoadingShaderEnabled => true;
#else
        private static bool LoadingShaderEnabled => false;
#endif
        #endregion

        partial struct Unmanaged
        {
            #region Variables
            LatiosWorldUnmanaged latiosWorld;

            private long m_PersistentInstanceDataSize;
            private int  m_maxBytesPerBatch;
            private bool m_useConstantBuffers;
            private int  m_maxBytesPerCBuffer;
            private uint m_batchAllocationAlignment;
            private bool m_reuploadAllData;

            // Store this in a member variable, because culling callback
            // already sees the new value and we want to check against
            // the value that was seen by OnUpdate.
            private uint m_LastSystemVersionAtLastUpdate;
            private uint m_globalSystemVersionAtLastUpdate;

            private EntityQuery     m_EntitiesGraphicsRenderedQuery;
            private EntityQueryMask m_entitiesGraphicsRenderedQueryMask;
            private EntityQuery     m_LodSelectGroup;
            private EntityQuery     m_ChangedTransformQuery;
            private EntityQuery     m_MetaEntitiesForHybridRenderableChunksQuery;

            const int   kInitialMaxBatchCount         = 1 * 1024;
            const float kMaxBatchGrowFactor           = 2f;
            const int   kNumNewChunksPerThread        = 1;  // TODO: Tune this
            const int   kNumScatteredIndicesPerThread = 8;  // TODO: Tune this

            const int   kMaxChunkMetadata      = 1 * 1024 * 1024;
            const ulong kMaxGPUAllocatorMemory = 1024 * 1024 * 1024;  // 1GiB of potential memory space
            const long  kGPUBufferSizeInitial  = 32 * 1024 * 1024;
            const long  kGPUBufferSizeMax      = 1023 * 1024 * 1024;

            private JobHandle            m_UpdateJobDependency;
            private ThreadedBatchContext m_ThreadedBatchContext;

            private HeapAllocator m_GPUPersistentAllocator;
            private HeapBlock     m_SharedZeroAllocation;

            private HeapAllocator m_ChunkMetadataAllocator;

            private NativeList<BatchInfo>      m_BatchInfos;
            private NativeArray<ChunkProperty> m_ChunkProperties;
            private NativeParallelHashSet<int> m_ExistingBatchIndices;

            private SortedSetUnmanaged m_SortedBatchIds;

            private NativeList<ValueBlitDescriptor> m_ValueBlits;

            NativeParallelMultiHashMap<int, MaterialPropertyType> m_NameIDToMaterialProperties;
            NativeParallelHashMap<int, MaterialPropertyType>      m_TypeIndexToMaterialProperty;

            public bool m_needsFirstUpdate;

            private EntitiesGraphicsArchetypes m_GraphicsArchetypes;

            // Burst accessible variants for each shared component index
            public NativeParallelHashMap<int, BatchFilterSettings> m_FilterSettings;
            public NativeParallelHashMap<int, BRGRenderMeshArray>  m_brgRenderMeshArrays;  // Not owned

            private ThreadLocalAllocator m_ThreadLocalAllocators;

            SystemHandle m_cullingDispatchSuperSystem;
            int          m_cullPassIndexThisFrame;
            int          m_dispatchPassIndexThisFrame;
            int          m_cullPassIndexForLastDispatch;

            NativeList<JobHandle> m_cullingCallbackFinalJobHandles;  // Used for safe destruction of threaded allocators.

            ComponentTypeCache.BurstCompatibleTypeArray m_burstCompatibleTypeArray;

            private GraphicsBufferHandle m_GPUPersistentInstanceBufferHandle;
            #endregion
        }
    }
}

