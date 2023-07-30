#define LATIOS_ALPHA

#if LATIOS_ALPHA
using Latios.Transforms;
#else
using System.Runtime.InteropServices;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        public static NativeText LogDistanceBetween(in Collider colliderA,
                                                    in TransformQvvs transformA,
                                                    in Collider colliderB,
                                                    in TransformQvvs transformB,
                                                    float maxDistance,
                                                    Allocator allocator = Allocator.Temp)
        {
            var writer = new HexWriter(allocator);
            writer.Write((byte)QueryType.DistanceBetweenColliderCollider);
            writer.WriteCollider(colliderA);
            writer.WriteTransform(transformA);
            writer.WriteCollider(colliderB);
            writer.WriteTransform(transformB);
            writer.WriteFloat(maxDistance);
            return writer.content;
        }

        enum QueryType : byte
        {
            //DistanceBetweenPointCollider = 0,
            DistanceBetweenColliderCollider = 1,
        }

        unsafe struct HexWriter : BinaryWriter
        {
            public NativeText content;

            public HexWriter(Allocator allocator = Allocator.Temp)
            {
                content  = new NativeText(allocator);
                Position = 0;
            }
            public byte* Data => content.GetUnsafePtr();

            public int Length => content.Length;

            public long Position { get; set; }

            public void Dispose()
            {
                content.Dispose();
            }

            public void WriteBytes(void* data, int bytes)
            {
                var bytePtr = (byte*)data;
                for (int i = 0; i < bytes; i++)
                {
                    var b          = *bytePtr;
                    var highNibble = (b >> 4) & 0xf;
                    var lowNibble  = b & 0xf;
                    content.Append(CharFromNibble(highNibble));
                    content.Append(CharFromNibble(lowNibble));
                    bytePtr++;
                    Position++;
                }

                static char CharFromNibble(int nibble)
                {
                    return nibble switch
                           {
                               0 => '0',
                               1 => '1',
                               2 => '2',
                               3 => '3',
                               4 => '4',
                               5 => '5',
                               6 => '6',
                               7 => '7',
                               8 => '8',
                               9 => '9',
                               10 => 'a',
                               11 => 'b',
                               12 => 'c',
                               13 => 'd',
                               14 => 'e',
                               15 => 'f',
                               _ => ' ',
                           };
                }
            }
        }

        unsafe static void WriteCollider<T>(this ref T writer, Collider collider) where T : unmanaged, BinaryWriter
        {
            writer.WriteBytes(UnsafeUtility.AddressOf(ref collider), UnsafeUtility.SizeOf<Collider>());
            if (collider.type == ColliderType.Convex)
            {
                ConvexCollider convex = collider;
                writer.Write(convex.convexColliderBlob);
            }
            else if (collider.type == ColliderType.TriMesh)
            {
                TriMeshCollider triMesh = collider;
                writer.Write(triMesh.triMeshColliderBlob);
            }
            else if (collider.type == ColliderType.Compound)
            {
                CompoundCollider compound = collider;
                writer.Write(compound.compoundColliderBlob);
            }
        }

        unsafe static void WriteTransform<T>(this ref T writer, TransformQvvs transform) where T : unmanaged, BinaryWriter
        {
            writer.WriteBytes(UnsafeUtility.AddressOf(ref transform), UnsafeUtility.SizeOf<TransformQvvs>());
        }

        unsafe static void WriteFloat<T>(this ref T writer, float value) where T : unmanaged, BinaryWriter
        {
            writer.WriteBytes(&value, sizeof(float));
        }

#if !LATIOS_ALPHA
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        public struct TransformQvvs
        {
            [FieldOffset(0)] public quaternion rotation;  // All qvvs operations assume this value is normalized
            [FieldOffset(16)] public float3 position;
            [FieldOffset(28)] public int worldIndex;  // User-define-able, can be used for floating origin or something
            [FieldOffset(32)] public float3 stretch;
            [FieldOffset(44)] public float scale;

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
#endif
    }
}

