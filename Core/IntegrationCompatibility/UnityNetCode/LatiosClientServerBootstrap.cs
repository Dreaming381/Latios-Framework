#if NETCODE_PROJECT
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Latios.Compatibility.UnityNetCode
{
    // Note: The first world must be created before generating the system list in order to have a valid TypeManager instance.
    // The TypeManage is initialised the first time we create a world.

    /// <summary>
    /// Implement this interface to create the World during LatiosClientServerBootstrap.CreateLocalWorld() calls
    /// </summary>
    public interface ICustomLocalWorldBootstrap
    {
        /// <summary>
        /// Called during LatiosClientServerBootstrap.CreateLocalWorld() to allow for customized creation of such a world.
        /// </summary>
        /// <param name="defaultWorldName">The name of the default <see cref="World"/> that will be created</param>
        /// <param name="worldFlags">The WorldFlags the world should be created with.</param>
        /// <param name="worldSystemFilterFlags">The WorldSytstemFilterFlags that would be appropriate for this World</param>
        /// <returns>The created World if the bootstrap has performed initialization, or null if ClientServerBootstrap.CreateLocalWorld() should be performed.</returns>
        public World Initialize(string defaultWorldName, WorldFlags worldFlags, WorldSystemFilterFlags worldSystemFilterFlags);
    }

    /// <summary>
    /// Implement this interface to create the World during LatiosClientServerBootstrap.CreateClientWorld() calls
    /// </summary>
    public interface ICustomClientWorldBootstrap
    {
        /// <summary>
        /// Called during LatiosClientServerBootstrap.CreateClientWorld() to allow for customized creation of such a world.
        /// </summary>
        /// <param name="defaultWorldName">The name of the default <see cref="World"/> that will be created</param>
        /// <param name="worldFlags">The WorldFlags the world should be created with.</param>
        /// <param name="worldSystemFilterFlags">The WorldSytstemFilterFlags that would be appropriate for this World</param>
        /// <returns>The created World if the bootstrap has performed initialization, or null if ClientServerBootstrap.CreateClientWorld() should be performed.</returns>
        public World Initialize(string defaultWorldName, WorldFlags worldFlags, WorldSystemFilterFlags worldSystemFilterFlags);
    }

    /// <summary>
    /// Implement this interface to create the World during LatiosClientServerBootstrap.CreateServerWorld() calls
    /// </summary>
    public interface ICustomServerWorldBootstrap
    {
        /// <summary>
        /// Called during LatiosClientServerBootstrap.CreateServerWorld() to allow for customized creation of such a world.
        /// </summary>
        /// <param name="defaultWorldName">The name of the default <see cref="World"/> that will be created</param>
        /// <param name="worldFlags">The WorldFlags the world should be created with.</param>
        /// <param name="worldSystemFilterFlags">The WorldSytstemFilterFlags that would be appropriate for this World</param>
        /// <returns>The created World if the bootstrap has performed initialization, or null if ClientServerBootstrap.CreateServerWorld() should be performed.</returns>
        public World Initialize(string defaultWorldName, WorldFlags worldFlags, WorldSystemFilterFlags worldSystemFilterFlags);
    }

    /// <summary>
    /// Implement this interface to create the World during LatiosClientServerBootstrap.CreateThinClientWorld() calls
    /// </summary>
    public interface ICustomThinClientWorldBootstrap
    {
        /// <summary>
        /// Called during LatiosClientServerBootstrap.CreateThinClientWorld() to allow for customized creation of such a world.
        /// </summary>
        /// <param name="defaultWorldName">The name of the default <see cref="World"/> that will be created</param>
        /// <param name="worldFlags">The WorldFlags the world should be created with.</param>
        /// <param name="worldSystemFilterFlags">The WorldSytstemFilterFlags that would be appropriate for this World</param>
        /// <returns>The created World if the bootstrap has performed initialization, or null if ClientServerBootstrap.CreateThinClientWorld() should be performed.</returns>
        public World Initialize(string defaultWorldName, WorldFlags worldFlags, WorldSystemFilterFlags worldSystemFilterFlags);
    }

    /// <summary>
    /// Implement this interface to specify default GhostComponentVariations without having to create new systems for them.
    /// This code will run in servers, clients, and even baking worlds. The result will be statically cached after the first run.
    /// </summary>
    public interface ISpecifyDefaultVariantsBootstrap
    {
        /// <summary>
        /// Called once after domain reload by a server world, client world, thin client world, or baking world.
        /// </summary>
        /// <param name="defaultVariants">Default variants to add, same as in DefaultVariantSystemBase</param>
        public void RegisterDefaultVariants(Dictionary<ComponentType, DefaultVariantSystemBase.Rule> defaultVariants);
    }

    /// <summary>
    /// LatiosClientServerBootstrap is a derived class of <see cref="=ClientServerBootstrap"/> and has a very similar interface.
    /// The main difference is that its methods are able to invoke the custom bootstraps to create customized worlds akin to non-NetCode projects.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public abstract class LatiosClientServerBootstrap : ClientServerBootstrap
    {
#if UNITY_EDITOR || !UNITY_SERVER
        private static int NextThinClientId;
        /// <summary>
        /// Initialize the bootstrap class and reset the static data everytime a new instance is created.
        /// </summary>

        public LatiosClientServerBootstrap()
        {
            NextThinClientId = 1;
        }
#endif
#if UNITY_SERVER && UNITY_CLIENT
        public LatiosClientServerBootstrap()
        {
            UnityEngine.Debug.LogError(
                "Both UNITY_SERVER and UNITY_CLIENT defines are present. This is not allowed and will lead to undefined behaviour, they are for dedicated server or client only logic so can't work together.");
        }
#endif

        /// <summary>
        /// Utility method for creating a local world without any NetCode systems.
        /// <param name="defaultWorldName">Name of the world instantiated.</param>
        /// <returns>World with default systems added, set to run as the Main Live world.
        /// See <see cref="WorldFlags"/>.<see cref="WorldFlags.Game"/></returns>
        /// </summary>
        /// <param name="defaultWorldName">The name to use for the default world.</param>
        /// <returns>A new world instance.</returns>
        public static new World CreateLocalWorld(string defaultWorldName)
        {
            var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ICustomLocalWorldBootstrap>();
            if (bootstrap == null)
                return ClientServerBootstrap.CreateLocalWorld(defaultWorldName);

            var world = bootstrap.Initialize(defaultWorldName, WorldFlags.Game, WorldSystemFilterFlags.Default);
            if (World.DefaultGameObjectInjectionWorld == null)
                World.DefaultGameObjectInjectionWorld = world;
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return world;
        }

        /// <summary>
        /// Utility method for creating the default client and server worlds based on the settings
        /// in the playmode tools in the editor or client / server defined in a player.
        /// Should be used in custom implementations of `Initialize`.
        /// </summary>
        protected override void CreateDefaultClientServerWorlds()
        {
            var requestedPlayType = RequestedPlayType;
            if (requestedPlayType != PlayType.Client)
            {
                CreateServerWorld("ServerWorld");
            }

            if (requestedPlayType != PlayType.Server)
            {
                CreateClientWorld("ClientWorld");

#if UNITY_EDITOR
                var requestedNumThinClients = RequestedNumThinClients;
                for (var i = 0; i < requestedNumThinClients; i++)
                {
                    CreateThinClientWorld();
                }
#endif
            }
        }

        /// <summary>
        /// Utility method for creating thin clients worlds.
        /// Can be used in custom implementations of `Initialize` as well at runtime,
        /// to add new clients dynamically.
        /// </summary>
        /// <returns></returns>
        public static new World CreateThinClientWorld()
        {
#if UNITY_SERVER && !UNITY_EDITOR
            throw new NotImplementedException();
#else
            var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ICustomThinClientWorldBootstrap>();
            if (bootstrap == null)
            {
                var world = new World("ThinClientWorld" + NextThinClientId++, WorldFlags.GameThinClient);

                var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ThinClientSimulation);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);

                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                ThinClientWorlds.Add(world);

                return world;
            }
            else
            {
                var world = bootstrap.Initialize("ThinClientWorld" + NextThinClientId++, WorldFlags.GameThinClient, WorldSystemFilterFlags.ThinClientSimulation);
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                ThinClientWorlds.Add(world);
                return world;
            }
#endif
        }

#pragma warning disable CS0109  // The member 'LatiosClientServerBootstrap.CreateClientWorld(string, bool)' does not hide an accessible member. The new keyword is not required.
        /// <summary>
        /// Utility method for creating new clients worlds.
        /// Can be used in custom implementations of `Initialize` as well at runtime, to add new clients dynamically.
        /// </summary>
        /// <param name="name">The client world name</param>
        /// <returns></returns>
        public static new World CreateClientWorld(string name, bool setAsDefault = true)
        {
#if UNITY_SERVER && !UNITY_EDITOR
            throw new NotImplementedException();
#else
            var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ICustomClientWorldBootstrap>();
            if (bootstrap == null)
                return ClientServerBootstrap.CreateClientWorld(name);

            var world = bootstrap.Initialize(name, WorldFlags.GameClient, WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Presentation);
            if (World.DefaultGameObjectInjectionWorld == null || setAsDefault)
                World.DefaultGameObjectInjectionWorld = world;
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            ClientWorlds.Add(world);
            return world;
#endif
        }
#pragma warning restore CS0109

        /// <summary>
        /// Utility method for creating a new server world.
        /// Can be used in custom implementations of `Initialize` as well as in your game logic (in particular client/server build)
        /// when you need to create server programmatically (ex: frontend that allow selecting the role or other logic).
        /// </summary>
        /// <param name="name">The server world name</param>
        /// <returns></returns>
        public static new World CreateServerWorld(string name)
        {
#if UNITY_CLIENT && !UNITY_SERVER && !UNITY_EDITOR
            throw new NotImplementedException();
#else
            var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ICustomServerWorldBootstrap>();
            if (bootstrap == null)
                return ClientServerBootstrap.CreateServerWorld(name);

            var world = bootstrap.Initialize(name, WorldFlags.GameServer, WorldSystemFilterFlags.ServerSimulation);
            if (World.DefaultGameObjectInjectionWorld == null)
                World.DefaultGameObjectInjectionWorld = world;
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            ServerWorlds.Add(world);
            return world;
#endif
        }

        static class ReflectionHelper
        {
            // From:
            // http://dotnetfollower.com/wordpress/2012/12/c-how-to-set-or-get-value-of-a-private-or-internal-property-through-the-reflection/

            private static FieldInfo GetFieldInfo(Type type, string fieldName)
            {
                FieldInfo fieldInfo;
                do
                {
                    fieldInfo = type.GetField(fieldName,
                                              BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    type = type.BaseType;
                }
                while (fieldInfo == null && type != null);
                return fieldInfo;
            }

            public static object GetFieldValue(object obj, string fieldName)
            {
                if (obj == null)
                    throw new ArgumentNullException("obj");
                Type objType   = obj.GetType();
                FieldInfo fieldInfo = GetFieldInfo(objType, fieldName);
                if (fieldInfo == null)
                    throw new ArgumentOutOfRangeException("fieldName",
                                                          string.Format("Couldn't find field {0} in type {1}", fieldName, objType.FullName));
                return fieldInfo.GetValue(obj);
            }

            public static void SetFieldValue(object obj, string fieldName, object val)
            {
                if (obj == null)
                    throw new ArgumentNullException("obj");
                Type objType   = obj.GetType();
                FieldInfo fieldInfo = GetFieldInfo(objType, fieldName);
                if (fieldInfo == null)
                    throw new ArgumentOutOfRangeException("fieldName",
                                                          string.Format("Couldn't find field {0} in type {1}", fieldName, objType.FullName));
                fieldInfo.SetValue(obj, val);
            }
        }
    }

namespace UnityInject
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ThinClientSimulation | WorldSystemFilterFlags.BakingSystem)]
    [CreateAfter(typeof(GhostComponentSerializerCollectionSystemGroup))]
    [CreateBefore(typeof(DefaultVariantSystemGroup))]
    [UpdateInGroup(typeof(DefaultVariantSystemGroup))]
    public partial class BootstrappedDefaultVariantRegistrationSystem : DefaultVariantSystemBase
    {
        static Dictionary<ComponentType, Rule> s_defaultVariants;

        protected override void RegisterDefaultVariants(Dictionary<ComponentType, Rule> defaultVariants)
        {
            if (s_defaultVariants == null)
            {
                s_defaultVariants = new Dictionary<ComponentType, Rule>();
                var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ISpecifyDefaultVariantsBootstrap>();
                bootstrap?.RegisterDefaultVariants(s_defaultVariants);
            }

            foreach (var variant in s_defaultVariants)
                defaultVariants.Add(variant.Key, variant.Value);
        }
    }
}
}
#endif

