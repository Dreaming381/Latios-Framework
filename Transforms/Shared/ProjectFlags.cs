using Unity.Burst;

#if LATIOS_BURST_DETERMINISM
[assembly: BurstCompile(FloatMode = FloatMode.Deterministic)]
#endif

