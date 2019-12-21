using System;
using Unity.Collections;
using Unity.Entities;

//Todo: Async scene requests
namespace Latios
{
    public struct DontDestroyOnSceneChangeTag : IComponentData { }

    /// <summary>
    /// Unlike Unity.Entities.RequestSceneLoaded, this requests a true scene (not a subscene) to be loaded synchronously.
    /// Entities without the DontDestroyOnSceneChangeTag or the WorldGlobalTag will be deleted.
    /// </summary>
    public struct RequestLoadScene : IComponentData
    {
        public NativeString128 newScene;
    }

    public struct CurrentScene : IComponentData
    {
        internal NativeString128 currentScene;
        internal NativeString128 previousScene;
        internal bool            isSceneFirstFrame;

        public NativeString128 current => currentScene;
        public NativeString128 previous => previousScene;
        public bool isFirstFrame => isSceneFirstFrame;
    }
}

