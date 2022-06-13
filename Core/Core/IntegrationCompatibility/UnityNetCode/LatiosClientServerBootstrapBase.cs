#if NETCODE_PROJECT
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Latios.Compatibility.UnityNetCode
{
    public abstract class LatiosClientServerBootstrapBase : ClientServerBootstrap
    {
        public abstract World CreateCustomServerWorld(string worldName);
        public abstract World CreateCustomClientWorld(string worldName);

        public IReadOnlyList<Type> ServerSystems
        {
            get
            {
                if (s_serverSystems == null)
                    BuildSystemsCache();
                return s_serverSystems;
            }
        }
        public IReadOnlyList<Type> ClientSystems
        {
            get
            {
                if (s_clientSystems == null)
                    BuildSystemsCache();
                return s_clientSystems;
            }
        }
        public IReadOnlyDictionary<Type, Type> ServerGroupRemap
        {
            get
            {
                if (s_serverRemap == null)
                    BuildRemapCache();
                return s_serverRemap;
            }
        }
        public IReadOnlyDictionary<Type, Type> ClientGroupRemap
        {
            get
            {
                if (s_clientRemap == null)
                    BuildRemapCache();
                return s_clientRemap;
            }
        }

        public override void CreateDefaultClientServerWorlds(World defaultWorld)
        {
            PlayType playModeType    = RequestedPlayType;
            int numClientWorlds = 1;
            int totalNumClients = numClientWorlds;

            if (playModeType == PlayType.Server || playModeType == PlayType.ClientAndServer)
            {
                CreateAndWrapServerWorld(defaultWorld, "ServerWorld");
            }

            if (playModeType != PlayType.Server)
            {
#if UNITY_EDITOR
                int numThinClients = RequestedNumThinClients;
                totalNumClients += numThinClients;
#endif
                for (int i = 0; i < numClientWorlds; ++i)
                {
                    CreateAndWrapClientWorld(defaultWorld, "ClientWorld" + i);
                }
#if UNITY_EDITOR
                for (int i = numClientWorlds; i < totalNumClients; ++i)
                {
                    var clientWorld = CreateAndWrapClientWorld(defaultWorld, "ClientWorld" + i);
                    clientWorld.EntityManager.CreateEntity(typeof(ThinClientComponent));
                }
#endif
            }
        }

        private static List<Type> s_clientSystems;
        private static List<Type> s_serverSystems;

        private static Dictionary<Type, Type> s_clientRemap;
        private static Dictionary<Type, Type> s_serverRemap;

        private void BuildSystemsCache()
        {
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);

            // We call this for now because it performs error checking.
            GenerateSystemLists(systems);

            s_clientSystems = new List<Type>();
            s_serverSystems = new List<Type>();

            foreach (var s in systems)
            {
                var mask = GetTopLevelWorldMask(s);
                if ((mask & WorldType.ServerWorld) != 0)
                    s_serverSystems.Add(s);
                if ((mask & WorldType.ClientWorld) != 0)
                    s_clientSystems.Add(s);
            }
        }

        private void BuildRemapCache()
        {
            s_clientRemap = new Dictionary<Type, Type>();
            s_serverRemap = new Dictionary<Type, Type>();

            s_clientRemap.Add(typeof(Systems.LatiosInitializationSystemGroup),  typeof(LatiosClientInitializationSystemGroup));
            s_clientRemap.Add(typeof(ClientAndServerInitializationSystemGroup), typeof(LatiosClientInitializationSystemGroup));
            s_clientRemap.Add(typeof(Systems.LatiosSimulationSystemGroup),      typeof(LatiosClientSimulationSystemGroup));
            s_clientRemap.Add(typeof(ClientAndServerSimulationSystemGroup),     typeof(LatiosClientSimulationSystemGroup));
            s_clientRemap.Add(typeof(ServerInitializationSystemGroup),          null);
            s_clientRemap.Add(typeof(LatiosServerInitializationSystemGroup),    null);
            s_clientRemap.Add(typeof(ServerSimulationSystemGroup),              null);
            s_clientRemap.Add(typeof(LatiosServerSimulationSystemGroup),        null);

            s_serverRemap.Add(typeof(Systems.LatiosInitializationSystemGroup),  typeof(LatiosServerInitializationSystemGroup));
            s_serverRemap.Add(typeof(ClientAndServerInitializationSystemGroup), typeof(LatiosServerInitializationSystemGroup));
            s_serverRemap.Add(typeof(Systems.LatiosSimulationSystemGroup),      typeof(LatiosServerSimulationSystemGroup));
            s_serverRemap.Add(typeof(ClientAndServerSimulationSystemGroup),     typeof(LatiosServerSimulationSystemGroup));
            s_serverRemap.Add(typeof(ClientInitializationSystemGroup),          null);
            s_serverRemap.Add(typeof(LatiosClientInitializationSystemGroup),    null);
            s_serverRemap.Add(typeof(ClientSimulationSystemGroup),              null);
            s_serverRemap.Add(typeof(LatiosClientSimulationSystemGroup),        null);
        }

        [Flags]
        private enum WorldType
        {
            NoWorld = 0,
            DefaultWorld = 1,
            ClientWorld = 2,
            ServerWorld = 4,
            ExplicitWorld = 8
        }

        static WorldType GetTopLevelWorldMask(Type type)
        {
            var targetWorld = GetSystemAttribute<UpdateInWorldAttribute>(type);
            if (targetWorld != null)
            {
                if (targetWorld.World == TargetWorld.Default)
                    return WorldType.DefaultWorld | WorldType.ExplicitWorld;
                if (targetWorld.World == TargetWorld.Client)
                    return WorldType.ClientWorld;
                if (targetWorld.World == TargetWorld.Server)
                    return WorldType.ServerWorld;
                return WorldType.ClientWorld | WorldType.ServerWorld;
            }

            var groups = TypeManager.GetSystemAttributes(type, typeof(UpdateInGroupAttribute));
            if (groups.Length == 0)
            {
                if (typeof(ClientAndServerSimulationSystemGroup).IsAssignableFrom(type) ||
                    typeof(ClientAndServerInitializationSystemGroup).IsAssignableFrom(type))
                    return WorldType.ClientWorld | WorldType.ServerWorld;
                if (typeof(ServerSimulationSystemGroup).IsAssignableFrom(type) || typeof(ServerInitializationSystemGroup).IsAssignableFrom(type))
                    return WorldType.ServerWorld;
                if (typeof(ClientSimulationSystemGroup).IsAssignableFrom(type) ||
                    typeof(ClientInitializationSystemGroup).IsAssignableFrom(type))
                    return WorldType.ClientWorld;
                if (typeof(SimulationSystemGroup).IsAssignableFrom(type) || typeof(InitializationSystemGroup).IsAssignableFrom(type))
                    return WorldType.DefaultWorld | WorldType.ClientWorld | WorldType.ServerWorld;
                if (typeof(PresentationSystemGroup).IsAssignableFrom(type))
                    return WorldType.DefaultWorld | WorldType.ClientWorld;
                // Empty means the same thing as SimulationSystemGroup
                return WorldType.DefaultWorld | WorldType.ClientWorld | WorldType.ServerWorld;
            }

            WorldType mask = WorldType.NoWorld;
            foreach (var grp in groups)
            {
                var group = grp as UpdateInGroupAttribute;
                mask |= GetTopLevelWorldMask(group.GroupType);
            }

            return mask;
        }

        private World CreateAndWrapServerWorld(World defaultWorld, string serverWorldName)
        {
#if UNITY_CLIENT && !UNITY_SERVER && !UNITY_EDITOR
            throw new NotImplementedException();
#else
            var serverWorld = CreateCustomServerWorld(serverWorldName);

            // Todo: Make users install into PlayerLoop? Would allow for N-1 loops.
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(serverWorld);

            if (AutoConnectPort != 0)
                serverWorld.GetExistingSystem<NetworkStreamReceiveSystem>().Listen(DefaultListenAddress.WithPort(AutoConnectPort));

            return serverWorld;
#endif
        }

        private World CreateAndWrapClientWorld(World defaultWorld, string clientWorldName)
        {
#if UNITY_SERVER
            throw new NotImplementedException();
#else

            var clientWorld = CreateCustomClientWorld(clientWorldName);

            // Todo: Make users install into PlayerLoop? Would allow for N-1 loops.
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(clientWorld);

            if (AutoConnectPort != 0 && DefaultConnectAddress != NetworkEndPoint.AnyIpv4)
            {
                NetworkEndPoint ep;
#if UNITY_EDITOR
                var addr = RequestedAutoConnect;
                if (!NetworkEndPoint.TryParse(addr, AutoConnectPort, out ep))
#endif
                ep = DefaultConnectAddress.WithPort(AutoConnectPort);
                clientWorld.GetExistingSystem<NetworkStreamReceiveSystem>().Connect(ep);
            }
            return clientWorld;
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
}
#endif

