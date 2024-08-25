using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// Warning: This class and all of its contents are still in a highly experimental status.
    /// Feel free to play with them and report bugs, but also be aware that catastropic bugs
    /// are expected.
    ///
    /// This class provides simulation types and methods based on Unity Physics.
    /// </summary>
    public static partial class UnitySim
    {
        /// <summary>
        /// Computes up to 32 contact points between the colliders based on the ColliderDistanceResult.
        /// For composite collider types, this only computes contacts for the subcolliders involved.
        /// The colliders and transforms must match those which were used to generate the ColliderDistanceResult.
        /// The relative order of the colliders must match what was used in the call to DistanceBetween()
        /// or DistanceBetweenAll(), or else distanceResult must be flipped to compensate.
        /// Contacts do not necessarily indicate areas of overlap, and can still be generated even if the
        /// colliders are far apart.
        /// </summary>
        /// <param name="colliderA">The first collider for which contacts should be generated</param>
        /// <param name="transformA">The transform of the first collider</param>
        /// <param name="colliderB">The second collider for which contacts should be generated</param>
        /// <param name="transformB">The transform of the second collider</param>
        /// <param name="distanceResult">The result from DistanceBetween() or DistanceBetweenAll() between
        /// these colliders and transforms</param>
        /// <returns>Up to 32 contact points between the two colliders</returns>
        public static ContactsBetweenResult ContactsBetween(in Collider colliderA,
                                                            in TransformQvvs transformA,
                                                            in Collider colliderB,
                                                            in TransformQvvs transformB,
                                                            in ColliderDistanceResult distanceResult)
        {
            var scaledColliderA = colliderA;
            var scaledColliderB = colliderB;

            Physics.ScaleStretchCollider(ref scaledColliderA, transformA.scale, transformA.stretch);
            Physics.ScaleStretchCollider(ref scaledColliderB, transformB.scale, transformB.stretch);
            return ColliderColliderDispatch.UnityContactsBetween(in scaledColliderA,
                                                                 new RigidTransform(transformA.rotation, transformA.position),
                                                                 in scaledColliderB,
                                                                 new RigidTransform(transformB.rotation, transformB.position),
                                                                 in distanceResult);
        }

        /// <summary>
        /// Data structure for storing up to 32 contact points.
        /// Contacts by default are stored relative to "B" in a collision pair.
        /// Multiply the contact distance by the contactNormal to get a vector
        /// from the contact point on B to the contact point on A.
        /// </summary>
        public unsafe struct ContactsBetweenResult
        {
            /// <summary>
            /// A vector that points from a non-penetrating contact on collider "B"
            /// to the surface on collider "A". The normal should always be outward
            /// from "B".
            /// </summary>
            public float3 contactNormal;
            /// <summary>
            /// The number of contacts contained
            /// </summary>
            public int contactCount;
            /// <summary>
            /// The raw memory storage of the contacts
            /// </summary>
            public fixed float contactsData[128];

            /// <summary>
            /// Retrieves the contact at the specified index
            /// </summary>
            public ref ContactOnB this[int index]
            {
                get
                {
                    CheckInRange(index);
                    fixed(void* ptr = contactsData)
                    return ref ((ContactOnB*)ptr)[index];
                }
            }

            /// <summary>
            /// Adds a new contact point. The safety check will fail if there are already 32 contacts.
            /// </summary>
            public void Add(ContactOnB contact)
            {
                CheckCapacityBeforeAdd();
                var index = contactCount;
                contactCount++;
                this[index] = contact;
            }

            /// <summary>
            /// Adds a new contact point. The safety check will fail if there are already 32 contacts.
            /// </summary>
            public void Add(float3 locationOnB, float distanceToA)
            {
                Add(new ContactOnB { location = locationOnB, distanceToA = distanceToA });
            }

            /// <summary>
            /// Removes the contact at the specified index, and the contact at the highest index will
            /// take its place (as long as that is not the contact being removed).
            /// </summary>
            public void RemoveAtSwapBack(int index)
            {
                CheckInRange(index);
                this[index] = this[contactCount - 1];
                contactCount--;
            }

            /// <summary>
            /// Flips the perspective of the contacts such that they lie on "A"
            /// with the contact normal pointed in the opposite direction.
            /// </summary>
            public void FlipInPlace()
            {
                for (int i = 0; i < contactCount; i++)
                {
                    var contact       = this[i];
                    contact.location += contact.distanceToA * contactNormal;
                    this[i]           = contact;
                }
                contactNormal = -contactNormal;
            }

            /// <summary>
            /// Creates a flipped perspective of the contacts such that they lie on "A"
            /// with the contact normal pointed in the opposite direction.
            /// </summary>
            public ContactsBetweenResult ToFlipped()
            {
                var result = this;
                result.FlipInPlace();
                return result;
            }

            /// <summary>
            /// Acquires a span of the contacts.
            /// WARNING: This may only be called on an instance that lives on the stack or the unmanaged heap.
            /// The Span MUST only be used while this instance is valid.
            /// </summary>
            public Span<ContactOnB> AsSpan()
            {
                fixed (ContactOnB* ptr = &this[0])
                return new Span<ContactOnB>(ptr, contactCount);
            }

            /// <summary>
            /// A contact that lies on the "B" collider in a pair of colliders
            /// </summary>
            public struct ContactOnB
            {
                /// <summary>
                /// A packed representation of the contact
                /// </summary>
                public float4 contactData;
                /// <summary>
                /// The position of the contact on the surface of the "B" collider
                /// </summary>
                public float3 location
                {
                    get => contactData.xyz;
                    set => contactData.xyz = value;
                }
                /// <summary>
                /// The distance of the contact to the surface of the "A" collider
                /// along the contact normal. Negative if the contact is penetrating.
                /// </summary>
                public float distanceToA
                {
                    get => contactData.w;
                    set => contactData.w = value;
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckCapacityBeforeAdd()
            {
                if (contactCount >= 32)
                    throw new System.InvalidOperationException("Cannot add more than 32 contacts.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckInRange(int index)
            {
                if (index < 0 || index >= contactCount)
                    throw new System.ArgumentOutOfRangeException($"Contact index {index} is out of range of [0, {contactCount})");
            }
        }
    }
}

