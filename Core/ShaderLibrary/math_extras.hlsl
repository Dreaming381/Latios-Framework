#define FLT_MIN_NORMAL 1.175494351e-38F

float4 select(float4 a, float4 b, bool c)
{
	return c ? b : a;
}

float3 select(float3 a, float3 b, bool c)
{
	return c ? b : a;
}

float4 chgsign(float4 x, float4 y)
{
	return asfloat(asuint(x) ^ (asuint(y) & 0x80000000));
}

float3 normalizesafe(float3 x)
{
	float len = dot(x, x);
    return select(float3(0.0, 0.0, 0.0), x * rsqrt(len), len > FLT_MIN_NORMAL);
}