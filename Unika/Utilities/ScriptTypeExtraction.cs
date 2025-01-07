using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// Static class which can be used to invoke a generic method typed by a script's type using only an untyped Script instance.
    /// This is NOT Burst-compatible.
    /// </summary>
    public static class ScriptTypeExtraction
    {
        /// <summary>
        /// An interface which can receive the strongly-typed script
        /// </summary>
        public interface IReceiver
        {
            /// <summary>
            /// Receives the strongly-typed representation of the script passed into ScriptTypeExtraction.Extract().
            /// </summary>
            /// <typeparam name="T">The type of the Script</typeparam>
            /// <param name="script">The script instance, in its strongly-typed form</param>
            public void Receive<T>(Script<T> script) where T : unmanaged, IUnikaScript, IUnikaScriptGen;
        }

        /// <summary>
        /// Determines the underlying type of the passed-in script, and invokes the receiver's Receive() method using the underlying type.
        /// </summary>
        /// <typeparam name="T">The type of the receiver</typeparam>
        /// <param name="script">The untyped script to analyze</param>
        /// <param name="receiver">The receiver instance</param>
        public static void Extract<T>(Script script, ref T receiver) where T : IReceiver
        {
            if (script == Script.Null)
                throw new System.ArgumentNullException("script");
            extractors[script.m_headerRO.scriptType].Extract(script, ref receiver);
        }

        /// <summary>
        /// An interface which can receive a concrete script type derived from a system type (without the need for reflection)
        /// </summary>
        public interface ITypeReceiver
        {
            /// <summary>
            /// Receives the strong type of the script type passed into ScriptTypeExtraction.TryExtractType().
            /// </summary>
            /// <typeparam name="T">The type of the Script</typeparam>
            public void Receive<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen;
        }

        /// <summary>
        /// Attempts to use the script type registry to generate a concrete receiver of the passed in script type, without the need for reflection.
        /// </summary>
        /// <typeparam name="T">The type of the receiver</typeparam>
        /// <param name="scriptType">The reflected type of the script</param>
        /// <param name="receiver">The receiver instance</param>
        /// <returns>True if the extraction was successful, false if the passed in type was not a known Unika script type</returns>
        public static bool TryExtractType<T>(System.Type scriptType, ref T receiver) where T : ITypeReceiver
        {
            if (extractorTypeLookup.TryGetValue(scriptType, out var index))
            {
                extractors[index].ExtractType<T>(ref receiver);
                return true;
            }
            return false;
        }

        internal abstract class ExtractorBase
        {
            public abstract void Extract<TReceiver>(Script script, ref TReceiver receiver) where TReceiver : IReceiver;
            public abstract void ExtractType<TTypeReceiver>(ref TTypeReceiver receiver) where TTypeReceiver : ITypeReceiver;
        }

        internal class Extractor<T> : ExtractorBase where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            public override void Extract<TReceiver>(Script script, ref TReceiver receiver)
            {
                script.TryCastScript<T>(out var casted);
                receiver.Receive(casted);
            }

            public override void ExtractType<TTypeReceiver>(ref TTypeReceiver receiver)
            {
                receiver.Receive<T>();
            }
        }

        internal static List<ExtractorBase> extractors = new List<ExtractorBase>()
        {
            null
        };

        internal static Dictionary<System.Type, int> extractorTypeLookup = new Dictionary<System.Type, int>();

        internal static void AddExtractorType<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            extractorTypeLookup.Add(typeof(T), extractors.Count);
            extractors.Add(new Extractor<T>());
        }
    }
}

