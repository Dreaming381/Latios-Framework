#if false
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A component the lives on the worldBlackbaordEntity and is enabled whenever Ticking is processing a new tick.
    /// When disabled, rollback-and-resimulate behavior should be performed instead.
    /// Usage: Read-Only, read within TickInputReadSuperSystem and TickUpdateHistorySuperSystem
    /// </summary>
    public struct AdvanceTick : IComponentData, IEnableableComponent { }

    /// <summary>
    /// A component that signifies that ticking behavior should be applied to it.
    /// </summary>
    public struct TickedEntityTag : IComponentData { }

    internal struct TickLocalTiming : IComponentData
    {
        public double elapsedTime;
        public float deltaTime;
        public int ticksThisFrame;
    }
}
#endif

