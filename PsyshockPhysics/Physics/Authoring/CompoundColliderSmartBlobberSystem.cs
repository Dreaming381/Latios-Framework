using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Transforms;
using Latios.Transforms.Authoring.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

using UnityCollider = UnityEngine.Collider;

namespace Latios.Psyshock.Authoring
{
    public static class CompoundColliderSmartBlobberExtensions
    {
        /// <summary>
        /// Requests the creation of a compound collider using a list of Unity Collider objects.
        /// The list is not serialized, so you can reuse the list after this call.
        /// </summary>
        /// <param name="colliders">The list of colliders to bake into the compound</param>
        /// <param name="compoundTransform">A reference transform that the colliders are baked relative to</param>
        /// <returns></returns>
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
            UnsafeList<ChildProperties> childPropertiesCache;

            public void Execute(ref SmartBlobberResult result, in DynamicBuffer<CompoundColliderBakingSubCollider> buffer)
            {
                if (!childPropertiesCache.IsCreated)
                    childPropertiesCache = new UnsafeList<ChildProperties>(buffer.Length, Allocator.Temp);
                childPropertiesCache.Clear();

                var     builder        = new BlobBuilder(Allocator.Temp);
                ref var root           = ref builder.ConstructRoot<CompoundColliderBlob>();
                var     blobColliders  = builder.Allocate(ref root.colliders, buffer.Length);
                var     blobTransforms = builder.Allocate(ref root.transforms, buffer.Length);

                Aabb   aabb                 = new Aabb(float.MaxValue, float.MinValue);
                float3 combinedCenterOfMass = float3.zero;
                float  combinedVolume       = 0f;
                for (int i = 0; i < buffer.Length; i++)
                {
                    blobColliders[i]  = Physics.ScaleStretchCollider(buffer[i].collider, buffer[i].transform.scale, buffer[i].transform.stretch);
                    blobTransforms[i] = new RigidTransform(buffer[i].transform.rotation, buffer[i].transform.position);
                    var newbox        = Physics.AabbFrom(in blobColliders[i], in blobTransforms[i]);
                    aabb.min          = math.min(aabb.min, newbox.min);
                    aabb.max          = math.max(aabb.max, newbox.max);

                    switch (blobColliders[i].type)
                    {
                        case ColliderType.Sphere:
                        {
                            var sphere            = blobColliders[i].m_sphere;
                            var volume            = (4f / 3f) * math.PI * sphere.radius * sphere.radius * sphere.radius;
                            combinedVolume       += volume;
                            var centerOfMass      = math.transform(blobTransforms[i], sphere.center);
                            combinedCenterOfMass += centerOfMass * volume;

                            // The following comes from Unity Physics
                            float3x3 childInertia = UnitySim.LocalInertiaTensorFrom(sphere, 1f).ToMatrix();
                            // rotate inertia into compound space
                            float3x3 temp = math.mul(childInertia, new float3x3(math.inverse(blobTransforms[i].rot)));
                            childPropertiesCache.Add(new ChildProperties
                            {
                                inertiaMatrixUnshifted = math.mul(new float3x3(blobTransforms[i].rot), temp),
                                centerOfMassInCompound = centerOfMass,
                                volume                 = volume,
                            });
                            break;
                        }
                        case ColliderType.Capsule:
                        {
                            var capsule           = blobColliders[i].m_capsule;
                            var volume            = math.PI * capsule.radius * capsule.radius * ((4f / 3f) * capsule.radius + math.distance(capsule.pointA, capsule.pointB));
                            combinedVolume       += volume;
                            var centerOfMass      = math.transform(blobTransforms[i], (capsule.pointA + capsule.pointB) / 2f);
                            combinedCenterOfMass += centerOfMass * volume;

                            // The following comes from Unity Physics
                            float3x3 childInertia = UnitySim.LocalInertiaTensorFrom(capsule, 1f).ToMatrix();
                            // rotate inertia into compound space
                            float3x3 temp = math.mul(childInertia, new float3x3(math.inverse(blobTransforms[i].rot)));
                            childPropertiesCache.Add(new ChildProperties
                            {
                                inertiaMatrixUnshifted = math.mul(new float3x3(blobTransforms[i].rot), temp),
                                centerOfMassInCompound = centerOfMass,
                                volume                 = volume,
                            });
                            break;
                        }
                        case ColliderType.Box:
                        {
                            var box               = blobColliders[i].m_box;
                            var volume            = 8f * box.halfSize.x * box.halfSize.y * box.halfSize.z;
                            combinedVolume       += volume;
                            var centerOfMass      = math.transform(blobTransforms[i], box.center);
                            combinedCenterOfMass += centerOfMass * volume;

                            // The following comes from Unity Physics
                            float3x3 childInertia = UnitySim.LocalInertiaTensorFrom(box, 1f).ToMatrix();
                            // rotate inertia into compound space
                            float3x3 temp = math.mul(childInertia, new float3x3(math.inverse(blobTransforms[i].rot)));
                            childPropertiesCache.Add(new ChildProperties
                            {
                                inertiaMatrixUnshifted = math.mul(new float3x3(blobTransforms[i].rot), temp),
                                centerOfMassInCompound = centerOfMass,
                                volume                 = volume,
                            });
                            break;
                        }
                    }
                }

                // From Unity Physics
                if (combinedVolume > 0f)
                    combinedCenterOfMass /= combinedVolume;

                var combinedInertiaMatrix = float3x3.zero;
                foreach (var child in childPropertiesCache)
                {
                    // shift the inertia to be relative to the new center of mass
                    float3 shift          = child.centerOfMassInCompound - combinedCenterOfMass;
                    float3 shiftSq        = shift * shift;
                    var    diag           = new float3(shiftSq.y + shiftSq.z, shiftSq.x + shiftSq.z, shiftSq.x + shiftSq.y);
                    var    offDiag        = new float3(shift.x * shift.y, shift.y * shift.z, shift.z * shift.x) * -1.0f;
                    var    inertiaMatrix  = child.inertiaMatrixUnshifted;
                    inertiaMatrix.c0     += new float3(diag.x, offDiag.x, offDiag.z);
                    inertiaMatrix.c1     += new float3(offDiag.x, diag.y, offDiag.y);
                    inertiaMatrix.c2     += new float3(offDiag.z, offDiag.y, diag.z);

                    // weight by its proportional volume (=mass)
                    inertiaMatrix         *= child.volume / (combinedVolume + float.Epsilon);
                    combinedInertiaMatrix += inertiaMatrix;
                }

                root.localAabb     = aabb;
                root.centerOfMass  = combinedCenterOfMass;
                root.inertiaTensor = combinedInertiaMatrix;
                mathex.DiagonalizeSymmetricApproximation(root.inertiaTensor, out var inertiaTensorOrientation, out root.unscaledInertiaTensorDiagonal);
                root.unscaledInertiaTensorOrientation = new quaternion(inertiaTensorOrientation);

                result.blob = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<CompoundColliderBlob>(Allocator.Persistent));
            }

            struct ChildProperties
            {
                public float3x3 inertiaMatrixUnshifted;
                public float3   centerOfMassInCompound;
                public float    volume;
            }
        }
    }
}

