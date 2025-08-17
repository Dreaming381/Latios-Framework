using System;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class TrueSim
    {
        public static float3x3 ScaleInertiaTensor(float3x3 original, float scale) => original * math.square(scale);

        public static float3x3 StretchInertiaTensor(float3x3 original, float3 stretch)
        {
            // The inertia tensor matrix diagonal components (not necessarily a diagonalized inertia tensor) are defined as follows:
            // diagonal.x = sum_1_k(mass_k * (y_k^2 + z_k^2)) = sum_1_k(mass_k * y_k^2) + sum_1_k(mass_k * z_k^2)
            // And for uniform density, m_k is constant, so:
            // diagonal.x = mass * sum_1_k(y_k^2) + sum_1_k(z_k^2)
            // diagonal.y = mass * sum_1_k(x_k^2) + sum_1_k(z_k^2)
            // diagonal.z = mass * sum_1_k(x_k^2) + sum_1_k(y_k^2)
            // The base inertia diagonal has mass divided out to be 1f, so we can drop it from our expression.
            //
            // We can define a property s as the sum of diagonals.
            // diagonal.x + diagonal.y + diagonal.z = sum_1_k(y_k^2) + sum_1_k(z_k^2) + sum_1_k(x_k^2) + sum_1_k(z_k^2) + sum_1_k(x_k^2) + sum_1_k(y_k^2)
            // diagonal.x + diagonal.y + diagonal.z = 2 * ( sum_1_k(x_k^2) + sum_1_k(y_k^2) + sum_1_k(z_k^2) )
            //
            // And with this, we can write this expression:
            // (diagonal.x + diagonal.y + diagonal.z) / 2 - diagonal.x = sum_1_k(x_k^2)
            // And we can do similar for the other two axes.
            //
            // Applying stretch changes the expression of sum_1_k(x_k^2) to sum_1_k( (x_k * stretch.x)^2 ) = sum_1_k(x_k^2 * stretch.x^2) = stretch.x^2 * sum_1_k(x_k^2)
            // And with that, we have all the data we need to reassemble the inertia tensor.
            var diagonal        = new float3(original.c0.x, original.c1.y, original.c2.z);
            var diagonalHalfSum = math.csum(diagonal) / 2f;
            var xSqySqzSq       = diagonalHalfSum - diagonal;
            var newDiagonal     = stretch * stretch * xSqySqzSq;

            // The off diagonals are just products, so we can actually just scale those.
            var scaleMatrix = new float3x3(new float3(0f, stretch.x * stretch.yz),
                                           new float3(stretch.x * stretch.y, 0f, stretch.x * stretch.z),
                                           new float3(stretch.z * stretch.xy, 0f));
            var result  = original * scaleMatrix;
            result.c0.x = newDiagonal.x;
            result.c1.y = newDiagonal.y;
            result.c2.z = newDiagonal.z;
            return result;
        }

        public static float3x3 RotateInertiaTensor(float3x3 original, quaternion rotation)
        {
            var rotMat        = new float3x3(rotation);
            var rotMatInverse = new float3x3(math.conjugate(rotation));
            return math.mul(rotMat, math.mul(original, rotMatInverse));
        }

        public static float3x3 TranslateInertiaTensor(float3x3 original, float3 translation)
        {
            float3 shift          = translation;
            float3 shiftSq        = shift * shift;
            var    diag           = new float3(shiftSq.y + shiftSq.z, shiftSq.x + shiftSq.z, shiftSq.x + shiftSq.y);
            var    offDiag        = new float3(shift.x * shift.y, shift.y * shift.z, shift.z * shift.x) * -1.0f;
            var    inertiaMatrix  = original;
            inertiaMatrix.c0     += new float3(diag.x, offDiag.x, offDiag.z);
            inertiaMatrix.c1     += new float3(offDiag.x, diag.y, offDiag.y);
            inertiaMatrix.c2     += new float3(offDiag.z, offDiag.y, diag.z);
            return inertiaMatrix;
        }

        public static float3x3 TransformInertiaTensor(float3x3 original, RigidTransform transform)
        {
            return TranslateInertiaTensor(RotateInertiaTensor(original, transform.rot), transform.pos);
        }

        public static float3x3 TransformInertiaTensor(float3x3 original, TransformQvvs transform)
        {
            float3x3 result = ScaleInertiaTensor(original, transform.scale);
            result          = StretchInertiaTensor(result, transform.stretch);
            result          = RotateInertiaTensor(result, transform.rotation);
            return TranslateInertiaTensor(result, transform.position);
        }

        /// <summary>
        /// Computes the composite mass-independent inertia tensor (gyration tensor) from the individual gyration tensors pre-transformed into the composite space.
        /// Negative masses are allowed to carve out holes, however the total mass sum must be greater than 0 to get a valid result.
        /// </summary>
        /// <param name="individualTransformedGyrationTensors">The individual gyration tensors that make up the composite, already transformed into composite space</param>
        /// <param name="individualMasses">The individual total masses of each object making up the composite</param>
        /// <returns>The gyration tensor of the overall compound</returns>
        public static float3x3 GyrationTensorFrom(ReadOnlySpan<float3x3> individualTransformedGyrationTensors, ReadOnlySpan<float> individualMasses)
        {
            float3x3 it = float3x3.zero;
            float    m  = 0f;
            for (int i = 0; i < individualTransformedGyrationTensors.Length; i++)
            {
                it += individualTransformedGyrationTensors[i] * m;
                m  += individualMasses[i];
            }
            return it / m;
        }

        /// <summary>
        /// Converts an inertia tensor in the optimized orientation and diagonal form back to a matrix.
        /// </summary>
        /// <param name="orientation">The orientation of the inertia tensor</param>
        /// <param name="diagonal">The diagonal values of the diagonal matrix prior to orientation</param>
        /// <returns>The inertia tensor matrix</returns>
        public static float3x3 InertiaTensorFrom(quaternion orientation, float3 diagonal)
        {
            var r  = new float3x3(orientation);
            var r2 = new float3x3(diagonal.x * r.c0, diagonal.y * r.c1, diagonal.z * r.c2);
            return math.mul(r2, math.transpose(r));
        }

        /// <summary>
        /// Converts a gyration tensor into an inertia tensor for an object with the specified mass
        /// </summary>
        public static float3x3 InertiaTensorFromGyrationTensor(float3x3 gyrationTensor, float mass) => gyrationTensor * mass;

        /// <summary>
        /// Converts an inertia tensor into a mass-independent gyration tensor for an object with the specified mass
        /// </summary>
        public static float3x3 GyrationTensorFromInertiaTensor(float3x3 inertiaTensor, float mass) => inertiaTensor / mass;

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density sphere in orientation-diagonal form.
        /// The sphere is assumed to have its center at the origin. Any valid quaternion (including identity) can be
        /// used for the orientation.
        /// </summary>
        /// <param name="radius">The radius of the sphere</param>
        /// <returns>The shared x, y, and z value of the diagonal of the gyration tensor.</returns>
        public static float DiagonalizedGyrationTensorOfSphere(float radius) => (2f / 5f) * radius * radius;

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a hollowed-out sphere in orientation-diagonal form.
        /// The sphere is assumed to have its center at the origin. Any valid quaternion (including identity) can be used for the orientation.
        /// All mass is assumed to exist exclusively on the surface of the sphere (uniform surface density)
        /// </summary>
        /// <param name="radius">The radius of the sphere</param>
        /// <returns>The shared x, y, and z value of the diagonal of the gyration tensor.</returns>
        public static float DiagonalizedGyrationTensorOfHollowSphere(float radius) => (2f / 3f) * radius * radius;

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density capsule in orientation-diagonal form.
        /// The capsule is assumed to be centered at the origin, such that the origin is at the midpoint of the central axis.
        /// The orientation is identity if the capsule's central axis aligned with one of the primary axes (x, y, or z).
        /// A non-identity orientation represents a central axis that has been rotated away from alignment with the primary
        /// axis of choice.
        /// For a vertical capsule, you would assign the orientation to quaternion.identity, and assign the diagonal as
        /// new float3(axisPerpendicularDiagonalValue, axisAlignedDiagonalValue, axisPerpendicularDiagonalValue)
        /// </summary>
        /// <param name="axisLength">The length of the interior axis. The extreme cap points of the capsule are this axisLength + 2 * radius apart.</param>
        /// <param name="radius">The radius of the capsule</param>
        /// <param name="axisAlignedDiagonalValue">The value to assign to the diagonal component corresponding to the primary axis the central axis is aligned with.</param>
        /// <param name="axisPerpendicularDiagonalValue">The value to assign to the other two diagonal components.</param>
        public static void DiagonalizedGyrationTensorOfCapsule(float axisLength, float radius, out float axisAlignedDiagonalValue, out float axisPerpendicularDiagonalValue)
        {
            float radiusSq = radius * radius;
            float lengthSq = axisLength * axisLength;

            float cylinderMassPart  = math.PI * axisLength * radiusSq;
            float sphereMassPart    = math.PI * (4f / 3f) * radiusSq * radius;
            float totalMass         = cylinderMassPart + sphereMassPart;
            cylinderMassPart       /= totalMass;
            sphereMassPart         /= totalMass;

            axisAlignedDiagonalValue       = (cylinderMassPart / 2f + sphereMassPart * 2f / 5f) * radiusSq;
            axisPerpendicularDiagonalValue = cylinderMassPart * (radiusSq / 4f + lengthSq / 12f) +
                                             sphereMassPart * (radiusSq * 2f / 5f + radius * axisLength * 3f / 8f + lengthSq / 4f);
        }

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density cylinder in orientation-diagonal form.
        /// The cylinder is assumed to be centered at the origin, such that the origin is at the midpoint of the central axis.
        /// The orientation is identity if the cylinder's central axis aligned with one of the primary axes (x, y, or z).
        /// A non-identity orientation represents a central axis that has been rotated away from alignment with the primary
        /// axis of choice.
        /// For a vertical cylinder, you would assign the orientation to quaternion.identity, and assign the diagonal as
        /// new float3(axisPerpendicularDiagonalValue, axisAlignedDiagonalValue, axisPerpendicularDiagonalValue)
        /// </summary>
        /// <param name="axisLength">The length of the interior axis, sometimes referred to as the cylinder's height.</param>
        /// <param name="radius">The radius of the cylinder</param>
        /// <param name="axisAlignedDiagonalValue">The value to assign to the diagonal component corresponding to the primary axis the central axis is aligned with.</param>
        /// <param name="axisPerpendicularDiagonalValue">The value to assign to the other two diagonal components.</param>
        public static void DiagonalizedGyrationTensorOfCylinder(float axisLength, float radius, out float axisAlignedDiagonalValue, out float axisPerpendicularDiagonalValue)
        {
            float radiusSq = radius * radius;
            float lengthSq = axisLength * axisLength;

            axisAlignedDiagonalValue       = 0.5f * radiusSq;
            axisPerpendicularDiagonalValue = radiusSq / 4f + lengthSq / 12f;
        }

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density cone in orientation-diagonal form.
        /// The cone is assumed to have its apex placed at the origin. The base is away from the origin.
        /// The orientation is identity if the cone's central axis aligned with one of the primary axes (x, y, or z).
        /// A non-identity orientation represents a central axis that has been rotated away from alignment with the primary
        /// axis of choice.
        /// For a cone oriented like a spinning top, you would assign the orientation to quaternion.identity, and assign the diagonal as
        /// new float3(axisPerpendicularDiagonalValue, axisAlignedDiagonalValue, axisPerpendicularDiagonalValue)
        /// </summary>
        /// <param name="axisLength">The length of the interior axis, sometimes referred to as the cone's height.</param>
        /// <param name="baseRadius">The radius of the cone's base</param>
        /// <param name="axisAlignedDiagonalValue">The value to assign to the diagonal component corresponding to the primary axis the central axis is aligned with.</param>
        /// <param name="axisPerpendicularDiagonalValue">The value to assign to the other two diagonal components.</param>
        public static void DiagonalizedGyrationTensorOfCone(float axisLength, float baseRadius, out float axisAlignedDiagonalValue, out float axisPerpendicularDiagonalValue)
        {
            float radiusSq = baseRadius * baseRadius;
            float lengthSq = axisLength * axisLength;

            axisAlignedDiagonalValue       = (3f / 10f) * radiusSq;
            axisPerpendicularDiagonalValue = (3f / 20f) * radiusSq + (3f / 5f) * lengthSq;
        }

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density box in orentation-diagonal form.
        /// The box is assumed to be centered at the origin. The orientation is identity if the box's local axes
        /// align to the primary axes (x, y, and z). A non-identity orientation represents the box rotated away
        /// from alignment of the primary axes.
        /// </summary>
        /// <param name="halfSizes">The positive distances from the box center to each side of the box</param>
        /// <returns>The inertia tensor diagonal of the box</returns>
        public static float3 DiagonalizedGyrationTensorOfBox(float3 halfSizes)
        {
            var halfSq = halfSizes * halfSizes;
            return new float3(halfSq.y + halfSq.z, halfSq.x + halfSq.z, halfSq.x + halfSq.y) / 3f;
        }

        /// <summary>
        /// Computes the gyration tensor (mass-independent inertia tensor) of a uniform density ellipsoid in orentation-diagonal form.
        /// The ellipsoid is assumed to be centered at the origin. The orientation is identity if the ellipsoid's local axes
        /// align to the primary axes (x, y, and z). A non-identity orientation represents the ellipsoid rotated away
        /// from alignment of the primary axes.
        /// </summary>
        /// <param name="semiAxes">The positive distances from the ellipoid center to the surface along each axis</param>
        /// <returns>The inertia tensor diagonal of the ellipsoid</returns>
        public static float3 DiagonalizedGyrationTensorOfEllipsoid(float3 semiAxes)
        {
            var halfSq = semiAxes * semiAxes;
            return new float3(halfSq.y + halfSq.z, halfSq.x + halfSq.z, halfSq.x + halfSq.y) / 5f;
        }
    }
}

