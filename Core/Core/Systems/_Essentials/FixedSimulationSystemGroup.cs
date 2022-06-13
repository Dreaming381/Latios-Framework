using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios.Systems
{
    [DisableAutoCreation, NoGroupInjection]
    public class FixedSimulationSystemGroup : ComponentSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }
}

