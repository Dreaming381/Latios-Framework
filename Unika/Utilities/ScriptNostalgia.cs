using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// Extension method APIs that mimic classical Unity GameObject APIs where appropriate
    /// </summary>
    public static class ScriptNostalgia
    {
        /// <summary>
        /// Gets the script of the specified type. If no script is found, returns Null.
        /// </summary>
        public static Script<T> GetScript<T>(this EntityScriptCollection allScripts) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            foreach (var candidate in allScripts.OfType<T>())
                return candidate;
            return default;
        }

        /// <summary>
        /// Gets the script of the specified type. If no script is found, returns Null.
        /// </summary>
        public static Script<T> GetScript<T>(this Script script) where T : unmanaged, IUnikaScript, IUnikaScriptGen => script.allScripts.GetScript<T>();

        /// <summary>
        /// Gets the script in the form of the specified type and returns true. If no script is found, returns false.
        /// </summary>
        public static bool TryGetScript<T>(this EntityScriptCollection allScripts, out T script) where T : unmanaged, IScriptTypedExtensionsApi
        {
            script = allScripts.GetScript<T>();
            return script.ToScript() != Script.Null;
        }

        /// <summary>
        /// Gets the script in the form of the specified type and returns true. If no script is found, returns false.
        /// </summary>
        public static bool TryGetScript<T>(this Script srcScript, out T script) where T : unmanaged, IScriptTypedExtensionsApi
        {
            script = srcScript.GetScript<T>();
            return script.ToScript() != Script.Null;
        }

        /// <summary>
        /// Fills the provided list with scripts in the form of the specified type. The list is resized to match the number of results found,
        /// and any existing values in the list are overwritten.
        /// </summary>
        public static void GetScripts<T>(this EntityScriptCollection allScripts, NativeList<T> results) where T : unmanaged, IScriptTypedExtensionsApi
        {
            results.Clear();
            foreach (var candidate in allScripts.Of<T>())
                results.Add(candidate);
        }
    }

    /// <summary>
    /// More extension method APIs that mimic classical Unity GameObject APIs where appropriate, specifically for handling overloads that only differ by constraints
    /// </summary>
    public static class ScriptNostalgiaTyped
    {
        /// <summary>
        /// Gets the script in the form of the specified type. If no script is found, returns Null.
        /// </summary>
        public static T GetScript<T>(this EntityScriptCollection allScripts) where T : unmanaged, IScriptTypedExtensionsApi
        {
            foreach (var candidate in allScripts.Of<T>())
                return candidate;
            return default;
        }

        /// <summary>
        /// Gets the script in the form of the specified type. If no script is found, returns Null.
        /// </summary>
        public static T GetScript<T>(this Script script) where T : unmanaged, IScriptTypedExtensionsApi => script.allScripts.GetScript<T>();
    }

    //static class ExtensionResolutionTest
    //{
    //    static void Do()
    //    {
    //        EntityScriptCollection allScripts = new EntityScriptCollection();
    //
    //    }
    //}
}

