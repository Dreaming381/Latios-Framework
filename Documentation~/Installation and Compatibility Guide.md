# Latios Framework Installation and Compatibility Guide

This guide will walk you through installing Latios Framework into your DOTS
project. It will also show you how to enable or disable features of the
framework.

## Installation

Nearly all Latios Framework functionality requires that the World instance be a
subclass instance called LatiosWorld. If your project currently uses default
world initialization, you simply have to go to your project window, *right
click-\>Create-\>Latios-\>Bootstrap – Injection Workflow*. This will create an
instance of an ICustomBootstrap which sets up a variation of the default world,
but with Latios Framework components installed.

### Installing with Existing ICustomBootstrap

If you are already using an ICustomBootstrap, you may still be able to install
Latios Framework. The following lines in Initialize() are the minimum
requirements to create a working LatiosWorld. You can either assign this world
to World.DefaultGameObjectInjectionWorld or create multiple LatiosWorld
instances in a multi-world setup.

```charp
var world = new LatiosWorld(defaultWorldName);
world.initializationSystemGroup.SortSystems();
return true;
```

LatiosWorld creates several systems in its constructor. This can throw off
`DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups()`. You may need
to remove some system types from the list. You can refer to the templates as
examples of what may need to be removed.

### Installing Myri in Bootstrap - Explicit Workflow

When using the Bootstrap – Explicit Workflow, Myri does not install by default.
The following will suffice:

```csharp
[UpdateInGroup(typeof(Latios.Systems.PreSyncPointGroup))]
public class MyriPreSyncRootSuperSystem : RootSuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddSystem<Latios.Myri.Systems.AudioSystem>();
    }
}
```

## Resolving ISystemBase

`ISystemBase` is not natively supported in Latios Framework yet. But you can
make it work by specifying a custom `ComponentSystemGroup` for it to be injected
into. And subclass of `ComponentSystemGroup` provided by the framework (abstract
or not) will ignore these unmanaged systems.

## Disabling Core Features

### Uniform Scale Patching

Uniform scale patching converts instances of `NonUniformScale` to instances of
`Scale` at conversion time when all three components are identical.

Add the following code to your project in an authoring-compatible assembly:

```csharp
class DisableUniformScalePatchSystem : Latios.Authoring.Systems.GameObjectConversionConfigurationSystem
{
    protected override void OnUpdate()
    {
        World.GetExistingSystem<Latios.Authoring.Systems.TransformUniformScalePatchConversionSystem>().Enabled = false;
    }
}
```

### Scene Management

The scene management solution automatically destroys entities whenever the
*Active Scene* is changed.

Add the following code to your bootstrap (by default, LatiosBootstrap.cs):

```csharp
world.GetExistingSystem<DestroyEntitiesOnSceneChangeSystem>().Enabled = false;
```

## Disabling Psyshock Physics Features

### Legacy Collider Conversion

Psyshock converts supported `UnityEngine.Collider` types to
`Latios.Psyshock.Collider` at conversion time.

Add the following code to your project in an authoring-compatible assembly:

```csharp
class DisablePsyshockLegacyConversionSystem : Latios.Authoring.Systems.GameObjectConversionConfigurationSystem
{
    protected override void OnUpdate()
    {
        World.GetExistingSystem<Latios.Psyshock.Authoring.Systems.LegacyColliderConversionSystem>().Enabled = false;
    }
}
```

## Disabling Myri Audio Features

### Myri DSPGraph Runtime

Myri takes direct control of the audio system and generates its own DSPGraph
instance. This breaks any MonoBehaviour-based audio (known DSPGraph bug).

If using *Bootstrap – Explicit Workflow*, you do not have to do anything, as
Myri must be manually instantiated just like any other system.

If using *Bootstrap – Injection Workflow*, add the following code to your
bootstrap (by default, LatiosBootstrap.cs)

```csharp
systems.RemoveSwapBack(typeof(Latios.Myri.Systems.AudioSystem));  // Add this line!
DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
```
