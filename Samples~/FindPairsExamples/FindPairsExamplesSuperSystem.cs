using Latios;
using Unity.Entities;

namespace Dragons
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class FindPairsExamplesSuperSystem : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<FindPairsOverlapsExampleSystem>();
            GetOrCreateAndAddSystem<FindPairsSimpleBenchmark>();
            GetOrCreateAndAddSystem<FindPairsBuildLayerExample>();
        }
    }
}

