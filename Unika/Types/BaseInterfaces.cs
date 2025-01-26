using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A base interface for anything that provides an EntityScriptCollection to facilitate a suite of extension methods
    /// </summary>
    public interface IScriptCollectionExtensionsApi
    {
        public Entity entity { get; }

        public EntityScriptCollection allScripts { get; }
    }

    /// <summary>
    /// A base interface all resolved script types implement to facilitate a suite of extension methods
    /// </summary>
    public interface IScriptExtensionsApi : IScriptCollectionExtensionsApi
    {
        public int indexInEntity { get; }

        public byte userByte { get; set; }

        public bool userFlagA { get; set; }

        public bool userFlagB { get; set; }

        // Should be explicit implementations only
        public ScriptRef ToRef();
    }

    /// <summary>
    /// A base interface for typed resolved scripts (either concrete types or interfaces) to faciliate a suite of extension methods
    /// </summary>
    public interface IScriptTypedExtensionsApi : IScriptExtensionsApi
    {
        public Script ToScript();

        bool Is(in Script script);

        // I don't know if this is a Mono bug or some weird caveat in the C# specification, but it seems impossible
        // for default interface methods to self-mutate the structure they belong to, even if they call into explicit implementations
        // that perform the mutation. Therefore, we instead pass the pointer to the object we want to mutate. The implementation
        // will leverage the smuggled type to copy the necessary data into this pointer.
        //
        // The ScriptCast extension method for Script wraps this complex invocation safely.
        bool TryCastInit(in Script script, WrappedThisPtr thisPtr);

        public struct WrappedIdAndMask
        {
            internal ScriptTypeInfoManager.IdAndMask idAndMask;
        }

        public WrappedIdAndMask GetIdAndMask();

        public unsafe struct WrappedThisPtr
        {
            internal void* ptr;
        }
    }

    public interface IScriptRefTypedExtensionsApi
    {
        public ScriptRef ToScriptRef();
    }

    // This interface is to mark Unika interfaces that have been processed by source generators.
    // If you get an error about this, you probably forgot the partial keyword.
    public interface IUnikaInterfaceGen
    {
    }
    // This interface is to mark Unika scripts that have been processed by source generators.
    // If you get an error about this, you probably forgot the partial keyword.
    public interface IUnikaScriptGen
    {
    }
}

