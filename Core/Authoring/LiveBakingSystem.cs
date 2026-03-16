using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [BurstCompile]
    public partial struct LiveBakingSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().Without<LiveBakedTag>().IncludePrefabs().IncludeDisabledEntities().Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Keep the framework inert if no bootstrap is installed.
            bool foundLatiosWorld = false;
            foreach (var world in World.All)
            {
                if (world is LatiosWorld latiosWorld)
                {
                    var system = world.GetExistingSystemManaged<Latios.Systems.BeforeLiveBakingSuperSystem>();
                    SuperSystem.UpdateSystem(latiosWorld.latiosWorldUnmanaged, system.SystemHandle);
                }
            }
            if (foundLatiosWorld)
                OnUpdateBurst(ref state);
        }

        [BurstCompile]
        public void OnUpdateBurst(ref SystemState state)
        {
            state.EntityManager.AddComponent<LiveBakedTag>(m_query);
        }
    }

    // Unity bug doesn't allow ISystem with this flag
    //[WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    //[BurstCompile]
    //public partial struct NonLiveBakingSystem : ISystem
    //{
    //    EntityQuery m_query;
    //
    //    [BurstCompile]
    //    public void OnCreate(ref SystemState state)
    //    {
    //        m_query = state.Fluent().With<LiveBakedTag>(true).IncludePrefabs().IncludeDisabledEntities().Build();
    //    }
    //
    //    [BurstCompile]
    //    public void OnUpdate(ref SystemState state)
    //    {
    //        state.EntityManager.RemoveComponent<LiveBakedTag>(m_query);
    //    }
    //}
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    public partial class NonLiveBakingSystem : SystemBase
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = CheckedStateRef.Fluent().With<LiveBakedTag>(true).IncludePrefabs().IncludeDisabledEntities().Build();
        }

        protected override void OnUpdate()
        {
            EntityManager.RemoveComponent<LiveBakedTag>(m_query);
        }
    }
}

