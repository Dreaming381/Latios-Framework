using Latios.Transforms.Systems;
using Unity.Burst;
using Unity.Entities;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PreTransformSuperSystem))]
    [UpdateBefore(typeof(CopyTransformFromBoneSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ForceInitializeUninitializedOptimizedSkeletonsSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithAspect<OptimizedSkeletonAspect>().Without<OptimizedSkeletonTag>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job().ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobEntity
        {
            public void Execute(OptimizedSkeletonAspect skeleton)
            {
                skeleton.ForceInitialize();
            }
        }
    }
}

