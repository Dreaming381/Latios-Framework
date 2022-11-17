# Scene Management

In Latios.Core, a hard line is drawn between Scenes and Subscenes. Fortunately,
the names make sense!

## Unity vs Latios

Unlike Unity’s DOTS scene management system which operates on subscenes, the
scene management performed by Latios.Core operates on scenes. The two systems
are complimentary.

-   Load requests
    -   In Unity, you call `SceneSystem.LoadSceneAsync()` to load a new scene or
        subscene, which generates a `RequestSceneLoaded` component
    -   In Latios, you attach a `RequestLoadScene` (note the slightly different
        name) component to any entity (disabled and prefab entities are ignored)
-   Unloading
    -   In Unity, you must use `SceneSystem.UnloadScene()` to unload the scene
        or subscene, unless you load a GameObject scene non-additively
    -   In Latios, the current scene is unloaded when you load a new scene
-   Async
    -   In Unity, subscenes are loaded asynchronously, and only GameObject
        scenes can be loaded synchronously
    -   In Latios, the scene is loaded using the synchronous API and a pause
        frame, and all subscenes are forced to load synchronously as well
-   Destroyed entities
    -   In Unity, only the entities that were contained within the subscene are
        destroyed
    -   In Latios, all entities without the `WorldGlobalTag` or the
        `DontDestroyOnSceneChangeTag` are destroyed during the pause frame
-   Querying the scene
    -   In Unity, you can detect if a current scene is loaded using
        `SceneSystem.IsSceneLoaded()`
    -   In Latios, you can check what the currently loaded scene is by reading
        the `CurrentScene.current` on the `worldBlackboardEntity`

## The Pause Frame

Even when using the synchronous API, scene switching does not occur until the
next `EarlyUpdate.UpdatePreloading` `PlayerloopSystem`. By default, this occurs
after `LatiosInitializationSystemGroup` but before
`LatiosSimulationSystemGroup`. This behavior meant that on the first scene,
systems in the `LatiosInitializationSystemGroup` would first see the new
`sceneBlackboardEntity`, but would be the last systems to see it for any other
scene.

To resolve this, all top-level Latios system groups stop executing for the
remainder of the frame. During that time, a special system called
`DestroyEntitiesOnSceneChangeSystem` will execute, destroying all entities
(including prefabs and disabled entities) except for the `worldBlackboardEntity`
and entities with the `DontDestroyOnSceneChangeTag` component.

Afterward, all systems in the `LatiosInitializationSystemGroup` with `OrderFirst
= true` will run (important if you do hybrid stuff), followed by
`SceneManagerSystem` which will update itself with the successful scene load.
Finally, the remaining systems will execute as if there was never a pause.

## CurrentScene

This component is attached to the `worldBlackboardEntity`. You can read from it,
but **do not modify or remove it!**

`CurrentScene` contains the following properties:

-   current – The currently loaded scene
-   previous – The scene that was replaced by the currently loaded scene
-   isFirstFrame – True if this is the first frame after the pause frame
    following a scene load

## RequestLoadScene

When you add a `RequestLoadScene` component to an entity or enable or
instantiate an entity with such a component, the `SceneManagerSystem` will react
to it in the next frame immediately after the `SyncPointPlaybackSystem`
executes. If multiple entities are found to have such a component, an error will
be logged and no scene will be loaded.

The `RequestLoadScene` components are removed after processing.

## DontDestroyOnSceneChangeTag

You can add this component to any entity to prevent it from being destroyed
during a scene change. This includes entities that belong to an unloaded
subscene, allowing them to persist even after the subscene unloaded.
