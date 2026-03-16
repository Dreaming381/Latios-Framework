using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyMipMapStreamingLevelsSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            latiosWorld.worldBlackboardEntity.AddComponent(new TypePack<MipMapStreamingAssignment, MipMapCameraParameters>());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var assignments = latiosWorld.worldBlackboardEntity.GetBuffer<MipMapStreamingAssignment>(false);
            foreach (var assignment in assignments)
            {
                assignment.texture.RequestMipMapLevelIfValid(assignment.level);
            }
            assignments.Clear();
            latiosWorld.worldBlackboardEntity.GetBuffer<MipMapCameraParameters>(false).Clear();
        }
    }
}

