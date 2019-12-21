using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    // The concrete type of a collider
    public enum ColliderType : byte
    {
        // Convex types
        Sphere = 0,
        Capsule = 1,
        //Box = 2,
        //Triangle = 3,
        //Quad = 4,
        //Convex = ,
        //Cylinder = ,
        // Composite types
        //Mesh = 7,
        //Compound = 8,
        //Terrain = 9,
        // Dynamic types
    }

    [Serializable]
    public unsafe partial struct Collider : IComponentData, ICollider
    {
        private ColliderType m_type;
        private byte         reserved1;
        private byte         reserved2;
        private byte         reserved3;
        private Storage      m_storage;

        public ColliderType type => m_type;

        private struct Storage
        {
            public float4 a;
            public float4 b;
            public float4 c;
        }
    }
}

