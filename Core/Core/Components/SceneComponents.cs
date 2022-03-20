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
        public FixedString128Bytes newScene;
    }

    public struct CurrentScene : IComponentData
    {
        internal FixedString128Bytes currentScene;
        internal FixedString128Bytes previousScene;
        internal bool           isSceneFirstFrame;

        public FixedString128Bytes current => currentScene;
        public FixedString128Bytes previous => previousScene;
        public bool isFirstFrame => isSceneFirstFrame;
    }
}

