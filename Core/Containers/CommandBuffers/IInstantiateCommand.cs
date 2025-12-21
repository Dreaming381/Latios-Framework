using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A struct implementing this type can be used in an InstantiateCommandBuffer to provide custom
    /// per-entity post-processing logic during playback.
    /// To create your own type, implement the interface and provide a custom Burst function pointer.
    /// Don't forget to add the [BurstCompile] and [MonoPInvokeCallback] attributes to the static method.
    /// </summary>
    public interface IInstantiateCommand
    {
        public struct Context
        {
            /// <summary>
            /// An EntityManager to use for post-processing entities.
            /// </summary>
            public EntityManager entityManager { get; internal set; }
            /// <summary>
            /// The newly instantiated entities (does not include entities instantiated indirectly through LinkedEntityGroup).
            /// </summary>
            public NativeArray<Entity> entities { get; internal set; }

            internal NativeArray<UnsafeIndexedBlockList.ElementPtr> dataPtrs;
            internal int                                            commandOffset;
            internal int                                            expectedSize;

            /// <summary>
            /// Reads the command corresponding to the entity at the same index.
            /// The type should be of this particular type of IInstantiateCommand,
            /// or a size-aliasable equivalent.
            /// </summary>
            public unsafe T ReadCommand<T>(int index) where T : unmanaged, IInstantiateCommand
            {
                CheckTypeAccess<T>(expectedSize);
                var ptr = dataPtrs[index].ptr + commandOffset;
                UnsafeUtility.CopyPtrToStructure<T>(ptr, out var result);
                return result;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckTypeAccess<T>(int expectedSize) where T : unmanaged, IInstantiateCommand
            {
                if (UnsafeUtility.SizeOf<T>() != expectedSize)
                    throw new InvalidOperationException($"Attempted to access a type of size {UnsafeUtility.SizeOf<T>()} when the stored command type is of size {expectedSize}");
            }
        }

        public delegate void OnPlayback(ref Context context);

        /// <summary>
        /// This is called once on application startup on a defaulted instance outside of Burst.
        /// This method should define either a Burst-compiled function pointer or a managed function pointer
        /// that will be invoked once per playback of an InstantiateCommandBufferCommandX variant containing
        /// the implementing type of IInstantiateCommand.
        /// </summary>
        /// <returns></returns>
        public FunctionPointer<OnPlayback> GetFunctionPointer();
    }

    internal static class InstantiateCommandRegistry
    {
        struct SharedKey { }

        public struct TypeToPointer<T> where T : unmanaged, IInstantiateCommand
        {
            public static readonly SharedStatic<FunctionPointer<IInstantiateCommand.OnPlayback> > functionPtr =
                SharedStatic< FunctionPointer<IInstantiateCommand.OnPlayback> >.GetOrCreate<SharedKey, T>();
        }

        static FunctionPointer<IInstantiateCommand.OnPlayback> GetFunctionPtr<T>() where T : unmanaged, IInstantiateCommand
        {
            T t = default;
            return t.GetFunctionPointer();
        }

        static void InitCommands(IEnumerable<Type> types)
        {
            var method  = typeof(InstantiateCommandRegistry).GetMethod(nameof(GetFunctionPtr), BindingFlags.Static | BindingFlags.NonPublic);
            var keyType = typeof(SharedKey);

            foreach (var type in types)
            {
                if (type.IsGenericType || type.IsInterface)
                    continue;

                var generic     = method.MakeGenericMethod(type);
                var functionPtr = (FunctionPointer<IInstantiateCommand.OnPlayback>)generic.Invoke(
                    null,
                    null);
                SharedStatic<FunctionPointer<IInstantiateCommand.OnPlayback> >.GetOrCreate(keyType, type).Data = functionPtr;
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitEditor()
        {
            var commandTypes = UnityEditor.TypeCache.GetTypesDerivedFrom<IInstantiateCommand>();

            InitCommands(commandTypes);
        }
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        internal static void InitRuntime()
        {
            var commandTypes = new List<Type>();
            var commandType  = typeof(IInstantiateCommand);
            var coreAssembly = commandType.Assembly;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BootstrapTools.IsAssemblyReferencingOtherAssembly(assembly, coreAssembly))
                    continue;

                try
                {
                    var assemblyTypes = assembly.GetTypes();
                    foreach (var t in assemblyTypes)
                    {
                        if (commandType.IsAssignableFrom(t))
                            commandTypes.Add(t);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    foreach (var t in e.Types)
                    {
                        if (t != null && commandType.IsAssignableFrom(t))
                            commandTypes.Add(t);
                    }

                    UnityEngine.Debug.LogWarning($"Core IInstantiateCommand.cs failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
                }
            }

            InitCommands(commandTypes);
        }
    }
}

