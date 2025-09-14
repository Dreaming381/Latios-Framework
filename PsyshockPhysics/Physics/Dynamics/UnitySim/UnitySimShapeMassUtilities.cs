using Latios.Calci;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        /// <summary>
        /// A local-space normalized inertia tensor diagonal and orientation.
        /// The diagonal should have any stretch already applied to the collider
        /// it was derived from. The diagonal is used to compute the inverse inertia
        /// in a Mass instance while the orientation is transformed into the
        /// inertialPoseWorldTransform. The inertia tensor diagonal is normalized to
        /// be independent of the object's mass.
        /// </summary>
        public struct LocalInertiaTensorDiagonal
        {
            public quaternion tensorOrientation;
            public float3     inertiaDiagonal;

            /// <summary>
            /// Converts the inertia diagonal and orientation back into a singular matrix.
            /// This can be useful when combining inertia tensors.
            /// </summary>
            public float3x3 ToMatrix() => TrueSim.InertiaTensorFrom(tensorOrientation, inertiaDiagonal);

            /// <summary>
            /// Computes an approximated LocalInertiaTensorDiagonal from the inertia tensor matrix.
            /// This uses a numeric solver with a hardcoded max of 10 iterations.
            /// </summary>
            public static LocalInertiaTensorDiagonal ApproximateFrom(float3x3 inertiaTensor)
            {
                mathex.DiagonalizeSymmetricApproximation(inertiaTensor, out float3x3 orientation, out float3 diagonal);
                return new LocalInertiaTensorDiagonal { tensorOrientation = new quaternion(orientation), inertiaDiagonal = diagonal };
            }
        }

        /// <summary>
        /// Computes the Mass properties and inertialPoseWorldTransform from the results
        /// of calls to LocalCenterOfMassFrom() and LocalInertiaTensorFrom() transformed
        /// by the worldTransform.
        /// </summary>
        /// <param name="worldTransform">The world transform of the body entity</param>
        /// <param name="localInertiaTensorDiagonal">The local inertia tensor diagonal, already stretched by the worldTransform stretch value</param>
        /// <param name="localCenterOfMassUnscaled">The center of mass relative to the body entity in the body entity's local space</param>
        /// <param name="inverseMass">The reciprocal of the mass, where 0 makes the object immovable</param>
        /// <param name="massOut">The Mass containing the inverse mass and inverse inertia</param>
        /// <param name="inertialPoseWorldTransform">A world transform centered around the body's center of mass and oriented relative to the inertia tensor diagonal</param>
        public static void ConvertToWorldMassInertia(in TransformQvvs worldTransform,
                                                     in LocalInertiaTensorDiagonal localInertiaTensorDiagonal,
                                                     float3 localCenterOfMassUnscaled,
                                                     float inverseMass,
                                                     out Mass massOut,
                                                     out RigidTransform inertialPoseWorldTransform)
        {
            inertialPoseWorldTransform = new RigidTransform(qvvs.TransformRotation(in worldTransform, localInertiaTensorDiagonal.tensorOrientation),
                                                            qvvs.TransformPoint(in worldTransform, localCenterOfMassUnscaled * worldTransform.scale * worldTransform.stretch));
            massOut = new Mass
            {
                inverseMass    = inverseMass,
                inverseInertia = math.rcp(localInertiaTensorDiagonal.inertiaDiagonal) * inverseMass / (worldTransform.scale * worldTransform.scale)
            };
        }

        /// <summary>
        /// Gets the default center of mass computed for the type of collider, in the collider's local space
        /// </summary>
        public static float3 LocalCenterOfMassFrom(in Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return collider.m_sphere.center;
                case ColliderType.Capsule:
                    return (collider.m_capsule.pointA + collider.m_capsule.pointB) / 2f;
                case ColliderType.Box:
                    return collider.m_box.center;
                case ColliderType.Triangle:
                    return (collider.m_triangle.pointA + collider.m_triangle.pointB + collider.m_triangle.pointC) / 3f;
                case ColliderType.Convex:
                    return collider.m_convex.scale * collider.m_convex.convexColliderBlob.Value.centerOfMass;
                case ColliderType.TriMesh:
                    return (collider.m_triMesh().triMeshColliderBlob.Value.localAabb.min + collider.m_triMesh().triMeshColliderBlob.Value.localAabb.max) / 2f;
                case ColliderType.Compound:
                    return collider.m_compound().scale * collider.m_compound().compoundColliderBlob.Value.centerOfMass;
                default: return default;
            }
        }

        /// <summary>
        /// Gets the default local center-of-mass-relative-space inertia tensor diagonal from the collider,
        /// after stretching the collider. This method can be somewhat expensive, especially for blob-based
        /// collider types when scaled, though this cost is independent of the blob size.
        /// </summary>
        /// <param name="collider">The collider to compute the local inertia tensor from</param>
        /// <param name="stretch">How much the collider should be stretched by. Use 1f for no stretching.</param>
        /// <returns>A local space inertia tensor matrix diagonal and orientation</returns>
        public static LocalInertiaTensorDiagonal LocalInertiaTensorFrom(in Collider collider, float3 stretch)
        {
            if (stretch.Equals(new float3(1f)))
                return LocalInertiaTensorFrom(in collider);
            var scaled = Physics.ScaleStretchCollider(collider, 1f, stretch);
            return LocalInertiaTensorFrom(in scaled);
        }

        /// <summary>
        /// Gets the default angular expansion factor of the collider, used in MotionExpansion
        /// </summary>
        public static float AngularExpansionFactorFrom(in Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return 0f;
                case ColliderType.Capsule:
                    return math.distance(collider.m_capsule.pointA, collider.m_capsule.pointB) / 2f;
                case ColliderType.Box:
                    return math.length(collider.m_box.halfSize);
                case ColliderType.Triangle:
                {
                    var center     = (collider.m_triangle.pointA + collider.m_triangle.pointB + collider.m_triangle.pointC) / 3f;
                    var distanceSq = math.cmax(simd.distancesq(collider.m_triangle.AsSimdFloat3(), center));
                    return math.sqrt(distanceSq);
                }
                case ColliderType.Convex:
                {
                    ref var blob      = ref collider.m_convex.convexColliderBlob.Value;
                    var     aabb      = blob.localAabb;
                    aabb.min         *= collider.m_convex.scale;
                    aabb.max         *= collider.m_convex.scale;
                    var centerOfMass  = blob.centerOfMass;
                    Physics.GetCenterExtents(aabb, out var aabbCenter, out var aabbExtents);
                    var delta        = centerOfMass - aabbCenter;
                    var extremePoint = math.select(aabbExtents, -aabbExtents, delta >= 0f);
                    return math.distance(extremePoint, delta);
                }
                case ColliderType.TriMesh:
                {
                    var aabb  = collider.m_triMesh().triMeshColliderBlob.Value.localAabb;
                    aabb.min *= collider.m_triMesh().scale;
                    aabb.max *= collider.m_triMesh().scale;
                    Physics.GetCenterExtents(aabb, out _, out var extents);
                    return math.length(extents);
                }
                case ColliderType.Compound:
                {
                    ref var blob      = ref collider.m_compound().compoundColliderBlob.Value;
                    var     aabb      = blob.localAabb;
                    aabb.min         *= collider.m_compound().scale;
                    aabb.max         *= collider.m_compound().scale;
                    var centerOfMass  = blob.centerOfMass;
                    Physics.GetCenterExtents(aabb, out var aabbCenter, out var aabbExtents);
                    var delta        = centerOfMass - aabbCenter;
                    var extremePoint = math.select(aabbExtents, -aabbExtents, delta >= 0f);
                    return math.distance(extremePoint, delta);
                }
                default: return default;
            }
        }

        static LocalInertiaTensorDiagonal LocalInertiaTensorFrom(in Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return new LocalInertiaTensorDiagonal
                    {
                        inertiaDiagonal   = TrueSim.DiagonalizedGyrationTensorOfSphere(collider.m_sphere.radius),
                        tensorOrientation = quaternion.identity
                    };
                case ColliderType.Capsule:
                {
                    float3 axis     = collider.m_capsule.pointB - collider.m_capsule.pointA;
                    float  lengthSq = math.lengthsq(axis);
                    float  length   = math.sqrt(lengthSq);
                    if (lengthSq == 0f || !math.isfinite(length))
                    {
                        new LocalInertiaTensorDiagonal
                        {
                            inertiaDiagonal   = TrueSim.DiagonalizedGyrationTensorOfSphere(collider.m_capsule.radius),
                            tensorOrientation = quaternion.identity
                        };
                    }

                    TrueSim.DiagonalizedGyrationTensorOfCapsule(length, collider.m_capsule.radius, out var onAxisInertia, out var offAxisInertia);

                    return new LocalInertiaTensorDiagonal
                    {
                        inertiaDiagonal   = new float3(offAxisInertia, onAxisInertia, offAxisInertia),
                        tensorOrientation = mathex.FromToRotation(new float3(0f, 1f, 0f), math.normalize(axis))
                    };
                }
                case ColliderType.Box:
                {
                    return new LocalInertiaTensorDiagonal
                    {
                        inertiaDiagonal   = TrueSim.DiagonalizedGyrationTensorOfBox(collider.m_box.halfSize),
                        tensorOrientation = quaternion.identity
                    };
                }
                case ColliderType.Triangle:
                {
                    // Note: Unity Physics uses inertia tensor of sphere here.
                    var center     = (collider.m_triangle.pointA + collider.m_triangle.pointB + collider.m_triangle.pointC) / 3f;
                    var distanceSq = math.cmax(simd.distancesq(collider.m_triangle.AsSimdFloat3(), center));
                    return new LocalInertiaTensorDiagonal
                    {
                        inertiaDiagonal   = (2f / 5f) * distanceSq,
                        tensorOrientation = quaternion.identity
                    };
                }
                case ColliderType.Convex:
                {
                    ref var blob  = ref collider.m_convex.convexColliderBlob.Value;
                    var     scale = collider.m_convex.scale;
                    if (scale.Equals(new float3(1f, 1f, 1f)))
                    {
                        // Fast path
                        return new LocalInertiaTensorDiagonal
                        {
                            inertiaDiagonal   = blob.unscaledInertiaTensorDiagonal,
                            tensorOrientation = blob.unscaledInertiaTensorOrientation
                        };
                    }
                    if (math.abs(math.cmax(collider.m_convex.scale) - math.cmin(collider.m_convex.scale)) < math.abs(math.cmax(collider.m_convex.scale)) * math.EPSILON)
                    {
                        // Kinda fast path
                        var absScale = math.abs(collider.m_convex.scale);
                        return new LocalInertiaTensorDiagonal
                        {
                            inertiaDiagonal   = blob.unscaledInertiaTensorDiagonal * absScale * absScale,
                            tensorOrientation = blob.unscaledInertiaTensorOrientation
                        };
                    }

                    // Todo: Is there a faster way to do this when we already have the unscaled orientation and diagonal?
                    var scaledTensor = TrueSim.StretchInertiaTensor(blob.inertiaTensor, scale);
                    return LocalInertiaTensorDiagonal.ApproximateFrom(scaledTensor);
                }
                case ColliderType.TriMesh:
                {
                    // Note: Unity Physics approximates this as a box formed from the AABB
                    var aabb  = collider.m_triMesh().triMeshColliderBlob.Value.localAabb;
                    aabb.min *= collider.m_triMesh().scale;
                    aabb.max *= collider.m_triMesh().scale;
                    Physics.GetCenterExtents(aabb, out _, out var extents);
                    return new LocalInertiaTensorDiagonal
                    {
                        inertiaDiagonal   = TrueSim.DiagonalizedGyrationTensorOfBox(extents),
                        tensorOrientation = quaternion.identity
                    };
                }
                case ColliderType.Compound:
                {
                    ref var blob  = ref collider.m_compound().compoundColliderBlob.Value;
                    var     scale = collider.m_compound().scale * collider.m_compound().stretch;
                    if (scale.Equals(new float3(1f, 1f, 1f)))
                    {
                        // Fast path
                        return new LocalInertiaTensorDiagonal
                        {
                            inertiaDiagonal   = blob.unscaledInertiaTensorDiagonal,
                            tensorOrientation = blob.unscaledInertiaTensorOrientation
                        };
                    }
                    if (collider.m_compound().stretch.Equals(new float3(1f, 1f, 1f)))
                    {
                        // Kinda fast path
                        var absScale = math.abs(collider.m_compound().scale);
                        return new LocalInertiaTensorDiagonal
                        {
                            inertiaDiagonal   = blob.unscaledInertiaTensorDiagonal * absScale * absScale,
                            tensorOrientation = blob.unscaledInertiaTensorOrientation
                        };
                    }

                    // Todo: Is there a faster way to do this when we already have the unscaled orientation and diagonal?
                    // Side note: This is an approximation because compound children don't necessarily stretch perfectly,
                    // whereas this is a true stretching of the inertia tensor.
                    var scaledTensor = TrueSim.StretchInertiaTensor(blob.inertiaTensor, scale);
                    return LocalInertiaTensorDiagonal.ApproximateFrom(scaledTensor);
                }
                default: return default;
            }
        }
    }
}

