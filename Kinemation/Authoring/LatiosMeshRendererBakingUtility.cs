using System.Collections.Generic;
using System.Reflection;
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
            public int      LodGroupIndex;
        }

        static void CreateLODState(IBaker baker, Renderer authoringSource, out LODState lodState)
        {
            // LODGroup
            lodState                = new LODState();
            lodState.LodGroup       = baker.GetComponentInParent<LODGroup>();
            lodState.LodGroupEntity = baker.GetEntity(lodState.LodGroup, TransformUsageFlags.Renderable);
            lodState.LodGroupIndex  = FindInLODs(lodState.LodGroup, authoringSource);
        }

        private static int FindInLODs(LODGroup lodGroup, Renderer authoring)
        {
            if (lodGroup != null)
            {
                var lodGroupLODs = lodGroup.GetLODs();

                // Find the renderer inside the LODGroup
                for (int i = 0; i < lodGroupLODs.Length; ++i)
                {
                    foreach (var renderer in lodGroupLODs[i].renderers)
                    {
                        if (renderer == authoring)
                        {
                            return i;
                        }
                    }
                }
            }
            return -1;
        }

#pragma warning disable CS0162
        [BakingType] struct RequestPreviousTag : IRequestPreviousTransform { }

        private static void AddRendererComponents(Entity entity, IBaker baker, in RenderMeshDescription renderMeshDescription, RenderMesh renderMesh)
        {
            // Entities with Static are never rendered with motion vectors
            bool inMotionPass = RenderMeshUtility.kUseHybridMotionPass &&
                                renderMeshDescription.FilterSettings.IsInMotionPass &&
                                !baker.IsStatic();

            RenderMeshUtility.EntitiesGraphicsComponentFlags flags = RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking;
            if (inMotionPass)
                flags |= RenderMeshUtility.EntitiesGraphicsComponentFlags.InMotionPass;
            flags     |= RenderMeshUtility.LightProbeFlags(renderMeshDescription.LightProbeUsage);
            flags     |= RenderMeshUtility.DepthSortedFlags(renderMesh.material);

            // Add all components up front using as few calls as possible.
            var componentTypes = RenderMeshUtility.s_EntitiesGraphicsComponentTypes.GetComponentTypes(flags);
            baker.AddComponent(entity, componentTypes);
            for (int i = 0; i < componentTypes.Length; i++)
            {
                // Todo: What to do for Unity Transforms?
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                if (componentTypes.GetTypeIndex(i) == TypeManager.GetTypeIndex<PreviousTransform>())
                    baker.AddComponent<RequestPreviousTag>(entity);
#endif
            }

            baker.SetSharedComponentManaged(entity, renderMesh);
            baker.SetSharedComponentManaged(entity, renderMeshDescription.FilterSettings);

            var localBounds                                     = renderMesh.mesh.bounds.ToAABB();
            baker.SetComponent(entity, new RenderBounds { Value = localBounds });
        }

        internal static void Convert(IBaker baker,
                                     Renderer authoring,
                                     Mesh mesh,
                                     List<Material>   sharedMaterials,
                                     bool attachToPrimaryEntityForSingleMaterial,
                                     out List<Entity> additionalEntities)
        {
            additionalEntities = new List<Entity>();

            if (mesh == null || sharedMaterials.Count == 0)
            {
                Debug.LogWarning(
                    $"Renderer is not converted because either the assigned mesh is null or no materials are assigned on GameObject {authoring.name}.",
                    authoring);
                return;
            }

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
            var desc       = new RenderMeshDescription(authoring);
            var renderMesh = new RenderMesh(authoring, mesh, sharedMaterials);

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic())
            {
                if (desc.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                    desc.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
            }

            if (attachToPrimaryEntityForSingleMaterial && sharedMaterials.Count == 1)
            {
                ConvertToSingleEntity(
                    baker,
                    desc,
                    renderMesh,
                    authoring);
            }
            else
            {
                ConvertToMultipleEntities(
                    baker,
                    desc,
                    renderMesh,
                    authoring,
                    sharedMaterials,
                    out additionalEntities);
            }
        }

#pragma warning restore CS0162

        static void ConvertToSingleEntity(
            IBaker baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer)
        {
            CreateLODState(baker, renderer, out var lodState);

            var entity = baker.GetEntity(renderer, TransformUsageFlags.Renderable);

            AddRendererComponents(entity, baker, renderMeshDescription, renderMesh);

            if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupIndex != -1)
            {
                var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = 1 << lodState.LodGroupIndex };
                baker.AddComponent(entity, lodComponent);
            }
        }

        internal static void ConvertToMultipleEntities(
            IBaker baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer,
            List<Material>        sharedMaterials,
            out List<Entity>      additionalEntities)
        {
            CreateLODState(baker, renderer, out var lodState);

            int materialCount  = sharedMaterials.Count;
            additionalEntities = new List<Entity>();

            for (var m = 0; m != materialCount; m++)
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
                    renderMeshDescription,
                    renderMesh);

                if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupIndex != -1)
                {
                    var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = 1 << lodState.LodGroupIndex };
                    baker.AddComponent(meshEntity, lodComponent);
                }
            }
        }
    }
}

