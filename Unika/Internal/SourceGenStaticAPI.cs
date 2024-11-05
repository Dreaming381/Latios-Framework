using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika.InternalSourceGen
{
    public static partial class StaticAPI
    {
        #region Types
        public interface IUnikaInterfaceSourceGenerated : IUnikaInterfaceGen
        {
        }

        public interface IUnikaScriptSourceGenerated : IUnikaScriptGen
        {
        }

        public interface IInterfaceData : IScriptTypedExtensionsApi
        {
        }

        public interface IInterfaceDataTyped<TInterface, TInterfaceStruct> : IInterfaceData where TInterface : IUnikaInterface
            where TInterfaceStruct : unmanaged, IInterfaceDataTyped<TInterface, TInterfaceStruct>
        {
            TInterfaceStruct assign { set; }

            bool IScriptTypedExtensionsApi.Is(in Script script)
            {
                var idAndMask = ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<TInterface>();
                if ((script.m_headerRO.bloomMask & idAndMask.bloomMask) == idAndMask.bloomMask)
                {
                    return ScriptVTable.Contains((short)script.m_headerRO.scriptType, idAndMask.runtimeId);
                }
                return false;
            }

            bool IScriptTypedExtensionsApi.TryCastInit(in Script script)
            {
                var idAndMask = ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<TInterface>();
                if ((script.m_headerRO.bloomMask & idAndMask.bloomMask) == idAndMask.bloomMask)
                {
                    if (ScriptVTable.TryGet((short)script.m_headerRO.scriptType, idAndMask.runtimeId, out var functionPtr))
                    {
                        var result = new InterfaceData
                        {
                            functionPointer = functionPtr,
                            script          = script
                        };
                        assign = UnsafeUtility.As<InterfaceData, TInterfaceStruct>(ref result);
                        return true;
                    }
                }
                return false;
            }
        }

        public struct InterfaceData
        {
            internal FunctionPointer<BurstDispatchScriptDelegate> functionPointer;
            internal Script                                       script;

            public Entity entity => script.entity;
            public EntityScriptCollection allScripts => script.allScripts;
            public int indexInEntity => script.indexInEntity;
            public byte userByte { get => script.userByte; set => script.userByte    = value; }
            public bool userFlagA { get => script.userFlagA; set => script.userFlagA = value; }
            public bool userFlagB { get => script.userFlagB; set => script.userFlagB = value; }
            public Script ToScript() => script;
            public T ToRef<T>() where T : unmanaged, IInterfaceRefData
            {
                var data = new InterfaceRefData { scriptRef = script };
                return UnsafeUtility.As<InterfaceRefData, T>(ref data);
            }
        }

        public interface IInterfaceRefData
        {
        }

        public struct InterfaceRefData
        {
            internal ScriptRef scriptRef;

            public Entity entity => scriptRef.entity;
            public ScriptRef ToScriptRef() => scriptRef;
        }
        #endregion

        #region Registration
        public struct ScriptInterfaceRegistrationData
        {
            public short                                        runtimeId;
            public ulong                                        bloomMask;
            public FunctionPointer<BurstDispatchScriptDelegate> functionPtr;
        }

        // functionPtr is left unpopulated
        public static ScriptInterfaceRegistrationData InitializeInterface<T>() where T : IUnikaInterface
        {
            ScriptTypeInfoManager.InitializeInterface<T>();
            var idAndMask = ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<T>();
            return new ScriptInterfaceRegistrationData
            {
                runtimeId = idAndMask.runtimeId,
                bloomMask = idAndMask.bloomMask
            };
        }

        public static void InitializeScript<T>(ReadOnlySpan<ScriptInterfaceRegistrationData> interfacesImplemented) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            Span<ScriptTypeInfoManager.IdAndMask> idAndMasks = stackalloc ScriptTypeInfoManager.IdAndMask[interfacesImplemented.Length];
            for (int i = 0; i < idAndMasks.Length; i++)
            {
                idAndMasks[i] = new ScriptTypeInfoManager.IdAndMask
                {
                    runtimeId = interfacesImplemented[i].runtimeId,
                    bloomMask = interfacesImplemented[i].bloomMask
                };
            }
            ScriptTypeInfoManager.InitializeScriptType<T>(idAndMasks);

            var scriptId = ScriptTypeInfoManager.GetScriptRuntimeId<T>().runtimeId;
            foreach (var i in interfacesImplemented)
            {
                ScriptVTable.Add(scriptId, i.runtimeId, i.functionPtr);
            }
        }
        #endregion

        #region Casting
        public static TDst DownCast<TDst, TDstInterface>(InterfaceData src) where TDst : unmanaged, IInterfaceData where TDstInterface : IUnikaInterface
        {
            InterfaceData dst = default;
            dst.script        = src.script;
            var type          = (short)src.script.m_headerRO.scriptType;
            ScriptVTable.TryGet(type, ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<TDstInterface>().runtimeId, out dst.functionPointer);
            return UnsafeUtility.As<InterfaceData, TDst>(ref dst);
        }

        public static TDst DownCast<TDst, TDstInterface, TScript>(Script<TScript> src) where TDst : unmanaged,
        IInterfaceData where TDstInterface : IUnikaInterface where TScript : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            InterfaceData dst = default;
            dst.script        = src;
            var type          = (short)src.m_headerRO.scriptType;
            ScriptVTable.TryGet(type, ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<TDstInterface>().runtimeId, out dst.functionPointer);
            return UnsafeUtility.As<InterfaceData, TDst>(ref dst);
        }

        public static bool TryResolve<TDst>(ref ScriptRef src, in EntityScriptCollection allScripts, out TDst dst)
            where TDst : unmanaged, IScriptTypedExtensionsApi
        {
            if (ScriptCast.TryResolve(ref src, in allScripts, out var script))
            {
                dst = default;
                return dst.TryCastInit(in script);
            }
            dst = default;
            return false;
        }

        public static bool TryResolve<TDst>(ref InterfaceRefData src, in EntityScriptCollection allScripts, out TDst dst)
            where TDst : unmanaged, IScriptTypedExtensionsApi
        {
            return TryResolve(ref src.scriptRef, in allScripts, out dst);
        }

        public static bool TryResolve<TDst, TResolver>(ref ScriptRef src, ref TResolver resolver, out TDst dst)
            where TDst : unmanaged, IScriptTypedExtensionsApi
            where TResolver : unmanaged, IScriptResolverBase
        {
            if (ScriptCast.TryResolve(ref src, ref resolver, out var script))
            {
                dst = default;
                return dst.TryCastInit(in script);
            }
            dst = default;
            return false;
        }

        public static bool TryResolve<TDst, TResolver>(ref InterfaceRefData src, ref TResolver resolver, out TDst dst)
            where TDst : unmanaged, IScriptTypedExtensionsApi
            where TResolver : unmanaged, IScriptResolverBase
        {
            return TryResolve(ref src.scriptRef, ref resolver, out dst);
        }

        public static TDst Resolve<TDst>(ref ScriptRef src, in EntityScriptCollection allScripts)
            where TDst : unmanaged, IScriptTypedExtensionsApi
        {
            var found = TryResolve<TDst>(ref src, in allScripts, out var dst);
            ScriptCast.AssertInCollection(found, allScripts.entity);
            return dst;
        }

        public static TDst Resolve<TDst>(ref InterfaceRefData src, in EntityScriptCollection allScripts)
            where TDst : unmanaged, IScriptTypedExtensionsApi
        {
            return Resolve<TDst>(ref src.scriptRef, in allScripts);
        }

        public static TDst Resolve<TDst, TResolver>(ref ScriptRef src, ref TResolver resolver)
            where TDst : unmanaged, IScriptTypedExtensionsApi
            where TResolver : unmanaged, IScriptResolverBase
        {
            var  script = ScriptCast.Resolve(ref src, ref resolver);
            TDst dst    = default;
            if (dst.TryCastInit(in script))
                return dst;
            ThrowBadCastOnResolve(script);
            return default;
        }

        public static TDst Resolve<TDst, TResolver>(ref InterfaceRefData src, ref TResolver resolver)
            where TDst : unmanaged, IScriptTypedExtensionsApi
            where TResolver : unmanaged, IScriptResolverBase
        {
            return Resolve<TDst, TResolver>(ref src.scriptRef, ref resolver);
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
            public void* script;
        }

        public static unsafe void Dispatch(ref InterfaceData data, int operation)
        {
            var context = new ZeroArg
            {
                script = data.script.GetUnsafePtrAsBytePtr()
            };
            data.functionPointer.Invoke(&context, operation);
        }

        public static unsafe ref TScriptType ExtractScript<TScriptType>(ContextPtr context) where TScriptType : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var scriptPtr = ((ZeroArg*)context.ptr)->script;
            return ref UnsafeUtility.AsRef<TScriptType>(scriptPtr);
        }

        unsafe struct OneArg
        {
            public void* script;
            public void* arg0;
        }

        public static unsafe void Dispatch<TArg0>(ref InterfaceData data, int operation, ref TArg0 targ0) where TArg0 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            fixed (byte* a0   = &arg0)
            {
                var context = new OneArg
                {
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg0<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((OneArg*)context.ptr)->arg0;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct TwoArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
        }

        public static unsafe void Dispatch<TArg0, TArg1>(ref InterfaceData data, int operation, ref TArg0 targ0, ref TArg1 targ1) where TArg0 : unmanaged where TArg1 : unmanaged
        {
            ref var      arg0 = ref UnsafeUtility.As<TArg0, byte>(ref targ0);
            ref var      arg1 = ref UnsafeUtility.As<TArg1, byte>(ref targ1);
            fixed (byte* a0   = &arg0, a1 = &arg1)
            {
                var context = new TwoArg
                {
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg1<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((TwoArg*)context.ptr)->arg1;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        // Todo: 3 - 16
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ThrowBadCastOnResolve(Script script)
        {
            throw new System.InvalidCastException($"{script.ToFixedString()} does not implement the requested interface.");
        }
        #endregion
    }

    interface ITestInterface : IUnikaInterface
    {
    }

    struct TestStruct : StaticAPI.IInterfaceDataTyped<ITestInterface, TestStruct>
    {
        StaticAPI.InterfaceData data;

        TestStruct StaticAPI.IInterfaceDataTyped<ITestInterface, TestStruct>.assign { set => data = value.data; }

        public Entity entity => throw new System.NotImplementedException();

        public EntityScriptCollection allScripts => throw new System.NotImplementedException();

        public int indexInEntity => throw new System.NotImplementedException();

        public byte userByte { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public bool userFlagA { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
        public bool userFlagB { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public ScriptRef ToRef()
        {
            throw new System.NotImplementedException();
        }

        public Script ToScript()
        {
            throw new System.NotImplementedException();
        }
    }

    //[BurstCompile]
    static class TestStaticClass
    {
        //[BurstCompile]
        public static void DoTest()
        {
            Script    script    = default;
            ScriptRef scriptRef = script;
            StaticAPI.TryResolve<TestStruct>(ref scriptRef, script.allScripts, out var result);
        }
    }
}

