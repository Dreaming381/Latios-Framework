using System;
using Unity.Entities;

namespace Latios.PhysicsEngine.Tests
{
    public struct EntityPair
    {
        public Entity a;
        public Entity b;

        public EntityPair(Entity aa, Entity bb)
        {
            a = aa;
            b = bb;
        }
    }
}

