using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Authoring.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PreBakingSystemGroup), OrderFirst = true)]
    public partial class FixBakingAllocatorSystem : ComponentSystemGroup
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            if (World.GetExistingSystem<WorldUpdateAllocatorResetSystem>() == default)
            {
                var system = World.GetOrCreateSystem<WorldUpdateAllocatorResetSystem>();
                AddSystemToUpdateList(system);
            }
            else
                Enabled = false;
        }
    }
}

