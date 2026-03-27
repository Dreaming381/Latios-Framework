//using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    public partial interface IAuxDisposable : System.IDisposable, IVInterface
    {
        //public struct VPtrFunction : global::System.IEquatable<VPtrFunction>
        //{
        //    global::Unity.Burst.FunctionPointer<global::Latios.Unsafe.InternalSourceGen.StaticAPI.BurstDispatchVptrDelegate> __functionPtr;
        //
        //    public bool Equals(VPtrFunction other) => __functionPtr.Value.Equals(other.__functionPtr.Value);
        //    public override int GetHashCode() => __functionPtr.Value.GetHashCode();
        //}
        //
        //public struct VPtr : IAuxDisposable
        //{
        //    global::Latios.Unsafe.InternalSourceGen.StaticAPI.VPtr __ptr;
        //    VPtrFunction                                  __function;
        //
        //    public VPtrFunction vptrFunction => __function;
        //    public global::Latios.Unsafe.UnsafeApiPointer ptr => __ptr.AsPtr();
        //    public VPtr(global::Latios.Unsafe.UnsafeApiPointer pointer, VPtrFunction function)
        //    {
        //        __ptr      = global::Latios.Unsafe.InternalSourceGen.StaticAPI.VPtr.Create(pointer);
        //        __function = function;
        //    }
        //    public static VPtr Create<T>(global::Latios.Unsafe.UnsafeApiPointer<T> pointer) where T : unmanaged, IAuxDisposable
        //    {
        //        var functionPtr = global::Latios.Unsafe.InternalSourceGen.StaticAPI.GetFunctionChecked<IAuxDisposable, T>();
        //        return new VPtr(pointer, global::Latios.Unsafe.InternalSourceGen.StaticAPI.ConvertFunctionPointerToWrapper<VPtrFunction>(functionPtr));
        //    }
        //
        //    // For each interface
        //    [global::AOT.MonoPInvokeCallback(typeof(global::Latios.Unsafe.InternalSourceGen.StaticAPI.BurstDispatchVptrDelegate))]
        //    [global::UnityEngine.Scripting.Preserve]
        //    [global::Unity.Burst.BurstCompile]
        //    public static void __BurstDispatch_IAuxDisposable(global::Latios.Unsafe.InternalSourceGen.StaticAPI.ContextPtr context, int operation)
        //    {
        //        IAuxDisposable.__Dispatch<VPtr>(context, operation);
        //    }
        //
        //    public static void __Initialize()
        //    {
        //        // For each interface
        //        {
        //            var functionPtr = global::Unity.Burst.BurstCompiler.CompileFunctionPointer<global::Latios.Unsafe.InternalSourceGen.StaticAPI.BurstDispatchVptrDelegate>(
        //                __BurstDispatch_IAuxDisposable);
        //            global::Latios.Unsafe.InternalSourceGen.StaticAPI.RegisterVptrFunction<IAuxDisposable, VPtr>(functionPtr);
        //        }
        //    }
        //
        //    void global::Latios.Unsafe.IVInterface.__ThisMethodIsSupposedToBeGeneratedByASourceGenerator()
        //    {
        //    }
        //
        //    public void Dispose()
        //    {
        //        var vptrDelegate = global::Latios.Unsafe.InternalSourceGen.StaticAPI.ConvertFunctionPointerFromWrapper(__function);
        //        global::Latios.Unsafe.InternalSourceGen.StaticAPI.Dispatch(__ptr, vptrDelegate, 0);
        //    }
        //}
        //
        //public static VPtrFunction GetVPtrFunctionFrom<T>() where T : unmanaged, IAuxDisposable
        //{
        //    global::Latios.Unsafe.InternalSourceGen.StaticAPI.TryGetFunction<IAuxDisposable, T>(out var functionPtr);
        //    return global::Latios.Unsafe.InternalSourceGen.StaticAPI.ConvertFunctionPointerToWrapper<VPtrFunction>(functionPtr);
        //}
        //
        //public static bool TryGetVptrFunctionFrom(long structTypeBurstHash, out VPtrFunction function)
        //{
        //    var result = global::Latios.Unsafe.InternalSourceGen.StaticAPI.TryGetFunction<IAuxDisposable>(structTypeBurstHash, out var functionPtr);
        //    function   = result ? global::Latios.Unsafe.InternalSourceGen.StaticAPI.ConvertFunctionPointerToWrapper<VPtrFunction>(functionPtr) : default;
        //    return result;
        //}
        //
        //public static void __Dispatch<T>(global::Latios.Unsafe.InternalSourceGen.StaticAPI.ContextPtr __context, int __operation) where T : unmanaged, IAuxDisposable
        //{
        //    switch (__operation)
        //    {
        //        case 0:
        //        {
        //            ref var obj = ref global::Latios.Unsafe.InternalSourceGen.StaticAPI.ExtractObject<T>(__context);
        //            obj.Dispose();
        //            break;
        //        }
        //    }
        //}
    }

    public partial struct TestDisposable : IAuxDisposable, IVInterface
    {
        public void Dispose()
        {
        }

        //// For each interface
        //[global::AOT.MonoPInvokeCallback(typeof(global::Latios.Unsafe.InternalSourceGen.StaticAPI.BurstDispatchVptrDelegate))]
        //[global::UnityEngine.Scripting.Preserve]
        //[global::Unity.Burst.BurstCompile]
        //public static void __BurstDispatch_IAuxDisposable(global::Latios.Unsafe.InternalSourceGen.StaticAPI.ContextPtr context, int operation)
        //{
        //    IAuxDisposable.__Dispatch<TestDisposable>(context, operation);
        //}
        //
        //public static void __Initialize()
        //{
        //    // For each interface
        //    {
        //        var functionPtr = global::Unity.Burst.BurstCompiler.CompileFunctionPointer<global::Latios.Unsafe.InternalSourceGen.StaticAPI.BurstDispatchVptrDelegate>(
        //            __BurstDispatch_IAuxDisposable);
        //        global::Latios.Unsafe.InternalSourceGen.StaticAPI.RegisterVptrFunction<IAuxDisposable, TestDisposable>(functionPtr);
        //    }
        //}
        //
        //void global::Latios.Unsafe.IVInterface.__ThisMethodIsSupposedToBeGeneratedByASourceGenerator()
        //{
        //}
    }
}

