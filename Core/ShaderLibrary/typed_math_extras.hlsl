#include "Packages/com.latios.latiosframework/Core/ShaderLibrary/RigidTransform.hlsl"
//#include "RigidTransform.hlsl"

float3x3 new_float3x3(quaternion q)
{
    float4 v = q.value;
    float4 v2 = v + v;
    
    uint3 npn = uint3(0x80000000, 0x00000000, 0x80000000);
    uint3 nnp = uint3(0x80000000, 0x80000000, 0x00000000);
    uint3 pnn = uint3(0x00000000, 0x80000000, 0x80000000);

	float3x3 result;
    result._m00_m10_m20 = v2.y * asfloat(asuint(v.yxw) ^ npn) - v2.z * asfloat(asuint(v.zwx) ^ pnn) + float3(1, 0, 0);
    result._m01_m11_m21 = v2.z * asfloat(asuint(v.wzy) ^ nnp) - v2.x * asfloat(asuint(v.yxw) ^ npn) + float3(0, 1, 0);
    result._m02_m12_m22 = v2.x * asfloat(asuint(v.zwx) ^ pnn) - v2.y * asfloat(asuint(v.wzy) ^ nnp) + float3(0, 0, 1);
	return result;
}

float3x4 TRS(float3 translation, quaternion rotation, float3 scale)
{
	float3x3 r = new_float3x3(rotation);
	return float3x4((r._m00_m10_m20 * scale.x),
                    (r._m01_m11_m21 * scale.y),
                    (r._m02_m12_m22 * scale.z),
                    (translation));
}

float3 InverseRotateFast(quaternion normalizedRotation, float3 v)
{
    return rotate(conjugate(normalizedRotation), v);
}

float3 InverseRotateFast(quaternion normalizedRotation, quaternion q)
{
	return mul(conjugate(normalizedRotation), q);
}