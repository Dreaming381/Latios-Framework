using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios.Systems
{
    /// <summary>
    /// A top-level system group which updates during Unity Engine's Fixed Update loop, useful for projects that rely on PhysX.
    /// This is not created by default by any of the bootstrap templates. You must add it via BootstrapTools.
    /// </summary>
    [DisableAutoCreation, NoGroupInjection]
    public partial class FixedSimulationSystemGroup : ComponentSystemGroup
    {
    }
}

