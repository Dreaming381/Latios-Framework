using System.Diagnostics;
using Latios.PhysicsEngine;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios
{
    [AlwaysUpdateSystem]
    public class FindPairsBuildLayerExample : SubSystem
    {
        private EntityQuery m_query;
        protected override void OnCreate()
        {
            m_query = Fluent.PatchQueryForBuildingCollisionLayer().Build();
        }

        public override bool ShouldUpdateSystem()
        {
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsBuildLayerExample");
        }

        protected override void OnUpdate()
        {
            CollisionLayerSettings settings = new CollisionLayerSettings
            {
                worldSubdivisionsPerAxis = new int3(2, 2, 2),
                worldAABB                = new Aabb { min = new float3(-50f, -50f, -50f), max = new float3(50f, 50f, 50f) },
            };
            var jh = Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob);
            jh.Complete();
            //Physics.WithSettings(settings).BuildCollisionLayer(m_query, this).Run(out CollisionLayer layer, Allocator.TempJob);
            PhysicsDebug.DrawLayer(layer);
            layer.Dispose();
        }
    }
}

