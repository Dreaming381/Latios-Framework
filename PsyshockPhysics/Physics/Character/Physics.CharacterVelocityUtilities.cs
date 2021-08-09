using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        /// <summary>
        /// Applies a velocity step for a single axis given an input which drives both the target velocity and acceleration strength
        /// </summary>
        /// <param name="inputAxis">A value in the range [-1f, 1f] which represent an input value along an axis</param>
        /// <param name="currentVelocity">The current signed velocity along an axis</param>
        /// <param name="positiveMaxAcceleration">The maximum acceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveMaxDeceleration">The maximum deceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveSoftMaxSpeed">The desired speed when passed in an inputAxis value of 1f. If the currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="negativeMaxAcceleration">The maximum acceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeMaxDeceleration">The maximum deceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeSoftMaxSpeed">The desired speed when passed in an inputAxis value of -1f. If the -currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="deltaTime">The time step over which to apply the velocity update.</param>
        /// <returns>A new signed velocity value for the axis</returns>
        /// <remarks>
        /// This algorithm provides a uniquely responsive feel for mapping input to an object's velocity using acceleration constraints.
        /// It linearly interpolates the desiredSpeed between zero and the maxSpeed in either the positive or negative domain.
        /// It also attenuates the acceleration by the input. This means that with an initial velocity of 0f, applying an input of 0.5f
        /// will reach half the max speed in the same amount of time applying an input of 1f would reach max speed.
        /// However, deceleration is always clamped to [0.5f, 1f] multiplied by the max deceleration. This gives a snappier release.
        /// A neutral input of 0f will always apply 0.5f deceleration.
        ///
        /// Another feature of this function is that the speed arguments are "soft" meaning that if the currentVelocity exceeds them,
        /// the velocity will not be clamped but instead decelerate to that speed value. This allows all speed and acceleration values
        /// to be "dynamic", useful for turbo mechanics, control reduction in the air, ect.
        /// </remarks>
        public static float StepVelocityWithInput(float inputAxis,
                                                  float currentVelocity,
                                                  float positiveMaxAcceleration,
                                                  float positiveMaxDeceleration,
                                                  float positiveSoftMaxSpeed,
                                                  float negativeMaxAcceleration,
                                                  float negativeMaxDeceleration,
                                                  float negativeSoftMaxSpeed,
                                                  float deltaTime)
        {
            var input     = math.clamp(inputAxis, -1f, 1f);
            var pAccel    = positiveMaxAcceleration;
            var pDecel    = positiveMaxDeceleration;
            var pMaxSpeed = positiveSoftMaxSpeed;
            var nAccel    = negativeMaxAcceleration;
            var nDecel    = negativeMaxDeceleration;
            var nMaxSpeed = negativeSoftMaxSpeed;

            var pInput = math.saturate(input);
            var nInput = math.saturate(-input);

            // Solve for positive first
            // This is the speed we want based on our input
            var desiredSpeed = pMaxSpeed * pInput;
            // We also multiply the input to the acceleration to get better easing behavior (this is a "feel" preference)
            var accel = pInput * pAccel;
            // If our current velocity exceeds our desired speed, apply half the deceleration
            accel = math.select(accel, -pDecel * 0.5f, currentVelocity > desiredSpeed);
            // If a negative input is applied, apply up to the remainder of the deceleration
            var decel = nInput * -pDecel * 0.5f;
            // Combine the acceleration and deceleration
            var a = accel + decel;
            // Step the simulation
            var pNewVelocity = currentVelocity + a * deltaTime;
            // If we "cross over" the desired speed, then we fix ourselves to the desired speed.
            pNewVelocity = math.select(pNewVelocity, math.min(pNewVelocity, desiredSpeed), a >= 0f);
            pNewVelocity = math.select(pNewVelocity, math.max(pNewVelocity, desiredSpeed), a < 0f);

            // Now solve for the negative
            desiredSpeed     = nMaxSpeed * nInput;
            accel            = nInput * nAccel;
            accel            = math.select(accel, -nDecel * 0.5f, -currentVelocity > desiredSpeed);
            decel            = pInput * -nDecel * 0.5f;
            a                = accel + decel;
            var nNewVelocity = currentVelocity - a * deltaTime;
            nNewVelocity     = math.select(nNewVelocity, math.max(nNewVelocity, -desiredSpeed), a >= 0f);
            nNewVelocity     = math.select(nNewVelocity, math.min(nNewVelocity, -desiredSpeed), a < 0f);

            // Pick the better option
            var useNegative  = currentVelocity < 0f;
            useNegative     |= currentVelocity == 0f & nInput > 0f;
            return math.select(pNewVelocity, nNewVelocity, useNegative);
        }

        /// <summary>
        /// Applies a velocity step for two axes given an input which drives both the target velocity and acceleration strength.
        /// This is the same as multiple calls to the single axis version for each axis.
        /// </summary>
        /// <param name="inputAxis">A value in the range [-1f, 1f] which represent an input value along each axis</param>
        /// <param name="currentVelocity">The current signed velocity along each axis</param>
        /// <param name="positiveMaxAcceleration">The maximum acceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveMaxDeceleration">The maximum deceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveSoftMaxSpeed">The desired speed when passed in an inputAxis value of 1f. If the currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="negativeMaxAcceleration">The maximum acceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeMaxDeceleration">The maximum deceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeSoftMaxSpeed">The desired speed when passed in an inputAxis value of -1f. If the -currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="deltaTime">The time step over which to apply the velocity update.</param>
        /// <returns>A new signed velocity value for the axis</returns>
        /// <remarks>
        /// This algorithm provides a uniquely responsive feel for mapping input to an object's velocity using acceleration constraints.
        /// It linearly interpolates the desiredSpeed between zero and the maxSpeed in either the positive or negative domain.
        /// It also attenuates the acceleration by the input. This means that with an initial velocity of 0f, applying an input of 0.5f
        /// will reach half the max speed in the same amount of time applying an input of 1f would reach max speed.
        /// However, deceleration is always clamped to [0.5f, 1f] multiplied by the max deceleration. This gives a snappier release.
        /// A neutral input of 0f will always apply 0.5f deceleration.
        ///
        /// Another feature of this function is that the speed arguments are "soft" meaning that if the currentVelocity exceeds them,
        /// the velocity will not be clamped but instead decelerate to that speed value. This allows all speed and acceleration values
        /// to be "dynamic", useful for turbo mechanics, control reduction in the air, ect.
        /// </remarks>
        public static float2 StepVelocityWithInput(float2 inputAxis,
                                                   float2 currentVelocity,
                                                   float2 positiveMaxAcceleration,
                                                   float2 positiveMaxDeceleration,
                                                   float2 positiveSoftMaxSpeed,
                                                   float2 negativeMaxAcceleration,
                                                   float2 negativeMaxDeceleration,
                                                   float2 negativeSoftMaxSpeed,
                                                   float deltaTime)
        {
            var input     = math.clamp(inputAxis, -1f, 1f);
            var pAccel    = positiveMaxAcceleration;
            var pDecel    = positiveMaxDeceleration;
            var pMaxSpeed = positiveSoftMaxSpeed;
            var nAccel    = negativeMaxAcceleration;
            var nDecel    = negativeMaxDeceleration;
            var nMaxSpeed = negativeSoftMaxSpeed;

            var pInput = math.saturate(input);
            var nInput = math.saturate(-input);

            // Solve for positive first
            // This is the speed we want based on our input
            var desiredSpeed = pMaxSpeed * pInput;
            // We also multiply the input to the acceleration to get better easing behavior (this is a "feel" preference)
            var accel = pInput * pAccel;
            // If our current velocity exceeds our desired speed, apply half the deceleration
            accel = math.select(accel, -pDecel * 0.5f, currentVelocity > desiredSpeed);
            // If a negative input is applied, apply up to the remainder of the deceleration
            var decel = nInput * -pDecel * 0.5f;
            // Combine the acceleration and deceleration
            var a = accel + decel;
            // Step the simulation
            var pNewVelocity = currentVelocity + a * deltaTime;
            // If we "cross over" the desired speed, then we fix ourselves to the desired speed.
            pNewVelocity = math.select(pNewVelocity, math.min(pNewVelocity, desiredSpeed), a >= 0f);
            pNewVelocity = math.select(pNewVelocity, math.max(pNewVelocity, desiredSpeed), a < 0f);

            // Now solve for the negative
            desiredSpeed     = nMaxSpeed * nInput;
            accel            = nInput * nAccel;
            accel            = math.select(accel, -nDecel * 0.5f, -currentVelocity > desiredSpeed);
            decel            = pInput * -nDecel * 0.5f;
            a                = accel + decel;
            var nNewVelocity = currentVelocity - a * deltaTime;
            nNewVelocity     = math.select(nNewVelocity, math.max(nNewVelocity, -desiredSpeed), a >= 0f);
            nNewVelocity     = math.select(nNewVelocity, math.min(nNewVelocity, -desiredSpeed), a < 0f);

            // Pick the better option
            var useNegative  = currentVelocity < 0f;
            useNegative     |= currentVelocity == 0f & nInput > 0f;
            return math.select(pNewVelocity, nNewVelocity, useNegative);
        }

        /// <summary>
        /// Applies a velocity step for two axes given an input which drives both the target velocity and acceleration strength.
        /// This is the same as multiple calls to the single axis version for each axis.
        /// </summary>
        /// <param name="inputAxis">A value in the range [-1f, 1f] which represent an input value along each axis</param>
        /// <param name="currentVelocity">The current signed velocity along each axis</param>
        /// <param name="positiveMaxAcceleration">The maximum acceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveMaxDeceleration">The maximum deceleration when moving in the positive direction. Must not be negative.</param>
        /// <param name="positiveSoftMaxSpeed">The desired speed when passed in an inputAxis value of 1f. If the currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="negativeMaxAcceleration">The maximum acceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeMaxDeceleration">The maximum deceleration when moving in the negative direction. Must not be negative.</param>
        /// <param name="negativeSoftMaxSpeed">The desired speed when passed in an inputAxis value of -1f. If the -currentVelocity is higher than this value,
        /// smooth deceleration will be applied. Must not be negative.</param>
        /// <param name="deltaTime">The time step over which to apply the velocity update.</param>
        /// <returns>A new signed velocity value for the axis</returns>
        /// <remarks>
        /// This algorithm provides a uniquely responsive feel for mapping input to an object's velocity using acceleration constraints.
        /// It linearly interpolates the desiredSpeed between zero and the maxSpeed in either the positive or negative domain.
        /// It also attenuates the acceleration by the input. This means that with an initial velocity of 0f, applying an input of 0.5f
        /// will reach half the max speed in the same amount of time applying an input of 1f would reach max speed.
        /// However, deceleration is always clamped to [0.5f, 1f] multiplied by the max deceleration. This gives a snappier release.
        /// A neutral input of 0f will always apply 0.5f deceleration.
        ///
        /// Another feature of this function is that the speed arguments are "soft" meaning that if the currentVelocity exceeds them,
        /// the velocity will not be clamped but instead decelerate to that speed value. This allows all speed and acceleration values
        /// to be "dynamic", useful for turbo mechanics, control reduction in the air, ect.
        /// </remarks>
        public static float3 StepVelocityWithInput(float3 inputAxis,
                                                   float3 currentVelocity,
                                                   float3 positiveMaxAcceleration,
                                                   float3 positiveMaxDeceleration,
                                                   float3 positiveSoftMaxSpeed,
                                                   float3 negativeMaxAcceleration,
                                                   float3 negativeMaxDeceleration,
                                                   float3 negativeSoftMaxSpeed,
                                                   float deltaTime)
        {
            var input     = math.clamp(inputAxis, -1f, 1f);
            var pAccel    = positiveMaxAcceleration;
            var pDecel    = positiveMaxDeceleration;
            var pMaxSpeed = positiveSoftMaxSpeed;
            var nAccel    = negativeMaxAcceleration;
            var nDecel    = negativeMaxDeceleration;
            var nMaxSpeed = negativeSoftMaxSpeed;

            var pInput = math.saturate(input);
            var nInput = math.saturate(-input);

            // Solve for positive first
            // This is the speed we want based on our input
            var desiredSpeed = pMaxSpeed * pInput;
            // We also multiply the input to the acceleration to get better easing behavior (this is a "feel" preference)
            var accel = pInput * pAccel;
            // If our current velocity exceeds our desired speed, apply half the deceleration
            accel = math.select(accel, -pDecel * 0.5f, currentVelocity > desiredSpeed);
            // If a negative input is applied, apply up to the remainder of the deceleration
            var decel = nInput * -pDecel * 0.5f;
            // Combine the acceleration and deceleration
            var a = accel + decel;
            // Step the simulation
            var pNewVelocity = currentVelocity + a * deltaTime;
            // If we "cross over" the desired speed, then we fix ourselves to the desired speed.
            pNewVelocity = math.select(pNewVelocity, math.min(pNewVelocity, desiredSpeed), a >= 0f);
            pNewVelocity = math.select(pNewVelocity, math.max(pNewVelocity, desiredSpeed), a < 0f);

            // Now solve for the negative
            desiredSpeed     = nMaxSpeed * nInput;
            accel            = nInput * nAccel;
            accel            = math.select(accel, -nDecel * 0.5f, -currentVelocity > desiredSpeed);
            decel            = pInput * -nDecel * 0.5f;
            a                = accel + decel;
            var nNewVelocity = currentVelocity - a * deltaTime;
            nNewVelocity     = math.select(nNewVelocity, math.max(nNewVelocity, -desiredSpeed), a >= 0f);
            nNewVelocity     = math.select(nNewVelocity, math.min(nNewVelocity, -desiredSpeed), a < 0f);

            // Pick the better option
            var useNegative  = currentVelocity < 0f;
            useNegative     |= currentVelocity == 0f & nInput > 0f;
            return math.select(pNewVelocity, nNewVelocity, useNegative);
        }
    }
}

