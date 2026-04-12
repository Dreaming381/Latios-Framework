using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Calligraphics.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct TextRendererInitializeSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_newGlyphsQuery;
        EntityQuery          m_deadMmiQuery;
        EntityQuery          m_deadRenderBoundsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld      = state.GetLatiosWorldUnmanaged();
            m_newGlyphsQuery =
                QueryBuilder().WithPresent<MaterialMeshInfo, RenderBounds>().WithAny<RenderGlyph, AnimatedRenderGlyph>().WithAbsent<PreviousRenderGlyph>().WithOptions(
                    EntityQueryOptions.IgnoreComponentEnabledState).Build();
            m_deadMmiQuery          = QueryBuilder().WithPresent<PreviousRenderGlyph>().WithAbsent<MaterialMeshInfo>().Build();
            m_deadRenderBoundsQuery = QueryBuilder().WithPresent<PreviousRenderGlyph>().WithAbsent<RenderBounds>().Build();
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new NewEntitiesList
            {
                newGlyphEntities = new NativeList<Entity>(Allocator.Persistent)
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var newEntitiesList = latiosWorld.worldBlackboardEntity.GetCollectionComponent<NewEntitiesList>(false);
            state.CompleteDependency();
            newEntitiesList.newGlyphEntities.AddRange(m_newGlyphsQuery.ToEntityArray(state.WorldUpdateAllocator));

            var renderingComponents = new ComponentTypeSet(ComponentType.ReadWrite<PreviousRenderGlyph>(), ComponentType.ReadWrite<GpuState>(),
                                                           ComponentType.ReadWrite<ResidentRange>(), ComponentType.ReadWrite<TextShaderIndex>());
            state.EntityManager.AddComponent(m_newGlyphsQuery, in renderingComponents);
            var glyphComponents = new ComponentTypeSet(ComponentType.ReadWrite<RenderGlyph>(), ComponentType.ReadWrite<AnimatedRenderGlyph>());
            state.EntityManager.RemoveComponent(m_deadMmiQuery,          in glyphComponents);
            state.EntityManager.RemoveComponent(m_deadRenderBoundsQuery, in glyphComponents);
        }
    }
}

