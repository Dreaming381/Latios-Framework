using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Override this class and attach to a GameObject to disable normal Mesh Renderer or Skinned Mesh Renderer baking
    /// </summary>
    public class OverrideMeshRendererBase : MonoBehaviour
    {
    }

    public static class OverrideMeshRendererBakerExtensions
    {
        /// <summary>
        /// Bake a Mesh Renderer using the provided mesh and materials
        /// </summary>
        /// <param name="renderer">The Renderer to base shadow mapping, visibility, and global illumination settings from</param>
        /// <param name="mesh">The mesh to use</param>
        /// <param name="materials">The materials to use, one for each submesh in the mesh</param>
        public static void BakeMeshAndMaterial(this IBaker baker, Renderer renderer, Mesh mesh, List<Material> materials, int firstUniqueSubmeshIndex = int.MaxValue)
        {
            MeshRendererBakingUtility.Convert(baker, renderer, mesh, materials, out var additionalEntities, firstUniqueSubmeshIndex);

            baker.AddComponent(baker.GetEntity(TransformUsageFlags.Renderable), new MeshRendererBakingData { MeshRenderer = renderer });

            if (additionalEntities != null)
            {
                foreach (var entity in additionalEntities)
                {
                    baker.AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });
                }
            }
        }

        /// <summary>
        /// Bake a Skinned Mesh Renderer using the provided mesh and materials.
        /// This does not bake the MeshDeformDataBlob.
        /// </summary>
        /// <param name="renderer">The Renderer to base shadow mapping, visibility, and global illumination settings from</param>
        /// <param name="mesh">The mesh to use</param>
        /// <param name="materials">The materials to use, one for each submesh in the mesh</param>
        public static void BakeDeformMeshAndMaterial(this IBaker baker, Renderer renderer, Mesh mesh, List<Material> materials, int firstUniqueSubmeshIndex = int.MaxValue)
        {
            var sharedMesh = mesh;
            if (sharedMesh == null)
            {
                Debug.LogError($"Kinemation failed to bake Dynamic Mesh because no mesh was assigned.");
                return;
            }

            if (materials.Count == 0)
            {
                Debug.LogError($"Kinemation failed to bake Dynamic Mesh because no materials were assigned.");
                return;
            }

            int  countValidMaterials     = 0;
            int  knownValidMaterialIndex = -1;
            bool needsWarning            = false;
            for (int i = 0; i < materials.Count; i++)
            {
                if (materials[i] == null)
                {
                    needsWarning = true;
                }
                else
                {
                    if (knownValidMaterialIndex < 0)
                        knownValidMaterialIndex = i;
                    countValidMaterials++;
                }
            }

            if (countValidMaterials == 0)
            {
                Debug.LogError($"Kinemation failed to bake Dynamic Mesh because no materials were assigned.");
                return;
            }
            if (needsWarning)
            {
                Debug.LogWarning($"Some materials for Dynamic Mesh were not assigned. Rendering results may be incorrect.");
            }

            var additionalEntities = new NativeList<Entity>(Allocator.Temp);
            LatiosDeformMeshRendererBakingUtility.Convert(baker, renderer, mesh, materials, additionalEntities, knownValidMaterialIndex, firstUniqueSubmeshIndex);

            var primaryEntity = baker.GetEntity(TransformUsageFlags.Renderable);
            if (renderer is SkinnedMeshRenderer smr)
            {
                baker.AddComponent(primaryEntity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = smr });

                foreach (var entity in additionalEntities)
                {
                    baker.AddComponent(entity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = smr });
                }
            }
            else
            {
                baker.AddComponent(baker.GetEntity(TransformUsageFlags.Renderable), new MeshRendererBakingData { MeshRenderer = renderer });

                foreach (var entity in additionalEntities)
                {
                    baker.AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });
                }
            }
        }
    }

    /// <summary>
    /// This is the default Mesh Renderer baker in Kinemation.
    /// Kinemation replaces Unity's version to accomodate QVVS transforms
    /// and allow overrides. But it is otherwise identical.
    /// </summary>
    [DisableAutoCreation]
    public class DefaultMeshRendererBaker : Baker<MeshRenderer>
    {
        List<Material> m_materialsCache = new List<Material>();

        public override void Bake(MeshRenderer authoring)
        {
            if (GetComponent<OverrideMeshRendererBase>() != null)
                return;

            if (GetComponent<BakingOnlyEntityAuthoring>() != null)
                return;

            // TextMeshes don't need MeshFilters, early out
            var textMesh = GetComponent<TextMesh>();
            if (textMesh != null)
                return;

            // Takes a dependency on the mesh
            var meshFilter = GetComponent<MeshFilter>();
            var mesh       = (meshFilter != null) ? GetComponent<MeshFilter>().sharedMesh : null;

            // Takes a dependency on the materials
            m_materialsCache.Clear();
            authoring.GetSharedMaterials(m_materialsCache);

            DependsOnLightBaking();

            this.BakeMeshAndMaterial(authoring, mesh, m_materialsCache);
        }
    }
}

