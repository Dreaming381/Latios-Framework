//#include "math_extras.hlsl"
#include "Packages/com.latios.latiosframework/Core/ShaderLibrary/math_extras.hlsl"

struct quaternion
{
	float4 value;
};

quaternion new_quaternion(float x, float y, float z, float w)
{
	quaternion result;
	result.value = float4(x, y, z, w);
	return result;
}

quaternion new_quaternion(float4 newValue)
{
	quaternion result;
	result.value = newValue;
	return result;
}

quaternion new_quaternion(float3x3 m)
{
	float3 u = m._m00_m10_m20;  //m.c0;
	float3 v = m._m01_m11_m21;  //m.c1;
	float3 w = m._m02_m12_m22;  //m.c2;

	uint u_sign = (asuint(u.x) & 0x80000000);
	float t = v.y + asfloat(asuint(w.z) ^ u_sign);
	uint4 u_mask = uint4((int) u_sign >> 31);
	uint4 t_mask = uint4(asint(t) >> 31);

	float tr = 1.0f + abs(u.x);

	uint4 sign_flips = uint4(0x00000000, 0x80000000, 0x80000000, 0x80000000) ^ (u_mask & uint4(0x00000000, 0x80000000, 0x00000000, 0x80000000)) ^ (t_mask & uint4(0x80000000, 0x80000000, 0x80000000, 0x00000000));

	float4 value = float4(tr, u.y, w.x, v.z) + asfloat(asuint(float4(t, v.x, u.z, w.y)) ^ sign_flips); // +---, +++-, ++-+, +-++

	value = asfloat((asuint(value) & ~u_mask) | (asuint(value.zwxy) & u_mask));
	value = asfloat((asuint(value.wzyx) & ~t_mask) | (asuint(value) & t_mask));
	value = normalize(value);
	return new_quaternion(value);
}

quaternion new_quaternion(float3x4 m)
{
	float3 u = m._m00_m10_m20; //m.c0;
	float3 v = m._m01_m11_m21; //m.c1;
	float3 w = m._m02_m12_m22; //m.c2;

	uint u_sign = (asuint(u.x) & 0x80000000);
	float t = v.y + asfloat(asuint(w.z) ^ u_sign);
	uint4 u_mask = uint4((int) u_sign >> 31);
	uint4 t_mask = uint4(asint(t) >> 31);

	float tr = 1.0f + abs(u.x);

	uint4 sign_flips = uint4(0x00000000, 0x80000000, 0x80000000, 0x80000000) ^ (u_mask & uint4(0x00000000, 0x80000000, 0x00000000, 0x80000000)) ^ (t_mask & uint4(0x80000000, 0x80000000, 0x80000000, 0x00000000));

	float4 value = float4(tr, u.y, w.x, v.z) + asfloat(asuint(float4(t, v.x, u.z, w.y)) ^ sign_flips); // +---, +++-, ++-+, +-++

	value = asfloat((asuint(value) & ~u_mask) | (asuint(value.zwxy) & u_mask));
	value = asfloat((asuint(value.wzyx) & ~t_mask) | (asuint(value) & t_mask));
	value = normalize(value);
	return new_quaternion(value);
}

quaternion new_quaternion(float4x4 m)
{
	float3 u = m._m00_m10_m20; //m.c0;
	float3 v = m._m01_m11_m21; //m.c1;
	float3 w = m._m02_m12_m22; //m.c2;

	uint u_sign = (asuint(u.x) & 0x80000000);
	float t = v.y + asfloat(asuint(w.z) ^ u_sign);
	uint4 u_mask = uint4((int) u_sign >> 31);
	uint4 t_mask = uint4(asint(t) >> 31);

	float tr = 1.0f + abs(u.x);

	uint4 sign_flips = uint4(0x00000000, 0x80000000, 0x80000000, 0x80000000) ^ (u_mask & uint4(0x00000000, 0x80000000, 0x00000000, 0x80000000)) ^ (t_mask & uint4(0x80000000, 0x80000000, 0x80000000, 0x00000000));

	float4 value = float4(tr, u.y, w.x, v.z) + asfloat(asuint(float4(t, v.x, u.z, w.y)) ^ sign_flips); // +---, +++-, ++-+, +-++

	value = asfloat((asuint(value) & ~u_mask) | (asuint(value.zwxy) & u_mask));
	value = asfloat((asuint(value.wzyx) & ~t_mask) | (asuint(value) & t_mask));
	value = normalize(value);
	return new_quaternion(value);
}

#define quaternion_identity new_quaternion(0.0, 0.0, 0.0, 1.0)

quaternion AxisAngle(float3 axis, float angle)
{
	float sina, cosa;
	sincos(0.5f * angle, sina, cosa);
	return new_quaternion(float4(axis * sina, cosa));
}

quaternion EulerXYZ(float3 xyz)
{
            // return mul(rotateZ(xyz.z), mul(rotateY(xyz.y), rotateX(xyz.x)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z - s.y * s.z * c.x,
                // s.y * c.x * c.z + s.x * s.z * c.y,
                // s.z * c.x * c.y - s.x * s.y * c.z,
                // c.x * c.y * c.z + s.y * s.z * s.x
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(-1.0f, 1.0f, -1.0f, 1.0f)
                );
}

quaternion EulerXYZ(float x, float y, float z)
{
	return EulerXYZ(float3(x, y, z));
}

quaternion EulerXZY(float3 xyz)
{
            // return mul(rotateY(xyz.y), mul(rotateZ(xyz.z), rotateX(xyz.x)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z + s.y * s.z * c.x,
                // s.y * c.x * c.z + s.x * s.z * c.y,
                // s.z * c.x * c.y - s.x * s.y * c.z,
                // c.x * c.y * c.z - s.y * s.z * s.x
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(1.0f, 1.0f, -1.0f, -1.0f)
                );
}

quaternion EulerXZY(float x, float y, float z)
{
	return EulerXZY(float3(x, y, z));
}

quaternion EulerYXZ(float3 xyz)
{
            // return mul(rotateZ(xyz.z), mul(rotateX(xyz.x), rotateY(xyz.y)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z - s.y * s.z * c.x,
                // s.y * c.x * c.z + s.x * s.z * c.y,
                // s.z * c.x * c.y + s.x * s.y * c.z,
                // c.x * c.y * c.z - s.y * s.z * s.x
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(-1.0f, 1.0f, 1.0f, -1.0f)
                );
}

quaternion EulerYXZ(float x, float y, float z)
{
	return EulerYXZ(float3(x, y, z));
}

quaternion EulerYZX(float3 xyz)
{
            // return mul(rotateX(xyz.x), mul(rotateZ(xyz.z), rotateY(xyz.y)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z - s.y * s.z * c.x,
                // s.y * c.x * c.z - s.x * s.z * c.y,
                // s.z * c.x * c.y + s.x * s.y * c.z,
                // c.x * c.y * c.z + s.y * s.z * s.x
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(-1.0f, -1.0f, 1.0f, 1.0f)
                );
}

quaternion EulerYZX(float x, float y, float z)
{
	return EulerYZX(float3(x, y, z));
}

quaternion EulerZXY(float3 xyz)
{
            // return mul(rotateY(xyz.y), mul(rotateX(xyz.x), rotateZ(xyz.z)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z + s.y * s.z * c.x,
                // s.y * c.x * c.z - s.x * s.z * c.y,
                // s.z * c.x * c.y - s.x * s.y * c.z,
                // c.x * c.y * c.z + s.y * s.z * s.x
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(1.0f, -1.0f, -1.0f, 1.0f)
                );
}

quaternion EulerZXY(float x, float y, float z)
{
	return EulerZXY(float3(x, y, z));
}

quaternion EulerZYX(float3 xyz)
{
            // return mul(rotateX(xyz.x), mul(rotateY(xyz.y), rotateZ(xyz.z)));
	float3 s, c;
	sincos(0.5f * xyz, s, c);
	return new_quaternion(
                // s.x * c.y * c.z + s.y * s.z * c.x,
                // s.y * c.x * c.z - s.x * s.z * c.y,
                // s.z * c.x * c.y + s.x * s.y * c.z,
                // c.x * c.y * c.z - s.y * s.x * s.z
                float4(s.xyz, c.x) * c.yxxy * c.zzyz + s.yxxy * s.zzyz * float4(c.xyz, s.x) * float4(1.0f, -1.0f, 1.0f, -1.0f)
                );
}

quaternion EulerZYX(float x, float y, float z)
{
	return EulerZYX(float3(x, y, z));
}

// Todo: RotationOrder enum and functions

quaternion RotateX(float angle)
{
	float sina, cosa;
	sincos(0.5f * angle, sina, cosa);
	return new_quaternion(sina, 0.0f, 0.0f, cosa);
}

quaternion RotateY(float angle)
{
	float sina, cosa;
	sincos(0.5f * angle, sina, cosa);
	return new_quaternion(0.0f, sina, 0.0f, cosa);
}

quaternion RotateZ(float angle)
{
	float sina, cosa;
	sincos(0.5f * angle, sina, cosa);
	return new_quaternion(0.0f, 0.0f, sina, cosa);
}

quaternion LookRotation(float3 forward, float3 up)
{
	float3 t = normalize(cross(up, forward));
	return new_quaternion(float3x3(t, cross(forward, t), forward));
}

quaternion LookRotationSafe(float3 forward, float3 up)
{
	float forwardLengthSq = dot(forward, forward);
	float upLengthSq = dot(up, up);

	forward *= rsqrt(forwardLengthSq);
	up *= rsqrt(upLengthSq);

	float3 t = cross(up, forward);
	float tLengthSq = dot(t, t);
	t *= rsqrt(tLengthSq);

	float mn = min(min(forwardLengthSq, upLengthSq), tLengthSq);
	float mx = max(max(forwardLengthSq, upLengthSq), tLengthSq);

	bool accept = mn > 1e-35f && mx < 1e35f && isfinite(forwardLengthSq) && isfinite(upLengthSq) && isfinite(tLengthSq);
	return new_quaternion(select(float4(0.0f, 0.0f, 0.0f, 1.0f), new_quaternion(float3x3(t, cross(forward, t), forward)).value, accept));
}

quaternion conjugate(quaternion q)
{
	return new_quaternion(q.value * float4(-1.0f, -1.0f, -1.0f, 1.0f));
}

quaternion inverse(quaternion q)
{
	float4 x = q.value;
	return new_quaternion(rcp(dot(x, x)) * x * float4(-1.0f, -1.0f, -1.0f, 1.0f));
}

float dot(quaternion a, quaternion b)
{
	return dot(a.value, b.value);
}

float length(quaternion q)
{
	return sqrt(dot(q.value, q.value));
}

float lengthsq(quaternion q)
{
	return dot(q.value, q.value);
}

quaternion normalize(quaternion q)
{
	float4 x = q.value;
	return new_quaternion(rsqrt(dot(x, x)) * x);
}

quaternion normalizesafe(quaternion q)
{
	float4 x = q.value;
	float len = dot(x, x);
	return quaternion(select(quaternion_identity.value, x * rsqrt(len), len > FLT_MIN_NORMAL));
}

quaternion normalizesafe(quaternion q, quaternion defaultvalue)
{
	float4 x = q.value;
	float len = dot(x, x);
	return quaternion(select(defaultvalue.value, x * rsqrt(len), len > FLT_MIN_NORMAL));
}

quaternion unitexp(quaternion q)
{
	float v_rcp_len = rsqrt(dot(q.value.xyz, q.value.xyz));
	float v_len = rcp(v_rcp_len);
	float sin_v_len, cos_v_len;
	sincos(v_len, sin_v_len, cos_v_len);
	return new_quaternion(float4(q.value.xyz * v_rcp_len * sin_v_len, cos_v_len));
}

quaternion exp(quaternion q)
{
	float v_rcp_len = rsqrt(dot(q.value.xyz, q.value.xyz));
	float v_len = rcp(v_rcp_len);
	float sin_v_len, cos_v_len;
	sincos(v_len, sin_v_len, cos_v_len);
	return quaternion(float4(q.value.xyz * v_rcp_len * sin_v_len, cos_v_len) * exp(q.value.w));
}

quaternion unitlog(quaternion q)
{
	float w = clamp(q.value.w, -1.0f, 1.0f);
	float s = acos(w) * rsqrt(1.0f - w * w);
	return new_quaternion(float4(q.value.xyz * s, 0.0f));
}

quaternion log(quaternion q)
{
	float v_len_sq = dot(q.value.xyz, q.value.xyz);
	float q_len_sq = v_len_sq + q.value.w * q.value.w;

	float s = acos(clamp(q.value.w * rsqrt(q_len_sq), -1.0f, 1.0f)) * rsqrt(v_len_sq);
	return new_quaternion(float4(q.value.xyz * s, 0.5f * log(q_len_sq)));
}

quaternion mul(quaternion a, quaternion b)
{
	return new_quaternion(a.value.wwww * b.value + (a.value.xyzx * b.value.wwwx + a.value.yzxy * b.value.zxyy) * float4(1.0f, 1.0f, 1.0f, -1.0f) - a.value.zxyz * b.value.yzxz);
}

float3 mul(quaternion q, float3 v)
{
	float3 t = 2 * cross(q.value.xyz, v);
	return v + q.value.w * t + cross(q.value.xyz, t);
}

float3 rotate(quaternion q, float3 v)
{
	float3 t = 2 * cross(q.value.xyz, v);
	return v + q.value.w * t + cross(q.value.xyz, t);
}

quaternion nlerp(quaternion q1, quaternion q2, float t)
{
	return normalize(q1.value + t * (chgsign(q2.value, dot(q1, q2)) - q1.value));
}

// Todo: acos is pricy
quaternion slerp(quaternion q1, quaternion q2, float t)
{
	float dt = dot(q1, q2);
	if (dt < 0.0f)
	{
		dt = -dt;
		q2.value = -q2.value;
	}

	if (dt < 0.9995f)
	{
		float angle = acos(dt);
		float s = rsqrt(1.0f - dt * dt); // 1.0f / sin(angle)
		float w1 = sin(angle * (1.0f - t)) * s;
		float w2 = sin(angle * t) * s;
		return new_quaternion(q1.value * w1 + q2.value * w2);
	}
	else
	{
        // if the angle is small, use linear interpolation
		return nlerp(q1, q2, t);
	}
}

// Todo: asin is pricy
float angle(quaternion q1, quaternion q2)
{
	float diff = asin(length(normalize(mul(conjugate(q1), q2)).value.xyz));
	return diff + diff;
}

// Todo: quaternion from non-uniform column vectors using https://matthias-research.github.io/pages/publications/stablePolarDecomp.pdf
