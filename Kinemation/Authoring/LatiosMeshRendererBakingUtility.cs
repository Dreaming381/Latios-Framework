using System.Collections.Generic;
using Latios.Transforms;
using Latios.Transforms.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    class MeshRendererBakingUtility
    {
        [BakingType]
        struct CopyParentRequestTag : IRequestCopyParentTransform { }

        struct LODState
        {
            public LODGroup LodGroup;
            public Entity   LodGroupEntity;
            public int      LodGroupMask;
        }

        static void CreateLODState(IBaker baker, Renderer authoringSource, out LODState lodState)
        {
            // LODGroup
            lodState                = new LODState();
            lodState.LodGroup       = baker.GetComponentInParent<LODGroup>();
            lodState.LodGroupEntity = baker.GetEntity(lodState.LodGroup, TransformUsageFlags.Renderable);
            lodState.LodGroupMask   = FindInLODs(lodState.LodGroup, authoringSource);
        }

        private static int FindInLODs(LODGroup lodGroup, Renderer authoring)
        {
            if (lodGroup != null)
            {
                var lodGroupLODs = lodGroup.GetLODs();

                int lodGroupMask = 0;

                // Find the renderer inside the LODGroup
                for (int i = 0; i < lodGroupLODs.Length; ++i)
                {
                    foreach (var renderer in lodGroupLODs[i].renderers)
                    {
                        if (renderer == authoring)
                        {
                            lodGroupMask |= (1 << i);
                        }
                    }
                }
                return lodGroupMask > 0 ? lodGroupMask : -1;
            }
            return -1;
        }

#pragma warning disable CS0162
        [BakingType] struct RequestPreviousTag : IRequestPreviousTransform { }

        private static void AddRendererComponents(Entity entity, IBaker baker, in RenderMeshDescription renderMeshDescription, RenderMesh renderMesh)
        {
            // Add all components up front using as few calls as possible.
            var componentSet = RenderMeshUtility.ComputeComponentTypes(RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking,
                                                                       renderMeshDescription, baker.IsStatic(), renderMesh.materials);
            baker.AddComponent(entity, componentSet);
            for (int i = 0; i < componentSet.Length; i++)
            {
                // Todo: What to do for Unity Transforms?
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                if (componentSet.GetTypeIndex(i) == TypeManager.GetTypeIndex<PreviousTransform>())
                    baker.AddComponent<RequestPreviousTag>(entity);
#endif
            }

            baker.SetSharedComponentManaged(entity, renderMesh);
            baker.SetSharedComponentManaged(entity, renderMeshDescription.FilterSettings);

            var localBounds                                     = renderMesh.mesh.bounds.ToAABB();
            baker.SetComponent(entity, new RenderBounds { Value = localBounds });
        }

#if !ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING
#error Latios Framework requires ENABLE_MESH_RENDERER_SUBMESH_DATA_SHARING to be defined in your scripting define symbols.
#endif

        internal static void Convert(IBaker baker,
                                     Renderer authoring,
                                     Mesh mesh,
                                     List<Material>   sharedMaterials,
                                     out List<Entity> additionalEntities,
                                     int firstUniqueSubmeshIndex = int.MaxValue)
        {
            additionalEntities = default;

            if (mesh == null || sharedMaterials.Count == 0)
            {
                Debug.LogWarning(
                    $"Renderer is not converted because either the assigned mesh is null or no materials are assigned on GameObject {authoring.name}.",
                    authoring);
                return;
            }

            firstUniqueSubmeshIndex = math.clamp(firstUniqueSubmeshIndex, 1, sharedMaterials.Count);

            if (sharedMaterials.Count > firstUniqueSubmeshIndex)
                additionalEntities = new List<Entity>();

            // Takes a dependency on the material
            foreach (var material in sharedMaterials)
                baker.DependsOn(material);

            // Takes a dependency on the mesh
            baker.DependsOn(mesh);

            foreach (var material in sharedMaterials)
            {
                if (LatiosDeformMeshRendererBakingUtility.CheckHasDeformMaterialProperty(material))
                {
                    Debug.LogWarning(
                        $"Material {material.name} used on mesh {mesh.name} baked with reference Renderer {authoring.gameObject.name} is a deform material but is not being baked as such. This will result in incorrect rendering.");
                }
            }

            // RenderMeshDescription accesses the GameObject layer.
            // Declaring the dependency on the GameObject with GetLayer, so the baker rebakes if the layer changes
            baker.GetLayer(authoring);
            var desc             = new RenderMeshDescription(authoring);
            var batchedMaterials = new List<Material>(firstUniqueSubmeshIndex);
            for (int i = 0; i < firstUniqueSubmeshIndex; i++)
                batchedMaterials.Add(sharedMaterials[i]);
            var renderMesh = new RenderMesh(authoring, mesh, batchedMaterials);

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic())
            {
                if (desc.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                    desc.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
            }

            CreateLODState(baker, authoring, out var lodState);

            var entity = baker.GetEntity(authoring, TransformUsageFlags.Renderable);

            AddRendererComponents(entity, baker, desc, renderMesh);

            if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupMask != -1)
            {
                var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = lodState.LodGroupMask };
                baker.AddComponent(entity, lodComponent);
            }

            if (additionalEntities != null)
            {
                for (int m = firstUniqueSubmeshIndex; m < sharedMaterials.Count; m++)
                {
                    Entity meshEntity;
                    meshEntity = baker.CreateAdditionalEntity(TransformUsageFlags.Renderable, false, $"{baker.GetName()}-MeshRendererEntity");

                    // Update Transform components:
                    baker.AddComponent<AdditionalMeshRendererEntity>(meshEntity);
                    if (!baker.IsStatic())
                        baker.AddComponent<CopyParentRequestTag>(meshEntity);

                    additionalEntities.Add(meshEntity);

                    var material = sharedMaterials[m];

                    renderMesh.subMesh  = m;
                    renderMesh.material = material;

                    AddRendererComponents(
                        meshEntity,
                        baker,
                        desc,
                        renderMesh);

                    if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupMask != -1)
                    {
                        var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = lodState.LodGroupMask };
                        baker.AddComponent(meshEntity, lodComponent);
                    }
                }
            }
        }
#pragma warning restore CS0162
    }
}

