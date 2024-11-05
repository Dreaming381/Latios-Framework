#if NETCODE_PROJECT
using Latios.Systems;
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Latios.Compatibility.UnityNetCode.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    [UpdateAfter(typeof(ManagedStructComponentsReactiveSystem))]
    [UpdateAfter(typeof(CollectionComponentsReactiveSystem))]
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct EnablePreSpawnedGhostsInEditorSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<Disabled>().With<PreSpawnedGhostIndex>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.RemoveComponent<Disabled>(m_query);
        }
    }
}
#endif

