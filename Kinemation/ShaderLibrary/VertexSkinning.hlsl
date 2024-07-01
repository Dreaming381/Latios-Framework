#ifndef LATIOS_VERTEX_SKINNING_INCLUDED
#define LATIOS_VERTEX_SKINNING_INCLUDED

// Can be a float3x4, a QVVS, or a DQS
struct TransformUnion
{
    float4 a;
    float4 b;
    float4 c;
};

struct Qvvs
{
    float4 rotation;
    float4 position;
    float4 stretchScale;
};

// Dual Quaternion with Scale
struct Dqs
{
    float4 r; // real
    float4 d; // dual
    float4 scale;
};

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
uniform StructuredBuffer<TransformUnion> _latiosBindPoses;
uniform ByteAddressBuffer                _latiosBoneTransforms;


TransformUnion readBone(uint absoluteBoneIndex)
{
    TransformUnion result = (TransformUnion)0;
    result.a = asfloat(_latiosBoneTransforms.Load4(absoluteBoneIndex * 48));
    result.b = asfloat(_latiosBoneTransforms.Load4(absoluteBoneIndex * 48 + 16));
    result.c = asfloat(_latiosBoneTransforms.Load4(absoluteBoneIndex * 48 + 32));
    return result;
}
#endif

void fromQuaternion(float4 v, out float3 c0, out float3 c1, out float3 c2)
{
    float4 v2 = v + v;

    uint3 npn = uint3(0x80000000, 0x00000000, 0x80000000);
    uint3 nnp = uint3(0x80000000, 0x80000000, 0x00000000);
    uint3 pnn = uint3(0x00000000, 0x80000000, 0x80000000);
    c0 = v2.y * asfloat(asuint(v.yxw) ^ npn) - v2.z * asfloat(asuint(v.zwx) ^ pnn) + float3(1, 0, 0);
    c1 = v2.z * asfloat(asuint(v.wzy) ^ nnp) - v2.x * asfloat(asuint(v.yxw) ^ npn) + float3(0, 1, 0);
    c2 = v2.x * asfloat(asuint(v.zwx) ^ pnn) - v2.y * asfloat(asuint(v.wzy) ^ nnp) + float3(0, 0, 1);
}

float4 mulQuatQuat(float4 a, float4 b)
{
    return float4(a.wwww * b + (a.xyzx * b.wwwx + a.yzxy * b.zxyy) * float4(1, 1, 1, -1) - a.zxyz * b.yzxz);
}

float3x4 qvvsToMatrix(Qvvs qvvs)
{
    float3 scale = qvvs.stretchScale.xyz * qvvs.stretchScale.w;
    float3 c0 = 0;
    float3 c1 = 0;
    float3 c2 = 0;
    fromQuaternion(qvvs.rotation, c0, c1, c2);
    c0 *= scale.x;
    c1 *= scale.y;
    c2 *= scale.z;
    return float3x4(
        c0.x, c1.x, c2.x, qvvs.position.x,
        c0.y, c1.y, c2.y, qvvs.position.y,
        c0.z, c1.z, c2.z, qvvs.position.z
        );
}

float3x4 transformUnionMatrixToMatrix(TransformUnion transform)
{
    return float3x4(
        transform.a.x, transform.a.w, transform.b.z, transform.c.y,
        transform.a.y, transform.b.x, transform.b.w, transform.c.z,
        transform.a.z, transform.b.y, transform.c.x, transform.c.w
        );
}

Dqs transformUnionDqsToDqs(TransformUnion transform)
{
    Dqs result = (Dqs)0;
    result.r = transform.a;
    result.d = transform.b;
    result.scale.xyz = transform.c.xyz;
    return result;
}

float3x4 mul3x4(float3x4 a, float3x4 b)
{
    float4x4 x = 0.;
    x._m00 = a._m00;
    x._m10 = a._m10;
    x._m20 = a._m20;
    x._m30 = 0.;
    x._m01 = a._m01;
    x._m11 = a._m11;
    x._m21 = a._m21;
    x._m31 = 0.;
    x._m02 = a._m02;
    x._m12 = a._m12;
    x._m22 = a._m22;
    x._m32 = 0.;
    x._m03 = a._m03;
    x._m13 = a._m13;
    x._m23 = a._m23;
    x._m33 = 1.;

    float4x4 y = 0.;
    y._m00 = b._m00;
    y._m10 = b._m10;
    y._m20 = b._m20;
    y._m30 = 0.;
    y._m01 = b._m01;
    y._m11 = b._m11;
    y._m21 = b._m21;
    y._m31 = 0.;
    y._m02 = b._m02;
    y._m12 = b._m12;
    y._m22 = b._m22;
    y._m32 = 0.;
    y._m03 = b._m03;
    y._m13 = b._m13;
    y._m23 = b._m23;
    y._m33 = 1.;

    float4x4 r = mul(x, y);

    float3x4 result = 0.;
    result._m00 = r._m00;
    result._m10 = r._m10;
    result._m20 = r._m20;

    result._m01 = r._m01;
    result._m11 = r._m11;
    result._m21 = r._m21;

    result._m02 = r._m02;
    result._m12 = r._m12;
    result._m22 = r._m22;

    result._m03 = r._m03;
    result._m13 = r._m13;
    result._m23 = r._m23;

    return result;
}

void vertexSkinMatrix(uint4 boneIndices, float4 boneWeights, uint skeletonBase, inout float3 position, inout float3 normal, inout float3 tangent)
{
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    float3x4 mat = transformUnionMatrixToMatrix(readBone(skeletonBase + boneIndices.x)) * boneWeights.x;

    if (boneWeights.y > 0.0)
    {
        mat += transformUnionMatrixToMatrix(readBone(skeletonBase + boneIndices.y)) * boneWeights.y;
    }
    if (boneWeights.z > 0.0)
    {
        mat += transformUnionMatrixToMatrix(readBone(skeletonBase + boneIndices.z)) * boneWeights.z;
    }
    if (boneWeights.w > 0.0 && boneWeights.w < 0.5)
    {
        mat += transformUnionMatrixToMatrix(readBone(skeletonBase + boneIndices.w)) * boneWeights.w;
    }
    
    position = mul(mat, float4(position, 1)).xyz;
    normal = mul(mat, float4(normal, 0)).xyz;
    tangent = mul(mat, float4(tangent, 0)).xyz;
#endif
}

void vertexSkinDqs(uint4 boneIndices, float4 boneWeights, uint2 skeletonBase, inout float3 position, inout float3 normal, inout float3 tangent)
{
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    {
        // Reminder: Bindposes do not have bone offsets, as they come from the mesh.
        Dqs dqs = transformUnionDqsToDqs(_latiosBindPoses[skeletonBase.y + boneIndices.x]);
        float4 bindposeReal = dqs.r * boneWeights.x;
        float4 bindposeDual = dqs.d * boneWeights.x;
        float3 localScale = dqs.scale;
        float4 firstBoneRot = dqs.r;

        if (boneWeights.y > 0.0)
        {
            dqs = transformUnionDqsToDqs(_latiosBindPoses[skeletonBase.y + boneIndices.y]);
            localScale += dqs.scale.xyz * boneWeights.y;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.y = -boneWeights.y;
            bindposeReal += dqs.r * boneWeights.y;
            bindposeDual += dqs.d * boneWeights.y;
        }
        if (boneWeights.z > 0.0)
        {
            dqs = transformUnionDqsToDqs(_latiosBindPoses[skeletonBase.y + boneIndices.z]);
            localScale += dqs.scale.xyz * boneWeights.z;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.z = -boneWeights.z;
            bindposeReal += dqs.r * boneWeights.z;
            bindposeDual += dqs.d * boneWeights.z;
        }
        if (boneWeights.w > 0.0 && boneWeights.w < 0.5)
        {
            dqs = transformUnionDqsToDqs(_latiosBindPoses[skeletonBase.y + boneIndices.w]);
            localScale += dqs.scale.xyz * boneWeights.w;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.w = -boneWeights.w;
            bindposeReal += dqs.r * boneWeights.w;
            bindposeDual += dqs.d * boneWeights.w;
        }

        {
            // Todo: Deform via DQS directly?
            float mag = length(bindposeReal);
            bindposeReal /= mag;
            bindposeDual /= mag;

            Qvvs bpQvvs = (Qvvs)0;
            bpQvvs.rotation = bindposeReal;
            bindposeReal.xyz = -bindposeReal.xyz;
            bpQvvs.position.xyz = mulQuatQuat(2 * bindposeDual, bindposeReal).xyz;
            bpQvvs.stretchScale = float4(1, 1, 1, 1);

            float3x4 deform = qvvsToMatrix(bpQvvs);
            float3x4 scale = float3x4(
                localScale.x, 0, 0, 0,
                0, localScale.y, 0, 0,
                0, 0, localScale.z, 0
                );
            deform = mul3x4(scale, deform);

            position = mul(deform, float4(position, 1));
            normal = mul(deform, float4(normal, 0));
            tangent = mul(deform, float4(tangent, 0));
        }
    }

    {
        Dqs dqs = transformUnionDqsToDqs(readBone(skeletonBase.x + boneIndices.x));
        float4 worldReal = dqs.r * boneWeights.x;
        float4 worldDual = dqs.d * boneWeights.x;
        float3 localScale = dqs.scale;
        float4 firstBoneRot = dqs.r;

        if (boneWeights.y > 0.0)
        {
            dqs = transformUnionDqsToDqs(readBone(skeletonBase.x + boneIndices.y));
            localScale += dqs.scale.xyz * boneWeights.y;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.y = -boneWeights.y;
            worldReal += dqs.r * boneWeights.y;
            worldDual += dqs.d * boneWeights.y;
        }
        if (boneWeights.z > 0.0)
        {
            dqs = transformUnionDqsToDqs(readBone(skeletonBase.x + boneIndices.z));
            localScale += dqs.scale.xyz * boneWeights.z;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.z = -boneWeights.z;
            worldReal += dqs.r * boneWeights.z;
            worldDual += dqs.d * boneWeights.z;
        }
        if (boneWeights.w > 0.0 && boneWeights.w < 0.5)
        {
            dqs = transformUnionDqsToDqs(readBone(skeletonBase.x + boneIndices.w));
            localScale += dqs.scale.xyz * boneWeights.w;
            if (dot(dqs.r, firstBoneRot) < 0)
                boneWeights.w = -boneWeights.w;
            worldReal += dqs.r * boneWeights.w;
            worldDual += dqs.d * boneWeights.w;
        }

        {
            float mag = length(worldReal);
            worldReal /= mag;
            worldDual /= mag;

            Qvvs worldQvvs = (Qvvs)0;
            worldQvvs.rotation = worldReal;
            worldReal.xyz = -worldReal.xyz;
            worldQvvs.position.xyz = mulQuatQuat(2 * worldDual, worldReal).xyz;
            worldQvvs.stretchScale = float4(localScale, 1);

            float3x4 deform = qvvsToMatrix(worldQvvs);

            position = mul(deform, float4(position, 1));
            normal = mul(deform, float4(normal, 0));
            tangent = mul(deform, float4(tangent, 0));
        }
    }
#endif
}

#endif