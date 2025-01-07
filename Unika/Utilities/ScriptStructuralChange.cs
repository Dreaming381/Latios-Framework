using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// Utility methods for adding and removing scripts at runtime.
    /// WARNING: Calling this on a script buffer from within a script that lives in that buffer can corrupt memory and result in crashes.
    /// </summary>
    public static class ScriptStructuralChange
    {
        /// <summary>
        /// Adds a script to the entity. The new script will be at the last index in the script collection.
        /// </summary>
        /// <typeparam name="T">The type of script to add</typeparam>
        /// <param name="script">The script initial field values to add</param>
        /// <param name="userByte">The initial userByte value for the script</param>
        /// <param name="userFlagA">The initial userFlagA value for the script</param>
        /// <param name="userFlagB">The initial userFlagB value for the script</param>
        /// <returns>The index of the new script in the script collection</returns>
        public static int AddScript<T>(this DynamicBuffer<UnikaScripts> scriptsBuffer,
                                       in T script,
                                       byte userByte = 0,
                                       bool userFlagA = false,
                                       bool userFlagB = false) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var scriptType  = ScriptTypeInfoManager.GetScriptRuntimeIdAndMask<T>().runtimeId;
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

            return index;
        }

        /// <summary>
        /// Removes the script at the specified index from the entity
        /// </summary>
        /// <param name="index">The index to remove of the script within the list of all scripts on the entity</param>
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

