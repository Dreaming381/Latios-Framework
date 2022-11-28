using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

using UnityCollider = UnityEngine.Collider;

namespace Latios.Psyshock.Authoring
{
    public static class CompoundColliderSmartBlobberExtensions
    {
        public static SmartBlobberHandle<CompoundColliderBlob> RequestCreateBlobAsset(this IBaker baker, List<UnityCollider> colliders, UnityEngine.Transform compoundTransform)
        {
            return baker.RequestCreateBlobAsset<CompoundColliderBlob, CompoundColliderBakeData>(new CompoundColliderBakeData
            {
                colliders         = colliders,
                compoundTransform = compoundTransform
            });
        }
    }

    public struct CompoundColliderBakeData : ISmartBlobberRequestFilter<CompoundColliderBlob>
    {
        public List<UnityCollider>   colliders;
        public UnityEngine.Transform compoundTransform;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (colliders == null)
                return false;
            if (colliders.Count == 0)
                return false;

            baker.AddComponent(blobBakingEntity, new CompoundColliderBakingRootTransform
            {
                worldTransform = compoundTransform.localToWorldMatrix
            });

            var    buffer        = baker.AddBuffer<CompoundColliderBakingSubCollider>(blobBakingEntity);
            float3 compoundScale = compoundTransform.lossyScale;

            foreach (var unityCollider in colliders)
            {
                if (unityCollider == null)
                    continue;
                if (unityCollider is UnityEngine.MeshCollider)
                    continue;

                var  currentTransform = baker.GetComponent<UnityEngine.Transform>(unityCollider);
                var  scale            = currentTransform.lossyScale / compoundScale;
                bool nonUniformScale  = math.cmax(scale) - math.cmin(scale) > 1.0E-5f;

                if (unityCollider is UnityEngine.SphereCollider unitySphere)
                {
                    if (nonUniformScale)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed to bake {unitySphere.gameObject.name} in compound. Only uniform scaling is supported on SphereCollider. Lossy Scale divergence was: {math.cmax(scale) - math.cmin(scale)}");
                        continue;
                    }

                    buffer.Add(new CompoundColliderBakingSubCollider
                    {
                        collider = new SphereCollider
                        {
                            center = unitySphere.center,
                            radius = unitySphere.radius * scale.x
                        },
                        worldTransform = currentTransform.localToWorldMatrix
                    });
                }
                else if (unityCollider is UnityEngine.CapsuleCollider unityCapsule)
                {
                    if (nonUniformScale)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Failed to bake {unityCapsule.gameObject.name} in compound. Only uniform scaling is supported on CapsuleCollider. Lossy Scale divergence was: {math.cmax(scale) - math.cmin(scale)}");
                        continue;
                    }

                    float3 dir;
                    if (unityCapsule.direction == 0)
                    {
                        dir = new float3(1f, 0f, 0f);
                    }
                    else if (unityCapsule.direction == 1)
                    {
                        dir = new float3(0f, 1f, 0f);
                    }
                    else
                    {
                        dir = new float3(0f, 0f, 1f);
                    }
                    buffer.Add(new CompoundColliderBakingSubCollider
                    {
                        collider = new CapsuleCollider
                        {
                            pointB = (float3)unityCapsule.center + ((unityCapsule.height / 2f - unityCapsule.radius) * unityCapsule.transform.lossyScale.x * dir),
                            pointA = (float3)unityCapsule.center - ((unityCapsule.height / 2f - unityCapsule.radius) * unityCapsule.transform.lossyScale.x * dir),
                            radius = unityCapsule.radius * scale.x
                        },
                        worldTransform = currentTransform.localToWorldMatrix
                    });
                }
                else if (unityCollider is UnityEngine.BoxCollider unityBox)
                {
                    buffer.Add(new CompoundColliderBakingSubCollider
                    {
                        collider = new BoxCollider
                        {
                            center   = unityBox.center,
                            halfSize = unityBox.size * scale / 2f
                        },
                        worldTransform = currentTransform.localToWorldMatrix
                    });
                }
            }

            return !buffer.IsEmpty;
        }
    }

    [TemporaryBakingType]
    internal struct CompoundColliderBakingSubCollider : IBufferElementData
    {
        public Collider collider;
        public float4x4 worldTransform;
    }

    [TemporaryBakingType]
    internal struct CompoundColliderBakingRootTransform : IComponentData
    {
        public float4x4 worldTransform;
    }
}
namespace Latios.Psyshock.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    [BurstCompile]
    public partial struct CompoundColliderSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<CompoundColliderBlob>().Register(state.World);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        partial struct Job : IJobEntity
        {
            public void Execute(ref SmartBlobberResult result, in CompoundColliderBakingRootTransform rootTransform, in DynamicBuffer<CompoundColliderBakingSubCollider> buffer)
            {
                var     builder        = new BlobBuilder(Allocator.Temp);
                ref var root           = ref builder.ConstructRoot<CompoundColliderBlob>();
                var     blobColliders  = builder.Allocate(ref root.colliders, buffer.Length);
                var     blobTransforms = builder.Allocate(ref root.transforms, buffer.Length);

                var rootInverse = math.inverse(rootTransform.worldTransform);

                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var ltr      = new Unity.Transforms.LocalToWorld { Value = math.mul(rootInverse, buffer[i].worldTransform) };
                    var rotation                                             = quaternion.LookRotationSafe(ltr.Forward, ltr.Up);

                    blobColliders[i]  = buffer[i].collider;
                    blobTransforms[i] = new RigidTransform(rotation, ltr.Position);
                    var newbox        = Physics.AabbFrom(in blobColliders[i], in blobTransforms[i]);
                    aabb.min          = math.min(aabb.min, newbox.min);
                    aabb.max          = math.max(aabb.max, newbox.max);
                }

                root.localAabb = aabb;
                result.blob    = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<CompoundColliderBlob>(Allocator.Persistent));
            }
        }
    }
}

