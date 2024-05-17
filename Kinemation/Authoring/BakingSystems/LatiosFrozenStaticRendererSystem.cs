using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

// Todo: This is a dead system due to the query for RenderMesh. Unity hasn't updated the query either.

namespace Latios.Kinemation.Authoring
{
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LatiosFrozenStaticRendererSystem : ISystem
    {
        EntityQuery m_query;

        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<SceneSection>(true).With<MaterialMeshInfo>(true).With<Unity.Transforms.Static>().WithWorldTransformReadOnly()
                      .Without<FrozenRenderSceneTag>().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.GetAllUniqueSharedComponents<SceneSection>(out var sections, Allocator.Temp);

            // @TODO: Perform full validation that all Low LOD levels are in section 0
            int hasStreamedLOD = 0;
            foreach (var section in sections)
            {
                m_query.SetSharedComponentFilter(section);
                if (section.Section != 0)
                    hasStreamedLOD = 1;
            }

            foreach (var section in sections)
            {
                m_query.SetSharedComponentFilter(section);
                state.EntityManager.AddSharedComponent(m_query, new FrozenRenderSceneTag {
                    SceneGUID = section.SceneGUID, SectionIndex = section.Section, HasStreamedLOD = hasStreamedLOD
                });
            }

            m_query.ResetFilter();
        }
    }
}

