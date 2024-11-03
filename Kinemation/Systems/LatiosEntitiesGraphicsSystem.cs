#region Header
// This define fails tests due to the extra log spam. Don't check this in enabled
// #define DEBUG_LOG_HYBRID_RENDERER

// #define DEBUG_LOG_CHUNK_CHANGES
// #define DEBUG_LOG_GARBAGE_COLLECTION
// #define DEBUG_LOG_BATCH_UPDATES
// #define DEBUG_LOG_CHUNKS
// #define DEBUG_LOG_INVALID_CHUNKS
// #define DEBUG_LOG_UPLOADS
// #define DEBUG_LOG_BATCH_CREATION
// #define DEBUG_LOG_BATCH_DELETION
// #define DEBUG_LOG_PROPERTY_ALLOCATIONS
// #define DEBUG_LOG_PROPERTY_UPDATES
// #define DEBUG_LOG_VISIBLE_INSTANCES
// #define DEBUG_LOG_MATERIAL_PROPERTY_TYPES
// #define DEBUG_LOG_MEMORY_USAGE
// #define DEBUG_LOG_AMBIENT_PROBE
// #define DEBUG_LOG_DRAW_COMMANDS
// #define DEBUG_LOG_DRAW_COMMANDS_VERBOSE
// #define DEBUG_VALIDATE_DRAW_COMMAND_SORT
// #define DEBUG_LOG_BRG_MATERIAL_MESH
// #define DEBUG_LOG_GLOBAL_AABB
// #define PROFILE_BURST_JOB_INTERNALS
// #define DISABLE_HYBRID_RENDERER_ERROR_LOADING_SHADER
// #define DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING

// Entities Graphics is disabled if SRP 10 is not found, unless an override define is present
// It is also disabled if -nographics is given from the command line.
#if !(SRP_10_0_0_OR_NEWER || HYBRID_RENDERER_ENABLE_WITHOUT_SRP)
#define HYBRID_RENDERER_DISABLED
#endif

#if UNITY_EDITOR
#define USE_PROPERTY_ASSERTS
#endif

#if UNITY_EDITOR
#define DEBUG_PROPERTY_NAMES
#endif

#if UNITY_EDITOR && !DISABLE_HYBRID_RENDERER_PICKING
#define ENABLE_PICKING
#endif

#if (ENABLE_UNITY_COLLECTIONS_CHECKS || DEVELOPMENT_BUILD) && !DISABLE_MATERIALMESHINFO_BOUNDS_CHECKING
#define ENABLE_MATERIALMESHINFO_BOUNDS_CHECKING
#endif

using System;
using System.Collections.Generic;
using System.Text;
using Latios.Transforms;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

using System.Runtime.InteropServices;
using Latios.Transforms.Abstract;
using Unity.Entities.Exposed;
using Unity.Rendering;
using Unity.Transforms;
#endregion

using MaterialPropertyType = Unity.Rendering.MaterialPropertyType;

#if !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
#error Latios Framework requires UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS to be defined in your scripting define symbols.
#endif

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdatePresentationSystemGroup))]
    [UpdateAfter(typeof(EntitiesGraphicsSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial class LatiosEntitiesGraphicsSystem : SubSystem
    {
        #region Variables
        /// <summary>
        /// Toggles the activation of EntitiesGraphicsSystem.
        /// </summary>
        /// <remarks>
        /// To disable this system, use the HYBRID_RENDERER_DISABLED define.
        /// </remarks>
#if HYBRID_RENDERER_DISABLED
        public static bool EntitiesGraphicsEnabled => false;
#else
        public static bool EntitiesGraphicsEnabled => EntitiesGraphicsUtils.IsEntitiesGraphicsSupportedOnSystem();
#endif

#if !DISABLE_HYBRID_RENDERER_ERROR_LOADING_SHADER
        private static bool ErrorShaderEnabled => true;
#else
        private static bool ErrorShaderEnabled => false;
#endif

#if UNITY_EDITOR && !DISABLE_HYBRID_RENDERER_ERROR_LOADING_SHADER
        private static bool LoadingShaderEnabled => true;
#else
        private static bool LoadingShaderEnabled => false;
#endif

        private long m_PersistentInstanceDataSize;

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
        private BatchRendererGroup   m_BatchRendererGroup;
        private ThreadedBatchContext m_ThreadedBatchContext;

        private HeapAllocator m_GPUPersistentAllocator;
        private HeapBlock     m_SharedZeroAllocation;

        private HeapAllocator m_ChunkMetadataAllocator;

        private NativeList<BatchInfo>      m_BatchInfos;
        private NativeArray<ChunkProperty> m_ChunkProperties;
        private NativeParallelHashSet<int> m_ExistingBatchIndices;
        private ComponentTypeCache         m_ComponentTypeCache;

        private SortedSetUnmanaged m_SortedBatchIds;

        private NativeList<ValueBlitDescriptor> m_ValueBlits;

        NativeParallelMultiHashMap<int, MaterialPropertyType> m_NameIDToMaterialProperties;
        NativeParallelHashMap<int, MaterialPropertyType>      m_TypeIndexToMaterialProperty;

        static Dictionary<Type, NamedPropertyMapping> s_TypeToPropertyMappings = new Dictionary<Type, NamedPropertyMapping>();

#if DEBUG_PROPERTY_NAMES
        internal static Dictionary<int, string> s_NameIDToName    = new Dictionary<int, string>();
        internal static Dictionary<int, string> s_TypeIndexToName = new Dictionary<int, string>();
#endif

        private bool m_FirstFrameAfterInit;

        private EntitiesGraphicsArchetypes m_GraphicsArchetypes;

        // Burst accessible filter settings for each RenderFilterSettings shared component index
        private NativeParallelHashMap<int, BatchFilterSettings> m_FilterSettings;

#if ENABLE_PICKING
        Material m_PickingMaterial;
#endif

        Material m_LoadingMaterial;
        Material m_ErrorMaterial;

        // Reuse Lists used for GetAllUniqueSharedComponentData to avoid GC allocs every frame
        private List<RenderFilterSettings> m_RenderFilterSettings   = new List<RenderFilterSettings>();
        private List<int>                  m_SharedComponentIndices = new List<int>();

        private ThreadLocalAllocator m_ThreadLocalAllocators;

        internal static readonly bool UseConstantBuffers       = EntitiesGraphicsUtils.UseHybridConstantBufferMode();
        internal static readonly int  MaxBytesPerCBuffer       = EntitiesGraphicsUtils.MaxBytesPerCBuffer;
        internal static readonly uint BatchAllocationAlignment = (uint)EntitiesGraphicsUtils.BatchAllocationAlignment;

        internal const int kMaxBytesPerBatchRawBuffer = 16 * 1024 * 1024;

        /// <summary>
        /// The maximum GPU buffer size (in bytes) that a batch can access.
        /// </summary>
        public static int MaxBytesPerBatch => UseConstantBuffers ?
        MaxBytesPerCBuffer :
        kMaxBytesPerBatchRawBuffer;

        KinemationCullingSuperSystem m_cullingSuperSystem;
#if UNITY_6000_0_OR_NEWER
        KinemationCullingDispatchSuperSystem m_cullingDispatchSuperSystem;
#endif
        int m_cullPassIndexThisFrame;
        int m_dispatchPassIndexThisFrame;
#pragma warning disable CS0414  // Variable assigned but unused in 2022 LTS
        int m_cullPassIndexForLastDispatch;
#pragma warning restore CS0414
        NativeList<JobHandle> m_cullingCallbackFinalJobHandles;  // Used for safe destruction of threaded allocators.

        ComponentTypeCache.BurstCompatibleTypeArray m_burstCompatibleTypeArray;

        private GraphicsBufferHandle m_GPUPersistentInstanceBufferHandle;

        RegisterMaterialsAndMeshesSystem m_registerMaterialsAndMeshesSystem;

        #endregion

        #region Callbacks

        /// <inheritdoc/>
        protected override void OnCreate()
        {
            var entitiesGraphicsSystem     = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            entitiesGraphicsSystem.Enabled = false;

            // If -nographics is enabled, or if there is no compute shader support, disable HR.
            if (!EntitiesGraphicsEnabled)
            {
                Enabled = false;
                Debug.Log("No SRP present, no compute shader support, or running with -nographics. Entities Graphics package disabled");
                return;
            }

            m_cullingSuperSystem = World.GetOrCreateSystemManaged<KinemationCullingSuperSystem>();
#if UNITY_6000_0_OR_NEWER
            m_cullingDispatchSuperSystem = World.GetOrCreateSystemManaged<KinemationCullingDispatchSuperSystem>();
#endif
            worldBlackboardEntity.AddComponent<CullingContext>();
            worldBlackboardEntity.AddComponent<DispatchContext>();
            worldBlackboardEntity.AddBuffer<CullingPlane>();
            worldBlackboardEntity.AddBuffer<CullingSplitElement>();
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new PackedCullingSplits { packedSplits = new NativeReference<CullingSplits>(Allocator.Persistent) });
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new BrgCullingContext());
            worldBlackboardEntity.AddBuffer<MaterialPropertyComponentType>();
            worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new MaterialPropertiesUploadContext());
            m_cullingCallbackFinalJobHandles = new NativeList<JobHandle>(Allocator.Persistent);

            m_FirstFrameAfterInit = true;

            m_PersistentInstanceDataSize = kGPUBufferSizeInitial;

            m_EntitiesGraphicsRenderedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ChunkComponentReadOnly<ChunkWorldRenderBounds>(),
                    ComponentType.ReadOnly<WorldRenderBounds>(),
                    QueryExtensions.GetAbstractWorldTransformROComponentType(),
                    ComponentType.ReadOnly<MaterialMeshInfo>(),
                    ComponentType.ChunkComponent<EntitiesGraphicsChunkInfo>(),
                },
            });
            m_entitiesGraphicsRenderedQueryMask = m_EntitiesGraphicsRenderedQuery.GetEntityQueryMask();

            m_LodSelectGroup = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<EntitiesGraphicsChunkInfo>(),
                    ComponentType.ReadOnly<ChunkHeader>()
                },
            });

            // We check for changes in the job itself rather than via filters, because we need to check for optional PostProcessMatrix changes
            m_ChangedTransformQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    QueryExtensions.GetAbstractWorldTransformROComponentType(),
                    ComponentType.ChunkComponent<EntitiesGraphicsChunkInfo>(),
                },
            });

            // We steal the BRG to avoid duplicating the Mesh and Material registration system.
            // Ideally, we want to remain compatible with plugins that register custom meshes and materials.
            // But we need our own BRG to specify our own culling callback.
            // The solution is to steal the BRG, destroy it, and then swap it with our replacement.
#if UNITY_6000_0_OR_NEWER
            m_BatchRendererGroup = new BatchRendererGroup(new BatchRendererGroupCreateInfo
            {
                cullingCallback         = this.OnPerformCulling,
                finishedCullingCallback = this.OnFinishedCulling,
                userContext             = IntPtr.Zero
            });
#else
            m_BatchRendererGroup = new BatchRendererGroup(this.OnPerformCulling, IntPtr.Zero);
#endif
            var brgField = entitiesGraphicsSystem.GetType().GetField("m_BatchRendererGroup",
                                                                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var oldBrg = brgField.GetValue(entitiesGraphicsSystem) as BatchRendererGroup;
            oldBrg.Dispose();
            brgField.SetValue(entitiesGraphicsSystem, m_BatchRendererGroup);
            // Hybrid Renderer supports all view types
            m_BatchRendererGroup.SetEnabledViewTypes(new BatchCullingViewType[]
            {
                BatchCullingViewType.Camera,
                BatchCullingViewType.Light,
                BatchCullingViewType.Picking,
                BatchCullingViewType.SelectionOutline
            });
            m_ThreadedBatchContext = m_BatchRendererGroup.GetThreadedBatchContext();

            m_GPUPersistentAllocator = new HeapAllocator(kMaxGPUAllocatorMemory, 16);
            m_ChunkMetadataAllocator = new HeapAllocator(kMaxChunkMetadata);

            m_BatchInfos           = NewNativeListResized<BatchInfo>(kInitialMaxBatchCount, Allocator.Persistent);
            m_ChunkProperties      = new NativeArray<ChunkProperty>(kMaxChunkMetadata, Allocator.Persistent);
            m_ExistingBatchIndices = new NativeParallelHashSet<int>(128, Allocator.Persistent);
            m_ComponentTypeCache   = new ComponentTypeCache(128);

            m_ValueBlits = new NativeList<ValueBlitDescriptor>(Allocator.Persistent);

            // Globally allocate a single zero matrix at offset zero, so loads from zero return zero
            m_SharedZeroAllocation = m_GPUPersistentAllocator.Allocate((ulong)sizeof(float4x4));
            Assert.IsTrue(!m_SharedZeroAllocation.Empty, "Allocation of constant-zero data failed");
            // Make sure the global zero is actually zero.
            m_ValueBlits.Add(new ValueBlitDescriptor
            {
                Value             = float4x4.zero,
                DestinationOffset = (uint)m_SharedZeroAllocation.begin,
                ValueSizeBytes    = (uint)sizeof(float4x4),
                Count             = 1,
            });
            Assert.IsTrue(m_SharedZeroAllocation.begin == 0, "Global zero allocation should have zero address");

            ResetIds();

            m_MetaEntitiesForHybridRenderableChunksQuery = GetEntityQuery(
                new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<EntitiesGraphicsChunkInfo>(),
                    ComponentType.ReadOnly<ChunkHeader>(),
                },
            });

            // Collect all components with [MaterialProperty] attribute
            m_NameIDToMaterialProperties  = new NativeParallelMultiHashMap<int, MaterialPropertyType>(256, Allocator.Persistent);
            m_TypeIndexToMaterialProperty = new NativeParallelHashMap<int, MaterialPropertyType>(256, Allocator.Persistent);

            m_GraphicsArchetypes = new EntitiesGraphicsArchetypes(256);

            m_FilterSettings = new NativeParallelHashMap<int, BatchFilterSettings>(256, Allocator.Persistent);

            // Some hardcoded mappings to avoid dependencies to Hybrid from DOTS (*cough Latios Transforms)
            RegisterMaterialPropertyType<WorldToLocal_Tag>(                            "unity_WorldToObject",   overrideTypeSizeGPU: 4 * 4 * 3);
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            RegisterMaterialPropertyType<WorldTransform>(                              "unity_ObjectToWorld",   4 * 4 * 3);
            RegisterMaterialPropertyType<PreviousTransform>(                           "unity_MatrixPreviousM", 4 * 4 * 3);
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            RegisterMaterialPropertyType<LocalToWorld>(                                "unity_ObjectToWorld",   4 * 4 * 3);
            RegisterMaterialPropertyType<BuiltinMaterialPropertyUnity_MatrixPreviousM>("unity_MatrixPreviousM", 4 * 4 * 3);
#endif

#if ENABLE_PICKING
            RegisterMaterialPropertyType(typeof(Entity), "unity_EntityId");
#endif

            foreach (var typeInfo in TypeManager.AllTypes)
            {
                var type = typeInfo.Type;

                bool isComponent = typeof(IComponentData).IsAssignableFrom(type);
                if (isComponent)
                {
                    var attributes = type.GetCustomAttributes(typeof(MaterialPropertyAttribute), false);
                    if (attributes.Length > 0)
                    {
                        var propertyAttr = (MaterialPropertyAttribute)attributes[0];

                        RegisterMaterialPropertyType(type, propertyAttr.Name, propertyAttr.OverrideSizeGPU);
                    }
                }
            }

            m_ThreadLocalAllocators = new ThreadLocalAllocator(-1);

            if (ErrorShaderEnabled)
            {
                m_ErrorMaterial = EntitiesGraphicsUtils.LoadErrorMaterial();
                if (m_ErrorMaterial != null)
                {
                    m_BatchRendererGroup.SetErrorMaterial(m_ErrorMaterial);
                }
            }

            if (LoadingShaderEnabled)
            {
                m_LoadingMaterial = EntitiesGraphicsUtils.LoadLoadingMaterial();
                if (m_LoadingMaterial != null)
                {
                    m_BatchRendererGroup.SetLoadingMaterial(m_LoadingMaterial);
                }
            }

#if ENABLE_PICKING
            m_PickingMaterial = EntitiesGraphicsUtils.LoadPickingMaterial();
            if (m_PickingMaterial != null)
            {
                m_BatchRendererGroup.SetPickingMaterial(m_PickingMaterial);
            }
#endif
            InitializeMaterialProperties();
            var     uploadMaterialPropertiesSystem      = World.Unmanaged.GetExistingUnmanagedSystem<UploadMaterialPropertiesSystem>();
            ref var uploadMaterialPropertiesSystemState = ref World.Unmanaged.ResolveSystemStateRef(uploadMaterialPropertiesSystem);
            m_ComponentTypeCache.FetchTypeHandles(ref uploadMaterialPropertiesSystemState);
            ref var uploadMaterialPropertiesSystemMemory =
                ref World.Unmanaged.GetUnsafeSystemRef<UploadMaterialPropertiesSystem>(uploadMaterialPropertiesSystem);
            uploadMaterialPropertiesSystemMemory.m_burstCompatibleTypeArray = m_ComponentTypeCache.ToBurstCompatible(Allocator.Persistent);
            m_ComponentTypeCache.FetchTypeHandles(ref CheckedStateRef);
            m_burstCompatibleTypeArray = m_ComponentTypeCache.ToBurstCompatible(Allocator.Persistent);
            // This assumes uploadMaterialPropertiesSystem is already fully created from when the KinemationCullingSuperSystem was created.
            m_GPUPersistentInstanceBufferHandle = uploadMaterialPropertiesSystemMemory.m_GPUPersistentInstanceBufferHandle;

            // UsedTypes values are the ComponentType values while the keys are the same
            // except with the bit flags in the high bits masked off.
            // The HybridRenderer packs ComponentTypeHandles by the order they show up
            // in the value array from the hashmap.
            var types  = m_ComponentTypeCache.UsedTypes.GetValueArray(Allocator.Temp);
            var ctypes = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>().Reinterpret<ComponentType>();
            ctypes.ResizeUninitialized(types.Length);
            for (int i = 0; i < types.Length; i++)
                ctypes[i] = ComponentType.ReadOnly(types[i]);

            m_registerMaterialsAndMeshesSystem = World.GetExistingSystemManaged<RegisterMaterialsAndMeshesSystem>();
        }

        /// <inheritdoc/>
        protected override void OnDestroy()
        {
            if (!EntitiesGraphicsEnabled)
                return;
            CompleteJobs(true);
            Dispose();
            m_burstCompatibleTypeArray.Dispose(default);
        }

        /// <inheritdoc/>
        protected override void OnUpdate()
        {
            var unitySystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (unitySystem != null && unitySystem.Enabled == true)
            {
                UnityEngine.Debug.Log("Entities Graphics was enabled!");
                unitySystem.Enabled = false;
            }

            JobHandle inputDeps = Dependency;

            // Make sure any release culling jobs that have stored pointers in temp allocated
            // memory have finished before we rewind
            Dependency = default;
            worldBlackboardEntity.GetCollectionComponent<BrgCullingContext>(false);
            CompleteDependency();
            if (!m_cullingCallbackFinalJobHandles.IsEmpty)
                JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
            m_cullingCallbackFinalJobHandles.Clear();
            worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(default, false);

            m_ThreadLocalAllocators.Rewind();
            m_cullPassIndexThisFrame       = 0;
            m_dispatchPassIndexThisFrame   = 0;
            m_cullPassIndexForLastDispatch = -1;

            m_LastSystemVersionAtLastUpdate   = LastSystemVersion;
            m_globalSystemVersionAtLastUpdate = GlobalSystemVersion;

            if (m_FirstFrameAfterInit)
            {
                OnFirstFrame();
                m_FirstFrameAfterInit = false;
            }

            Profiler.BeginSample("CompleteJobs");
            inputDeps.Complete();  // #todo
            CompleteJobs();
            Profiler.EndSample();

            Profiler.BeginSample("UpdateFilterSettings");
            var updateFilterSettingsHandle = UpdateFilterSettings(inputDeps);
            Profiler.EndSample();

            inputDeps = JobHandle.CombineDependencies(inputDeps, updateFilterSettingsHandle);

            int totalChunks = 0;
            var done        = new JobHandle();
            try
            {
                Profiler.BeginSample("UpdateEntitiesGraphicsBatches");
                done = UpdateEntitiesGraphicsBatches(inputDeps, out totalChunks);
                Profiler.EndSample();

                Profiler.BeginSample("EndUpdate");
                EndUpdate();
                Profiler.EndSample();
            }
            finally
            {
                //m_GPUUploader.FrameCleanup();
            }

            worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new MaterialPropertiesUploadContext
            {
                chunkProperties          = m_ChunkProperties,
                valueBlits               = m_ValueBlits,
                hybridRenderedChunkCount = totalChunks,
            });

            EntitiesGraphicsEditorTools.EndFrame();

            Dependency = done;
        }

        private unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext batchCullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            using var callbackMarker = m_latiosPerformCullingMarker.Auto();

#if UNITY_6000_0_OR_NEWER
            cullingOutput.customCullingResult[0] = (IntPtr)m_cullPassIndexThisFrame;
#endif
            //UnityEngine.Debug.Log($"OnPerformCulling pass {m_cullPassIndexThisFrame} of type {batchCullingContext.viewType}");

            var setup = new BurstCullingSetup
            {
                wrappedIncludeExcludeList       = new WrappedPickingIncludeExcludeList(batchCullingContext.viewType),
                entityManager                   = EntityManager,
                worldBlackboardEntity           = worldBlackboardEntity,
                cullPassIndexThisFrame          = m_cullPassIndexThisFrame,
                dispatchPassIndexThisFrame      = m_dispatchPassIndexThisFrame,
                globalSystemVersionAtLastUpdate = m_globalSystemVersionAtLastUpdate,
                lastSystemVersionAtLastUpdate   = m_LastSystemVersionAtLastUpdate,
                batchCullingContext             = batchCullingContext,
                shadowProjection                = QualitySettings.shadowProjection,
                threadLocalAllocators           = m_ThreadLocalAllocators,
                filterSettings                  = m_FilterSettings,
                brgRenderMeshArrays             = m_registerMaterialsAndMeshesSystem.BRGRenderMeshArrays,
                cullingOutput                   = cullingOutput,

                //m_beforeCreateSplitsMarker = m_beforeCreateSplitsMarker,
                //m_createSplitsMarker       = m_createSplitsMarker,
                //m_afterCreateSplitsMarker  = m_afterCreateSplitsMarker
            };
            if (!DoBurstCullingSetup(&setup))
                return default;

            SuperSystem.UpdateSystem(latiosWorldUnmanaged, m_cullingSuperSystem.SystemHandle);

            var finalize = new BurstCullingFinalize
            {
                latiosWorldUnmanaged           = latiosWorldUnmanaged,
                cullingCallbackFinalJobHandles = m_cullingCallbackFinalJobHandles,
                finalHandle                    = default,

                //m_afterCullingMarker = m_afterCullingMarker,
            };
            DoBurstCullingFinalize(&finalize);

            m_cullPassIndexThisFrame++;
#if !UNITY_6000_0_OR_NEWER
            m_dispatchPassIndexThisFrame++;
            if (m_dispatchPassIndexThisFrame > 1024)
            {
                JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
                m_ThreadLocalAllocators.Rewind();
            }
#endif
            return finalize.finalHandle;
        }

#if UNITY_6000_0_OR_NEWER
        private unsafe void OnFinishedCulling(IntPtr customCullingResult)
        {
            //UnityEngine.Debug.Log($"OnFinishedCulling pass {(int)customCullingResult}");

            if (m_cullPassIndexThisFrame == m_cullPassIndexForLastDispatch)
                return;

            m_cullingDispatchSuperSystem.Update();
            m_cullPassIndexForLastDispatch = m_cullPassIndexThisFrame;
            m_dispatchPassIndexThisFrame++;
            if (m_dispatchPassIndexThisFrame > 1024)
            {
                JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
                m_ThreadLocalAllocators.Rewind();
            }
        }
#endif
        #endregion

        #region Burst Culling
        ProfilerMarker m_latiosPerformCullingMarker = new ProfilerMarker("LatiosOnPerformCulling");
        //ProfilerMarker m_beforeCreateSplitsMarker   = new ProfilerMarker("BeforeCreateSplits");
        //ProfilerMarker m_createSplitsMarker         = new ProfilerMarker("CreateSplits");
        //ProfilerMarker m_afterCreateSplitsMarker    = new ProfilerMarker("AfterCreateSplits");
        //ProfilerMarker m_afterCullingMarker         = new ProfilerMarker("AfterCulling");

        struct BurstCullingSetup
        {
            public WrappedPickingIncludeExcludeList                wrappedIncludeExcludeList;
            public EntityManager                                   entityManager;
            public BlackboardEntity                                worldBlackboardEntity;
            public int                                             cullPassIndexThisFrame;
            public int                                             dispatchPassIndexThisFrame;
            public uint                                            globalSystemVersionAtLastUpdate;
            public uint                                            lastSystemVersionAtLastUpdate;
            public BatchCullingContext                             batchCullingContext;
            public ShadowProjection                                shadowProjection;
            public ThreadLocalAllocator                            threadLocalAllocators;
            public NativeParallelHashMap<int, BatchFilterSettings> filterSettings;
            public NativeParallelHashMap<int, BRGRenderMeshArray>  brgRenderMeshArrays;
            public BatchCullingOutput                              cullingOutput;

            //public ProfilerMarker m_beforeCreateSplitsMarker;
            //public ProfilerMarker m_createSplitsMarker;
            //public ProfilerMarker m_afterCreateSplitsMarker;

            public bool Do()
            {
                //m_beforeCreateSplitsMarker.Begin();
                IncludeExcludeListFilter includeExcludeListFilter = GetPickingIncludeExcludeListFilterForCurrentCullingCallback(entityManager,
                                                                                                                                batchCullingContext,
                                                                                                                                wrappedIncludeExcludeList,
                                                                                                                                threadLocalAllocators.GeneralAllocator->ToAllocator);

                // If inclusive filtering is enabled and we know there are no included entities,
                // we can skip all the work because we know that the result will be nothing.
                if (includeExcludeListFilter.IsIncludeEnabled && includeExcludeListFilter.IsIncludeEmpty)
                {
                    includeExcludeListFilter.Dispose();
                    Profiler.EndSample();
                    return false;
                }

                worldBlackboardEntity.SetComponentData(new CullingContext
                {
                    cullIndexThisFrame  = cullPassIndexThisFrame,
                    cullingFlags        = batchCullingContext.cullingFlags,
                    cullingLayerMask    = batchCullingContext.cullingLayerMask,
                    localToWorldMatrix  = batchCullingContext.localToWorldMatrix,
                    lodParameters       = batchCullingContext.lodParameters,
                    projectionType      = batchCullingContext.projectionType,
                    receiverPlaneCount  = batchCullingContext.receiverPlaneCount,
                    receiverPlaneOffset = batchCullingContext.receiverPlaneOffset,
                    sceneCullingMask    = batchCullingContext.sceneCullingMask,
                    viewID              = batchCullingContext.viewID,
                    viewType            = batchCullingContext.viewType,
                });
                worldBlackboardEntity.SetComponentData(new DispatchContext
                {
                    globalSystemVersionOfLatiosEntitiesGraphics = globalSystemVersionAtLastUpdate,
                    lastSystemVersionOfLatiosEntitiesGraphics   = lastSystemVersionAtLastUpdate,
                    dispatchIndexThisFrame                      = dispatchPassIndexThisFrame
                });

                var cullingPlanesBuffer = worldBlackboardEntity.GetBuffer<CullingPlane>(false);
                cullingPlanesBuffer.Clear();
                cullingPlanesBuffer.Reinterpret<Plane>().AddRange(batchCullingContext.cullingPlanes);
                var splitsBuffer = worldBlackboardEntity.GetBuffer<CullingSplitElement>(false);
                splitsBuffer.Reinterpret<CullingSplit>().AddRange(batchCullingContext.cullingSplits);
                var packedSplits = worldBlackboardEntity.GetCollectionComponent<PackedCullingSplits>(false);
                //m_beforeCreateSplitsMarker.End();
                //m_createSplitsMarker.Begin();
                fixed (BatchCullingContext* bcc = &batchCullingContext)
                {
                    packedSplits.packedSplits.Value = CullingSplits.Create(bcc, shadowProjection, threadLocalAllocators.GeneralAllocator->Handle);
                }
                //m_createSplitsMarker.End();
                //m_afterCreateSplitsMarker.Begin();
                worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new BrgCullingContext
                {
                    cullingThreadLocalAllocator                          = threadLocalAllocators,
                    batchCullingOutput                                   = cullingOutput,
                    batchFilterSettingsByRenderFilterSettingsSharedIndex = filterSettings,
                    // To be able to access the material/mesh IDs, we need access to the registered material/mesh
                    // arrays. If we can't get them, then we simply skip in those cases.
                    brgRenderMeshArrays = brgRenderMeshArrays,

#if UNITY_EDITOR
                    includeExcludeListFilter = includeExcludeListFilter,
#endif
                });
                worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(  default, false);
                worldBlackboardEntity.UpdateJobDependency<PackedCullingSplits>(default, false);
                //m_afterCreateSplitsMarker.End();
                return true;
            }
        }

        [BurstCompile]
        static bool DoBurstCullingSetup(BurstCullingSetup* setup) => setup->Do();

        struct BurstCullingFinalize
        {
            public LatiosWorldUnmanaged  latiosWorldUnmanaged;
            public NativeList<JobHandle> cullingCallbackFinalJobHandles;
            public JobHandle             finalHandle;

            //public ProfilerMarker m_afterCullingMarker;

            public void Do()
            {
                //m_afterCullingMarker.Begin();
                var worldBlackboardEntity = latiosWorldUnmanaged.worldBlackboardEntity;
                latiosWorldUnmanaged.GetCollectionComponent<BrgCullingContext>(worldBlackboardEntity, out var finalHandle);
                worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(finalHandle, false);
                cullingCallbackFinalJobHandles.Add(finalHandle);
                //m_afterCullingMarker.End();
            }
        }

        [BurstCompile]
        static void DoBurstCullingFinalize(BurstCullingFinalize* finalize) => finalize->Do();
        #endregion

        #region Helper Methods

        /// <summary>
        /// Registers a material property type with the given name.
        /// </summary>
        /// <param name="type">The type of material property to register.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="overrideTypeSizeGPU">An optional size of the type on the GPU.</param>
        public static void RegisterMaterialPropertyType(Type type, string propertyName, short overrideTypeSizeGPU = -1)
        {
            Assert.IsTrue(type != null,                        "type must be non-null");
            Assert.IsTrue(!string.IsNullOrEmpty(propertyName), "Property name must be valid");

            short typeSizeCPU = (short)UnsafeUtility.SizeOf(type);
            if (overrideTypeSizeGPU == -1)
                overrideTypeSizeGPU = typeSizeCPU;

            // For now, we only support overriding one material property with one type.
            // Several types can override one property, but not the other way around.
            // If necessary, this restriction can be lifted in the future.
            if (s_TypeToPropertyMappings.ContainsKey(type))
            {
                string prevPropertyName = s_TypeToPropertyMappings[type].Name;
                Assert.IsTrue(propertyName.Equals(
                                  prevPropertyName),
                              $"Attempted to register type {type.Name} with multiple different property names. Registered with \"{propertyName}\", previously registered with \"{prevPropertyName}\".");
            }
            else
            {
                var pm                         = new NamedPropertyMapping();
                pm.Name                        = propertyName;
                pm.SizeCPU                     = typeSizeCPU;
                pm.SizeGPU                     = overrideTypeSizeGPU;
                s_TypeToPropertyMappings[type] = pm;
            }
        }

        /// <summary>
        /// A templated version of the material type registration method.
        /// </summary>
        /// <typeparam name="T">The type of material property to register.</typeparam>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="overrideTypeSizeGPU">An optional size of the type on the GPU.</param>
        public static void RegisterMaterialPropertyType<T>(string propertyName, short overrideTypeSizeGPU = -1)
            where T : IComponentData
        {
            RegisterMaterialPropertyType(typeof(T), propertyName, overrideTypeSizeGPU);
        }

        private void InitializeMaterialProperties()
        {
            m_NameIDToMaterialProperties.Clear();

            foreach (var kv in s_TypeToPropertyMappings)
            {
                Type   type         = kv.Key;
                string propertyName = kv.Value.Name;

                short sizeBytesCPU = kv.Value.SizeCPU;
                short sizeBytesGPU = kv.Value.SizeGPU;
                int   typeIndex    = TypeManager.GetTypeIndex(type);
                int   nameID       = Shader.PropertyToID(propertyName);

                var materialPropertyType =
                    new MaterialPropertyType
                {
                    TypeIndex    = typeIndex,
                    NameID       = nameID,
                    SizeBytesCPU = sizeBytesCPU,
                    SizeBytesGPU = sizeBytesGPU,
                };

                m_TypeIndexToMaterialProperty.Add(typeIndex, materialPropertyType);
                m_NameIDToMaterialProperties.Add(nameID, materialPropertyType);

#if DEBUG_PROPERTY_NAMES
                s_TypeIndexToName[typeIndex] = type.Name;
                s_NameIDToName[nameID]       = propertyName;
#endif

#if DEBUG_LOG_MATERIAL_PROPERTY_TYPES
                Debug.Log($"Type \"{type.Name}\" ({sizeBytesCPU} bytes) overrides material property \"{propertyName}\" (nameID: {nameID}, typeIndex: {typeIndex})");
#endif

                // We cache all IComponentData types that we know are capable of overriding properties
                m_ComponentTypeCache.UseType(typeIndex);
            }
        }

        private JobHandle UpdateEntitiesGraphicsBatches(JobHandle inputDependencies, out int totalChunks)
        {
            JobHandle done = default;
            Profiler.BeginSample("UpdateAllBatches");

            if (!m_EntitiesGraphicsRenderedQuery.IsEmptyIgnoreFilter)
                done = UpdateAllBatches(inputDependencies, out totalChunks);
            else
                totalChunks = 0;

            Profiler.EndSample();

            return done;
        }

        private void OnFirstFrame()
        {
            // Done at the end of OnCreate instead.
            //InitializeMaterialProperties();

#if DEBUG_LOG_HYBRID_RENDERER
            var mode = UseConstantBuffers ?
                       $"UBO mode (UBO max size: {MaxBytesPerCBuffer}, alignment: {BatchAllocationAlignment}, globals: {m_GlobalWindowSize})" :
                       "SSBO mode";
            Debug.Log(
                $"Entities Graphics active, MaterialProperty component type count {m_ComponentTypeCache.UsedTypeCount} / {ComponentTypeCache.BurstCompatibleTypeArray.kMaxTypes}, {mode}");
#endif
        }

        private JobHandle UpdateFilterSettings(JobHandle inputDeps)
        {
            m_RenderFilterSettings.Clear();
            m_SharedComponentIndices.Clear();

            // TODO: Maybe this could be partially jobified?

            EntityManager.GetAllUniqueSharedComponentsManaged(m_RenderFilterSettings, m_SharedComponentIndices);

            m_FilterSettings.Clear();
            for (int i = 0; i < m_SharedComponentIndices.Count; ++i)
            {
                int sharedIndex               = m_SharedComponentIndices[i];
                m_FilterSettings[sharedIndex] = MakeFilterSettings(m_RenderFilterSettings[i]);
            }

            m_RenderFilterSettings.Clear();
            m_SharedComponentIndices.Clear();

            return new JobHandle();
        }

        private static BatchFilterSettings MakeFilterSettings(RenderFilterSettings filterSettings)
        {
            return new BatchFilterSettings
            {
                layer              = (byte)filterSettings.Layer,
                renderingLayerMask = filterSettings.RenderingLayerMask,
                motionMode         = filterSettings.MotionMode,
                shadowCastingMode  = filterSettings.ShadowCastingMode,
                receiveShadows     = filterSettings.ReceiveShadows,
                staticShadowCaster = filterSettings.StaticShadowCaster,
                allDepthSorted     = false,  // set by culling
            };
        }

        private void ResetIds()
        {
            if (m_SortedBatchIds.isCreated)
                m_SortedBatchIds.Dispose();
            m_SortedBatchIds = new SortedSetUnmanaged(1024);
            m_ExistingBatchIndices.Clear();
        }

        private void EnsureHaveSpaceForNewBatch()
        {
            int currentCapacity = m_BatchInfos.Length;
            int neededCapacity  = BatchIndexRange;

            if (currentCapacity >= neededCapacity)
                return;

            Assert.IsTrue(kMaxBatchGrowFactor >= 1f,
                          "Grow factor should always be greater or equal to 1");

            var newCapacity = (int)(kMaxBatchGrowFactor * neededCapacity);

            m_BatchInfos.Resize(newCapacity, NativeArrayOptions.ClearMemory);
        }

        private void AddBatchIndex(int id)
        {
            Assert.IsTrue(!m_SortedBatchIds.Contains(id), "New batch ID already marked as used");
            m_SortedBatchIds.Add(id);
            m_ExistingBatchIndices.Add(id);
            EnsureHaveSpaceForNewBatch();
        }

        private void RemoveBatchIndex(int id)
        {
            if (!m_SortedBatchIds.Contains(id))
                Assert.IsTrue(false, $"Attempted to release an unused id {id}");
            m_SortedBatchIds.Remove(id);
            m_ExistingBatchIndices.Remove(id);
        }

        private int BatchIndexRange => m_SortedBatchIds.Max + 1;

        private void Dispose()
        {
            JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());

            if (ErrorShaderEnabled)
                Material.DestroyImmediate(m_ErrorMaterial);

            if (LoadingShaderEnabled)
                Material.DestroyImmediate(m_LoadingMaterial);

#if ENABLE_PICKING
            Material.DestroyImmediate(m_PickingMaterial);
#endif
            // EntitiesGraphicsSystem disposes this
            //m_BatchRendererGroup.Dispose();
            m_ThreadedBatchContext.batchRendererGroup = IntPtr.Zero;

            m_NameIDToMaterialProperties.Dispose();
            m_TypeIndexToMaterialProperty.Dispose();
            m_GPUPersistentAllocator.Dispose();
            m_ChunkMetadataAllocator.Dispose();

            m_BatchInfos.Dispose();
            m_ChunkProperties.Dispose();
            m_ExistingBatchIndices.Dispose();
            m_ValueBlits.Dispose();
            m_ComponentTypeCache.Dispose();

            m_SortedBatchIds.Dispose();

            m_GraphicsArchetypes.Dispose();

            m_FilterSettings.Dispose();

            if (!m_cullingCallbackFinalJobHandles.IsEmpty)
                JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
            m_cullingCallbackFinalJobHandles.Dispose();
            m_ThreadLocalAllocators.Dispose();
        }

        struct WrappedPickingIncludeExcludeList
        {
#if ENABLE_PICKING && !DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
            public PickingIncludeExcludeList includeExcludeList;

            public WrappedPickingIncludeExcludeList(BatchCullingViewType viewType)
            {
                includeExcludeList = default;
                if (viewType == BatchCullingViewType.Picking)
                    includeExcludeList = HandleUtility.GetPickingIncludeExcludeList(Allocator.Temp);
                else if (viewType == BatchCullingViewType.SelectionOutline)
                    includeExcludeList = HandleUtility.GetSelectionOutlineIncludeExcludeList(Allocator.Temp);
            }
#else
            public WrappedPickingIncludeExcludeList(BatchCullingViewType viewType)
            {
            }
#endif
        }

        // This function does only return a meaningful IncludeExcludeListFilter object when called from a BRG culling callback.
        static IncludeExcludeListFilter GetPickingIncludeExcludeListFilterForCurrentCullingCallback(EntityManager entityManager,
                                                                                                    in BatchCullingContext cullingContext,
                                                                                                    WrappedPickingIncludeExcludeList wrappedIncludeExcludeList,
                                                                                                    Allocator allocator)
        {
#if ENABLE_PICKING && !DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
            PickingIncludeExcludeList includeExcludeList = wrappedIncludeExcludeList.includeExcludeList;

            NativeArray<int> emptyArray = new NativeArray<int>(0, Allocator.Temp);

            NativeArray<int> includeEntityIndices = includeExcludeList.IncludeEntities;
            if (cullingContext.viewType == BatchCullingViewType.SelectionOutline)
            {
                // Make sure the include list for the selection outline is never null even if there is nothing in it.
                // Null NativeArray and empty NativeArray are treated as different things when used to construct an IncludeExcludeListFilter object:
                // - Null include list means that nothing is discarded because the filtering is skipped.
                // - Empty include list means that everything is discarded because the filtering is enabled but never passes.
                // With selection outline culling, we want the filtering to happen in any case even if the array contains nothing so that we don't highlight everything in the latter case.
                if (!includeEntityIndices.IsCreated)
                    includeEntityIndices = emptyArray;
            }
            else if (includeEntityIndices.Length == 0)
            {
                includeEntityIndices = default;
            }

            NativeArray<int> excludeEntityIndices = includeExcludeList.ExcludeEntities;
            if (excludeEntityIndices.Length == 0)
                excludeEntityIndices = default;

            IncludeExcludeListFilter includeExcludeListFilter = new IncludeExcludeListFilter(
                entityManager,
                includeEntityIndices,
                excludeEntityIndices,
                allocator);

            return includeExcludeListFilter;
#else
            return default;
#endif
        }

        private void DebugDrawCommands(JobHandle drawCommandsDependency, BatchCullingOutput cullingOutput)
        {
            drawCommandsDependency.Complete();

            var drawCommands = cullingOutput.drawCommands[0];

            Debug.Log(
                $"Draw Command summary: visibleInstanceCount: {drawCommands.visibleInstanceCount} drawCommandCount: {drawCommands.drawCommandCount} drawRangeCount: {drawCommands.drawRangeCount}");

#if DEBUG_LOG_DRAW_COMMANDS_VERBOSE
            bool verbose = true;
#else
            bool verbose = false;
#endif
            if (verbose)
            {
                for (int i = 0; i < drawCommands.drawCommandCount; ++i)
                {
                    var                 cmd      = drawCommands.drawCommands[i];
                    DrawCommandSettings settings = new DrawCommandSettings
                    {
                        BatchID      = cmd.batchID,
                        MaterialID   = cmd.materialID,
                        MeshID       = cmd.meshID,
                        SubMeshIndex = cmd.submeshIndex,
                        Flags        = cmd.flags,
                    };
                    Debug.Log($"Draw Command #{i}: {settings} visibleOffset: {cmd.visibleOffset} visibleCount: {cmd.visibleCount}");
                    StringBuilder sb                 = new StringBuilder((int)cmd.visibleCount * 30);
                    bool          hasSortingPosition = settings.HasSortingPosition;
                    for (int j = 0; j < cmd.visibleCount; ++j)
                    {
                        sb.Append(drawCommands.visibleInstances[cmd.visibleOffset + j]);
                        if (hasSortingPosition)
                            sb.AppendFormat(" ({0:F3} {1:F3} {2:F3})",
                                            drawCommands.instanceSortingPositions[cmd.sortingPosition + 0],
                                            drawCommands.instanceSortingPositions[cmd.sortingPosition + 1],
                                            drawCommands.instanceSortingPositions[cmd.sortingPosition + 2]);
                        sb.Append(", ");
                    }
                    Debug.Log($"Draw Command #{i} instances: [{sb}]");
                }
            }
        }

        private JobHandle UpdateAllBatches(JobHandle inputDependencies, out int totalChunks)
        {
            Profiler.BeginSample("GetComponentTypes");

            var entitiesGraphicsRenderedChunkType   = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(false);
            var entitiesGraphicsRenderedChunkTypeRO = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true);
            var chunkHeadersRO                      = GetComponentTypeHandle<ChunkHeader>(true);
            var materialMeshInfosRO                 = GetComponentTypeHandle<MaterialMeshInfo>(true);
            var renderMeshArrays                    = GetSharedComponentTypeHandle<RenderMeshArray>();

            Profiler.EndSample();

            var numNewChunksArray          = CollectionHelper.CreateNativeArray<int>(1, WorldUpdateAllocator);
            var totalChunksFromNormalQuery = m_EntitiesGraphicsRenderedQuery.CalculateChunkCountWithoutFiltering();
            var totalChunksFromMetaQuery   = m_MetaEntitiesForHybridRenderableChunksQuery.CalculateEntityCountWithoutFiltering();
            totalChunks                    = math.max(totalChunksFromNormalQuery, totalChunksFromMetaQuery);  // If these are different for some reason, reserve the larger.
            var newChunks                  = CollectionHelper.CreateNativeArray<ArchetypeChunk>(totalChunks, WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            var classifyNewChunksJob = new ClassifyNewChunksJobLatiosVersion
            {
                EntitiesGraphicsChunkInfo = entitiesGraphicsRenderedChunkTypeRO,
                ChunkHeader               = chunkHeadersRO,
                NumNewChunks              = numNewChunksArray,
                NewChunks                 = newChunks,
                chunkValidityMask         = m_entitiesGraphicsRenderedQueryMask
            }
            .ScheduleParallel(m_MetaEntitiesForHybridRenderableChunksQuery, inputDependencies);

            JobHandle entitiesGraphicsCompleted = new JobHandle();

            const int kNumBitsPerLong          = sizeof(long) * 8;
            var       unreferencedBatchIndices = CollectionHelper.CreateNativeArray<long>((BatchIndexRange + kNumBitsPerLong) / kNumBitsPerLong,
                                                                                    WorldUpdateAllocator,
                                                                                    NativeArrayOptions.ClearMemory);

            JobHandle initializedUnreferenced = default;
            var       existingKeys            = m_ExistingBatchIndices.ToNativeArray(WorldUpdateAllocator);
            initializedUnreferenced           = new InitializeUnreferencedIndicesScatterJob
            {
                ExistingBatchIndices     = existingKeys,
                UnreferencedBatchIndices = unreferencedBatchIndices,
            }.Schedule(existingKeys.Length, kNumScatteredIndicesPerThread);

            inputDependencies = JobHandle.CombineDependencies(inputDependencies, initializedUnreferenced);

            uint lastSystemVersion = LastSystemVersion;

            if (EntitiesGraphicsEditorTools.DebugSettings.ForceInstanceDataUpload)
            {
                Debug.Log("Reuploading all Entities Graphics instance data to GPU");
                lastSystemVersion = 0;
            }

            classifyNewChunksJob.Complete();
            int numNewChunks = numNewChunksArray[0];

            var maxBatchCount = math.max(kInitialMaxBatchCount, BatchIndexRange + numNewChunks);

            // Integer division with round up
            var maxBatchLongCount = (maxBatchCount + kNumBitsPerLong - 1) / kNumBitsPerLong;

            m_burstCompatibleTypeArray.Update(ref CheckedStateRef);
            var entitiesGraphicsChunkUpdater = new EntitiesGraphicsChunkUpdater
            {
                postProcessMatrixHandle         = GetComponentTypeHandle<PostProcessMatrix>(true),
                previousPostProcessMatrixHandle = GetComponentTypeHandle<PreviousPostProcessMatrix>(true),

                materialTypeHandleArray        = m_burstCompatibleTypeArray,
                chunkMaterialPropertyDirtyMask = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                unreferencedBatchIndices       = unreferencedBatchIndices,
                chunkProperties                = m_ChunkProperties,
                lastSystemVersion              = lastSystemVersion,

                worldToLocalType     = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
                prevWorldToLocalType = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                worldTransformType    = TypeManager.GetTypeIndex<WorldTransform>(),
                previousTransformType = TypeManager.GetTypeIndex<PreviousTransform>(),
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                worldTransformType    = TypeManager.GetTypeIndex<LocalToWorld>(),
                previousTransformType = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousM>(),
#endif

#if PROFILE_BURST_JOB_INTERNALS
                ProfileAddUpload = new ProfilerMarker("AddUpload"),
#endif
            };

            var updateOldJob = new UpdateOldEntitiesGraphicsChunksJob
            {
                EntitiesGraphicsChunkInfo    = entitiesGraphicsRenderedChunkType,
                ChunkHeader                  = chunkHeadersRO,
                WorldTransform               = GetDynamicComponentTypeHandle(QueryExtensions.GetAbstractWorldTransformROComponentType()),
                MaterialMeshInfo             = materialMeshInfosRO,
                EntitiesGraphicsChunkUpdater = entitiesGraphicsChunkUpdater,
            };

            JobHandle updateOldDependencies = inputDependencies;

            // We need to wait for the job to complete here so we can process the new chunks
            updateOldJob.ScheduleParallel(m_MetaEntitiesForHybridRenderableChunksQuery, updateOldDependencies).Complete();

            // Garbage collect deleted batches before adding new ones to minimize peak memory use.
            Profiler.BeginSample("GarbageCollectUnreferencedBatches");
            int numRemoved = GarbageCollectUnreferencedBatches(unreferencedBatchIndices);
            Profiler.EndSample();

            if (numNewChunks > 0)
            {
                Profiler.BeginSample("AddNewChunks");
                int numValidNewChunks = AddNewChunks(newChunks.GetSubArray(0, numNewChunks));
                Profiler.EndSample();

                var updateNewChunksJob = new UpdateNewEntitiesGraphicsChunksJob
                {
                    NewChunks                    = newChunks,
                    EntitiesGraphicsChunkInfo    = entitiesGraphicsRenderedChunkTypeRO,
                    EntitiesGraphicsChunkUpdater = entitiesGraphicsChunkUpdater,
                };

#if DEBUG_LOG_INVALID_CHUNKS
                if (numValidNewChunks != numNewChunks)
                    Debug.Log($"Tried to add {numNewChunks} new chunks, but only {numValidNewChunks} were valid, {numNewChunks - numValidNewChunks} were invalid");
#endif

                entitiesGraphicsCompleted = updateNewChunksJob.Schedule(numValidNewChunks, kNumNewChunksPerThread);
            }

            var drawCommandFlagsUpdated = new UpdateDrawCommandFlagsJob
            {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                WorldTransform    = GetComponentTypeHandle<WorldTransform>(true),
                PostProcessMatrix = GetComponentTypeHandle<PostProcessMatrix>(true),
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                WorldTransform = GetComponentTypeHandle<LocalToWorld>(true),
#endif
                RenderFilterSettings      = GetSharedComponentTypeHandle<RenderFilterSettings>(),
                EntitiesGraphicsChunkInfo = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(),
                FilterSettings            = m_FilterSettings,
                DefaultFilterSettings     = MakeFilterSettings(RenderFilterSettings.Default),
                lastSystemVersion         = LastSystemVersion
            }.ScheduleParallel(m_ChangedTransformQuery, entitiesGraphicsCompleted);
            DidScheduleUpdateJob(drawCommandFlagsUpdated);

            // TODO: Need to wait for new chunk updating to complete, so there are no more jobs writing to the bitfields.
            entitiesGraphicsCompleted.Complete();

            Profiler.BeginSample("StartUpdate");
            StartUpdate();
            Profiler.EndSample();

#if DEBUG_LOG_CHUNK_CHANGES
            if (numNewChunks > 0 || numRemoved > 0)
                Debug.Log(
                    $"Chunks changed, new chunks: {numNewChunks}, removed batches: {numRemoved}, batch count: {m_ExistingBatchBatchIndices.Count()}, chunk count: {m_MetaEntitiesForHybridRenderableChunks.CalculateEntityCount()}");
#endif

            Profiler.BeginSample("UpdateGlobalAABB");
            UpdateGlobalAABB();
            Profiler.EndSample();

            JobHandle outputDeps = drawCommandFlagsUpdated;

            return outputDeps;
        }

        private void UpdateGlobalAABB()
        {
            Psyshock.Physics.GetCenterExtents(worldBlackboardEntity.GetComponentData<BrgAabb>().aabb, out var center, out var extents);
            m_BatchRendererGroup.SetGlobalBounds(new Bounds(center, extents));
        }

        private void ComputeUploadSizeRequirements(
            int numGpuUploadOperations, NativeArray<GpuUploadOperation> gpuUploadOperations,
            out int numOperations, out int totalUploadBytes, out int biggestUploadBytes)
        {
            numOperations      = numGpuUploadOperations + m_ValueBlits.Length;
            totalUploadBytes   = 0;
            biggestUploadBytes = 0;

            for (int i = 0; i < numGpuUploadOperations; ++i)
            {
                var numBytes        = gpuUploadOperations[i].BytesRequiredInUploadBuffer;
                totalUploadBytes   += numBytes;
                biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
            }

            for (int i = 0; i < m_ValueBlits.Length; ++i)
            {
                var numBytes        = m_ValueBlits[i].BytesRequiredInUploadBuffer;
                totalUploadBytes   += numBytes;
                biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
            }
        }

        private int GarbageCollectUnreferencedBatches(NativeArray<long> unreferencedBatchIndices)
        {
            int numRemoved = 0;

            int firstInQw = 0;
            for (int i = 0; i < unreferencedBatchIndices.Length; ++i)
            {
                long qw = unreferencedBatchIndices[i];
                while (qw != 0)
                {
                    int  setBit     = math.tzcnt(qw);
                    long mask       = ~(1L << setBit);
                    int  batchIndex = firstInQw + setBit;

                    RemoveBatch(batchIndex);
                    ++numRemoved;

                    qw &= mask;
                }

                firstInQw += (int)AtomicHelpers.kNumBitsInLong;
            }

#if DEBUG_LOG_GARBAGE_COLLECTION
            Debug.Log($"GarbageCollectUnreferencedBatches(removed: {numRemoved})");
#endif

            return numRemoved;
        }

        private void RemoveBatch(int batchIndex)
        {
            var batchInfo            = m_BatchInfos[batchIndex];
            m_BatchInfos[batchIndex] = default;

#if DEBUG_LOG_BATCH_DELETION
            Debug.Log($"RemoveBatch({batchIndex})");
#endif

            RemoveBatchIndex(batchIndex);

            if (!batchInfo.GPUMemoryAllocation.Empty)
            {
                m_GPUPersistentAllocator.Release(batchInfo.GPUMemoryAllocation);
#if DEBUG_LOG_MEMORY_USAGE
                Debug.Log($"RELEASE; {batchInfo.GPUMemoryAllocation.Length}");
#endif
            }

            var metadataAllocation = batchInfo.ChunkMetadataAllocation;
            if (!metadataAllocation.Empty)
            {
                for (ulong j = metadataAllocation.begin; j < metadataAllocation.end; ++j)
                    m_ChunkProperties[(int)j] = default;

                m_ChunkMetadataAllocator.Release(metadataAllocation);
            }

            m_ThreadedBatchContext.RemoveBatch(new BatchID { value = (uint)batchIndex });
        }

        static int NumInstancesInChunk(ArchetypeChunk chunk) => chunk.Capacity;

        [BurstCompile]
        static void CreateBatchCreateInfo(
            ref BatchCreateInfoFactory batchCreateInfoFactory,
            ref NativeArray<ArchetypeChunk>  newChunks,
            ref NativeArray<BatchCreateInfo> sortedNewChunks,
            out MaterialPropertyType failureProperty
            )
        {
            failureProperty           = default;
            failureProperty.TypeIndex = -1;
            for (int i = 0; i < newChunks.Length; ++i)
            {
                sortedNewChunks[i] = batchCreateInfoFactory.Create(newChunks[i], ref failureProperty);
                if (failureProperty.TypeIndex >= 0)
                {
                    return;
                }
            }
            sortedNewChunks.Sort();
        }

        private int AddNewChunks(NativeArray<ArchetypeChunk> newChunks)
        {
            int numValidNewChunks = 0;

            Assert.IsTrue(newChunks.Length > 0, "Attempted to add new chunks, but list of new chunks was empty");

            var batchCreationTypeHandles = new BatchCreationTypeHandles
            {
                perInstanceCullingHandle                       = SystemAPI.GetComponentTypeHandle<PerInstanceCullingTag>(true),
                lodHeightPercentagesHandle                     = SystemAPI.GetComponentTypeHandle<LodHeightPercentages>(true),
                lodHeightPercentagesWithCrossfadeMarginsHandle = SystemAPI.GetComponentTypeHandle<LodHeightPercentagesWithCrossfadeMargins>(true),
            };

            // Sort new chunks by RenderMesh so we can put
            // all compatible chunks inside one batch.
            var batchCreateInfoFactory = new BatchCreateInfoFactory
            {
                GraphicsArchetypes          = m_GraphicsArchetypes,
                TypeIndexToMaterialProperty = m_TypeIndexToMaterialProperty,
            };

            var sortedNewChunks = new NativeArray<BatchCreateInfo>(newChunks.Length, Allocator.Temp);

            CreateBatchCreateInfo(ref batchCreateInfoFactory, ref newChunks, ref sortedNewChunks, out var failureProperty);
            if (failureProperty.TypeIndex >= 0)
            {
                Assert.IsTrue(false,
                              $"TypeIndex mismatch between key and stored property, Type: {failureProperty.TypeName} ({failureProperty.TypeIndex:x8}), Property: {failureProperty.PropertyName} ({failureProperty.NameID:x8})");
            }

            int batchBegin          = 0;
            int numInstances        = NumInstancesInChunk(sortedNewChunks[0].Chunk);
            int maxEntitiesPerBatch = m_GraphicsArchetypes
                                      .GetGraphicsArchetype(sortedNewChunks[0].GraphicsArchetypeIndex)
                                      .MaxEntitiesPerBatch;

            for (int i = 1; i <= sortedNewChunks.Length; ++i)
            {
                int  instancesInChunk = 0;
                bool breakBatch       = false;

                if (i < sortedNewChunks.Length)
                {
                    var cur          = sortedNewChunks[i];
                    breakBatch       = !sortedNewChunks[batchBegin].Equals(cur);
                    instancesInChunk = NumInstancesInChunk(cur.Chunk);
                }
                else
                {
                    breakBatch = true;
                }

                if (numInstances + instancesInChunk > maxEntitiesPerBatch)
                    breakBatch = true;

                if (breakBatch)
                {
                    int numChunks = i - batchBegin;

                    bool valid = AddNewBatch(
                        batchCreationTypeHandles,
                        sortedNewChunks.GetSubArray(batchBegin, numChunks),
                        numInstances);

                    // As soon as we encounter an invalid chunk, we know that all the rest are invalid
                    // too.
                    if (valid)
                        numValidNewChunks += numChunks;
                    else
                        return numValidNewChunks;

                    batchBegin   = i;
                    numInstances = instancesInChunk;

                    if (batchBegin < sortedNewChunks.Length)
                        maxEntitiesPerBatch = m_GraphicsArchetypes
                                              .GetGraphicsArchetype(sortedNewChunks[batchBegin].GraphicsArchetypeIndex)
                                              .MaxEntitiesPerBatch;
                }
                else
                {
                    numInstances += instancesInChunk;
                }
            }

            sortedNewChunks.Dispose();

            return numValidNewChunks;
        }

        private static int NextAlignedBy16(int size)
        {
            return ((size + 15) >> 4) << 4;
        }

        internal static MetadataValue CreateMetadataValue(int nameID, int gpuAddress, bool isOverridden)
        {
            const uint kPerInstanceDataBit = 0x80000000;

            return new MetadataValue
            {
                NameID = nameID,
                Value  = (uint)gpuAddress |
                         (isOverridden ? kPerInstanceDataBit : 0),
            };
        }

        private bool AddNewBatch(
            BatchCreationTypeHandles typeHandles,
            NativeArray<BatchCreateInfo> batchChunks,
            int numInstances)
        {
            var graphicsArchetype = m_GraphicsArchetypes.GetGraphicsArchetype(batchChunks[0].GraphicsArchetypeIndex);

            var overrides     = graphicsArchetype.PropertyComponents;
            var overrideSizes = new NativeArray<int>(overrides.Length, Allocator.Temp);

            int numProperties = overrides.Length;

            Assert.IsTrue(numProperties > 0,      "No overridden properties, expected at least one");
            Assert.IsTrue(numInstances > 0,       "No instances, expected at least one");
            Assert.IsTrue(batchChunks.Length > 0, "No chunks, expected at least one");

            int batchSizeBytes = 0;
            // Every chunk has the same graphics archetype, so each requires the same amount
            // of component metadata structs.
            int batchTotalChunkMetadata = numProperties * batchChunks.Length;

            for (int i = 0; i < overrides.Length; ++i)
            {
                // For each component, allocate a contiguous range that's aligned by 16.
                int sizeBytesComponent  = NextAlignedBy16(overrides[i].SizeBytesGPU * numInstances);
                overrideSizes[i]        = sizeBytesComponent;
                batchSizeBytes         += sizeBytesComponent;
            }

            BatchInfo batchInfo = default;

            // TODO: If allocations fail, bail out and stop spamming the log each frame.

            batchInfo.ChunkMetadataAllocation = m_ChunkMetadataAllocator.Allocate((ulong)batchTotalChunkMetadata);
            if (batchInfo.ChunkMetadataAllocation.Empty)
                Assert.IsTrue(false,
                              $"Out of memory in the Entities Graphics chunk metadata buffer. Attempted to allocate {batchTotalChunkMetadata} elements, buffer size: {m_ChunkMetadataAllocator.Size}, free size left: {m_ChunkMetadataAllocator.FreeSpace}.");

            batchInfo.GPUMemoryAllocation = m_GPUPersistentAllocator.Allocate((ulong)batchSizeBytes, BatchAllocationAlignment);
            if (batchInfo.GPUMemoryAllocation.Empty)
                Assert.IsTrue(false,
                              $"Out of memory in the Entities Graphics GPU instance data buffer. Attempted to allocate {batchSizeBytes}, buffer size: {m_GPUPersistentAllocator.Size}, free size left: {m_GPUPersistentAllocator.FreeSpace}.");

            // Physical offset inside the buffer, always the same on all platforms.
            int allocationBegin = (int)batchInfo.GPUMemoryAllocation.begin;

            // Metadata offset depends on whether a raw buffer or cbuffer is used.
            // Raw buffers index from start of buffer, cbuffers index from start of allocation.
            uint bindOffset = UseConstantBuffers ?
                              (uint)allocationBegin :
                              0;
            uint bindWindowSize = UseConstantBuffers ?
                                  (uint)MaxBytesPerBatch :
                                  0;

            // Compute where each individual property SoA stream starts
            var overrideStreamBegin = new NativeArray<int>(overrides.Length, Allocator.Temp);
            overrideStreamBegin[0]  = allocationBegin;
            for (int i = 1; i < numProperties; ++i)
                overrideStreamBegin[i] = overrideStreamBegin[i - 1] + overrideSizes[i - 1];

            int numMetadata      = numProperties;
            var overrideMetadata = new NativeArray<MetadataValue>(numMetadata, Allocator.Temp);

            int metadataIndex = 0;
            for (int i = 0; i < numProperties; ++i)
            {
                int gpuAddress                  = overrideStreamBegin[i] - (int)bindOffset;
                overrideMetadata[metadataIndex] = CreateMetadataValue(overrides[i].NameID, gpuAddress, true);
                ++metadataIndex;

#if DEBUG_LOG_PROPERTY_ALLOCATIONS
                Debug.Log(
                    $"Property Allocation: Property: {NameIDFormatted(overrides[i].NameID)} Type: {TypeIndexFormatted(overrides[i].TypeIndex)} Metadata: {overrideMetadata[i].Value:x8} Allocation: {overrideStreamBegin[i]}");
#endif
            }

            var batchID = m_ThreadedBatchContext.AddBatch(overrideMetadata, m_GPUPersistentInstanceBufferHandle,
                                                          bindOffset, bindWindowSize);
            int batchIndex = (int)batchID.value;

#if DEBUG_LOG_BATCH_CREATION
            Debug.Log(
                $"Created new batch, ID: {batchIndex}, chunks: {batchChunks.Length}, properties: {numProperties}, instances: {numInstances}, size: {batchSizeBytes}, buffer {m_GPUPersistentInstanceBufferHandle.value} (size {m_GPUPersistentInstanceData.count * m_GPUPersistentInstanceData.stride} bytes)");
#endif

            if (batchIndex == 0)
                Assert.IsTrue(false, "Failed to add new BatchRendererGroup batch.");

            AddBatchIndex(batchIndex);
            m_BatchInfos[batchIndex] = batchInfo;

            // Configure chunk components for each chunk
            var args = new SetBatchChunkDataArgs
            {
                BatchChunks         = batchChunks,
                BatchIndex          = batchIndex,
                ChunkProperties     = m_ChunkProperties,
                EntityManager       = EntityManager,
                NumProperties       = numProperties,
                TypeHandles         = typeHandles,
                ChunkMetadataBegin  = (int)batchInfo.ChunkMetadataAllocation.begin,
                ChunkOffsetInBatch  = 0,
                OverrideStreamBegin = overrideStreamBegin
            };
            SetBatchChunkData(ref args, ref overrides);

            Assert.IsTrue(args.ChunkOffsetInBatch == numInstances, "Batch instance count mismatch");

            return true;
        }

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

        [BurstCompile]
        static void SetBatchChunkData(ref SetBatchChunkDataArgs args, ref UnsafeList<ArchetypePropertyOverride> overrides)
        {
            var batchChunks         = args.BatchChunks;
            int numProperties       = args.NumProperties;
            var overrideStreamBegin = args.OverrideStreamBegin;
            int chunkOffsetInBatch  = args.ChunkOffsetInBatch;
            int chunkMetadataBegin  = args.ChunkMetadataBegin;
            for (int i = 0; i < batchChunks.Length; ++i)
            {
                var chunk                     = batchChunks[i].Chunk;
                var entitiesGraphicsChunkInfo = new EntitiesGraphicsChunkInfo
                {
                    Valid           = true,
                    BatchIndex      = args.BatchIndex,
                    ChunkTypesBegin = chunkMetadataBegin,
                    ChunkTypesEnd   = chunkMetadataBegin + numProperties,
                    CullingData     = new EntitiesGraphicsChunkCullingData
                    {
                        Flags               = ComputeCullingFlags(chunk, args.TypeHandles),
                        InstanceLodEnableds = default,
                        ChunkOffsetInBatch  = chunkOffsetInBatch,
                    },
                };

                args.EntityManager.SetChunkComponentData(chunk, entitiesGraphicsChunkInfo);

                for (int j = 0; j < numProperties; ++j)
                {
                    var propertyOverride = overrides[j];
                    var chunkProperty    = new ChunkProperty
                    {
                        ComponentTypeIndex = propertyOverride.TypeIndex,
                        GPUDataBegin       = overrideStreamBegin[j] + chunkOffsetInBatch * propertyOverride.SizeBytesGPU,
                        ValueSizeBytesCPU  = propertyOverride.SizeBytesCPU,
                        ValueSizeBytesGPU  = propertyOverride.SizeBytesGPU,
                    };

                    args.ChunkProperties[chunkMetadataBegin + j] = chunkProperty;
                }

                chunkOffsetInBatch += NumInstancesInChunk(chunk);
                chunkMetadataBegin += numProperties;
            }

            args.ChunkOffsetInBatch = chunkOffsetInBatch;
            args.ChunkMetadataBegin = chunkMetadataBegin;
        }

        static byte ComputeCullingFlags(ArchetypeChunk chunk, BatchCreationTypeHandles typeHandles)
        {
            bool hasLodData = chunk.Has(ref typeHandles.lodHeightPercentagesHandle) || chunk.Has(ref typeHandles.lodHeightPercentagesWithCrossfadeMarginsHandle);

            // TODO: Do we need non-per-instance culling anymore? It seems to always be added
            // for converted objects, and doesn't seem to be removed ever, so the only way to
            // not have it is to manually remove it or create entities from scratch.
            bool hasPerInstanceCulling = !hasLodData || chunk.Has(ref typeHandles.perInstanceCullingHandle);

            byte flags = 0;

            if (hasLodData)
                flags |= EntitiesGraphicsChunkCullingData.kFlagHasLodData;
            if (hasPerInstanceCulling)
                flags |= EntitiesGraphicsChunkCullingData.kFlagInstanceCulling;

            return flags;
        }

        private void CompleteJobs(bool completeEverything = false)
        {
            // TODO: This might not be necessary, remove?
            if (completeEverything)
            {
                m_EntitiesGraphicsRenderedQuery.CompleteDependency();
                m_LodSelectGroup.CompleteDependency();
                m_ChangedTransformQuery.CompleteDependency();
            }

            m_UpdateJobDependency.Complete();
            m_UpdateJobDependency = new JobHandle();
        }

        private void DidScheduleUpdateJob(JobHandle job)
        {
            m_UpdateJobDependency = JobHandle.CombineDependencies(job, m_UpdateJobDependency);
        }

        private void StartUpdate()
        {
            var persistentBytes = m_GPUPersistentAllocator.OnePastHighestUsedAddress;
            if (persistentBytes > (ulong)m_PersistentInstanceDataSize)
            {
                while ((ulong)m_PersistentInstanceDataSize < persistentBytes)
                {
                    m_PersistentInstanceDataSize *= 2;
                }

                if (m_PersistentInstanceDataSize > kGPUBufferSizeMax)
                {
                    m_PersistentInstanceDataSize = kGPUBufferSizeMax;  // Some backends fails at loading 1024 MiB, but 1023 is fine... This should ideally be a device cap.
                }

                if (persistentBytes > kGPUBufferSizeMax)
                    Debug.LogError(
                        "Entities Graphics: Current loaded scenes need more than 1GiB of persistent GPU memory. This is more than some GPU backends can allocate. Try to reduce amount of loaded data.");

                ref var uploadSystem = ref World.Unmanaged.GetUnsafeSystemRef<UploadMaterialPropertiesSystem>(
                    World.Unmanaged.GetExistingUnmanagedSystem<UploadMaterialPropertiesSystem>());
                if (uploadSystem.SetBufferSize(m_PersistentInstanceDataSize, out var newHandle))
                {
                    m_GPUPersistentInstanceBufferHandle = newHandle;
                    UpdateBatchBufferHandles();
                }
            }
        }

        private void UpdateBatchBufferHandles()
        {
            foreach (var b in m_ExistingBatchIndices)
            {
                m_BatchRendererGroup.SetBatchBuffer(new BatchID { value = (uint)b }, m_GPUPersistentInstanceBufferHandle);
            }
        }

#if DEBUG_LOG_MEMORY_USAGE
        private static ulong PrevUsedSpace = 0;
#endif

        private void EndUpdate()
        {
#if ENABLE_MATERIALMESHINFO_BOUNDS_CHECKING
            m_registerMaterialsAndMeshesSystem.LogBoundsCheckErrorMessages();
#endif
        }

        internal static NativeList<T> NewNativeListResized<T>(int length, Allocator allocator,
                                                              NativeArrayOptions resizeOptions = NativeArrayOptions.ClearMemory) where T : unmanaged
        {
            var list = new NativeList<T>(length, allocator);
            list.Resize(length, resizeOptions);

            return list;
        }

        /// <summary>
        /// Registers a material with the Entities Graphics System.
        /// </summary>
        /// <param name="material">The material instance to register</param>
        /// <returns>Returns the batch material ID</returns>
        public BatchMaterialID RegisterMaterial(Material material) => m_BatchRendererGroup.RegisterMaterial(material);

        /// <summary>
        /// Registers a mesh with the Entities Graphics System.
        /// </summary>
        /// <param name="mesh">Mesh instance to register</param>
        /// <returns>Returns the batch mesh ID</returns>
        public BatchMeshID RegisterMesh(Mesh mesh) => m_BatchRendererGroup.RegisterMesh(mesh);

        /// <summary>
        /// Unregisters a material from the Entities Graphics System.
        /// </summary>
        /// <param name="material">Material ID received from <see cref="RegisterMaterial"/></param>
        public void UnregisterMaterial(BatchMaterialID material) => m_BatchRendererGroup.UnregisterMaterial(material);

        /// <summary>
        /// Unregisters a mesh from the Entities Graphics System.
        /// </summary>
        /// <param name="mesh">A mesh ID received from <see cref="RegisterMesh"/>.</param>
        public void UnregisterMesh(BatchMeshID mesh) => m_BatchRendererGroup.UnregisterMesh(mesh);

        /// <summary>
        /// Returns the <see cref="Mesh"/> that corresponds to the given registered mesh ID, or <c>null</c> if no such mesh exists.
        /// </summary>
        /// <param name="mesh">A mesh ID received from <see cref="RegisterMesh"/>.</param>
        /// <returns>The <see cref="Mesh"/> object corresponding to the given mesh ID if the ID is valid, or <c>null</c> if it's not valid.</returns>
        public Mesh GetMesh(BatchMeshID mesh) => m_BatchRendererGroup.GetRegisteredMesh(mesh);

        /// <summary>
        /// Returns the <see cref="Material"/> that corresponds to the given registered material ID, or <c>null</c> if no such material exists.
        /// </summary>
        /// <param name="material">A material ID received from <see cref="RegisterMaterial"/>.</param>
        /// <returns>The <see cref="Material"/> object corresponding to the given material ID if the ID is valid, or <c>null</c> if it's not valid.</returns>
        public Material GetMaterial(BatchMaterialID material) => m_BatchRendererGroup.GetRegisteredMaterial(material);

        /// <summary>
        /// Converts a type index into a type name.
        /// </summary>
        /// <param name="typeIndex">The type index to convert.</param>
        /// <returns>The name of the type for given type index.</returns>
        internal static string TypeIndexToName(int typeIndex)
        {
#if DEBUG_PROPERTY_NAMES
            if (s_TypeIndexToName.TryGetValue(typeIndex, out var name))
                return name;
            else
                return "<unknown type>";
#else
            return null;
#endif
        }

        /// <summary>
        /// Converts a name ID to a name.
        /// </summary>
        /// <param name="nameID"></param>
        /// <returns>The name for the given name ID.</returns>
        internal static string NameIDToName(int nameID)
        {
#if DEBUG_PROPERTY_NAMES
            if (s_NameIDToName.TryGetValue(nameID, out var name))
                return name;
            else
                return "<unknown property>";
#else
            return null;
#endif
        }

        internal static string TypeIndexFormatted(int typeIndex)
        {
            return $"{TypeIndexToName(typeIndex)} ({typeIndex:x8})";
        }

        /// <summary>
        /// Converts a name ID to a formatted name.
        /// </summary>
        /// <param name="nameID"></param>
        /// <returns>The formatted name for the given name ID.</returns>
        internal static string NameIDFormatted(int nameID)
        {
            return $"{NameIDToName(nameID)} ({nameID:x8})";
        }
        #endregion

        #region Chunk Updaters
        [BurstCompile]
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
#if DEBUG_LOG_CHUNKS
                Debug.Log(
                    $"HybridChunkUpdater.ProcessChunk(internalBatchIndex: {chunkInfo.BatchIndex}, valid: {chunkInfo.Valid}, count: {chunk.Count}, chunk: {chunk.GetHashCode()})");
#endif

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

        [BurstCompile]
        internal struct ClassifyNewChunksJobLatiosVersion : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               ChunkHeader;
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            [ReadOnly] public EntityQueryMask                                chunkValidityMask;

            [NativeDisableParallelForRestriction]
            public NativeArray<ArchetypeChunk> NewChunks;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> NumNewChunks;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkHeaders               = metaChunk.GetNativeArray(ref ChunkHeader);
                var entitiesGraphicsChunkInfos = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);

                for (int i = 0, chunkEntityCount = metaChunk.Count; i < chunkEntityCount; i++)
                {
                    var chunkInfo   = entitiesGraphicsChunkInfos[i];
                    var chunkHeader = chunkHeaders[i];

                    if (ShouldCountAsNewChunk(chunkInfo, chunkHeader.ArchetypeChunk))
                    {
                        bool skip = false;
                        ValidateChunkArchetype(chunkHeader.ArchetypeChunk, ref skip);
                        if (skip)
                            continue;
                        ClassifyNewChunk(chunkHeader.ArchetypeChunk);
                    }
                }
            }

            [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void ValidateChunkArchetype(ArchetypeChunk chunk, ref bool skip)
            {
                if (!chunkValidityMask.MatchesIgnoreFilter(chunk))
                {
                    FixedString4096Bytes badArchetype  = default;
                    var                  badComponents = chunk.Archetype.GetComponentTypes();
                    foreach (var bad in badComponents)
                    {
                        var name = bad.ToFixedString();
                        if (badArchetype.Length + name.Length > 4000)
                        {
                            for (int i = 0; i < 3; i++)
                                badArchetype.Append('.');
                            break;
                        }
                        badArchetype.Append(name);
                        badArchetype.Append('\n');
                    }
                    UnityEngine.Debug.LogError(
                        $"An invalid archetype was detected in a renderable entity with the EntitiesGraphicsChunkInfo chunk component. The most common cause for this is attempting to use Unity Physics with QVVS Transforms. Invalid archetype: {badArchetype}");
                }
            }

            bool ShouldCountAsNewChunk(in EntitiesGraphicsChunkInfo chunkInfo, in ArchetypeChunk chunk)
            {
                return !chunkInfo.Valid && !chunk.Archetype.Prefab && !chunk.Archetype.Disabled;
            }

            public unsafe void ClassifyNewChunk(ArchetypeChunk chunk)
            {
                int* numNewChunks = (int*)NumNewChunks.GetUnsafePtr();
                int  iPlus1       = System.Threading.Interlocked.Add(ref numNewChunks[0], 1);
                int  i            = iPlus1 - 1;  // C# Interlocked semantics are weird
                Assert.IsTrue(i < NewChunks.Length, "Out of space in the NewChunks buffer");
                NewChunks[i] = chunk;
            }
        }

        [BurstCompile]
        internal struct UpdateOldEntitiesGraphicsChunksJob : IJobChunk
        {
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo>   EntitiesGraphicsChunkInfo;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>      ChunkHeader;
            [ReadOnly] public DynamicComponentTypeHandle            WorldTransform;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo> MaterialMeshInfo;
            public EntitiesGraphicsChunkUpdater                     EntitiesGraphicsChunkUpdater;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                // metaChunk is the chunk which contains the meta entities (= entities holding the chunk components) for the actual chunks

                var entitiesGraphicsChunkInfos = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);
                var chunkHeaders               = metaChunk.GetNativeArray(ref ChunkHeader);

                for (int i = 0, chunkEntityCount = metaChunk.Count; i < chunkEntityCount; i++)
                {
                    var chunkInfo   = entitiesGraphicsChunkInfos[i];
                    var chunkHeader = chunkHeaders[i];
                    var chunk       = chunkHeader.ArchetypeChunk;

                    // Skip chunks that for some reason have EntitiesGraphicsChunkInfo, but don't have the
                    // other required components. This should normally not happen, but can happen
                    // if the user manually deletes some components after the fact.
                    bool hasMaterialMeshInfo = chunk.Has(ref MaterialMeshInfo);
                    bool hasWorldTransform   = chunk.Has(ref WorldTransform);

                    if (!math.all(new bool2(hasMaterialMeshInfo, hasWorldTransform)))
                        continue;

                    // When LOD ranges change, we must reset the movement grace to avoid using stale data
                    //bool lodRangeChange =
                    //    chunkHeader.ArchetypeChunk.DidOrderChange(EntitiesGraphicsChunkUpdater.lastSystemVersion) |
                    //    chunkHeader.ArchetypeChunk.DidChange(ref LodRange, EntitiesGraphicsChunkUpdater.lastSystemVersion) |
                    //    chunkHeader.ArchetypeChunk.DidChange(ref RootLodRange, EntitiesGraphicsChunkUpdater.lastSystemVersion);
                    //
                    //if (lodRangeChange)
                    //{
                    //    chunkInfo.CullingData.MovementGraceFixed16 = 0;
                    //    entitiesGraphicsChunkInfos[i]              = chunkInfo;
                    //}

                    EntitiesGraphicsChunkUpdater.ProcessChunk(in chunkInfo, in chunk);
                }
            }
        }

        [BurstCompile]
        internal struct UpdateNewEntitiesGraphicsChunksJob : IJobParallelFor
        {
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;

            public NativeArray<ArchetypeChunk>  NewChunks;
            public EntitiesGraphicsChunkUpdater EntitiesGraphicsChunkUpdater;

            public void Execute(int index)
            {
                var chunk     = NewChunks[index];
                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);

                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");
                EntitiesGraphicsChunkUpdater.ProcessValidChunk(in chunkInfo, in chunk, true);
            }
        }
        #endregion

#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        [BurstCompile]
        internal unsafe struct UpdateDrawCommandFlagsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform>             WorldTransform;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>          PostProcessMatrix;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo>             EntitiesGraphicsChunkInfo;

            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> FilterSettings;
            public BatchFilterSettings                                        DefaultFilterSettings;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool hasPostProcess  = chunk.Has(ref PostProcessMatrix);
                var  changed         = chunk.DidChange(ref WorldTransform, lastSystemVersion);
                changed             |= chunk.DidOrderChange(lastSystemVersion);
                changed             |= hasPostProcess && chunk.DidChange(ref PostProcessMatrix, lastSystemVersion);
                if (!changed)
                    return;

                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);
                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");

                // This job runs for all chunks that have structural changes, so if different
                // RenderFilterSettings get set on entities, they should be picked up by
                // the order change filter.
                int filterIndex = chunk.GetSharedComponentIndex(RenderFilterSettings);
                if (!FilterSettings.TryGetValue(filterIndex, out var filterSettings))
                    filterSettings = DefaultFilterSettings;

                bool hasPerObjectMotion = filterSettings.motionMode != MotionVectorGenerationMode.Camera;
                if (hasPerObjectMotion)
                    chunkInfo.CullingData.Flags |= EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion;
                else
                    chunkInfo.CullingData.Flags &= unchecked ((byte)~EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion);

                var worldTransforms = chunk.GetNativeArray(ref WorldTransform);

                if (hasPostProcess)
                {
                    var postProcessTransforms = chunk.GetNativeArray(ref PostProcessMatrix);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        bool flippedWinding = RequiresFlippedWinding(worldTransforms[i], postProcessTransforms[i]);

                        int   qwordIndex = i / 64;
                        int   bitIndex   = i % 64;
                        ulong mask       = 1ul << bitIndex;

                        if (flippedWinding)
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                        else
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                    }
                }
                else
                {
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        bool flippedWinding = RequiresFlippedWinding(worldTransforms[i]);

                        int   qwordIndex = i / 64;
                        int   bitIndex   = i % 64;
                        ulong mask       = 1ul << bitIndex;

                        if (flippedWinding)
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                        else
                            chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                    }
                }

                chunk.SetChunkComponentData(ref EntitiesGraphicsChunkInfo, chunkInfo);
            }

            private bool RequiresFlippedWinding(in WorldTransform worldTransform)
            {
                var isNegative = worldTransform.nonUniformScale < 0f;
                return (math.countbits(math.bitmask(new bool4(isNegative, false))) & 1) == 1;
            }

            private bool RequiresFlippedWinding(in WorldTransform worldTransform, in PostProcessMatrix postProcessMatrix)
            {
                var wt4x4  = worldTransform.worldTransform.ToMatrix4x4();
                var ppm4x4 = new float4x4(new float4(postProcessMatrix.postProcessMatrix.c0, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c1, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c2, 0f),
                                          new float4(postProcessMatrix.postProcessMatrix.c3, 1f));
                var product = math.mul(ppm4x4, wt4x4);
                return math.determinant(product) < 0f;
            }
        }
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        [BurstCompile]
        internal unsafe struct UpdateDrawCommandFlagsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> WorldTransform;
            [ReadOnly] public SharedComponentTypeHandle<RenderFilterSettings> RenderFilterSettings;
            public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;

            [ReadOnly] public NativeParallelHashMap<int, BatchFilterSettings> FilterSettings;
            public BatchFilterSettings DefaultFilterSettings;

            public uint lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var changed = chunk.DidChange(ref WorldTransform, lastSystemVersion);
                changed |= chunk.DidOrderChange(lastSystemVersion);
                if (!changed)
                    return;

                // This job is not written to support queries with enableable component types.
                Assert.IsFalse(useEnabledMask);

                var chunkInfo = chunk.GetChunkComponentData(ref EntitiesGraphicsChunkInfo);
                Assert.IsTrue(chunkInfo.Valid, "Attempted to process a chunk with uninitialized Hybrid chunk info");

                // This job runs for all chunks that have structural changes, so if different
                // RenderFilterSettings get set on entities, they should be picked up by
                // the order change filter.
                int filterIndex = chunk.GetSharedComponentIndex(RenderFilterSettings);
                if (!FilterSettings.TryGetValue(filterIndex, out var filterSettings))
                    filterSettings = DefaultFilterSettings;

                bool hasPerObjectMotion = filterSettings.motionMode != MotionVectorGenerationMode.Camera;
                if (hasPerObjectMotion)
                    chunkInfo.CullingData.Flags |= EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion;
                else
                    chunkInfo.CullingData.Flags &= unchecked ((byte)~EntitiesGraphicsChunkCullingData.kFlagPerObjectMotion);

                var worldTransforms = chunk.GetNativeArray(ref WorldTransform);

                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                {
                    bool flippedWinding = RequiresFlippedWinding(worldTransforms[i]);

                    int qwordIndex = i / 64;
                    int bitIndex   = i % 64;
                    ulong mask       = 1ul << bitIndex;

                    if (flippedWinding)
                        chunkInfo.CullingData.FlippedWinding[qwordIndex] |= mask;
                    else
                        chunkInfo.CullingData.FlippedWinding[qwordIndex] &= ~mask;
                }

                chunk.SetChunkComponentData(ref EntitiesGraphicsChunkInfo, chunkInfo);
            }

            private bool RequiresFlippedWinding(in LocalToWorld worldTransform)
            {
                return math.determinant(worldTransform.Value) < 0f;
            }
        }
#endif

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
                m_cachedMax = math.max(index, m_cachedMax);
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

