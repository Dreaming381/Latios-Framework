using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    internal struct MailboxAllocator : IComponentData
    {
        public AllocatorManager.AllocatorHandle allocator;
    }

    internal struct MailboxInitializedTag : ICleanupComponentData { }

    // Blackboard
    internal partial struct UploadCommands : ICollectionComponent
    {
        public struct Command
        {
            public Entity             visualEffectEntity;
            public FixedString64Bytes bufferPropertyName;
            public FixedString64Bytes bufferStartPropertyName;
            public FixedString64Bytes bufferCountPropertyName;
            public int                start;
            public int                count;
            public int                bufferIndex;
        }

        public NativeList<Command> commands;

        public JobHandle TryDispose(JobHandle inputDeps) => commands.Dispose(inputDeps);
    }

    internal partial struct MappedGraphicsBuffers : ICollectionComponent
    {
        public struct MappedBuffer
        {
            public UnityEngine.GraphicsBuffer mappedBuffer;
            public int                        length;
        }

        public static MappedGraphicsBuffers Create()
        {
            return new MappedGraphicsBuffers { m_list = GCHandle.Alloc(new List<MappedBuffer>(), GCHandleType.Normal) };
        }

        public List<MappedBuffer> buffers => m_list.Target as List<MappedBuffer>;

        public void Dispose() => m_list.Free();

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            return inputDeps;
        }

        GCHandle m_list;
    }
}

