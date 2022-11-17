using Unity.Burst;
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddMissingMatrixCacheSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAll<BuiltinMaterialPropertyUnity_MatrixPreviousM>(true).Without<MatrixPreviousCache>().IncludeDisabled().IncludePrefabs().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.AddComponent<MatrixPreviousCache>(m_query);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

