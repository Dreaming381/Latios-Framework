using System;
using Unity.Entities;

namespace Latios
{
    public struct WorldBlackboardTag : IComponentData { }

    public struct SceneBlackboardTag : IComponentData { }

    public enum BlackboardScope
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

    //[GenerateAuthoringComponent]
    public struct BlackboardEntityData : IComponentData
    {
        public BlackboardScope blackboardScope;
        public MergeMethod     mergeMethod;
    }
}

