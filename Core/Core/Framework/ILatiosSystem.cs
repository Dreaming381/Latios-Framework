using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public interface ILatiosSystem
    {
        LatiosWorld latiosWorld { get; }

        BlackboardEntity worldBlackboardEntity { get; }
        BlackboardEntity sceneBlackboardEntity { get; }

        bool ShouldUpdateSystem();
        void OnNewScene();

        EntityQuery GetEntityQuery(EntityQueryDesc desc);
        EntityQuery GetEntityQuery(EntityQueryDescBuilder desc);

        FluentQuery Fluent { get; }
    }
}

