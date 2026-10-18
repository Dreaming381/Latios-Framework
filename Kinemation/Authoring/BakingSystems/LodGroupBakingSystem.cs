using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct LodGroupBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
#if LATIOS_TRANSFORMS_UNITY
            public void Execute(ref LodGroupReferencePoint localRef, in BakingLodGroupReferencePoint worldRef, in LocalToWorld ltw)
            {
                localRef.localPosition = math.mul(math.inverse(new float3x3(ltw.Value)), worldRef.worldPosition - ltw.Value.c3.xyz);
            }
#else
            public void Execute(ref LodGroupReferencePoint localRef, in BakingLodGroupReferencePoint worldRef, in WorldTransform transform)
            {
                localRef.localPosition = qvvs.InverseTransformPoint(in transform.worldTransform, worldRef.worldPosition);
            }
#endif
        }
    }
}

