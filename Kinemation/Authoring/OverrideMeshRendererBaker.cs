using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    public class OverrideMeshRendererBase : MonoBehaviour
    {
    }

    public abstract class OverrideMeshRendererBakerBase<T> : Baker<T> where T : Component
    {
        protected void BakeMeshAndMaterial(Renderer renderer, Mesh mesh, List<Material> materials)
        {
            MeshRendererBakingUtility.Convert(this, renderer, mesh, materials, true, out var additionalEntities);

            if (additionalEntities.Count == 0)
                AddComponent(new MeshRendererBakingData { MeshRenderer = renderer });

            foreach (var entity in additionalEntities)
            {
                AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });
            }
        }
    }

    [DisableAutoCreation]
    public class DefaultMeshRendererBaker : OverrideMeshRendererBakerBase<MeshRenderer>
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

            BakeMeshAndMaterial(authoring, mesh, m_materialsCache);
        }
    }
}

