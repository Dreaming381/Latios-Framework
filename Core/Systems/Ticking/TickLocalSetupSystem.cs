#if false
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Systems
{
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct TickLocalSetupSystem : ISystem
    {
        public float tickDeltaTime;

        LatiosWorldUnmanaged latiosWorld;

        double finishedTicksElapsedTime;
        float timeInTick;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            tickDeltaTime            = 1f;
            finishedTicksElapsedTime = 0.0;
            timeInTick               = 0f;

            latiosWorld.worldBlackboardEntity.AddComponent(new TypePack<TickLocalTiming, AdvanceTick>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt        = Time.DeltaTime;
            int rollovers = 0;
            timeInTick += dt;
            while (timeInTick >= tickDeltaTime)
            {
                rollovers++;
                timeInTick -= tickDeltaTime;
            }

            if (rollovers == 0)
            {
                // We did not advance the tick. Roll back.
                latiosWorld.worldBlackboardEntity.SetComponentData(new TickLocalTiming
                {
                    ticksThisFrame = 1,
                    deltaTime      = tickDeltaTime,
                    elapsedTime    = finishedTicksElapsedTime + tickDeltaTime
                });
                latiosWorld.worldBlackboardEntity.SetEnabled<AdvanceTick>(false);
            }
            else
            {
                var ticksToRun = rollovers + 1;
                latiosWorld.worldBlackboardEntity.SetComponentData(new TickLocalTiming
                {
                    ticksThisFrame = ticksToRun,
                    deltaTime      = tickDeltaTime,
                    elapsedTime    = finishedTicksElapsedTime + ticksToRun * tickDeltaTime
                });
                latiosWorld.worldBlackboardEntity.SetEnabled<AdvanceTick>(true);
                finishedTicksElapsedTime += rollovers + tickDeltaTime;
            }
        }
    }
}
#endif

