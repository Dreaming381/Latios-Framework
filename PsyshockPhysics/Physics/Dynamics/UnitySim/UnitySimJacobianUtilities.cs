using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class UnitySim
    {
        // Integrate the relative orientation of a pair of bodies, faster and less memory than storing both bodies' orientations and integrating them separately
        static quaternion IntegrateOrientationBFromA(quaternion bFromA, float3 angularVelocityA, float3 angularVelocityB, float timestep)
        {
            var halfDeltaTime = timestep * 0.5f;
            var dqA           = new quaternion(new float4(angularVelocityA * halfDeltaTime, 1f));
            var invDqB        = new quaternion(new float4(angularVelocityB * -halfDeltaTime, 1f));
            return math.normalize(math.mul(math.mul(invDqB, bFromA), dqA));
        }

        // Calculate the inverse effective mass of a linear jacobian
        static float CalculateInvEffectiveMassDiag(float3 angA, float3 invInertiaA, float invMassA,
                                                   float3 angB, float3 invInertiaB, float invMassB)
        {
            float3 angularPart = angA * angA * invInertiaA + angB * angB * invInertiaB;
            float  linearPart  = invMassA + invMassB;
            return angularPart.x + angularPart.y + angularPart.z + linearPart;
        }

        // Calculate the inverse effective mass for a pair of jacobians with perpendicular linear parts
        static float CalculateInvEffectiveMassOffDiag(float3 angA0, float3 angA1, float3 invInertiaA,
                                                      float3 angB0, float3 angB1, float3 invInertiaB)
        {
            return math.csum(angA0 * angA1 * invInertiaA + angB0 * angB1 * invInertiaB);
        }

        // Inverts a symmetric 3x3 matrix with diag = (0, 0), (1, 1), (2, 2), offDiag = (0, 1), (0, 2), (1, 2) = (1, 0), (2, 0), (2, 1)
        static bool InvertSymmetricMatrix(float3 diag, float3 offDiag, out float3 invDiag, out float3 invOffDiag)
        {
            float3 offDiagSq      = offDiag.zyx * offDiag.zyx;
            float  determinant    = (mathex.cproduct(diag) + 2.0f * mathex.cproduct(offDiag) - math.csum(offDiagSq * diag));
            bool   determinantOk  = (determinant != 0);
            float  invDeterminant = math.select(0.0f, 1.0f / determinant, determinantOk);
            invDiag               = (diag.yxx * diag.zzy - offDiagSq) * invDeterminant;
            invOffDiag            = (offDiag.yxx * offDiag.zzy - diag.zyx * offDiag) * invDeterminant;
            return determinantOk;
        }

        // Returns x - clamp(x, min, max)
        static float CalculateError(float x, float min, float max)
        {
            float error = math.max(x - max, 0.0f);
            error       = math.min(x - min, error);
            return error;
        }

        // Returns the amount of error for the solver to correct, where initialError is the pre-integration error and predictedError is the expected post-integration error
        // If (predicted > initial) HAVE overshot target = (Predicted - initial)*damping + initial*tau
        // If (predicted < initial) HAVE NOT met target = predicted * tau (ie: damping not used if target not met)
        static float CalculateCorrection(float predictedError, float initialError, float tau, float damping)
        {
            return math.max(predictedError - initialError, 0.0f) * damping + math.min(predictedError, initialError) * tau;
        }

        /// <summary>
        /// Returns the twist angle of the swing-twist decomposition of q about i, j, or k corresponding
        /// to index = 0, 1, or 2 respectively. Full calculation for readability:
        ///      float invLength = RSqrtSafe(dot * dot + w * w);
        ///      float sinHalfAngle = dot * invLength;
        ///      float cosHalfAngle = w * invLength;
        /// Observe: invLength cancels in the tan^-1(sin / cos) calc, so avoid unnecessary calculations.
        /// </summary>
        ///
        /// <param name="q">                A quaternion to process. </param>
        /// <param name="twistAxisIndex">   Zero-based index of the twist axis. </param>
        ///
        /// <returns>   The calculated twist angle. </returns>
        static float CalculateTwistAngle(quaternion q, int twistAxisIndex)
        {
            // q = swing * twist, twist = normalize(twistAxis * twistAxis dot q.xyz, q.w)
            float dot       = q.value[twistAxisIndex];
            float w         = q.value.w;
            float halfAngle = math.atan2(dot, w);
            return halfAngle + halfAngle;
        }

        static float RSqrtSafe(float v) => math.select(math.rsqrt(v), 0.0f, math.abs(v) < 1e-10f);
    }
}

