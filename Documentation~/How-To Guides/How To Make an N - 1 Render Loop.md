# How To Make an N – 1 Render Loop

N – 1 rendering is where rendering happens at the beginning of the frame using
the previous frame’s simulation data. On platforms where the render thread is
required to sync with the simulation thread at the end of the frame, this leads
to lower frame times as rendering and simulation can run in parallel.

However, even when there is no such requirement, n – 1 rendering is still a
powerful tool in DOTS for the following reasons:

-   It removes a sync point
-   Worker threads can be better occupied during the sync point
-   Transforms stay in sync
-   Event-driven visuals are rendered on the same frame as their cause
-   Transform dependencies are easier to reason about

The following outlines the order of events in a frame using n – 1 rendering:

1.  Schedule ECS-free jobs (audio, statistics, ect)
2.  Command Buffer Playback
3.  Scene and Subscene Managements
4.  Reactive and other Structural Change Systems
5.  Sync Transforms
6.  Process `MonoBehaviours`
7.  Rendering
8.  Process Inputs
9.  Movement and Physics
10. Sync Transforms
11. Process Triggers, Events, AI, and other Gameplay (No Transform Modification)
12. Roll into Next Frame

## Setup

In your `ICustomBootstrap`, replace the following line:

```csharp
ScriptBehaviourUpdateOrder.AddWorldToCurrentPlayerLoop(world);
```

With this:

```csharp
BootstrapTools.AddWorldToCurrentPlayerLoopWithDelayedSimulation(world);
```

Then, add the following three `SuperSystems` somewhere in your project:

```csharp
[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
public class PreRenderTransformSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddSystem<TransformSystemGroup>();
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public class PreTransformSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
public class PostTransformSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
    }
}
```

## Usage

Every system typically performs one kind of action. Depending on that action,
there is likely a home for it. Follow these guidelines in order to determine the
likely best fit for your system.

1.  If your system performs structural change operations using `EntityManager` –
    IE: `Entities.WithStructuralChanges().ForEach` – Place the system in
    `LatiosWorldSyncGroup`.
2.  If your system modifies `Translation`, `Rotation`, `Scale`, or `Parent`,
    place the system in `PreTransformSuperSystem`.
3.  If your system reads input, place the system in `PreTransformSuperSystem`
    before all other systems.
4.  If your system generates jobs which can run during a sync point, place the
    system in `PresSyncPointGroup`.
5.  If your system modifies material properties, place the system in the
    `PresentationSystemGroup`.
6.  For all other systems, place the system in `PostTransformSuperSystem`.
