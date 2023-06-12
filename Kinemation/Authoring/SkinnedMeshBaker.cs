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
    /// <summary>
    /// This baker provides the default baking implementation for converting a SkinnedMeshRenderer into something Kinemation can render.
    /// See Kinemation's documentation for details.
    /// </summary>
    [DisableAutoCreation]
    public class SkinnedMeshBaker : Baker<SkinnedMeshRenderer>
    {
        List<Material> m_materialsCache = new List<Material>();

        public override void Bake(SkinnedMeshRenderer authoring)
        {
            if (GetComponent<OverrideMeshRendererBase>() != null)
                return;

            var sharedMesh = authoring.sharedMesh;
            if (sharedMesh == null)
            {
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no mesh was assigned.");
                return;
            }
            m_materialsCache.Clear();

            authoring.GetSharedMaterials(m_materialsCache);
            if (m_materialsCache.Count == 0)
            {
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no materials were assigned.");
                return;
            }

            int  countValidMaterials     = 0;
            int  knownValidMaterialIndex = -1;
            bool needsWarning            = false;
            for (int i = 0; i < m_materialsCache.Count; i++)
            {
                if (m_materialsCache[i] == null)
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
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no materials were assigned.");
                return;
            }
            if (needsWarning)
            {
                Debug.LogWarning($"Some materials on Skinned Mesh Renderer {authoring.gameObject.name} were not assigned. Rendering results may be incorrect.");
            }

            var entity = GetEntity(TransformUsageFlags.Renderable);

            var settings = GetComponent<SkinnedMeshSettingsAuthoring>();
            if (sharedMesh.bindposeCount > 0)
            {
                if (settings == null || settings.bindingMode == SkinnedMeshSettingsAuthoring.BindingMode.BakeTime)
                {
                    var bones = authoring.bones;
                    if (bones == null || bones.Length == 0)
                    {
                        var animator = GetComponentInParent<Animator>();
                        if (animator == null)
                        {
                            Debug.LogWarning(
                                $"Skinned Mesh Renderer {authoring.gameObject.name} is in an optimized hierachy (bones array is empty) which does not have an animator. No binding paths will be generated.");
                        }
                        else
                        {
                            AddComponent(entity, new PendingMeshBindingPathsBlob
                            {
                                blobHandle = this.RequestCreateBlobAsset(animator, authoring)
                            });
                            AddComponent<MeshBindingPathsBlobReference>(entity);
                        }
                    }
                    else
                    {
                        bool skipBecauseOfWarning = false;

                        foreach (var bone in bones)
                        {
                            if (bone == null)
                            {
                                Debug.LogWarning(
                                    $"Skinned Mesh Renderer {authoring.gameObject.name} has a missing bone in its bone array. Please ensure the bones array is set correctly. No binding paths will be generated.");
                                skipBecauseOfWarning = true;
                                break;
                            }
                        }

                        if (!skipBecauseOfWarning)
                        {
                            var pathsPacked = new NativeText(Allocator.Temp);
                            var offsets     = new NativeArray<int>(bones.Length, Allocator.Temp);
                            int index       = 0;
                            foreach (var bone in bones)
                            {
                                offsets[index] = pathsPacked.Length;
                                var go         = bone.gameObject;

                                var parent = GetParent(go);
                                while (parent != null)
                                {
                                    pathsPacked.Append(GetName(go));
                                    pathsPacked.Append('/');
                                    go     = parent;
                                    parent = GetParent(parent);
                                }
                                index++;
                            }

                            AddComponent(entity, new PendingMeshBindingPathsBlob
                            {
                                blobHandle = this.RequestCreateBlobAsset(pathsPacked, offsets)
                            });
                            AddComponent<MeshBindingPathsBlobReference>(entity);
                        }
                    }
                }
                else if (settings.bindingMode == SkinnedMeshSettingsAuthoring.BindingMode.OverridePaths)
                {
                    if (settings.customBonePathsReversed == null)
                    {
                        Debug.LogWarning(
                            $"Skinned Mesh Settings {authoring.gameObject.name} specify override paths but the override paths list is null. No binding paths will be generated.");
                    }
                    else
                    {
                        AddComponent(entity, new PendingMeshBindingPathsBlob
                        {
                            blobHandle = this.RequestCreateBlobAsset(settings.customBonePathsReversed)
                        });
                        AddComponent<MeshBindingPathsBlobReference>(entity);
                    }
                }
            }
            if (sharedMesh.blendShapeCount > 0)
            {
                AddComponent<BlendShapeState>(entity);
                var weightsBuffer = AddBuffer<BlendShapeWeight>(entity);
                weightsBuffer.ResizeUninitialized(sharedMesh.blendShapeCount);
                var weights = weightsBuffer.AsNativeArray().Reinterpret<float>();
                for (int i = 0; i < sharedMesh.blendShapeCount; i++)
                {
                    weights[i] = authoring.GetBlendShapeWeight(i) / 100f;
                }
            }

            AddComponent(entity, new PendingMeshDeformDataBlob
            {
                blobHandle = this.RequestCreateBlobAsset(sharedMesh)
            });
            AddComponent<MeshDeformDataBlobReference>(entity);

            var additionalEntities = new NativeList<Entity>(Allocator.Temp);
            LatiosDeformMeshRendererBakingUtility.Convert(this, authoring, sharedMesh, m_materialsCache, additionalEntities, knownValidMaterialIndex);

            AddComponent(entity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = authoring });

            foreach (var submeshEntity in additionalEntities)
            {
                AddComponent(submeshEntity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = authoring });
            }

            m_materialsCache.Clear();
        }
    }
}

