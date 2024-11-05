using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    // This API can be used directly in generics or as extensions
    public static class ScriptCast
    {
        public static bool IsScript<T>(this in Script script) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            return script.m_headerRO.scriptType == ScriptTypeInfoManager.GetScriptRuntimeId<T>().runtimeId;
        }

        public static bool IsInterface<T>(this in Script script) where T : unmanaged, IUnikaInterface, IUnikaInterfaceGen
        {
            var idAndMask = ScriptTypeInfoManager.GetInterfaceRuntimeIdAndMask<T>();
            if ((script.m_headerRO.bloomMask & idAndMask.bloomMask) == idAndMask.bloomMask)
            {
                return ScriptVTable.Contains((short)script.m_headerRO.scriptType, idAndMask.runtimeId);
            }
            return false;
        }

        public static bool Is<T>(this in Script script) where T : unmanaged, IScriptTypedExtensionsApi
        {
            T result = default;
            return result.TryCastInit(in script);
        }

        public static bool TryCastScript<T>(this in Script script, out Script<T> casted) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            if (IsScript<T>(in script))
            {
                casted = new Script<T>
                {
                    m_scriptBuffer = script.m_scriptBuffer,
                    m_entity       = script.m_entity,
                    m_headerOffset = script.m_headerOffset,
                    m_byteOffset   = script.m_byteOffset,
                };
                return true;
            }
            casted = default;
            return false;
        }

        public static bool TryCastScript<TSrcScriptType, TDstScript>(this TSrcScriptType script, out Script<TDstScript> casted) where TSrcScriptType : unmanaged,
        IScriptTypedExtensionsApi where TDstScript : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            return TryCastScript(script.ToScript(), out casted);
        }

        public static bool TryCast<T>(this in Script script, out T casted) where T : unmanaged, IScriptTypedExtensionsApi
        {
            casted = default;
            return casted.TryCastInit(in script);
        }

        public static bool TryCast<TSrcScriptType, TDstScriptType>(this TSrcScriptType script, out TDstScriptType casted) where TSrcScriptType : unmanaged,
        IScriptTypedExtensionsApi where TDstScriptType : unmanaged, IScriptTypedExtensionsApi
        {
            return TryCast(script.ToScript(), out casted);
        }

        public static bool TryResolve(this ref ScriptRef scriptRef, in EntityScriptCollection allScripts, out Script script)
        {
            if (allScripts.entity != scriptRef.entity || allScripts.m_buffer.Length == 0)
            {
                script = default;
                return false;
            }

            if (math.clamp(scriptRef.m_cachedHeaderIndex, 0, allScripts.length) == scriptRef.m_cachedHeaderIndex)
            {
                var candidate = allScripts[scriptRef.m_cachedHeaderIndex];
                if (candidate.m_headerRO.instanceId == scriptRef.m_instanceId)
                {
                    script = candidate;
                    return true;
                }
            }

            int foundIndex = 0;
            foreach (var s in allScripts)
            {
                if (s.m_headerRO.instanceId == scriptRef.m_instanceId)
                {
                    script                        = s;
                    scriptRef.m_cachedHeaderIndex = foundIndex;
                    return true;
                }
                foundIndex++;
            }

            scriptRef.m_cachedHeaderIndex = -1;
            script                        = default;
            return false;
        }

        public static bool TryResolve<T>(this ref ScriptRef<T> scriptRef, in EntityScriptCollection allScripts, out Script<T> script) where T : unmanaged, IUnikaScript,
        IUnikaScriptGen
        {
            ScriptRef r = scriptRef;
            if (TryResolve(ref r, in allScripts, out var s))
            {
                if (TryCast(s, out script))
                {
                    scriptRef.m_cachedHeaderIndex = r.m_cachedHeaderIndex;
                    return true;
                }
            }
            script = default;
            return false;
        }

        public static bool TryResolve<TResolver>(this ref ScriptRef scriptRef, ref TResolver resolver, out Script script) where TResolver : unmanaged, IScriptResolverBase
        {
            if (resolver.TryGet(scriptRef.entity, out var allScripts))
            {
                return TryResolve(ref scriptRef, allScripts, out script);
            }
            script = default;
            return false;
        }

        public static bool TryResolve<TResolver, TType>(this ref ScriptRef<TType> scriptRef, ref TResolver resolver, out Script<TType> script)
            where TResolver : unmanaged, IScriptResolverBase
            where TType : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            if (resolver.TryGet(scriptRef.entity, out var allScripts))
            {
                return TryResolve(ref scriptRef, allScripts, out script);
            }
            script = default;
            return false;
        }

        public static Script Resolve(this ref ScriptRef scriptRef, in EntityScriptCollection allScripts)
        {
            bool found = TryResolve(ref scriptRef, allScripts, out var script);
            AssertInCollection(found, allScripts.entity);
            return script;
        }

        public static Script Resolve<TResolver>(this ref ScriptRef scriptRef, ref TResolver resolver) where TResolver : unmanaged, IScriptResolverBase
        {
            resolver.TryGet(scriptRef.entity, out var allScripts, true);
            return Resolve(ref scriptRef, in allScripts);
        }

        public static Script<TType> Resolve<TType>(this ref ScriptRef<TType> scriptRef, in EntityScriptCollection allScripts) where TType : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            bool found = TryResolve(ref scriptRef, allScripts, out Script<TType> script);
            AssertInCollection(found, allScripts.entity);
            return script;
        }

        public static Script<TType> Resolve<TResolver, TType>(this ref ScriptRef<TType> scriptRef, ref TResolver resolver) where TResolver : unmanaged,
        IScriptResolverBase where TType : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            resolver.TryGet(scriptRef.entity, out var allScripts, true);
            return Resolve(ref scriptRef, in allScripts);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void AssertInCollection(bool inCollection, Entity entity)
        {
            if (!inCollection)
                throw new System.InvalidOperationException($"The script instance could not be found in {entity.ToFixedString()}");
        }
    }
}

