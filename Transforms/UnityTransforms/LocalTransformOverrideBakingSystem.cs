#if LATIOS_TRANSFORMS_UNITY
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

// Note: Unity has an order version filter for updating transforms. If a component is removed, Unity will rebake the transforms.

namespace Latios.Transforms.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderLast = true)]  // Unity's TransformBakingSystem is internal.
    [BurstCompile]
    public partial struct LocalTransformOverrideBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new LocalJob().ScheduleParallel();
            new ParentJob().ScheduleParallel();
        }

        [BurstCompile]
        partial struct LocalJob : IJobEntity
        {
            public void Execute(ref Unity.Transforms.LocalTransform dst, in BakedLocalTransformOverride src)
            {
                dst = Unity.Transforms.LocalTransform.FromPositionRotationScale(src.localTransform.position, src.localTransform.rotation, src.localTransform.scale);
            }
        }

        [BurstCompile]
        partial struct ParentJob : IJobEntity
        {
            public void Execute(ref Unity.Transforms.Parent dst, in BakedParentOverride src)
            {
                dst.Value = src.parent;
            }
        }
    }
}
#endif

