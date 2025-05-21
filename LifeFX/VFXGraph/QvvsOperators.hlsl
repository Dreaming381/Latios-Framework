#include "QvvsHelpers.hlsl"

void GetQvvsProperties(float4 qvvsA, float4 qvvsB, float4 qvvsC, out bool isAlive, out bool isEnabled, out float4 quaternion, out float3 position, out float scale, out float3 stretch, out int worldIndexWithoutFlags, out float3 forward, out float3 up, out float3 right, out float4x4 toMatrix)
{
	TransformQvvs transform = ConvertToTransformQvvs(qvvsA, qvvsB, qvvsC);
	isAlive = (transform.worldIndex & 0x80000000) != 0;
	isEnabled = (transform.worldIndex & 0x40000000) != 0;
	quaternion = transform.rotation.value;
	position = transform.position;
	scale = transform.scale;
	stretch = transform.stretch;
	worldIndexWithoutFlags = transform.worldIndex & 0x3fffffff;
	forward = rotate(transform.rotation, float3(0.0, 0.0, 1.0));
	up = rotate(transform.rotation, float3(0.0, 1.0, 0.0));
	right = rotate(transform.rotation, float3(1.0, 0.0, 0.0));
	toMatrix = transform.ToMatrix4x4();
}

void ConstructQvvs(float3 position, float4 rotation, float scale, float3 stretch, int worldIndex, out float4 qvvsA, out float4 qvvsB, out float4 qvvsC)
{
	TransformQvvs transform = new_TransformQvvs(position, new_quaternion(rotation), scale, stretch, worldIndex);
	ConvertToVfxQvvs(transform, qvvsA, qvvsB, qvvsC);
}

void TransformByQvvs(float4 qvvsA, float4 qvvsB, float4 qvvsC, float3 v, out float3 p, out float3 inversePoint, out float3 direction, out float3 directionWithStretch, out float3 directionScaledAndStretched, out float3 inverseDirection, out float3 inverseDirectionWithStretch, out float3 inverseDirectionScaledAndStretched)
{
	TransformQvvs transform = ConvertToTransformQvvs(qvvsA, qvvsB, qvvsC);
	p = TransformPoint(transform, v);
	inversePoint = InverseTransformPoint(transform, v);
	direction = TransformDirection(transform, v);
	directionWithStretch = TransformDirectionWithStretch(transform, v);
	directionScaledAndStretched = TransformDirectionScaledAndStretched(transform, v);
	inverseDirection = InverseTransformDirection(transform, v);
	inverseDirectionWithStretch = InverseTransformDirectionWithStretch(transform, v);
	inverseDirectionScaledAndStretched = InverseTransformDirectionScaledAndStretched(transform, v);
}

void MulQvvs(float4 AqvvsA, float4 AqvvsB, float4 AqvvsC, float4 BqvvsA, float4 BqvvsB, float4 BqvvsC, out float4 ABqvvsA, out float4 ABqvvsB, out float4 ABqvvsC, out float4 iABqvvsA, out float4 iABqvvsB, out float4 iABqvvsC)
{
	TransformQvvs a = ConvertToTransformQvvs(AqvvsA, AqvvsB, AqvvsC);
	TransformQvvs b = ConvertToTransformQvvs(BqvvsA, BqvvsB, BqvvsC);
	TransformQvvs ab = mul(a, b);
	TransformQvvs iab = inversemulqvvs(a, b);
	ConvertToVfxQvvs(ab, ABqvvsA, ABqvvsB, ABqvvsC);
	ConvertToVfxQvvs(iab, iABqvvsA, iABqvvsB, iABqvvsC);
}

void RotateAbout(float4 qvvsA, float4 qvvsB, float4 qvvsC, float4 rotation, float3 pivot, out float4 resultA, out float4 resultB, out float4 resultC)
{
	TransformQvvs transform = ConvertToTransformQvvs(qvvsA, qvvsB, qvvsC);
	TransformQvvs result = RotateAbout(transform, new_quaternion(rotation), pivot);
	ConvertToVfxQvvs(result, resultA, resultB, resultC);
}

// Quaternion

float4 MulQuat(float4 a, float4 b)
{
	return mul(new_quaternion(a), new_quaternion(b)).value;
}

float3 RotatePointByQuat(float4 quat, float3 p)
{
	return rotate(new_quaternion(quat), p);
}

float4 AxisAngleQuat(float3 axis, float angle)
{
	float3 fixedAxis = normalizesafe(axis);
	if (all(fixedAxis == 0.0))
		return float4(0.0, 0.0, 0.0, 1.0);
	return AxisAngle(fixedAxis, angle).value;
}

void LookRotationQuat(float3 forward, float3 up, out float4 quat, out float4 quatSafe)
{
	quat = LookRotation(forward, up).value;
	quatSafe = LookRotationSafe(forward, up).value;
}