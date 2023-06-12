using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A tag that specifies the parent's WorldTransform should be copied onto this Entity's WorldTransform exactly.
    /// With this component, an Entity does not need LocalTransform nor ParentToWorldTransform, saving memory.
    /// </summary>
    public struct CopyParentWorldTransformTag : IComponentData { }
}

