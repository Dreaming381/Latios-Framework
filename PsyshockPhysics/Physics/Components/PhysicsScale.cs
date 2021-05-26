using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
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

        public PhysicsScale(float3 scale)
        {
            if (math.all(scale.x == scale.yz))
            {
                if (scale.x == 1f)
                    state = State.None;
                else
                    state = State.Uniform;
            }
            else
                state    = State.NonUniform;
            this.scale   = scale;
            ignoreParent = false;
        }
    }

    public struct AutoUpdatePhysicsScaleTag : IComponentData { }
}

