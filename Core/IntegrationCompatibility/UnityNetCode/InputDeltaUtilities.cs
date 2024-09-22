using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Compatibility.UnityNetCode
{
    // Based on the official Online FPS sample from the Unity Character Controller
    public static class InputDeltaUtilities
    {
        public static void AddInputDelta(ref float input, float addedDelta, float wrapValue)
        {
            input = math.fmod(input + addedDelta, wrapValue);
        }

        public static void AddInputDelta(ref float2 input, float2 addedDelta, float2 wrapValue)
        {
            input = math.fmod(input + addedDelta, wrapValue);
        }

        public static float GetInputDelta(float currentValue, float previousValue, float wrapValue)
        {
            var delta        = currentValue - previousValue;
            var wrappedDelta = delta - math.sign(delta) * wrapValue;
            return math.select(delta, wrappedDelta, math.abs(delta) > wrapValue * 0.5f);
        }

        public static float2 GetInputDelta(float2 currentValue, float2 previousValue, float2 wrapValue)
        {
            var delta        = currentValue - previousValue;
            var wrappedDelta = delta - math.sign(delta) * wrapValue;
            return math.select(delta, wrappedDelta, math.abs(delta) > wrapValue * 0.5f);
        }
    }
}

