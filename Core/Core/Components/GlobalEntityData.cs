using System;
using Unity.Entities;

namespace Latios
{
    public struct WorldGlobalTag : IComponentData { }

    public struct SceneGlobalTag : IComponentData { }

    public enum GlobalScope
    {
        World,
        Scene
    }
    public enum MergeMethod
    {
        Overwrite,
        KeepExisting,
        ErrorOnConflict
    }

    [GenerateAuthoringComponent]
    public struct GlobalEntityData : IComponentData
    {
        public GlobalScope globalScope;
        public MergeMethod mergeMethod;
    }
}

