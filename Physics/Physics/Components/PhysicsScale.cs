using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public struct PhysicsScale : IComponentData
    {
        public float3 scale;
        public State  state;
        public bool   ignoreParent;

        public enum State : byte
        {
            None,
            Uniform,
            NonUniform,
            NonComputable,
        }
    }

    public struct AutoUpdatePhysicsScaleTag : IComponentData { }
}

