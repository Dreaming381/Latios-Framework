using Unity.Entities;

namespace Latios.Unika.Authoring.Systems
{
    // Unity bug doesn't allow ISystem with this flag
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    public partial class StripLiveBakedScriptsStateSystem : SystemBase
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = CheckedStateRef.Fluent().With<UnikaLiveBakedScriptsState>(true).IncludePrefabs().IncludeDisabledEntities().Build();
        }

        protected override void OnUpdate()
        {
            EntityManager.RemoveComponent<UnikaLiveBakedScriptsState>(m_query);
        }
    }
}
