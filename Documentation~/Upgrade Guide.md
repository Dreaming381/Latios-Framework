# Upgrade Guide [0.3.3] [0.4.0]

Please back up your project in a separate directory before upgrading! You will
likely need to reference the pre-upgraded project during the upgrade process.

## Core

### Scene Blackboard Entity Safety

Additional safety checks have been added to `sceneBlackboardEntity` to prevent
modifying it in a way that breaks in builds.

## Psyshock Physics

### CalculateAabb() -\> AabbFrom()

This rename was made to help unify the API conventions. It is the only rename in
Psyshock.

## Myri Audio

### Audio Settings Rewrite

Audio Settings were completely redesigned due to a new playhead tracking
mechanism that should be more robust. Forget everything you knew about the old
settings and check the documentation regarding how the new settings work. Some
fields share an old name but have a new meaning.

### Brickwall Limiter

You may notice that volumes behave differently, and that many things may be
quieter. This is due to a Brickwall Limiter which removes a bunch of artifacts
with loud volumes. You should no longer have to worry about balancing volumes to
get the right overall loudness. This Brickwall Limiter does cost a noticeable
chunk of the DSP thread, which may cause slight performance regressions.

## New Things to Try!

### Core

There’s a new `OnNewScene()` callback for `SubSystem` and `SuperSystem`. Use
this to initialize the `sceneBlackboardEntity`. Do not touch `Dependency` here.

`Rng` is a new API and workflow for random number generation for Unity.Entities.

### Psyshock Physics

Point Queries and Collider Casts are new. Try them out!

There’s also some new API to help with character controllers and simulations as
I further explore these aspects in Psyshock. This API is somewhat experimental,
but since you are reading this, you will probably be fine.

### Myri Audio

You can now do n + 1 sampling when sampling consumes most of the frame.
