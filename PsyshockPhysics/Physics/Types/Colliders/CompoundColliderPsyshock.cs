using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A uniformly scaled collection of colliders and their relative transforms.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct CompoundCollider
    {
        /// <summary>
        /// Defines how transform stretch values should morph the shape, since this shape cannot be perfectly stretched in an efficient manner
        /// </summary>
        public enum StretchMode : byte
        {
            /// <summary>
            /// Rotates the stretch value to each subcollider's local space, then stretches the local collider.
            /// This works especially well when the subcollider is rotated in increments of 90 degrees about each axis.
            /// However, other rotations may lead to less intuitive behavior.
            /// </summary>
            RotateStretchLocally = 0,
            /// <summary>
            /// Ignore the stretch of the transform entirely
            /// </summary>
            IgnoreStretch = 1,
            /// <summary>
            /// Only offset the positions of the subcolliders, as if they were children of the entity containing the collider
            /// </summary>
            StretchPositionsOnly = 2
        }

        /// <summary>
        /// The blob asset containing the subcolliders of this compound collider
        /// </summary>
        [FieldOffset(24)] public BlobAssetReference<CompoundColliderBlob> compoundColliderBlob;
        /// <summary>
        /// The scale of the collider that should be propagated to all subcolliders. It is applied in addition to any scaling from a transform.
        /// </summary>
        [FieldOffset(0)] public float scale;
        /// <summary>
        /// The stretch of the collider that should be propagated to all subcolliders. It is applied in addition to any stretching from a transform.
        /// </summary>
        [FieldOffset(4)] public float3 stretch;
        /// <summary>
        /// The stretch mode which specifies how subcolliders should respond to stretch values
        /// </summary>
        [FieldOffset(16)] public StretchMode stretchMode;

        /// <summary>
        /// Constructs a new CompoundCollider
        /// </summary>
        /// <param name="compoundColliderBlob">The blob asset containing the subcolliders of this compound collider</param>
        /// <param name="scale">The scale of the collider that should be propagated to all subcolliders. It is applied in addition to any scaling from a transform.</param>
        /// <param name="stretchMode">The stretch mode which specifies how subcolliders should respond to stretch values</param>
        public CompoundCollider(BlobAssetReference<CompoundColliderBlob> compoundColliderBlob, float scale = 1f, StretchMode stretchMode = StretchMode.RotateStretchLocally)
        {
            this.compoundColliderBlob = compoundColliderBlob;
            this.scale                = scale;
            this.stretch              = 1f;
            this.stretchMode          = stretchMode;
        }

        /// <summary>
        /// Constructs a new CompoundCollider
        /// </summary>
        /// <param name="compoundColliderBlob">The blob asset containing the subcolliders of this compound collider</param>
        /// <param name="scale">The scale of the collider that should be propagated to all subcolliders. It is applied in addition to any scaling from a transform.</param>
        /// <param name="stretch">The stretch of the collider that should be propagated to all subcolliders. It is applied in addition to any stretching from a transform.</param>
        /// <param name="stretchMode">The stretch mode which specifies how subcolliders should respond to stretch values</param>
        public CompoundCollider(BlobAssetReference<CompoundColliderBlob> compoundColliderBlob,
                                float scale,
                                float3 stretch,
                                StretchMode stretchMode = StretchMode.RotateStretchLocally)
        {
            this.compoundColliderBlob = compoundColliderBlob;
            this.scale                = scale;
            this.stretch              = stretch;
            this.stretchMode          = stretchMode;
        }

        internal void GetScaledStretchedSubCollider(int index, out Collider blobCollider, out RigidTransform blobTransform)
        {
            ref var blob = ref compoundColliderBlob.Value;
            if (math.all(new float4(stretch, scale) == 1f))
            {
                blobTransform = blob.transforms[index];
                blobCollider  = blob.colliders[index];
                return;
            }

            switch (stretchMode)
            {
                case StretchMode.RotateStretchLocally:
                {
                    blobTransform      = blob.transforms[index];
                    blobTransform.pos *= scale * stretch;
                    var localStretch   = math.InverseRotateFast(blobTransform.rot, stretch);
                    blobCollider       = blob.colliders[index];
                    Physics.ScaleStretchCollider(ref blobCollider, scale, localStretch);
                    break;
                }
                case StretchMode.IgnoreStretch:
                {
                    blobTransform      = blob.transforms[index];
                    blobTransform.pos *= scale;
                    blobCollider       = blob.colliders[index];
                    Physics.ScaleStretchCollider(ref blobCollider, scale, 1f);
                    break;
                }
                case StretchMode.StretchPositionsOnly:
                {
                    blobTransform      = blob.transforms[index];
                    blobTransform.pos *= scale;
                    blobCollider       = blob.colliders[index];
                    Physics.ScaleStretchCollider(ref blobCollider, scale, 1f);
                    break;
                }
                default:
                {
                    blobTransform = default;
                    blobCollider  = default;
                    break;
                }
            }
        }
    }

    internal struct BlobCollider
    {
#pragma warning disable CS0649
        internal float3x4 storage;
#pragma warning restore CS0649
    }

    //Todo: Use some acceleration structure in a future version
    /// <summary>
    /// A blob asset composed of a collection of subcolliders and their transforms in a unified coordinate space
    /// </summary>
    public struct CompoundColliderBlob
    {
        internal BlobArray<BlobCollider> blobColliders;

        /// <summary>
        /// The array of transforms of subcolliders
        /// </summary>
        public BlobArray<RigidTransform> transforms;
        /// <summary>
        /// A local space Aabb encompassing all of the subcolliders
        /// </summary>
        public Aabb localAabb;

        public float3     centerOfMass;
        public float3x3   inertiaTensor;
        public quaternion unscaledInertiaTensorOrientation;
        public float3     unscaledInertiaTensorDiagonal;

        /// <summary>
        /// The array of subcolliders
        /// </summary>
        public unsafe ref BlobArray<Collider> colliders => ref UnsafeUtility.AsRef<BlobArray<Collider> >(UnsafeUtility.AddressOf(ref blobColliders));
        //ref UnsafeUtility.As<BlobArray<BlobCollider>, BlobArray<Collider>>(ref blobColliders);
    }
}

