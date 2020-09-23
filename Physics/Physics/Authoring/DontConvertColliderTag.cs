using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine.Authoring
{
    [Serializable]
    [GenerateAuthoringComponent]
    public struct DontConvertColliderTag : IComponentData { }
}

