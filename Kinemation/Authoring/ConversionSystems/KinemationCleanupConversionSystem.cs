using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

// Note: We avoid adding components that would otherwise be added at runtime by the reactive systems.
// That way we keep the serialized data size down.
namespace Latios.Kinemation.Authoring.Systems
{
    [UpdateInGroup(typeof(GameObjectConversionGroup), OrderFirst = true)]
    [ConverterVersion("Latios", 1)]
    [DisableAutoCreation]
    public class KinemationCleanupConversionSystem : GameObjectConversionSystem
    {
        EntityQuery m_skeletonQuery;
        EntityQuery m_meshQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_skeletonQuery = GetEntityQuery(typeof(SkeletonConversionContext));
            m_meshQuery     = GetEntityQuery(typeof(SkinnedMeshConversionContext));
        }

        protected override void OnUpdate()
        {
            var exposedBoneCullingTypes     = CullingUtilities.GetBoneCullingComponentTypes();
            var transformComponentsToRemove = new ComponentTypes(typeof(Translation), typeof(Rotation), typeof(NonUniformScale));

            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            Entities.ForEach((SkeletonConversionContext context) =>
            {
                var entity = GetPrimaryEntity(context.animator);

                DstEntityManager.AddComponent<SkeletonRootTag>(entity);

                if (context.isOptimized)
                {
                    var ltrBuffer = DstEntityManager.AddBuffer<OptimizedBoneToRoot>(entity).Reinterpret<float4x4>();
                    ltrBuffer.ResizeUninitialized(context.skeleton.Length);
                    short i = 0;
                    foreach (var b in context.skeleton)
                    {
                        var ltp = float4x4.TRS(b.localPosition, b.localRotation, b.localScale);
                        if (b.parentIndex < 0)
                            ltrBuffer[i] = ltp;
                        else
                            ltrBuffer[i] = math.mul(ltrBuffer[b.parentIndex], ltp);

                        if (b.gameObjectTransform != null)
                        {
                            var boneEntity                                                              = GetPrimaryEntity(b.gameObjectTransform);
                            ecb.AddComponent(boneEntity, new BoneOwningSkeletonReference { skeletonRoot = entity });
                            if (b.gameObjectTransform.parent == context.animator.transform)
                            {
                                ecb.AddComponent(boneEntity, new CopyLocalToParentFromBone { boneIndex = i });
                                ecb.RemoveComponent(boneEntity, transformComponentsToRemove);
                            }
                        }

                        i++;
                    }
                }
                else
                {
                    var boneReferenceBuffer = DstEntityManager.AddBuffer<BoneReference>(entity).Reinterpret<Entity>();
                    boneReferenceBuffer.ResizeUninitialized(context.skeleton.Length);
                    short i = 0;

                    foreach (var b in context.skeleton)
                    {
                        var boneEntity = GetPrimaryEntity(b.gameObjectTransform);
                        ecb.AddComponent(boneEntity, exposedBoneCullingTypes);
                        ecb.SetComponent(boneEntity, new BoneOwningSkeletonReference { skeletonRoot = entity });
                        ecb.SetComponent(boneEntity, new BoneIndex { index                          = i });
                        boneReferenceBuffer[i]                                                      = boneEntity;

                        // Animation can have scale even if the default pose doesn't.
                        ecb.AddComponent(boneEntity, new NonUniformScale { Value = 1f });
                        if (b.ignoreParentScale)
                            ecb.AddComponent<ParentScaleInverse>(boneEntity);
                        i++;
                    }
                }

                context.DestroyShadowHierarchy();
            });

            ecb.Playback(DstEntityManager);
            ecb.Dispose();

            var computePropertyID     = UnityEngine.Shader.PropertyToID("_ComputeMeshIndex");
            var linearBlendPropertyID = UnityEngine.Shader.PropertyToID("_SkinMatrixIndex");
            var materialsCache        = new List<UnityEngine.Material>();

            var renderMeshConversionContext = new RenderMeshConversionContext(DstEntityManager, this)
            {
                AttachToPrimaryEntityForSingleMaterial = true
            };

            Entities.ForEach((SkinnedMeshConversionContext context) =>
            {
                var  entity                     = GetPrimaryEntity(context.renderer);
                bool needsComputeDeformFromCopy = false;
                bool needsLinearBlendFromCopy   = false;

                context.renderer.GetSharedMaterials(materialsCache);
                if (materialsCache.Count > 1)
                {
                    // We want the primary bound entity to stay the primary entity as that is intuitive for users.
                    // So to do that, we convert only the first material of the skinned mesh as the primary.
                    // And then convert all the materials as children. This duplicates the first material which we
                    // then destroy right after.
                    var firstMaterial = materialsCache[0];
                    materialsCache.Clear();
                    materialsCache.Add(firstMaterial);
                    renderMeshConversionContext.Convert(context.renderer, context.renderer.sharedMesh, materialsCache, context.renderer.transform);

                    // A null cache will cause the context to fetch the materials using its own cache.
                    renderMeshConversionContext.Convert(context.renderer, context.renderer.sharedMesh, null,           context.renderer.transform);

                    var entityToDestroy = Entity.Null;
                    foreach (var candidateEntity in GetEntities(context.renderer))
                    {
                        if (candidateEntity == entity)
                            continue;
                        if (DstEntityManager.HasComponent<RenderMesh>(candidateEntity))
                        {
                            var rm = DstEntityManager.GetSharedComponentData<RenderMesh>(candidateEntity);
                            if (rm.subMesh == 0)
                            {
                                entityToDestroy = candidateEntity;
                            }
                            else
                            {
                                DstEntityManager.AddComponentData(candidateEntity, new ShareSkinFromEntity { sourceSkinnedEntity = entity });
                                DstEntityManager.AddChunkComponentData<ChunkCopySkinShaderData>(candidateEntity);
                                if (rm.material.HasProperty(computePropertyID))
                                {
                                    DstEntityManager.AddComponent<ComputeDeformShaderIndex>(candidateEntity);
                                    needsComputeDeformFromCopy = true;
                                }
                                if (rm.material.HasProperty(linearBlendPropertyID))
                                {
                                    DstEntityManager.AddComponent<LinearBlendSkinningShaderIndex>(candidateEntity);
                                    needsLinearBlendFromCopy = true;
                                }
                            }
                        }
                    }

                    // Todo: Is this safe?
                    DstEntityManager.DestroyEntity(entityToDestroy);
                }
                else
                    renderMeshConversionContext.Convert(context.renderer, context.renderer.sharedMesh, materialsCache, context.renderer.transform);

                // Even if this material doesn't use a particular skinning property, its child might,
                // and that child will want to copy a valid property from the parent.
                if (materialsCache[0].HasProperty(computePropertyID) || needsComputeDeformFromCopy)
                {
                    DstEntityManager.AddComponent<ComputeDeformShaderIndex>(entity);
                    DstEntityManager.AddChunkComponentData<ChunkComputeDeformMemoryMetadata>(entity);
                }
                if (materialsCache[0].HasProperty(linearBlendPropertyID) || needsLinearBlendFromCopy)
                {
                    DstEntityManager.AddComponent<LinearBlendSkinningShaderIndex>(entity);
                    DstEntityManager.AddChunkComponentData<ChunkLinearBlendSkinningMemoryMetadata>(entity);
                }

                if (context.skeletonContext != null)
                {
                    var root                                                              = GetPrimaryEntity(context.skeletonContext.animator);
                    DstEntityManager.AddComponentData(entity, new BindSkeletonRoot { root = root });
                    DstEntityManager.AddComponentData(entity, new Parent { Value          = root });
                    DstEntityManager.AddComponentData(entity, new LocalToParent { Value   = float4x4.identity });
                    DstEntityManager.RemoveComponent(entity, transformComponentsToRemove);
                }
            });

            renderMeshConversionContext.EndConversion();

            EntityManager.RemoveComponent<SkeletonConversionContext>(   m_skeletonQuery);
            EntityManager.RemoveComponent<SkinnedMeshConversionContext>(m_meshQuery);
        }
    }
}

