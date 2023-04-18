using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    public class OverrideMeshRendererBase : MonoBehaviour
    {
    }

    public static class OverrideMeshRendererBakerExtensions
    {
        public static void BakeMeshAndMaterial(this IBaker baker, Renderer renderer, Mesh mesh, List<Material> materials)
        {
            MeshRendererBakingUtility.Convert(baker, renderer, mesh, materials, true, out var additionalEntities);

            if (additionalEntities.Count == 0)
                baker.AddComponent(new MeshRendererBakingData { MeshRenderer = renderer });

            foreach (var entity in additionalEntities)
            {
                baker.AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });
            }
        }

        public static void BakeDeformMeshAndMaterial(this IBaker baker, Renderer renderer, Mesh mesh, List<Material> materials)
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
            LatiosDeformMeshRendererBakingUtility.Convert(baker, renderer, mesh, materials, additionalEntities, knownValidMaterialIndex);

            if (renderer is SkinnedMeshRenderer smr)
            {
                baker.AddComponent(new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = smr });

                foreach (var entity in additionalEntities)
                {
                    baker.AddComponent(entity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = smr });
                }
            }
            else
            {
                baker.AddComponent(new MeshRendererBakingData { MeshRenderer = renderer });

                foreach (var entity in additionalEntities)
                {
                    baker.AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });
                }
            }
        }
    }

    [DisableAutoCreation]
    public class DefaultMeshRendererBaker : Baker<MeshRenderer>
    {
        List<Material> m_materialsCache = new List<Material>();

        public override void Bake(MeshRenderer authoring)
        {
            if (GetComponent<OverrideMeshRendererBase>() != null)
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

