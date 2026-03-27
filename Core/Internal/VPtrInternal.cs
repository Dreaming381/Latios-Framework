using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Unsafe.InternalSourceGen
{
    public static unsafe partial class StaticAPI
    {
        #region FunctionPtr and Utils
        public struct ContextPtr
        {
            public void* ptr;

            public static implicit operator ContextPtr(void* rawPte) => new ContextPtr
            {
                ptr = rawPte
            };
        }

        public delegate void BurstDispatchVptrDelegate(ContextPtr context, int operation);

        public unsafe struct VPtr
        {
            internal void* ptr;

            public ref T AsRef<T>() where T : unmanaged => ref *(T*)ptr;

            public UnsafeApiPointer AsPtr() => new UnsafeApiPointer {
                ptr = ptr
            };

            public static VPtr Create(UnsafeApiPointer ptr) => new VPtr
            {
                ptr = ptr.ptr
            };
        }

        public static void RegisterVptrFunction<TInterface, TStruct>(FunctionPointer<BurstDispatchVptrDelegate> functionPtr) where TStruct : unmanaged,
        TInterface where TInterface : IVInterface
        {
            VTable.Add<TInterface, TStruct>(functionPtr);
        }

        public static T ConvertFunctionPointerToWrapper<T>(FunctionPointer<BurstDispatchVptrDelegate> functionPtr) where T : unmanaged
        {
            return UnsafeUtility.As<FunctionPointer<BurstDispatchVptrDelegate>, T>(ref functionPtr);
        }

        public static FunctionPointer<BurstDispatchVptrDelegate> ConvertFunctionPointerFromWrapper<T>(T functionPtr) where T : unmanaged
        {
            return UnsafeUtility.As<T, FunctionPointer<BurstDispatchVptrDelegate> >(ref functionPtr);
        }

        public static FunctionPointer<BurstDispatchVptrDelegate> GetFunctionChecked<TInterface, TStruct>() where TStruct : unmanaged, TInterface where TInterface : IVInterface
        {
            var found = TryGetFunction<TInterface, TStruct>(out var result);
            CheckFunctionFound(found);
            return result;
        }

        public static bool TryGetFunction<TInterface, TStruct>(out FunctionPointer<BurstDispatchVptrDelegate> functionPtr) where TStruct : unmanaged,
        TInterface where TInterface : IVInterface
        {
            return VTable.TryGet<TInterface, TStruct>(out functionPtr);
        }

        public static bool TryGetFunction<TInterface>(long structHash, out FunctionPointer<BurstDispatchVptrDelegate> functionPtr) where TInterface : IVInterface
        {
            return VTable.TryGet<TInterface>(structHash, out functionPtr);
        }
        #endregion

        #region Dispatch
        public static unsafe ref T ExtractRefReturn<T>(ContextPtr refReturn) where T : unmanaged
        {
            return ref UnsafeUtility.AsRef<T>(refReturn.ptr);
        }

        public static unsafe ContextPtr AssignRefReturn<T>(ref T ret) where T : unmanaged
        {
            return new ContextPtr { ptr = UnsafeUtility.AddressOf(ref ret) };
        }

        public static unsafe ContextPtr AssignRefReadonlyReturn<T>(in T ret) where T : unmanaged
        {
            return new ContextPtr { ptr = UnsafeUtility.AddressOf(ref UnsafeUtilityExtensions.AsRef(in ret)) };
        }

        unsafe struct ZeroArg
        {
            public void* objPtr;
        }

        public static unsafe void Dispatch(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr, int operation)
        {
            var context = new ZeroArg
            {
                objPtr = vptr.ptr
            };
            functionPtr.Invoke(&context, operation);
        }

        public static unsafe ref T ExtractObject<T>(ContextPtr context) where T : unmanaged
        {
            var objPtrPtr = ((ZeroArg*)context.ptr)->objPtr;
            return ref UnsafeUtility.AsRef<T>(objPtrPtr);
        }

        unsafe struct OneArg
        {
            public void* objPtr;
            public void* arg0;
        }

        public static unsafe void Dispatch<TArg0>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr, int operation, ref TArg0 targ0) where TArg0 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            fixed (byte* a0   = &arg0)
            {
                var context = new OneArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg0<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((OneArg*)context.ptr)->arg0;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct TwoArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
        }

        public static unsafe void Dispatch<TArg0, TArg1>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr, int operation, ref TArg0 targ0,
                                                         ref TArg1 targ1) where TArg0 : unmanaged where TArg1 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            fixed (byte* a0   = &arg0, a1 = &arg1)
            {
                var context = new TwoArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg1<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((TwoArg*)context.ptr)->arg1;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct ThreeArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2>(VPtr vptr,
                                                                FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                int operation,
                                                                ref TArg0 targ0,
                                                                ref TArg1 targ1,
                                                                ref TArg2 targ2)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2)
            {
                var context = new ThreeArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg2<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((ThreeArg*)context.ptr)->arg2;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct FourArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3>(VPtr vptr,
                                                                       FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                       int operation,
                                                                       ref TArg0 targ0,
                                                                       ref TArg1 targ1,
                                                                       ref TArg2 targ2,
                                                                       ref TArg3 targ3)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
            where TArg3 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            ref var      arg3 = ref UnsafeUtility.As<TArg3, byte>(ref targ3);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2, a3 = &arg3)
            {
                var context = new FourArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg3<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((FourArg*)context.ptr)->arg3;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct FiveArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                              int operation,
                                                                              ref TArg0 targ0,
                                                                              ref TArg1 targ1,
                                                                              ref TArg2 targ2,
                                                                              ref TArg3 targ3,
                                                                              ref TArg4 targ4)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
            where TArg3 : unmanaged
            where TArg4 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            ref var      arg3 = ref UnsafeUtility.As<TArg3, byte>(ref targ3);
            ref var      arg4 = ref UnsafeUtility.As<TArg4, byte>(ref targ4);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2, a3 = &arg3, a4 = &arg4)
            {
                var context = new FiveArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg4<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((FiveArg*)context.ptr)->arg4;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct SixArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                                     int operation,
                                                                                     ref TArg0 targ0,
                                                                                     ref TArg1 targ1,
                                                                                     ref TArg2 targ2,
                                                                                     ref TArg3 targ3,
                                                                                     ref TArg4 targ4,
                                                                                     ref TArg5 targ5)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
            where TArg3 : unmanaged
            where TArg4 : unmanaged
            where TArg5 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            ref var      arg3 = ref UnsafeUtility.As<TArg3, byte>(ref targ3);
            ref var      arg4 = ref UnsafeUtility.As<TArg4, byte>(ref targ4);
            ref var      arg5 = ref UnsafeUtility.As<TArg5, byte>(ref targ5);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2, a3 = &arg3, a4 = &arg4, a5 = &arg5)
            {
                var context = new SixArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg5<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((SixArg*)context.ptr)->arg5;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct SevenArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
            public void* arg6;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                                            int operation,
                                                                                            ref TArg0 targ0,
                                                                                            ref TArg1 targ1,
                                                                                            ref TArg2 targ2,
                                                                                            ref TArg3 targ3,
                                                                                            ref TArg4 targ4,
                                                                                            ref TArg5 targ5,
                                                                                            ref TArg6 targ6)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
            where TArg3 : unmanaged
            where TArg4 : unmanaged
            where TArg5 : unmanaged
            where TArg6 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            ref var      arg3 = ref UnsafeUtility.As<TArg3, byte>(ref targ3);
            ref var      arg4 = ref UnsafeUtility.As<TArg4, byte>(ref targ4);
            ref var      arg5 = ref UnsafeUtility.As<TArg5, byte>(ref targ5);
            ref var      arg6 = ref UnsafeUtility.As<TArg6, byte>(ref targ6);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2, a3 = &arg3, a4 = &arg4, a5 = &arg5, a6 = &arg6)
            {
                var context = new SevenArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                    arg6   = a6,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg6<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((SevenArg*)context.ptr)->arg6;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct EightArg
        {
            public void* objPtr;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
            public void* arg6;
            public void* arg7;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(VPtr vptr, FunctionPointer<BurstDispatchVptrDelegate> functionPtr,
                                                                                                   int operation,
                                                                                                   ref TArg0 targ0,
                                                                                                   ref TArg1 targ1,
                                                                                                   ref TArg2 targ2,
                                                                                                   ref TArg3 targ3,
                                                                                                   ref TArg4 targ4,
                                                                                                   ref TArg5 targ5,
                                                                                                   ref TArg6 targ6,
                                                                                                   ref TArg7 targ7)
            where TArg0 : unmanaged
            where TArg1 : unmanaged
            where TArg2 : unmanaged
            where TArg3 : unmanaged
            where TArg4 : unmanaged
            where TArg5 : unmanaged
            where TArg6 : unmanaged
            where TArg7 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            ref var      arg2 = ref UnsafeUtility.As<TArg2, byte>(ref targ2);
            ref var      arg3 = ref UnsafeUtility.As<TArg3, byte>(ref targ3);
            ref var      arg4 = ref UnsafeUtility.As<TArg4, byte>(ref targ4);
            ref var      arg5 = ref UnsafeUtility.As<TArg5, byte>(ref targ5);
            ref var      arg6 = ref UnsafeUtility.As<TArg6, byte>(ref targ6);
            ref var      arg7 = ref UnsafeUtility.As<TArg7, byte>(ref targ7);
            fixed (byte* a0   = &arg0, a1 = &arg1, a2 = &arg2, a3 = &arg3, a4 = &arg4, a5 = &arg5, a6 = &arg6, a7 = &arg7)
            {
                var context = new EightArg
                {
                    objPtr = vptr.ptr,
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                    arg6   = a6,
                    arg7   = a7,
                };
                functionPtr.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg7<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((EightArg*)context.ptr)->arg7;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        // Todo: Need more than 8?
        #endregion

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckFunctionFound(bool found)
        {
            if (!found)
            {
                throw new System.InvalidOperationException(
                    "Failed to find the VPtrFunction. Please make sure you declared your struct as partial, loaded the assembly, and did not make your IDE generate empty IVInterface implementations.");
            }
        }
    }

    static class VTable
    {
        struct Key : IEquatable<Key>
        {
            public long interfaceHash;
            public long structHash;

            public bool Equals(Key other)
            {
                return interfaceHash.Equals(other.interfaceHash) && structHash.Equals(other.structHash);
            }

            public override int GetHashCode()
            {
                int4 split;
                split.x = (int)interfaceHash;
                split.y = (int)(interfaceHash >> 32);
                split.z = (int)structHash;
                split.w = (int)(structHash >> 32);
                return split.GetHashCode();
            }
        }

        static readonly SharedStatic<UnsafeHashMap<Key, FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> > > s_lookup = SharedStatic<UnsafeHashMap<Key,
                                                                                                                                                       FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> > >.
                                                                                                                            GetOrCreate<Key>();

        public static void Add<TInterface, TStruct>(FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> functionPtr) where TStruct : unmanaged,
        TInterface where TInterface : IVInterface
        {
            var key = new Key
            {
                interfaceHash = BurstRuntime.GetHashCode64<TInterface>(),
                structHash    = BurstRuntime.GetHashCode64<TStruct>()
            };
            s_lookup.Data.Add(key, functionPtr);
        }

        public static bool TryGet<TInterface, TStruct>(out FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> functionPtr) where TStruct : unmanaged,
        TInterface where TInterface : IVInterface
        {
            var key = new Key
            {
                interfaceHash = BurstRuntime.GetHashCode64<TInterface>(),
                structHash    = BurstRuntime.GetHashCode64<TStruct>()
            };
            return s_lookup.Data.TryGetValue(key, out functionPtr);
        }

        public static bool TryGet<TInterface>(long structHash, out FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> functionPtr) where TInterface : IVInterface
        {
            var key = new Key
            {
                interfaceHash = BurstRuntime.GetHashCode64<TInterface>(),
                structHash    = structHash
            };
            return s_lookup.Data.TryGetValue(key, out functionPtr);
        }

        public static void InitializeStatics()
        {
            s_lookup.Data = new UnsafeHashMap<Key, FunctionPointer<StaticAPI.BurstDispatchVptrDelegate> >(1024, Allocator.Persistent);
        }

        public static void DisposeStatics()
        {
            s_lookup.Data.Dispose();
        }
    }

    internal static class AssemblyManager
    {
        static bool              initialized      = false;
        static HashSet<Assembly> loadedAssemblies = new HashSet<Assembly>();
        static List<Type>        structTypeCache  = new List<Type>();

        delegate void InitializeDelegate();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        public static void Initialize()
        {
            if (initialized)
                return;

            VTable.InitializeStatics();

#if UNITY_EDITOR

            var structTypes  = UnityEditor.TypeCache.GetTypesDerivedFrom<IVInterface>();
            var coreAssembly = typeof(IVInterface).Assembly;

            foreach (var s in structTypes)
            {
                if (s.IsValueType)
                {
                    InitializeStructType(s);
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BootstrapTools.IsAssemblyReferencingOtherAssembly(assembly, coreAssembly))
                    continue;

                loadedAssemblies.Add(assembly);
            }
#else
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssembly(assembly);
            }
#endif
            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };

            initialized = true;
        }

        public static void Shutdown()
        {
            if (!initialized)
                return;

            VTable.DisposeStatics();
            loadedAssemblies.Clear();
            initialized = false;
        }

        public static void AddAssembly(Assembly assembly)
        {
            if (!BootstrapTools.IsAssemblyReferencingSubstring(assembly, "Unika"))
                return;

            if (loadedAssemblies.Contains(assembly))
                return;

            loadedAssemblies.Add(assembly);

            structTypeCache.Clear();

            var structType = typeof(IVInterface);

            try
            {
                var assemblyTypes = assembly.GetTypes();
                foreach (var t in assemblyTypes)
                {
                    if (t.IsValueType && structType.IsAssignableFrom(t))
                        structTypeCache.Add(t);
                }
            }
            catch (ReflectionTypeLoadException e)
            {
                foreach (var t in e.Types)
                {
                    if (t != null && t.IsValueType && structType.IsAssignableFrom(t))
                        structTypeCache.Add(t);
                }

                UnityEngine.Debug.LogWarning($"Core AssemblyManager.cs failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            foreach (var s in structTypeCache)
            {
                InitializeStructType(s);
            }
        }

        static void InitializeStructType(Type structType)
        {
            var method = structType.GetMethod("__Initialize", BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                UnityEngine.Debug.LogError($"Latios.Core failed to intialize {structType}. Are you missing the `partial` keyword?");
                return;
            }
            var invokable = method.CreateDelegate(typeof(InitializeDelegate)) as InitializeDelegate;
            invokable();
        }
    }
}

