#include "Packages/com.latios.latiosframework/Transforms/ShaderLibrary/TransformQvvs.hlsl"

TransformQvvs ConvertToTransformQvvs(float4 a, float4 b, float4 c)
{
	quaternion q = new_quaternion(a);
	return new_TransformQvvs(b.xyz, q, c.w, c.xyz, asint(b.w));
}

void ConvertToVfxQvvs(TransformQvvs transform, out float4 a, out float4 b, out float4 c)
{
	a = transform.rotation.value;
	b = float4(transform.position, asfloat(transform.worldIndex));
	c = float4(transform.stretch, transform.scale);
}