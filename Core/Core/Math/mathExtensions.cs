using Unity.Mathematics;

public static class MathExtensions
{
    public static float4 xyz1(this float3 value)
    {
        return new float4(value, 1f);
    }
}

