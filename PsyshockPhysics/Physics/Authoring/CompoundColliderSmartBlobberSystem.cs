using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Transforms;
using Latios.Transforms.Authoring.Abstract;
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

            var buffer = baker.AddBuffer<CompoundColliderBakingSubCollider>(blobBakingEntity);

            foreach (var unityCollider in colliders)
            {
                if (unityCollider == null)
                    continue;
                if (unityCollider is UnityEngine.MeshCollider)
                    continue;

                var currentTransform = baker.GetComponent<UnityEngine.Transform>(unityCollider);
                var transformQvvs    = AbstractBakingUtilities.ExtractTransformRelativeTo(currentTransform, compoundTransform);

                if (unityCollider is UnityEngine.SphereCollider unitySphere)
                {
                    buffer.Add(new CompoundColliderBakingSubCollider
                    {
                        collider = new SphereCollider
                        {
                            center      = unitySphere.center,
                            radius      = unitySphere.radius,
                            stretchMode = SphereCollider.StretchMode.StretchCenter
                        },
                        transform = transformQvvs
                    });
                }
                else if (unityCollider is UnityEngine.CapsuleCollider unityCapsule)
                {
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
                            pointB      = (float3)unityCapsule.center + ((unityCapsule.height / 2f - unityCapsule.radius) * dir),
                            pointA      = (float3)unityCapsule.center - ((unityCapsule.height / 2f - unityCapsule.radius) * dir),
                            radius      = unityCapsule.radius,
                            stretchMode = CapsuleCollider.StretchMode.StretchPoints
                        },
                        transform = transformQvvs
                    });
                }
                else if (unityCollider is UnityEngine.BoxCollider unityBox)
                {
                    buffer.Add(new CompoundColliderBakingSubCollider
                    {
                        collider = new BoxCollider
                        {
                            center   = unityBox.center,
                            halfSize = unityBox.size / 2f
                        },
                        transform = transformQvvs
                    });
                }
            }

            return !buffer.IsEmpty;
        }
    }

    [TemporaryBakingType]
    internal struct CompoundColliderBakingSubCollider : IBufferElementData
    {
        public Collider      collider;
        public TransformQvvs transform;
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
            public void Execute(ref SmartBlobberResult result, in DynamicBuffer<CompoundColliderBakingSubCollider> buffer)
            {
                var     builder        = new BlobBuilder(Allocator.Temp);
                ref var root           = ref builder.ConstructRoot<CompoundColliderBlob>();
                var     blobColliders  = builder.Allocate(ref root.colliders, buffer.Length);
                var     blobTransforms = builder.Allocate(ref root.transforms, buffer.Length);

                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < buffer.Length; i++)
                {
                    blobColliders[i]  = Physics.ScaleStretchCollider(buffer[i].collider, buffer[i].transform.scale, buffer[i].transform.stretch);
                    blobTransforms[i] = new RigidTransform(buffer[i].transform.rotation, buffer[i].transform.position);
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

