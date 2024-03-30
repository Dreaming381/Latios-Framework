using System;
using Unity.Collections;
using Unity.Entities;

//Todo: Async scene requests
namespace Latios
{
    /// <summary>
    /// Prevents the entity this is attached to from being destroyed on scene change, even if that entity was part of a subscene.
    /// It has no effect if the SceneManager is not installed (it is not by default).
    /// </summary>
    public struct DontDestroyOnSceneChangeTag : IComponentData { }

    /// <summary>
    /// Unlike Unity.Entities.RequestSceneLoaded, this requests a true scene (not a subscene) to be loaded synchronously.
    /// All subscenes within the scene will also be loaded synchronously by default.
    /// Entities without the DontDestroyOnSceneChangeTag or the WorldGlobalTag will be deleted.
    /// It has no effect if the SceneManager is not installed (it is not by default).
    /// </summary>
    public struct RequestLoadScene : IComponentData
    {
        public FixedString128Bytes newScene;
    }

    /// <summary>
    /// A component attached to the worldBlackboardEntity. It provides info about the current scene.
    /// It is not added if the SceneManager is not installed (it is not by default).
    /// </summary>
    public struct CurrentScene : IComponentData
    {
        internal FixedString128Bytes currentScene;
        internal FixedString128Bytes previousScene;
        internal bool                isSceneFirstFrame;

        public FixedString128Bytes current => currentScene;
        public FixedString128Bytes previous => previousScene;
        public bool isFirstFrame => isSceneFirstFrame;
    }

    /// <summary>
    /// Add this component to the subscene entity to disable forcing the subscene to load synchronously.
    /// </summary>
    public struct DisableSynchronousSubsceneLoadingTag : IComponentData { }
}

