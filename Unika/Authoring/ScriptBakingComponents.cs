using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika.Authoring
{
    [BakingType]
    internal struct BakedScriptByte : IBufferElementData
    {
        public byte b;
    }

    [BakingType]
    internal struct BakedScriptMetadata : IComponentData
    {
        public ScriptRef scriptRef;
        public int       scriptType;
        public byte      userByte;
        public bool      userFlagA;
        public bool      userFlagB;
    }
}

