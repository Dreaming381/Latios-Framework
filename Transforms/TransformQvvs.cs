using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Latios.Transforms
{
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct TransformQvvs
    {
        [FieldOffset(0)] public quaternion rotation;  // All qvvs operations assume this value is normalized
        [FieldOffset(16)] public float3    position;
        [FieldOffset(28)] public int       worldIndex;  // User-define-able, can be used for floating origin or something
        [FieldOffset(32)] public float3    stretch;
        [FieldOffset(44)] public float     scale;

        public static readonly TransformQvvs identity = new TransformQvvs
        {
            position   = float3.zero,
            rotation   = quaternion.identity,
            scale      = 1f,
            stretch    = 1f,
            worldIndex = 0
        };

        public TransformQvvs(float3 position, quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
            scale         = 1f;
            stretch       = 1f;
            worldIndex    = 0;
        }

        public TransformQvvs(float3 position, quaternion rotation, float scale, float3 stretch, int worldIndex = 0)
        {
            this.position   = position;
            this.rotation   = rotation;
            this.scale      = scale;
            this.stretch    = stretch;
            this.worldIndex = worldIndex;
        }

        public TransformQvvs(RigidTransform rigidTransform)
        {
            position   = rigidTransform.pos;
            rotation   = rigidTransform.rot;
            scale      = 1f;
            stretch    = 1f;
            worldIndex = 0;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct TransformQvs
    {
        [FieldOffset(0)] public quaternion rotation;  // All qvvs operations assume this value is normalized
        [FieldOffset(16)] public float3    position;
        [FieldOffset(28)] public float     scale;

        public static readonly TransformQvs identity = new TransformQvs
        {
            position = float3.zero,
            rotation = quaternion.identity,
            scale    = 1f
        };

        public TransformQvs(float3 position, quaternion rotation, float scale = 1f)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale    = scale;
        }
    }

    public static class qvvs
    {
        // Assuming B represents a transform in A's space, this converts B into the same space
        // A resides in
        public static TransformQvvs mul(in TransformQvvs a, in TransformQvvs b)
        {
            return new TransformQvvs
            {
                rotation   = math.mul(a.rotation, b.rotation),
                position   = a.position + math.rotate(a.rotation, b.position * a.stretch * a.scale),  // We scale by A's stretch and scale because we are leaving A's space
                worldIndex = b.worldIndex,  // We retain B's worldIndex as the result is B in a different coordinate space
                stretch    = b.stretch,  // We retain B's stretch as the result is B in a different coordinate space
                scale      = a.scale * b.scale
            };
        }

        // Assuming B represents a transform in A's space, this converts B into the same space
        // A resides in, where bStretch is forwarded from bWorld
        public static void mul(ref TransformQvvs bWorld, in TransformQvvs a, in TransformQvs b)
        {
            bWorld.rotation = math.mul(a.rotation, b.rotation);
            bWorld.position = a.position + math.rotate(a.rotation, b.position * a.stretch * a.scale);
            // bWorld.worldIndex is preserved
            // bWorld.stretch is preserved
            bWorld.scale = a.scale * b.scale;
        }

        public static TransformQvs inversemul(in TransformQvvs a, in TransformQvvs b)
        {
            var inverseRotation = math.conjugate(a.rotation);
            var rcps            = math.rcp(new float4(a.stretch, a.scale));
            return new TransformQvs
            {
                position = math.rotate(inverseRotation, b.position - a.position) * rcps.xyz * rcps.w,
                rotation = math.mul(inverseRotation, b.rotation),
                scale    = rcps.w * b.scale
            };
        }

        public static TransformQvvs inversemulqvvs(in TransformQvvs a, in TransformQvvs b)
        {
            var inverseRotation = math.conjugate(a.rotation);
            var rcps            = math.rcp(new float4(a.stretch, a.scale));
            return new TransformQvvs
            {
                position   = math.rotate(inverseRotation, b.position - a.position) * rcps.xyz * rcps.w,
                rotation   = math.mul(inverseRotation, b.rotation),
                scale      = rcps.w * b.scale,
                stretch    = b.stretch,
                worldIndex = b.worldIndex
            };
        }

        public static float3x4 ToMatrix3x4(in this TransformQvvs transform)
        {
            return float3x4.TRS(transform.position, transform.rotation, transform.scale * transform.stretch);
        }

        public static float4x4 ToMatrix4x4(in this TransformQvvs transform)
        {
            return float4x4.TRS(transform.position, transform.rotation, transform.scale * transform.stretch);
        }

        public static float3x4 ToMatrix3x4(in this TransformQvs transform)
        {
            return float3x4.TRS(transform.position, transform.rotation, transform.scale);
        }

        public static float4x4 ToMatrix4x4(in this TransformQvs transform)
        {
            return float4x4.TRS(transform.position, transform.rotation, transform.scale);
        }

        public static float3x4 ToMatrix3x4(in this TransformQvs transform, float3 stretch)
        {
            return float3x4.TRS(transform.position, transform.rotation, transform.scale * stretch);
        }

        public static float4x4 ToMatrix4x4(in this TransformQvs transform, float3 stretch)
        {
            return float4x4.TRS(transform.position, transform.rotation, transform.scale * stretch);
        }

        public static float3x4 ToInverseMatrix3x4(in this TransformQvvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(new float4(transform.stretch, transform.scale));
            var scaleInverse    = rcp.xyz * rcp.w;
            return new float3x4
            {
                c0 = rotMat.c0 * scaleInverse,
                c1 = rotMat.c1 * scaleInverse,
                c2 = rotMat.c2 * scaleInverse,
                c3 = positionInverse * scaleInverse
            };
        }

        public static float3x4 ToInverseMatrix3x4IgnoreStretch(in this TransformQvvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(transform.scale);
            return new float3x4
            {
                c0 = rotMat.c0,
                c1 = rotMat.c1,
                c2 = rotMat.c2,
                c3 = positionInverse
            } *rcp;
        }

        public static float3x4 ToInverseMatrix3x4(in this TransformQvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(transform.scale);
            return new float3x4
            {
                c0 = rotMat.c0,
                c1 = rotMat.c1,
                c2 = rotMat.c2,
                c3 = positionInverse
            } *rcp;
        }

        public static float3x4 ToInverseMatrix3x4(in this TransformQvs transform, float3 stretch)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(new float4(stretch, transform.scale));
            var scaleInverse    = rcp.xyz * rcp.w;
            return new float3x4
            {
                c0 = rotMat.c0 * scaleInverse,
                c1 = rotMat.c1 * scaleInverse,
                c2 = rotMat.c2 * scaleInverse,
                c3 = positionInverse * scaleInverse
            };
        }

        public static float4x4 ToInverseMatrix4x4(in this TransformQvvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(new float4(transform.stretch, transform.scale));
            var scaleInverse    = rcp.xyz * rcp.w;
            return new float4x4
            {
                c0 = new float4(rotMat.c0 * scaleInverse,       0f),
                c1 = new float4(rotMat.c1 * scaleInverse,       0f),
                c2 = new float4(rotMat.c2 * scaleInverse,       0f),
                c3 = new float4(positionInverse * scaleInverse, 1f)
            };
        }

        public static float4x4 ToInverseMatrix4x4IgnoreStretch(in this TransformQvvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var scaleInverse    = math.rcp(transform.scale);
            return new float4x4
            {
                c0 = new float4(rotMat.c0 * scaleInverse, 0f),
                c1 = new float4(rotMat.c1 * scaleInverse, 0f),
                c2 = new float4(rotMat.c2 * scaleInverse, 0f),
                c3 = new float4(positionInverse * scaleInverse, 1f)
            };
        }

        public static float4x4 ToInverseMatrix4x4(in this TransformQvs transform)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var scaleInverse    = math.rcp(transform.scale);
            return new float4x4
            {
                c0 = new float4(rotMat.c0 * scaleInverse, 0f),
                c1 = new float4(rotMat.c1 * scaleInverse, 0f),
                c2 = new float4(rotMat.c2 * scaleInverse, 0f),
                c3 = new float4(positionInverse * scaleInverse, 1f)
            };
        }

        public static float4x4 ToInverseMatrix4x4(in this TransformQvs transform, float3 stretch)
        {
            var rotationInverse = math.conjugate(transform.rotation);
            var rotMat          = new float3x3(rotationInverse);
            var positionInverse = math.rotate(rotationInverse, -transform.position);
            var rcp             = math.rcp(new float4(stretch, transform.scale));
            var scaleInverse    = rcp.xyz * rcp.w;
            return new float4x4
            {
                c0 = new float4(rotMat.c0 * scaleInverse, 0f),
                c1 = new float4(rotMat.c1 * scaleInverse, 0f),
                c2 = new float4(rotMat.c2 * scaleInverse, 0f),
                c3 = new float4(positionInverse * scaleInverse, 1f)
            };
        }

        public static float3 TransformPoint(in TransformQvvs qvvs, float3 point)
        {
            return qvvs.position + math.rotate(qvvs.rotation, point * qvvs.stretch * qvvs.scale);
        }

        public static float3 InverseTransformPoint(in TransformQvvs qvvs, float3 point)
        {
            var localPoint = math.InverseRotateFast(qvvs.rotation, point - qvvs.position);
            var rcps       = math.rcp(new float4(qvvs.stretch, qvvs.scale));
            return localPoint * rcps.xyz * rcps.w;
        }

        public static float3 TransformDirection(in TransformQvvs qvvs, float3 direction)
        {
            return math.rotate(qvvs.rotation, direction);
        }

        public static float3 TransformDirectionWithStretch(in TransformQvvs qvvs, float3 direction)
        {
            var magnitude = math.length(direction);
            return math.normalizesafe(math.rotate(qvvs.rotation, direction) * qvvs.stretch) * magnitude;
        }

        public static float3 TransformDirectionScaledAndStretched(in TransformQvvs qvvs, float3 direction)
        {
            return math.rotate(qvvs.rotation, direction) * qvvs.stretch * qvvs.scale;
        }

        public static float3 InverseTransformDirection(in TransformQvvs qvvs, float3 direction)
        {
            return math.InverseRotateFast(qvvs.rotation, direction);
        }

        public static float3 InverseTransformDirectionWithStretch(in TransformQvvs qvvs, float3 direction)
        {
            var magnitude = math.length(direction);
            var rcp       = math.rcp(qvvs.stretch);
            return math.normalizesafe(math.InverseRotateFast(qvvs.rotation, direction) * rcp) * magnitude;
        }

        public static float3 InverseTransformDirectionScaledAndStretched(in TransformQvvs qvvs, float3 direction)
        {
            var rcp = math.rcp(new float4(qvvs.stretch, qvvs.scale));
            return math.InverseRotateFast(qvvs.rotation, direction) * rcp.xyz * rcp.w;
        }

        public static quaternion TransformRotation(in TransformQvvs qvvs, quaternion rotation)
        {
            return math.mul(qvvs.rotation, rotation);
        }

        public static quaternion InverseTransformRotation(in TransformQvvs qvvs, quaternion rotation)
        {
            return math.InverseRotateFast(qvvs.rotation, rotation);
        }

        public static float TransformScale(in TransformQvvs qvvs, float scale) => qvvs.scale * scale;

        public static float InverseTransformScale(in TransformQvvs qvvs, float scale) => scale / qvvs.scale;

        public static TransformQvvs RotateAbout(in TransformQvvs qvvs, quaternion rotation, float3 pivot)
        {
            var pivotToOldPosition = qvvs.position - pivot;
            var pivotToNewPosition = math.rotate(rotation, pivotToOldPosition);
            return new TransformQvvs
            {
                position   = qvvs.position + pivotToNewPosition - pivotToOldPosition,
                rotation   = math.mul(rotation, qvvs.rotation),
                scale      = qvvs.scale,
                stretch    = qvvs.stretch,
                worldIndex = qvvs.worldIndex
            };
        }

        public static TransformQvvs IdentityWithWorldIndex(int worldIndex)
        {
            var result        = TransformQvvs.identity;
            result.worldIndex = worldIndex;
            return result;
        }
    }
}

