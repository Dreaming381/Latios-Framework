using Unity.Mathematics;

public static class MathExtensions
{
    /// <summary>
    /// Extends a 3-component vector to a 4-component vector, assigning 1f to the w component
    /// </summary>
    public static float4 xyz1(this float3 value)
    {
        return new float4(value, 1f);
    }

    /// <summary>
    /// Extends a 2-component vector to a 3-component vector, putting the y in the 2D vector into the z
    /// of the 3D vector and zeroing the y in the 3D vector.
    /// </summary>
    public static float3 x0y(this float2 value)
    {
        return new float3(value.x, 0f, value.y);
    }
}

