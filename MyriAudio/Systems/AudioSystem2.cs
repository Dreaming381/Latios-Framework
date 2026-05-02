using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Latios.Transforms;

namespace Latios.Myri
{
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AudioSystem2 : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        
        }
    }
}
