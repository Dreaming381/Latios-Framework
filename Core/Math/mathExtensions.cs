using Unity.Mathematics;

public static class MathExtensions
{
    /// <summary>
    /// Extends a 3-component vector to a 4 component vector, assigning 1f to the w component
    /// </summary>
    public static float4 xyz1(this float3 value)
    {
        return new float4(value, 1f);
    }
}

