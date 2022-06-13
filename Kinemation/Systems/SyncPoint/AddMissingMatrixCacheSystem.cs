using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class AddMissingMatrixCacheSystem : SubSystem
    {
        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = Fluent.WithAll<BuiltinMaterialPropertyUnity_MatrixPreviousM>(true).Without<MatrixPreviousCache>().IncludeDisabled().IncludePrefabs().Build();
        }

        protected override void OnUpdate()
        {
            EntityManager.AddComponent<MatrixPreviousCache>(m_query);
        }
    }
}

