#include "Packages/com.latios.latiosframework/Core/ShaderLibrary/typed_math_extras.hlsl"
//#include "../../Core/ShaderLibrary/typed_math_extras.hlsl"

struct TransformQvvs
{
	quaternion rotation;
	float3 position;
	int worldIndex;
	float3 stretch;
	float scale;
	
	float3x4 ToMatrix3x4()
	{
		return TRS(position, rotation, scale * stretch);
	}
	
	float4x4 ToMatrix4x4()
    {
        return TRS(position, rotation, scale * stretch);
	}
	
	float3x4 ToInverseMatrix3x4()
	{
		quaternion rotationInverse = conjugate(rotation);
		float3x3 rotMat = new_float3x3(rotationInverse);
		float3 positionInverse = rotate(rotationInverse, -position);
		float4 rcp = rcp(float4(stretch, scale));
		float3 scaleInverse = rcp.xyz * rcp.w;
		float3x4 result;
		result._m00_m10_m20 = rotMat._m00_m10_m20 * scaleInverse;
		result._m01_m11_m21 = rotMat._m01_m11_m21 * scaleInverse;
		result._m02_m12_m22 = rotMat._m02_m12_m22 * scaleInverse;
		result._m03_m13_m23 = positionInverse * scaleInverse;
		return result;
	}
	
	float3x4 ToInverseMatrix3x4IgnoreStretch()
    {
		quaternion rotationInverse = conjugate(rotation);
		float3x3 rotMat = new_float3x3(rotationInverse);
		float3 positionInverse = rotate(rotationInverse, -position);
		float rcp = rcp(scale);
		float3x4 result;
		result._m00_m10_m20 = rotMat._m00_m10_m20;
		result._m01_m11_m21 = rotMat._m01_m11_m21;
		result._m02_m12_m22 = rotMat._m02_m12_m22;
		result._m03_m13_m23 = positionInverse;
		return result * rcp;
    }
};

TransformQvvs new_TransformQvvs(float3 position, quaternion rotation)
{
	TransformQvvs result;
	result.position = position;
	result.rotation = rotation;
	result.worldIndex = 0;
	result.stretch = float3(1.0, 1.0, 1.0);
	result.scale = 1.0;
	return result;
}

TransformQvvs new_TransformQvvs(float3 position, quaternion rotation, float scale, float3 stretch)
{
	TransformQvvs result;
	result.position = position;
	result.rotation = rotation;
	result.worldIndex = 0;
	result.stretch = stretch;
	result.scale = scale;
	return result;
}

TransformQvvs new_TransformQvvs(float3 position, quaternion rotation, float scale, float3 stretch, int worldIndex)
{
	TransformQvvs result;
	result.position = position;
	result.rotation = rotation;
	result.worldIndex = worldIndex;
	result.stretch = stretch;
	result.scale = scale;
	return result;
}

TransformQvvs new_TransformQvvs(RigidTransform rigidTransform)
{
	return new_TransformQvvs(rigidTransform.pos, rigidTransform.rot);
}

#define TransformQvvs_identity new_TransformQvvs(RigidTransform_identity);

struct TransformQvs
{
	quaternion rotation;
	float3 position;
	float scale;
};

TransformQvs new_TransformQvs(float3 position, quaternion rotation)
{
	TransformQvs result;
	result.rotation = rotation;
	result.position = position;
	result.scale = 1.0;
	return result;
}

TransformQvs new_TransformQvs(float3 position, quaternion rotation, float scale)
{
	TransformQvs result;
	result.rotation = rotation;
	result.position = position;
	result.scale = scale;
	return result;
}

#define TransformQvs_identity new_TransformQvs(float3(0.0, 0.0, 0.0), quaternion_identity)

TransformQvvs mul(in TransformQvvs a, in TransformQvvs b)
{
	return new_TransformQvvs(a.position + rotate(a.rotation, b.position * a.stretch * a.scale),
							 mul(a.rotation, b.rotation),
							 a.scale * b.scale,
							 b.stretch,
							 b.worldIndex);
}

void mul(inout TransformQvvs bWorld, in TransformQvvs a, in TransformQvs b)
{
	bWorld.rotation = mul(a.rotation, b.rotation);
	bWorld.position = a.position + rotate(a.rotation, b.position * a.stretch * a.scale);
    // bWorld.worldIndex is preserved
    // bWorld.stretch is preserved
	bWorld.scale = a.scale * b.scale;
}

TransformQvs inversemul(in TransformQvvs a, in TransformQvvs b)
{
	quaternion inverseRotation = conjugate(a.rotation);
	float4 rcps = rcp(float4(a.stretch, a.scale));
	return new_TransformQvs(rotate(inverseRotation, b.position - a.position) * rcps.xyz * rcps.w, 
							mul(inverseRotation, b.rotation),
							rcps.w * b.scale);
}

TransformQvvs inversemulqvvs(in TransformQvvs a, in TransformQvvs b)
{
	quaternion inverseRotation = conjugate(a.rotation);
	float4 rcps = rcp(float4(a.stretch, a.scale));
	return new_TransformQvvs(rotate(inverseRotation, b.position - a.position) * rcps.xyz * rcps.w,
							 mul(inverseRotation, b.rotation),
							 rcps.w * b.scale,
							 b.stretch,
                             b.worldIndex);
}

float3 TransformPoint(in TransformQvvs qvvs, float3 p)
{
    return qvvs.position + rotate(qvvs.rotation, p * qvvs.stretch * qvvs.scale);
}

float3 InverseTransformPoint(in TransformQvvs qvvs, float3 p)
{
	float3 localPoint = InverseRotateFast(qvvs.rotation, p - qvvs.position);
	float4 rcps = rcp(float4(qvvs.stretch, qvvs.scale));
    return localPoint * rcps.xyz * rcps.w;
}

float3 TransformDirection(in TransformQvvs qvvs, float3 direction)
{
	return rotate(qvvs.rotation, direction);
}

float3 TransformDirectionWithStretch(in TransformQvvs qvvs, float3 direction)
{
	float magnitude = length(direction);
	return normalizesafe(rotate(qvvs.rotation, direction) * qvvs.stretch) * magnitude;
}

float3 TransformDirectionScaledAndStretched(in TransformQvvs qvvs, float3 direction)
{
	return rotate(qvvs.rotation, direction) * qvvs.stretch * qvvs.scale;
}

float3 InverseTransformDirection(in TransformQvvs qvvs, float3 direction)
{
	return InverseRotateFast(qvvs.rotation, direction);
}

float3 InverseTransformDirectionWithStretch(in TransformQvvs qvvs, float3 direction)
{
	float magnitude = length(direction);
	float3 rcp = rcp(qvvs.stretch);
	return normalizesafe(InverseRotateFast(qvvs.rotation, direction) * rcp) * magnitude;
}

float3 InverseTransformDirectionScaledAndStretched(in TransformQvvs qvvs, float3 direction)
{
	float4 rcp = rcp(float4(qvvs.stretch, qvvs.scale));
	return InverseRotateFast(qvvs.rotation, direction) * rcp.xyz * rcp.w;
}

quaternion TransformRotation(in TransformQvvs qvvs, quaternion rotation)
{
	return mul(qvvs.rotation, rotation);
}

quaternion InverseTransformRotation(in TransformQvvs qvvs, quaternion rotation)
{
	return InverseRotateFast(qvvs.rotation, rotation);
}

float TransformScale(in TransformQvvs qvvs, float scale)
{
	return qvvs.scale * scale;
}

float InverseTransformScale(in TransformQvvs qvvs, float scale)
{
	return scale / qvvs.scale;
}

TransformQvvs RotateAbout(in TransformQvvs qvvs, quaternion rotation, float3 pivot)
{
	float3 pivotToOldPosition = qvvs.position - pivot;
	float3 pivotToNewPosition = rotate(rotation, pivotToOldPosition);
	return new_TransformQvvs
    (
		qvvs.position + pivotToNewPosition - pivotToOldPosition,
        mul(rotation, qvvs.rotation),
        qvvs.scale,
        qvvs.stretch,
        qvvs.worldIndex
    );
}