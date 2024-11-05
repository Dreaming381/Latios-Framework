using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public interface IScriptExtensionsApi
    {
        public Entity entity { get; }

        public EntityScriptCollection allScripts { get; }

        public int indexInEntity { get; }

        public byte userByte { get; set; }

        public bool userFlagA { get; set; }

        public bool userFlagB { get; set; }

        // Should be explicit implementations only
        public ScriptRef ToRef();
    }

    public interface IScriptTypedExtensionsApi : IScriptExtensionsApi
    {
        // Should be explicit implementations only
        public Script ToScript();

        bool Is(in Script script);

        bool TryCastInit(in Script script);
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

