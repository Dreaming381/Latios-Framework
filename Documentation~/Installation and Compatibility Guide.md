# Latios Framework Installation and Compatibility Guide

This guide will walk you through installing Latios Framework into your DOTS
project. It will also show you how to enable or disable features of the
framework.

## Installation

Latios Framework 0.6 uses Transforms V1. If your project does not use Unity
Physics or NetCode, you need to enable Transforms V1 by adding
`ENABLE_TRANSFORM_V1` to the scripting define symbols of your project.

Nearly all Latios Framework functionality requires that the World instance be a
subclass instance called `LatiosWorld`. If your project currently uses default
world initialization, you simply have to go to your project window, *right
click-\>Create-\>Latios-\>Standard Bootstrap – Injection Workflow*. This will
create an instance of an `ICustomBootstrap` which sets up a variation of the
default world, but with Latios Framework components installed.

### Installing with Existing ICustomBootstrap

If you are already using an `ICustomBootstrap`, you may still be able to install
Latios Framework. The following lines in `Initialize()` are the minimum
requirements to create a working `LatiosWorld`. You can either assign this world
to World.`DefaultGameObjectInjectionWorld` or create multiple `LatiosWorld`
instances in a multi-world setup.

```charp
var world = new LatiosWorld(defaultWorldName);
world.initializationSystemGroup.SortSystems();
return true;
```

`LatiosWorld` creates several systems in its constructor. This can throw off
`DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups()`. You may need
to remove some system types from the list. `BootstrapTools.InjectSystems()`
often avoids this problem but otherwise produces the same results.

## Managing Features

Beginning with Latios Framework 0.5, features are controlled through the use of
*installers*. You can see these installers in action by looking through the
bootstrap templates.

## Platform Support

Latios Framework currently only supports Windows, Mac OS, and Linux desktop
platforms. This is because the Kinemation module ships with a native plugin
which is currently only built for those platforms. You can learn more about the
plugin and how you can help extend it to work on more platforms
[here](https://github.com/Dreaming381/AclUnity).

In addition, code stripping must be set to *Minimal* or *None*, and if building
for IL2CPP, you must set the code generation mode to *Faster (Smaller) Builds*.

If there is some other unexpected behavior, that is likely a bug. Please report
the issue!
