#include "Packages/com.latios.latiosframework/Core/ShaderLibrary/quaternion.hlsl"
//#include "quaternion.hlsl"

struct RigidTransform
{
	quaternion rot;
	float3 pos;
};

RigidTransform new_RigidTransform(quaternion newRot, float3 newPos)
{
	RigidTransform result;
	result.rot = newRot;
	result.pos = newPos;
	return result;
}

RigidTransform new_RigidTransform(float3x3 newRot, float3 newPos)
{
	return new_RigidTransform(new_quaternion(newRot), newPos);
}

RigidTransform new_RigidTransform(float4x4 m)
{
	return new_RigidTransform(new_quaternion(m), m._m03_m13_m23);
}

#define RigidTransform_identity new_RigidTransform(quaternion_identity, float3(0.0, 0.0, 0.0))

// Todo: Do we need the rotation-only and translation-only overloads?

RigidTransform inverse(RigidTransform t)
{
	quaternion invRotation = inverse(t.rot);
	float3 invTranslation = mul(invRotation, -t.pos);
	return new_RigidTransform(invRotation, invTranslation);
}

RigidTransform mul(RigidTransform a, RigidTransform b)
{
	return new_RigidTransform(mul(a.rot, b.rot), mul(a.rot, b.pos) + a.pos);
}

float4 mul(RigidTransform a, float4 pos)
{
	return float4(mul(a.rot, pos.xyz) + a.pos * pos.w, pos.w);
}

float3 rotate(RigidTransform a, float3 dir)
{
	return mul(a.rot, dir);
}

float3 transform(RigidTransform a, float3 pos)
{
	return mul(a.rot, pos) + a.pos;
}