using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Systems
{
    [BurstCompile, DisableAutoCreation]
    internal partial struct BlackboardUnmanagedSystem : ISystem
    {
        public EntityWith<WorldBlackboardTag> worldBlackboardEntity;
        public EntityWith<SceneBlackboardTag> sceneBlackboardEntity;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.Enabled = false;
        }
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Enabled = false;
        }
    }
}

namespace Latios
{
    public static class SystemStateBlackboardExtensions
    {
        public static BlackboardEntity GetWorldBlackboardEntity(this ref SystemState state)
        {
            var entity = state.WorldUnmanaged.GetExistingUnmanagedSystem<Systems.BlackboardUnmanagedSystem>().Struct.worldBlackboardEntity;
            return new BlackboardEntity(entity, state.EntityManager);
        }

        public static BlackboardEntity GetSceneBlackboardEntity(this ref SystemState state)
        {
            var entity = state.WorldUnmanaged.GetExistingUnmanagedSystem<Systems.BlackboardUnmanagedSystem>().Struct.sceneBlackboardEntity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (entity == Entity.Null)
            {
                throw new InvalidOperationException(
                    "The sceneBlackboardEntity has not been initialized yet. If you are trying to access this entity in OnCreate(), please use OnNewScene() or another callback instead.");
            }
#endif
            return new BlackboardEntity(entity, state.EntityManager);
        }
    }
}

