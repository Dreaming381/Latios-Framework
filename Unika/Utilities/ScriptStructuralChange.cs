using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public static class ScriptStructuralChange
    {
        public static void AddScript<T>(this DynamicBuffer<UnikaScripts> scriptsBuffer,
                                        in T script,
                                        byte userByte = 0,
                                        bool userFlagA = false,
                                        bool userFlagB = false) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var scriptType  = ScriptTypeInfoManager.GetScriptRuntimeId<T>().runtimeId;
            var index       = ScriptStructuralChangeInternal.AllocateScript(ref scriptsBuffer, scriptType);
            var result      = scriptsBuffer.AllScripts(default)[index];
            var typedResult = new Script<T>
            {
                m_scriptBuffer = result.m_scriptBuffer,
                m_entity       = result.m_entity,
                m_headerOffset = result.m_headerOffset,
                m_byteOffset   = result.m_byteOffset,
            };
            typedResult.valueRW   = script;
            typedResult.userByte  = userByte;
            typedResult.userFlagA = userFlagA;
            typedResult.userFlagB = userFlagB;
        }

        public static void RemoveScript(this DynamicBuffer<UnikaScripts> scriptsBuffer, int index)
        {
            CheckInRange(ref scriptsBuffer, index);
            ScriptStructuralChangeInternal.FreeScript(ref scriptsBuffer, index);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckInRange(ref DynamicBuffer<UnikaScripts> scriptsBuffer, int index)
        {
            if (index < 0 || index >= scriptsBuffer.AllScripts(default).length)
                throw new System.ArgumentOutOfRangeException($"Index {index} is outside the range [0, {scriptsBuffer.AllScripts(default).length}) of valid scripts in the entity.");
        }
    }
}

