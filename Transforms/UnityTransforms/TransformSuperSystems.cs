#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Transforms.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(EndInitializationEntityCommandBufferSystem))]
    public partial class MotionHistoryUpdateSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }
}
#endif

