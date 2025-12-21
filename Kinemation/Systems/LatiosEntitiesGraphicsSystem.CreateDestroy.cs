#region Header
#if UNITY_EDITOR && !DISABLE_HYBRID_RENDERER_PICKING
#define ENABLE_PICKING
#endif

using System;
using Latios.Transforms;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

using Latios.Transforms.Abstract;
using Unity.Rendering;

using MaterialPropertyType = Unity.Rendering.MaterialPropertyType;
#endregion

namespace Latios.Kinemation.Systems
{
    public unsafe partial class LatiosEntitiesGraphicsSystem : SubSystem
    {
        protected override void OnCreate()
        {
            m_unityEntitiesGraphicsSystem         = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            m_unityEntitiesGraphicsSystem.Enabled = false;

            // We steal the BRG to avoid duplicating the Mesh and Material registration system.
            // Ideally, we want to remain compatible with plugins that register custom meshes and materials.
            // But we need our own BRG to specify our own culling callback.
            // The solution is to steal the BRG, destroy it, and then swap it with our replacement.
            m_BatchRendererGroup = new BatchRendererGroup(new BatchRendererGroupCreateInfo
            {
                cullingCallback         = this.OnPerformCulling,
                finishedCullingCallback = this.OnFinishedCulling,
                userContext             = IntPtr.Zero
            });
            var brgField = m_unityEntitiesGraphicsSystem.GetType().GetField("m_BatchRendererGroup",
                                                                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var oldBrg = brgField.GetValue(m_unityEntitiesGraphicsSystem) as BatchRendererGroup;
            oldBrg.Dispose();
            brgField.SetValue(m_unityEntitiesGraphicsSystem, m_BatchRendererGroup);
            // Hybrid Renderer supports all view types
            m_BatchRendererGroup.SetEnabledViewTypes(new BatchCullingViewType[]
            {
                BatchCullingViewType.Camera,
                BatchCullingViewType.Light,
                BatchCullingViewType.Picking,
                BatchCullingViewType.SelectionOutline
            });

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

            m_registerMaterialsAndMeshesSystem = World.GetExistingSystemManaged<RegisterMaterialsAndMeshesSystem>();
            m_cullingSuperSystem               = World.GetOrCreateSystemManaged<KinemationCullingSuperSystem>();

            m_unmanaged.OnCreate(ref CheckedStateRef, m_BatchRendererGroup);
        }

        protected override void OnDestroy()
        {
            m_unmanaged.OnDestroy();
            if (ErrorShaderEnabled)
                Material.DestroyImmediate(m_ErrorMaterial);

            if (LoadingShaderEnabled)
                Material.DestroyImmediate(m_LoadingMaterial);

#if ENABLE_PICKING
            Material.DestroyImmediate(m_PickingMaterial);
#endif
        }

        partial struct Unmanaged
        {
            public void OnCreate(ref SystemState state, BatchRendererGroup batchRendererGroup)
            {
                latiosWorld = state.GetLatiosWorldUnmanaged();

                // If -nographics is enabled, or if there is no compute shader support, disable HR.
                if (!EntitiesGraphicsEnabled)
                {
                    state.Enabled = false;
                    Debug.Log("No SRP present, no compute shader support, or running with -nographics. Entities Graphics package disabled");
                    return;
                }

                m_cullingDispatchSuperSystem = state.World.GetOrCreateSystemManaged<KinemationCullingDispatchSuperSystem>().SystemHandle;
                latiosWorld.worldBlackboardEntity.AddComponent<CullingContext>();
                latiosWorld.worldBlackboardEntity.AddComponent<DispatchContext>();
                latiosWorld.worldBlackboardEntity.AddBuffer<CullingPlane>();
                latiosWorld.worldBlackboardEntity.AddBuffer<CullingSplitElement>();
                latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new BrgCullingContext());
                latiosWorld.worldBlackboardEntity.AddBuffer<MaterialPropertyComponentType>();
                latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new MaterialPropertiesUploadContext());
                m_cullingCallbackFinalJobHandles = new NativeList<JobHandle>(Allocator.Persistent);

                m_needsFirstUpdate = true;

                m_PersistentInstanceDataSize = kGPUBufferSizeInitial;
                m_maxBytesPerBatch           = MaxBytesPerBatch;
                m_useConstantBuffers         = UseConstantBuffers;
                m_maxBytesPerCBuffer         = MaxBytesPerCBuffer;
                m_batchAllocationAlignment   = BatchAllocationAlignment;

                m_EntitiesGraphicsRenderedQuery = state.GetEntityQuery(new EntityQueryDesc
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

                m_LodSelectGroup = state.GetEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadWrite<EntitiesGraphicsChunkInfo>(),
                        ComponentType.ReadOnly<ChunkHeader>()
                    },
                });

                // We check for changes in the job itself rather than via filters, because we need to check for optional PostProcessMatrix changes
                m_ChangedTransformQuery = state.GetEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        QueryExtensions.GetAbstractWorldTransformROComponentType(),
                        ComponentType.ChunkComponent<EntitiesGraphicsChunkInfo>(),
                    },
                });

                m_ThreadedBatchContext = batchRendererGroup.GetThreadedBatchContext();

                m_GPUPersistentAllocator = new HeapAllocator(kMaxGPUAllocatorMemory, 16);
                m_ChunkMetadataAllocator = new HeapAllocator(kMaxChunkMetadata);

                m_BatchInfos           = NewNativeListResized<BatchInfo>(kInitialMaxBatchCount, Allocator.Persistent);
                m_ChunkProperties      = new NativeArray<ChunkProperty>(kMaxChunkMetadata, Allocator.Persistent);
                m_ExistingBatchIndices = new NativeParallelHashSet<int>(128, Allocator.Persistent);
                var componentTypeCache = new ComponentTypeCache(128);

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

                m_MetaEntitiesForHybridRenderableChunksQuery = state.GetEntityQuery(
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
#if !LATIOS_TRANSFORMS_UNITY
                RegisterMaterialPropertyType<WorldTransform>(                              "unity_ObjectToWorld",   4 * 4 * 3);
                RegisterMaterialPropertyType<PreviousTransform>(                           "unity_MatrixPreviousM", 4 * 4 * 3);
#elif LATIOS_TRANSFORMS_UNITY
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

                InitializeMaterialProperties(ref componentTypeCache);
                var     uploadMaterialPropertiesSystem      = state.WorldUnmanaged.GetExistingUnmanagedSystem<UploadMaterialPropertiesSystem>();
                ref var uploadMaterialPropertiesSystemState = ref state.WorldUnmanaged.ResolveSystemStateRef(uploadMaterialPropertiesSystem);
                componentTypeCache.FetchTypeHandles(ref uploadMaterialPropertiesSystemState);
                ref var uploadMaterialPropertiesSystemMemory =
                    ref state.WorldUnmanaged.GetUnsafeSystemRef<UploadMaterialPropertiesSystem>(uploadMaterialPropertiesSystem);
                uploadMaterialPropertiesSystemMemory.m_burstCompatibleTypeArray = componentTypeCache.ToBurstCompatible(Allocator.Persistent);
                componentTypeCache.FetchTypeHandles(ref state);
                m_burstCompatibleTypeArray = componentTypeCache.ToBurstCompatible(Allocator.Persistent);
                // This assumes uploadMaterialPropertiesSystem is already fully created from when the KinemationCullingSuperSystem was created.
                m_GPUPersistentInstanceBufferHandle = uploadMaterialPropertiesSystemMemory.m_GPUPersistentInstanceBufferHandle;

                // UsedTypes values are the ComponentType values while the keys are the same
                // except with the bit flags in the high bits masked off.
                // The HybridRenderer packs ComponentTypeHandles by the order they show up
                // in the value array from the hashmap.
                var types  = componentTypeCache.UsedTypes.GetValueArray(Allocator.Temp);
                var ctypes = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>().Reinterpret<ComponentType>();
                ctypes.ResizeUninitialized(types.Length);
                for (int i = 0; i < types.Length; i++)
                    ctypes[i] = ComponentType.ReadOnly(types[i]);

                componentTypeCache.Dispose();
            }

            public void OnDestroy()
            {
                if (!EntitiesGraphicsEnabled)
                    return;
                CompleteJobs(true);
                Dispose();
            }

            internal static NativeList<T> NewNativeListResized<T>(int length, Allocator allocator,
                                                                  NativeArrayOptions resizeOptions = NativeArrayOptions.ClearMemory) where T : unmanaged
            {
                var list = new NativeList<T>(length, allocator);
                list.Resize(length, resizeOptions);

                return list;
            }

            private void ResetIds()
            {
                if (m_SortedBatchIds.isCreated)
                    m_SortedBatchIds.Dispose();
                m_SortedBatchIds = new SortedSetUnmanaged(1024);
                m_ExistingBatchIndices.Clear();
            }

            private void InitializeMaterialProperties(ref ComponentTypeCache componentTypeCache)
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

                    // We cache all IComponentData types that we know are capable of overriding properties
                    componentTypeCache.UseType(typeIndex);
                }
            }

            private void Dispose()
            {
                JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());

                m_ThreadedBatchContext.batchRendererGroup = IntPtr.Zero;

                m_NameIDToMaterialProperties.Dispose();
                m_TypeIndexToMaterialProperty.Dispose();
                m_GPUPersistentAllocator.Dispose();
                m_ChunkMetadataAllocator.Dispose();

                m_BatchInfos.Dispose();
                m_ChunkProperties.Dispose();
                m_ExistingBatchIndices.Dispose();
                m_ValueBlits.Dispose();
                m_burstCompatibleTypeArray.Dispose(default).Complete();

                m_SortedBatchIds.Dispose();

                m_GraphicsArchetypes.Dispose();

                m_FilterSettings.Dispose();

                if (!m_cullingCallbackFinalJobHandles.IsEmpty)
                    JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
                m_cullingCallbackFinalJobHandles.Dispose();
                m_ThreadLocalAllocators.Dispose();
            }
        }
    }
}

