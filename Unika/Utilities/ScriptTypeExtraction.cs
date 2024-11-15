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

        internal abstract class ExtractorBase
        {
            public abstract void Extract<T>(Script script, ref T receiver) where T : IReceiver;
        }

        internal class Extractor<T> : ExtractorBase where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            public override void Extract<TReceiver>(Script script, ref TReceiver receiver)
            {
                script.TryCastScript<T>(out var casted);
                receiver.Receive(casted);
            }
        }

        internal static List<ExtractorBase> extractors = new List<ExtractorBase>()
        {
            null
        };
    }
}

