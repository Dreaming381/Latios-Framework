using Latios;
using Unity.Entities;

namespace Dragons
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class FindPairsExamplesSuperSystem : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddManagedSystem<FindPairsOverlapsExampleSystem>();
            GetOrCreateAndAddManagedSystem<FindPairsSimpleBenchmark>();
            GetOrCreateAndAddManagedSystem<FindPairsBuildLayerExample>();
        }
    }
}

