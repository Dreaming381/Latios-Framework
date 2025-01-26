using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
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

        public static bool IsInterface<TInterface>(in Script script) where TInterface : IUnikaInterface, IUnikaInterfaceGen
        {
            return ScriptCast.IsInterface<TInterface>(in script);
        }

        public static unsafe bool TryCastInitInterface<TInterface>(in Script script, IScriptTypedExtensionsApi.WrappedThisPtr thisPtr) where TInterface : IUnikaInterface
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
                    UnsafeUtility.CopyStructureToPtr(ref result, thisPtr.ptr);
                    return true;
                }
            }
            return false;
        }

        public static IScriptTypedExtensionsApi.WrappedIdAndMask GetIdAndMaskInterface<TInterface>() where TInterface : IUnikaInterface
        {
            return new IScriptTypedExtensionsApi.WrappedIdAndMask { idAndMask = ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<TInterface>() };
        }

        public interface IInterfaceData : IScriptTypedExtensionsApi
        {
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

        public interface IInterfaceRefData : IScriptRefTypedExtensionsApi
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

            var scriptId = ScriptTypeInfoManager.GetScriptRuntimeIdAndMask<T>().runtimeId;
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
                return script.TryCast(out dst);
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
                return script.TryCast(out dst);
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
            var script = ScriptCast.Resolve(ref src, ref resolver);
            if (script.TryCast(out TDst dst))
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

        unsafe struct ThreeArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2>(ref InterfaceData data, int operation, ref TArg0 targ0, ref TArg1 targ1, ref TArg2 targ2)
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg2<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((ThreeArg*)context.ptr)->arg2;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct FourArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3>(ref InterfaceData data, int operation, ref TArg0 targ0, ref TArg1 targ1, ref TArg2 targ2, ref TArg3 targ3)
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg3<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((FourArg*)context.ptr)->arg3;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct FiveArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4>(ref InterfaceData data,
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg4<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((FiveArg*)context.ptr)->arg4;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct SixArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5>(ref InterfaceData data,
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg5<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((SixArg*)context.ptr)->arg5;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct SevenArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
            public void* arg6;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(ref InterfaceData data,
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                    arg6   = a6,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg6<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((SevenArg*)context.ptr)->arg6;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        unsafe struct EightArg
        {
            public void* script;
            public void* arg0;
            public void* arg1;
            public void* arg2;
            public void* arg3;
            public void* arg4;
            public void* arg5;
            public void* arg6;
            public void* arg7;
        }

        public static unsafe void Dispatch<TArg0, TArg1, TArg2, TArg3, TArg4, TArg5, TArg6, TArg7>(ref InterfaceData data,
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
                    script = data.script.GetUnsafePtrAsBytePtr(),
                    arg0   = a0,
                    arg1   = a1,
                    arg2   = a2,
                    arg3   = a3,
                    arg4   = a4,
                    arg5   = a5,
                    arg6   = a6,
                    arg7   = a7,
                };
                data.functionPointer.Invoke(&context, operation);
            }
        }

        public static unsafe ref TArg ExtractArg7<TArg>(ContextPtr context) where TArg : unmanaged
        {
            var argPtr = ((EightArg*)context.ptr)->arg7;
            return ref UnsafeUtility.AsRef<TArg>(argPtr);
        }

        // Todo: Need more than 8?
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ThrowBadCastOnResolve(Script script)
        {
            throw new System.InvalidCastException($"{script.ToFixedString()} does not implement the requested interface.");
        }
        #endregion
    }

    //interface ITestInterface : IUnikaInterface
    //{
    //}
    //
    //struct TestStruct : StaticAPI.IInterfaceDataTyped<ITestInterface, TestStruct>
    //{
    //    StaticAPI.InterfaceData data;
    //
    //    TestStruct StaticAPI.IInterfaceDataTyped<ITestInterface, TestStruct>.assign { set => data = value.data; }
    //
    //    public Entity entity => throw new System.NotImplementedException();
    //
    //    public EntityScriptCollection allScripts => throw new System.NotImplementedException();
    //
    //    public int indexInEntity => throw new System.NotImplementedException();
    //
    //    public byte userByte { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    //    public bool userFlagA { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    //    public bool userFlagB { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    //
    //    public ScriptRef ToRef()
    //    {
    //        throw new System.NotImplementedException();
    //    }
    //
    //    public Script ToScript()
    //    {
    //        throw new System.NotImplementedException();
    //    }
    //
    //    IScriptTypedExtensionsApi.WrappedIdAndMask IScriptTypedExtensionsApi.GetIdAndMask()
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
    //
    ////[BurstCompile]
    //static class TestStaticClass
    //{
    //    //[BurstCompile]
    //    public static void DoTest()
    //    {
    //        Script    script    = default;
    //        ScriptRef scriptRef = script;
    //        StaticAPI.TryResolve<TestStruct>(ref scriptRef, script.allScripts, out var result);
    //    }
    //}
}

