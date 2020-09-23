using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public interface ILatiosSystem
    {
        LatiosWorld latiosWorld { get; }

        ManagedEntity worldGlobalEntity { get; }
        ManagedEntity sceneGlobalEntity { get; }

        bool ShouldUpdateSystem();

        EntityQuery GetEntityQuery(EntityQueryDesc desc);

        FluentQuery Fluent { get; }
    }
}

