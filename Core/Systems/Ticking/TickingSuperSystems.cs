#if false
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Systems
{
    [DisableAutoCreation]
    public partial class TickReadInputSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    // Only updated on lag frames
    [DisableAutoCreation]
    public partial class TickSustainInputSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    [DisableAutoCreation]
    public partial class TickUpdateHistorySuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    [DisableAutoCreation]
    public partial class TickSimulationSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    [DisableAutoCreation]
    public partial class TickInterpolateSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    [DisableAutoCreation]
    public partial class TickLocalSuperSystem : RootSuperSystem
    {
        SystemHandle localSetupSystem;
        SystemHandle readInputSuperSystem;
        SystemHandle updateHistorySuperSystem;
        SystemHandle simulationSuperSystem;
        SystemHandle interpolateSuperSystem;
        SystemHandle expirablesSystem;
        SystemHandle syncPlaybackSystem;
        SystemHandle syncPointSuperSystem;
        SystemHandle sustainInputSuperSystem;

        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<TickLocalSetupSystem>();
            localSetupSystem         = World.GetExistingSystem<TickLocalSetupSystem>();
            readInputSuperSystem     = GetOrCreateAndAddManagedSystem<TickReadInputSuperSystem>().SystemHandle;
            updateHistorySuperSystem = GetOrCreateAndAddManagedSystem<TickUpdateHistorySuperSystem>().SystemHandle;
            simulationSuperSystem    = GetOrCreateAndAddManagedSystem<TickSimulationSuperSystem>().SystemHandle;
            interpolateSuperSystem   = GetOrCreateAndAddManagedSystem<TickInterpolateSuperSystem>().SystemHandle;
            GetOrCreateAndAddUnmanagedSystem<AutoDestroyExpirablesSystem>();
            expirablesSystem        = World.GetExistingSystem<AutoDestroyExpirablesSystem>();
            syncPlaybackSystem      = GetOrCreateAndAddManagedSystem<SyncPointPlaybackSystemDispatch>().SystemHandle;
            syncPointSuperSystem    = GetOrCreateAndAddManagedSystem<LatiosWorldSyncGroup>().SystemHandle;
            sustainInputSuperSystem = GetOrCreateAndAddManagedSystem<TickSustainInputSuperSystem>().SystemHandle;
        }

        protected override void OnUpdate()
        {
            SuperSystem.UpdateSystem(latiosWorldUnmanaged, localSetupSystem);
            SuperSystem.UpdateSystem(latiosWorldUnmanaged, readInputSuperSystem);

            // Todo: Replace this with the actual values computed, and push and pop time.
            var timing = worldBlackboardEntity.GetComponentData<TickLocalTiming>();
            World.PushTime(new Unity.Core.TimeData(timing.elapsedTime, timing.deltaTime));
            for (int i = 0; i < timing.ticksThisFrame; i++)
            {
                SuperSystem.UpdateSystem(latiosWorldUnmanaged, updateHistorySuperSystem);
                SuperSystem.UpdateSystem(latiosWorldUnmanaged, simulationSuperSystem);

                if (i + 1 < timing.ticksThisFrame)
                {
                    timing.elapsedTime += timing.deltaTime;
                    World.SetTime(new Unity.Core.TimeData(timing.elapsedTime, timing.deltaTime));
                    // This sync point block here is a backup only for when the framerate falls below the tick rate.
                    // This ideally shouldn't happen very often, as the target framerate is supposed to be higher than
                    // the tick rate. However, when it does happen, we want to initialize things again as if this is a
                    // new frame, though we skip all the variable update stuff.
                    SuperSystem.UpdateSystem(latiosWorldUnmanaged, expirablesSystem);
                    SuperSystem.UpdateSystem(latiosWorldUnmanaged, syncPlaybackSystem);
                    SuperSystem.UpdateSystem(latiosWorldUnmanaged, syncPointSuperSystem);
                    SuperSystem.UpdateSystem(latiosWorldUnmanaged, sustainInputSuperSystem);
                }
            }
            World.PopTime();

            SuperSystem.UpdateSystem(latiosWorldUnmanaged, interpolateSuperSystem);
        }
    }
}

/*
   No Network
   - TickLocalSetupSystem
   - TickReadInputSuperSystem
    - Advance Frame - Reset Input
    - Rollback Frame - Accumulate Input
   - TickUpdateHistorySuperSystem
    - Advance Frame - Copy Previous to TwoAgo, and then Current to Previous
    - Rollback Frame - Copy Previous to Current
   - TickSimulationSuperSystem
   - TickInterpolateSuperSystem

   Networking uses a "predict-the-past" for game environment and other players.
   The systems predicting the past have the ability to "read the future" as a means to implement simple interpolation.
   Every networked entity can exist with its own delay target, although usually these are grouped by player.
   This allows interpolation to seemlessly switch to extrapolation, and allows mediation between predicted and streamed physics.
   It also allows for dead-reckoning approaches to be inserted into the pipeline.
   Everything keeps a tick-level history on the client so it can easily be retrieved and when predicting other entities with older data.

   Network Client
   - ClientSetupSystem
   - TickReadInputSuperSystem
    - Advance Frame - Reset Input
    - Rollback Frame - Accumulate Input
   - SendInputSystem
   - BuildSnapshotApplicationQueuesSystem
   - ClearSimulateTagsSystem
   - PredictionSuperSystem
    - PredictionSyncPointSuperSystem
        - SyncPointPlaybackSystem
    - ApplySnapshotsForTickSystem
        - Responsible for structural changes and matching predictive spawns
        - Automatically enables Simulate tags for direct updates
    - PropagateSimulateTagsSuperSystem - For interactions known in advance, such as dead-reckoned entities
    - TickUpdateHistorySuperSystem - Only for Simulate enabled
        - Advance Frame - Copy Previous to TwoAgo, and then Current to Previous
        - Rollback Frame - Copy Previous to Current
    - ApplyTickRecordedValuesSuperSystem - Only for Simulate disabled, and maybe some other criteria for performance
    - TickSimulationSuperSystem - Only for Simulate enabled
    - RecordTickValuesSystem
   - TickInterpolateSuperSystem

   Network Server
   - InitializationSystemGroup - sync points are here
   - UpdateInputQueuesSystem
   - ApplyInputsForTickSystem
   - TickUpdateHistorySuperSystem
    - Advance Frame - Copy Previous to TwoAgo, and then Current to Previous
   - TickSimulationSuperSystem
   - SendSnapshotsSuperSystem
 */
#endif

