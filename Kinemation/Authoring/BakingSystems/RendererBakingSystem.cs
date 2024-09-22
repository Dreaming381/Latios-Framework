using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

using Hash128 = Unity.Entities.Hash128;
using static Unity.Entities.SystemAPI;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateBefore(typeof(AdditionalMeshRendererFilterBakingSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial class RendererBakingSystem : SystemBase
    {
        LightMapBakingContext m_lightmapBakingContext;

        protected override void OnCreate()
        {
            m_lightmapBakingContext = new LightMapBakingContext();
        }

        protected override void OnUpdate()
        {
            ref var state = ref CheckedStateRef;
            state.CompleteDependency();

            state.EntityManager.RemoveComponent<PostTransformMatrix>(QueryBuilder().WithAll<AdditionalMeshRendererEntity, PostTransformMatrix>()
                                                                     .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build());

            m_lightmapBakingContext.BeginConversion();

            var entitiesWithBadIndices = new NativeList<Entity>(Allocator.TempJob);
            var uniqueIndicesSet       = new NativeHashSet<int>(128, Allocator.TempJob);

            var lightmaps = LightmapSettings.lightmaps;
            new CollectUniqueIndicesJob
            {
                entitiesWithBadIndices = entitiesWithBadIndices,
                uniqueIndices          = uniqueIndicesSet,
                maxIndex               = lightmaps.Length
            }.Run();

            var uniqueIndicesSorted = new NativeList<int>(Allocator.TempJob);
            new SortUniqueIndicesJob
            {
                dst = uniqueIndicesSorted,
                src = uniqueIndicesSet
            }.Run();

            var lightmapSet = new ComponentTypeSet(ComponentType.ReadWrite<BakingLightmapIndex>(),
                                                   ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_LightmapIndex>(),
                                                   ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_LightmapST>(),
                                                   ComponentType.ReadWrite<LightMaps>());

            state.EntityManager.RemoveComponent(entitiesWithBadIndices.AsArray(), lightmapSet);
            entitiesWithBadIndices.Dispose();
            uniqueIndicesSet.Dispose();

            m_lightmapBakingContext.ProcessLightMapsForConversion(uniqueIndicesSorted.AsArray(), lightmaps);
            uniqueIndicesSorted.Dispose();

            var lightmapsSCD = new LightMaps();

            foreach ((var lightmapIndex, var lightmapProperty, var buffer) in Query<RefRO<BakingLightmapIndex>,
                                                                                    RefRW<BuiltinMaterialPropertyUnity_LightmapIndex>,
                                                                                    DynamicBuffer<BakingMaterialMeshSubmesh> >())
            {
                var lightmapRef = m_lightmapBakingContext.GetLightMapReference(lightmapIndex.ValueRO.lightmapIndex);

                if (lightmapRef != null)
                {
                    lightmapProperty.ValueRW.Value = lightmapRef.lightmapIndex;

                    for (int i = 0; i < buffer.Length; i++)
                    {
                        ref var element  = ref buffer.ElementAt(i);
                        var     mat      = element.material.Value;
                        var     newMat   = m_lightmapBakingContext.GetLightMappedMaterial(mat, lightmapRef);
                        element.material = newMat;
                    }
                    lightmapsSCD = lightmapRef.lightMaps;
                }
            }

            state.EntityManager.SetSharedComponentManaged(QueryBuilder().WithAll<LightMaps>().Build(), lightmapsSCD);

            var renderablesWithLightmapsQuery = QueryBuilder().WithAll<LightMaps, MaterialMeshInfo>().WithAllRW<BakingMaterialMeshSubmesh>()
                                                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build();
            var meshMap       = new NativeHashMap<UnityObjectRef<Mesh>, int>(128, Allocator.TempJob);
            var materialMap   = new NativeHashMap<UnityObjectRef<Material>, int>(128, Allocator.TempJob);
            var meshList      = new NativeList<UnityObjectRef<Mesh> >(Allocator.TempJob);
            var materialList  = new NativeList<UnityObjectRef<Material> >(Allocator.TempJob);
            var rangesList    = new NativeList<MaterialMeshIndex>(Allocator.TempJob);
            var duplicatesMap = new NativeParallelMultiHashMap<PossiblyUniqueMMI, Entity>(128, Allocator.TempJob);
            if (!renderablesWithLightmapsQuery.IsEmptyIgnoreFilter)
            {
                new CollectUniqueMeshesAndMaterialsJob
                {
                    meshMap      = meshMap,
                    meshList     = meshList,
                    materialMap  = materialMap,
                    materialList = materialList,
                }.Run(renderablesWithLightmapsQuery);
                new BuildMaterialMeshInfoJob
                {
                    materialMap              = materialMap,
                    meshMap                  = meshMap,
                    rangesList               = rangesList,
                    bufferLookup             = SystemAPI.GetBufferLookup<BakingMaterialMeshSubmesh>(true),
                    mmiLookup                = SystemAPI.GetComponentLookup<MaterialMeshInfo>(),
                    duplicateRangesFilterMap = duplicatesMap
                }.Run(renderablesWithLightmapsQuery);
                var rma = CreateRenderMeshArrayFromRefArrays(meshList.AsArray(), materialList.AsArray(), rangesList.AsArray());
                state.EntityManager.SetSharedComponentManaged(renderablesWithLightmapsQuery, rma);

                meshMap.Clear();
                meshList.Clear();
                materialMap.Clear();
                materialList.Clear();
                rangesList.Clear();
                duplicatesMap.Clear();
            }
            var renderablesWithoutLightmapsQuery = QueryBuilder().WithAll<MaterialMeshInfo>().WithAllRW<BakingMaterialMeshSubmesh>().WithAbsent<LightMaps>()
                                                   .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Build();
            if (!renderablesWithoutLightmapsQuery.IsEmptyIgnoreFilter)
            {
                new CollectUniqueMeshesAndMaterialsJob
                {
                    meshMap      = meshMap,
                    meshList     = meshList,
                    materialMap  = materialMap,
                    materialList = materialList,
                }.Run(renderablesWithoutLightmapsQuery);
                new BuildMaterialMeshInfoJob
                {
                    materialMap              = materialMap,
                    meshMap                  = meshMap,
                    rangesList               = rangesList,
                    bufferLookup             = SystemAPI.GetBufferLookup<BakingMaterialMeshSubmesh>(true),
                    mmiLookup                = SystemAPI.GetComponentLookup<MaterialMeshInfo>(),
                    duplicateRangesFilterMap = duplicatesMap
                }.Run(renderablesWithoutLightmapsQuery);
                var rma = CreateRenderMeshArrayFromRefArrays(meshList.AsArray(), materialList.AsArray(), rangesList.AsArray());
                state.EntityManager.SetSharedComponentManaged(renderablesWithoutLightmapsQuery, rma);
            }

            meshMap.Dispose();
            meshList.Dispose();
            materialMap.Dispose();
            materialList.Dispose();
            rangesList.Dispose();
            duplicatesMap.Dispose();

            m_lightmapBakingContext.EndConversion();
        }

        RenderMeshArray CreateRenderMeshArrayFromRefArrays(NativeArray<UnityObjectRef<Mesh> > meshes, NativeArray<UnityObjectRef<Material> > materials,
                                                           NativeArray<MaterialMeshIndex> indices)
        {
            return new RenderMeshArray(materials.AsReadOnlySpan(), meshes.AsReadOnlySpan(), indices.AsReadOnlySpan());
        }

        [WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)]
        [BurstCompile]
        partial struct CollectUniqueIndicesJob : IJobEntity
        {
            public NativeList<Entity> entitiesWithBadIndices;
            public NativeHashSet<int> uniqueIndices;

            public int maxIndex;

            public void Execute(Entity entity, BakingLightmapIndex index)
            {
                if (index.lightmapIndex >= maxIndex)
                    entitiesWithBadIndices.Add(entity);
                else
                    uniqueIndices.Add(index.lightmapIndex);
            }
        }

        [BurstCompile]
        struct SortUniqueIndicesJob : IJob
        {
            public NativeList<int>               dst;
            [ReadOnly] public NativeHashSet<int> src;

            public void Execute()
            {
                dst.Capacity = src.Count;
                foreach (var index in src)
                    dst.Add(index);
                dst.Sort();
            }
        }

        [BurstCompile]
        partial struct CollectUniqueMeshesAndMaterialsJob : IJobEntity
        {
            public NativeHashMap<UnityObjectRef<Mesh>, int>     meshMap;
            public NativeList<UnityObjectRef<Mesh> >            meshList;
            public NativeHashMap<UnityObjectRef<Material>, int> materialMap;
            public NativeList<UnityObjectRef<Material> >        materialList;

            public void Execute(ref DynamicBuffer<BakingMaterialMeshSubmesh> buffer)
            {
                AddLodDataToBuffer(ref buffer);

                foreach (var element in buffer)
                {
                    if (!meshMap.ContainsKey(element.mesh))
                    {
                        meshMap.Add(element.mesh, meshList.Length);
                        meshList.Add(element.mesh);
                    }
                    if (!materialMap.ContainsKey(element.material))
                    {
                        materialMap.Add(element.material, materialList.Length);
                        materialList.Add(element.material);
                    }
                }
            }

            void AddLodDataToBuffer(ref DynamicBuffer<BakingMaterialMeshSubmesh> buffer)
            {
                if (buffer.Length == 0)
                    return;

                ref var element0 = ref buffer.ElementAt(0);
                if ((element0.submesh & 0xff000000) == 0xff000000)
                {
                    element0.submesh |= 0x00ff0000;
                }
                else
                {
                    int mask = 0;
                    foreach (var element in buffer)
                    {
                        mask |= element.submesh;
                    }
                    element0.submesh |= (mask >> 8) & 0x00ff0000;
                }

                if (buffer.Length >= 127)
                {
                    buffer.ElementAt(1).submesh |= (buffer.Length << 16) & 0x00ff0000;
                    buffer.ElementAt(2).submesh |= (buffer.Length << 8) & 0x00ff0000;
                    buffer.ElementAt(3).submesh |= (buffer.Length) & 0x00ff0000;
                }
            }
        }

        struct PossiblyUniqueMMI : IEquatable<PossiblyUniqueMMI>
        {
            public UnityObjectRef<Mesh>     firstMesh;
            public UnityObjectRef<Mesh>     lastMesh;
            public UnityObjectRef<Material> firstMaterial;
            public UnityObjectRef<Material> lastMaterial;
            public int                      firstSubmesh;
            public int                      lastSubmesh;
            public int                      count;

            public bool Equals(PossiblyUniqueMMI other)
            {
                return (firstSubmesh == other.firstSubmesh && lastSubmesh == other.lastSubmesh &&
                        count == other.count && firstMesh.Equals(other.firstMesh) && lastMesh.Equals(other.lastMesh) &&
                        firstMaterial.Equals(other.firstMaterial) && lastMaterial.Equals(other.lastMaterial));
            }

            public override int GetHashCode()
            {
                int4x2 hash = new int4x2(new int4(firstMesh.GetHashCode(), lastMesh.GetHashCode(), firstMaterial.GetHashCode(), lastMesh.GetHashCode()),
                                         new int4(firstSubmesh, lastSubmesh, count, count));
                return hash.GetHashCode();
            }
        }

        [BurstCompile]
        partial struct BuildMaterialMeshInfoJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<UnityObjectRef<Mesh>, int>                         meshMap;
            [ReadOnly] public NativeHashMap<UnityObjectRef<Material>, int>                     materialMap;
            [ReadOnly] public BufferLookup<BakingMaterialMeshSubmesh>                          bufferLookup;
            public NativeList<MaterialMeshIndex>                                               rangesList;
            public NativeParallelMultiHashMap<PossiblyUniqueMMI, Entity>                       duplicateRangesFilterMap;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<MaterialMeshInfo> mmiLookup;

            public unsafe void Execute(Entity entity, ref MaterialMeshInfo mmi, in DynamicBuffer<BakingMaterialMeshSubmesh> buffer)
            {
                if (buffer.Length == 1)
                {
                    // Create MMI with negative indices
                    var element       = buffer[0];
                    var meshIndex     = meshMap[element.mesh];
                    var materialIndex = materialMap[element.material];

                    mmi = MaterialMeshInfo.FromRenderMeshArrayIndices(materialIndex, meshIndex, (ushort)(element.submesh & 0xffff));
                }
                else
                {
                    var first = buffer[0];
                    var last  = buffer[buffer.Length - 1];
                    var key   = new PossiblyUniqueMMI
                    {
                        firstMaterial = first.material,
                        firstMesh     = first.mesh,
                        firstSubmesh  = first.submesh,
                        lastMaterial  = last.material,
                        lastMesh      = last.mesh,
                        lastSubmesh   = last.submesh,
                        count         = buffer.Length
                    };

                    foreach (var candidateEntity in duplicateRangesFilterMap.GetValuesForKey(key))
                    {
                        var candidateBuffer = bufferLookup[candidateEntity];
                        var match           = UnsafeUtility.MemCmp(candidateBuffer.GetUnsafeReadOnlyPtr(), buffer.GetUnsafeReadOnlyPtr(),
                                                                   buffer.Length * UnsafeUtility.SizeOf<BakingMaterialMeshSubmesh>());
                        if (match == 0)
                        {
                            mmi = mmiLookup[candidateEntity];
                            return;
                        }
                    }

                    // Create MMI from ranges
                    int rangesStartIndex = rangesList.Length;

                    foreach (var element in buffer)
                    {
                        rangesList.Add(new MaterialMeshIndex
                        {
                            MaterialIndex = materialMap[element.material],
                            MeshIndex     = meshMap[element.mesh],
                            SubMeshIndex  = element.submesh
                        });
                    }

                    mmi = MaterialMeshInfo.FromMaterialMeshIndexRange(rangesStartIndex, math.min(buffer.Length, 127));
                    duplicateRangesFilterMap.Add(key, entity);
                }
            }
        }
    }

    class LightMapBakingContext
    {
        [Flags]
        enum LightMappingFlags
        {
            None = 0,
            Lightmapped = 1,
            Directional = 2,
            ShadowMask = 4
        }

        struct MaterialLookupKey
        {
            public UnityObjectRef<Material> baseMaterial;
            public LightMaps                lightmaps;
            public LightMappingFlags        flags;
        }

        struct LightMapKey : IEquatable<LightMapKey>
        {
            public Hash128 colorHash;
            public Hash128 directionHash;
            public Hash128 shadowMaskHash;

            public LightMapKey(LightmapData lightmapData) :
                this(lightmapData.lightmapColor,
                     lightmapData.lightmapDir,
                     lightmapData.shadowMask)
            {
            }

            public LightMapKey(Texture2D color, Texture2D direction, Texture2D shadowMask)
            {
                colorHash      = default;
                directionHash  = default;
                shadowMaskHash = default;

#if UNITY_EDITOR
                // imageContentsHash only available in the editor, but this type is only used
                // during conversion, so it's only used in the editor.
                if (color != null)
                    colorHash = color.imageContentsHash;
                if (direction != null)
                    directionHash = direction.imageContentsHash;
                if (shadowMask != null)
                    shadowMaskHash = shadowMask.imageContentsHash;
#endif
            }

            public bool Equals(LightMapKey other)
            {
                return colorHash.Equals(other.colorHash) && directionHash.Equals(other.directionHash) && shadowMaskHash.Equals(other.shadowMaskHash);
            }

            public override int GetHashCode()
            {
                var hash = new xxHash3.StreamingState(true);
                hash.Update(colorHash);
                hash.Update(directionHash);
                hash.Update(shadowMaskHash);
                return (int)hash.DigestHash64().x;
            }
        }

        public class LightMapReference
        {
            public LightMaps lightMaps;
            public int       lightmapIndex;
        }

        private int m_numLightMapCacheHits;
        private int m_numLightMapCacheMisses;
        private int m_numLightMappedMaterialCacheHits;
        private int m_numLightMappedMaterialCacheMisses;

        private Dictionary<LightMapKey, LightMapReference> m_lightMapArrayCache       = new Dictionary<LightMapKey, LightMapReference>();
        private Dictionary<MaterialLookupKey, Material>    m_lightMappedMaterialCache = new ();
        private Dictionary<int, LightMapReference>         m_lightMapReferences       = new Dictionary<int, LightMapReference>();

        public LightMapBakingContext()
        {
            Reset();
        }

        public void Reset()
        {
            m_lightMapArrayCache.Clear();
            m_lightMappedMaterialCache.Clear();

            BeginConversion();
        }

        public void BeginConversion()
        {
            m_lightMapReferences.Clear();

            m_numLightMapCacheHits              = 0;
            m_numLightMapCacheMisses            = 0;
            m_numLightMappedMaterialCacheHits   = 0;
            m_numLightMappedMaterialCacheMisses = 0;
        }

        public void EndConversion()
        {
#if DEBUG_LOG_LIGHT_MAP_CONVERSION
            Debug.Log(
                $"Light map cache: {m_numLightMapCacheHits} hits, {m_numLightMapCacheMisses} misses. Light mapped material cache: {m_numLightMappedMaterialCacheHits} hits, {m_numLightMappedMaterialCacheMisses} misses.");
#endif
        }

        private List<Texture2D> m_colors      = new List<Texture2D>();
        private List<Texture2D> m_directions  = new List<Texture2D>();
        private List<Texture2D> m_shadowMasks = new List<Texture2D>();

        // Check all light maps referenced within the current batch of converted Renderers.
        // Any references to light maps that have already been inserted into a lightmaps array
        // will be implemented by reusing the existing lightmaps object. Any leftover previously
        // unseen (or changed = content hash changed) light maps are combined into a new lightmaps array.
        public void ProcessLightMapsForConversion(NativeArray<int> uniqueIndices, LightmapData[] lightmaps)
        {
            m_colors.Clear();
            m_directions.Clear();
            m_shadowMasks.Clear();
            var lightMapIndices = new NativeList<int>(Allocator.TempJob);

            // Each light map reference is converted into a LightMapKey which identifies the light map
            // using the content hashes regardless of the index number. Previously encountered light maps
            // should be found from the cache even if their index number has changed. New or changed
            // light maps are placed into a new array.
            for (var i = 0; i < uniqueIndices.Length; i++)
            {
                var index        = uniqueIndices[i];
                var lightmapData = lightmaps[index];
                var key          = new LightMapKey(lightmapData);

                if (m_lightMapArrayCache.TryGetValue(key, out var lightMapRef))
                {
                    m_lightMapReferences[index] = lightMapRef;
                    ++m_numLightMapCacheHits;
                }
                else
                {
                    m_colors.Add(lightmapData.lightmapColor);
                    m_directions.Add(lightmapData.lightmapDir);
                    m_shadowMasks.Add(lightmapData.shadowMask);
                    lightMapIndices.Add(index);
                    ++m_numLightMapCacheMisses;
                }
            }

            if (lightMapIndices.Length > 0)
            {
#if DEBUG_LOG_LIGHT_MAP_CONVERSION
                Debug.Log($"Creating new DOTS light map array from {lightMapIndices.Count} light maps.");
#endif

                var lightMapArray = LightMaps.ConstructLightMaps(m_colors, m_directions, m_shadowMasks);

                for (int i = 0; i < lightMapIndices.Length; ++i)
                {
                    var lightMapRef = new LightMapReference
                    {
                        lightMaps     = lightMapArray,
                        lightmapIndex = i,
                    };

                    m_lightMapReferences[lightMapIndices[i]]                                              = lightMapRef;
                    m_lightMapArrayCache[new LightMapKey(m_colors[i], m_directions[i], m_shadowMasks[i])] = lightMapRef;
                }
            }
            lightMapIndices.Dispose();
        }

        public LightMapReference GetLightMapReference(int lightmapIndex)
        {
            if (m_lightMapReferences.TryGetValue(lightmapIndex, out var lightMapRef))
                return lightMapRef;
            else
                return null;
        }

        public Material GetLightMappedMaterial(UnityObjectRef<Material> baseMaterial, LightMapReference lightMapRef)
        {
            var flags = LightMappingFlags.Lightmapped;
            if (lightMapRef.lightMaps.hasDirections)
                flags |= LightMappingFlags.Directional;
            if (lightMapRef.lightMaps.hasShadowMask)
                flags |= LightMappingFlags.ShadowMask;

            var key = new MaterialLookupKey
            {
                baseMaterial = baseMaterial,
                lightmaps    = lightMapRef.lightMaps,
                flags        = flags
            };

            if (m_lightMappedMaterialCache.TryGetValue(key, out var lightMappedMaterial))
            {
                ++m_numLightMappedMaterialCacheHits;
                return lightMappedMaterial;
            }
            else
            {
                ++m_numLightMappedMaterialCacheMisses;
                lightMappedMaterial             = CreateLightMappedMaterial(baseMaterial, lightMapRef.lightMaps);
                m_lightMappedMaterialCache[key] = lightMappedMaterial;
                return lightMappedMaterial;
            }
        }

        private static Material CreateLightMappedMaterial(UnityObjectRef<Material> material, LightMaps lightMaps)
        {
            var lightMappedMaterial  = new Material(material);
            lightMappedMaterial.name = $"{lightMappedMaterial.name}_Lightmapped_";
            lightMappedMaterial.EnableKeyword("LIGHTMAP_ON");

            lightMappedMaterial.SetTexture("unity_Lightmaps",    lightMaps.colors);
            lightMappedMaterial.SetTexture("unity_LightmapsInd", lightMaps.directions);
            lightMappedMaterial.SetTexture("unity_ShadowMasks",  lightMaps.shadowMasks);

            if (lightMaps.hasDirections)
            {
                lightMappedMaterial.name = lightMappedMaterial.name + "_DIRLIGHTMAP";
                lightMappedMaterial.EnableKeyword("DIRLIGHTMAP_COMBINED");
            }

            if (lightMaps.hasShadowMask)
            {
                lightMappedMaterial.name = lightMappedMaterial.name + "_SHADOW_MASK";
            }

            return lightMappedMaterial;
        }
    }
}

