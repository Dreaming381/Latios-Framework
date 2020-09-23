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
        public FixedString128 newScene;
    }

    public struct CurrentScene : IComponentData
    {
        internal FixedString128 currentScene;
        internal FixedString128 previousScene;
        internal bool           isSceneFirstFrame;

        public FixedString128 current => currentScene;
        public FixedString128 previous => previousScene;
        public bool isFirstFrame => isSceneFirstFrame;
    }
}

