using System;
using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class LatiosSim
    {
        /// <summary>
        /// A contact point pair, represented by a point on the A collider, and a distance to the corresponding point on the B collider
        /// along the contact normal.
        /// </summary>
        public struct Contact
        {
            /// <summary>
            /// The point on the A collider, typically in world-space
            /// </summary>
            public float3 contactOnA;
            /// <summary>
            /// The distance to the point on the B collider along the contact normal
            /// </summary>
            public float distanceToB;

            /// <summary>
            /// Compute the contact point on the B collider based on this contact pair and the contact normal
            /// </summary>
            public float3 ContactPointOnBFrom(float3 contactNormal) => contactOnA + distanceToB * contactNormal;
        }

        /// <summary>
        /// Compute a general-purpose contact normal from the ColliderDistanceResult. You do not need to stick to this result.
        /// It is simply a good default without any application-specific context.
        /// </summary>
        /// <param name="distanceResult">A ColliderDistanceResult from which a contact manifold may be generated from</param>
        /// <returns>A contact normal outward from the A collider</returns>
        public static float3 ContactNormalFrom(in ColliderDistanceResult distanceResult)
        {
            var  abNormal     = math.normalizesafe(distanceResult.hitpointB - distanceResult.hitpointA, float3.zero);
            bool bad          = abNormal.Equals(float3.zero);
            var  signedNormal = math.chgsign(abNormal, distanceResult.distance);
            if (Hint.Likely(!bad && math.dot(signedNormal, distanceResult.normalA) >= -math.EPSILON && math.dot(signedNormal, distanceResult.normalB) <= math.EPSILON))
            {
                // If the contact normal is within 90 degrees of the normalA, and 90 degrees of -normalB, with a little bit of fudge, then it is probably reasonable.
                // This check catches wild normals that come from nearly-touching closest points.
                return signedNormal;
            }

            // We have essentially touching closest points. Use the normal of whichever feature is highest. Or prefer Bs if they are somehow equal.
            return distanceResult.featureCodeA > distanceResult.featureCodeB ? distanceResult.normalA : -distanceResult.normalB;
        }

        /// <summary>
        /// Computes contact points between the colliders up to contactsOutput.Length contacts, and stores them in contactsOutput.
        /// </summary>
        /// <param name="contactsOutput">The output buffer to store the contacts. This must be sized to the max number of contacts desired.</param>
        /// <param name="contactNormal">The contact normal to use for finding the contacts</param>
        /// <param name="colliderA">The first collider for which contacts should be generated</param>
        /// <param name="transformA">The transform of the first collider</param>
        /// <param name="colliderB">The second collider for which contacts should be generated</param>
        /// <param name="transformB">The transform of the second collider</param>
        /// <param name="distanceResult">The result from DistanceBetween() or DistanceBetweenAll() between these colliders and transforms</param>
        /// <returns>The number of contacts found</returns>
        // Todo: Make public once ready
        internal static int ContactsBetween(Span<Contact> contactsOutput, float3 contactNormal,
                                          in Collider colliderA, in TransformQvvs transformA, in Collider colliderB, in TransformQvvs transformB,
                                          in ColliderDistanceResult distanceResult)
        {
            throw new NotImplementedException();
        }
    }

    public static class LatiosSimExtensionMethods
    {
        /// <summary>
        /// Flips a LatiosSim contact manifold
        /// </summary>
        /// <param name="contacts">The span of contacts</param>
        /// <param name="contactNormal">The contact normal</param>
        public static void Flip(this Span<LatiosSim.Contact> contacts, ref float3 contactNormal)
        {
            for (int i = 0; i < contacts.Length; i++)
            {
                contacts[i].contactOnA = contacts[i].ContactPointOnBFrom(contactNormal);
            }
            contactNormal = -contactNormal;
        }
    }
}

