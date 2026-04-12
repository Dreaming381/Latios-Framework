using System.IO;
using Latios.Calligraphics.RichText;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        [BurstCompile]
        internal partial struct ExtractTagsJob : IJobChunk
        {
            [NativeDisableParallelForRestriction] public NativeStream.Writer xmlTagStream;
            [ReadOnly] public NativeArray<int>                               firstEntityIndexInChunk;
            [ReadOnly] public BufferTypeHandle<CalliByte>                    calliByteHandle;
            [ReadOnly] public ComponentTypeHandle<TextBaseConfiguration>     textBaseConfigurationHandle;

            public uint lastSystemVersion;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref calliByteHandle, lastSystemVersion) && !chunk.DidChange(ref textBaseConfigurationHandle, lastSystemVersion))
                    return;

                var firstEntityIndex = firstEntityIndexInChunk[unfilteredChunkIndex];
                //Debug.Log("Extract text segments job");
                var calliBytesBuffers = chunk.GetBufferAccessor(ref calliByteHandle);

                var m_htmlTag = new FixedString128Bytes();
                for (int indexInChunk = 0; indexInChunk < chunk.Count; indexInChunk++)
                {
                    int entityIndex = firstEntityIndex + indexInChunk;
                    xmlTagStream.BeginForEachIndex(entityIndex);

                    var calliBytesBuffer = calliBytesBuffers[indexInChunk];

                    var calliString               = new CalliString(calliBytesBuffer);
                    var rawCharacters             = calliString.GetEnumerator();
                    var previousRuneStartPosition = 0;
                    while (rawCharacters.MoveNext())
                    {
                        var currentRune = rawCharacters.Current;
                        if (currentRune == '<')  // '<'
                        {
                            if (RichTextParser.GetTag(in calliString, ref rawCharacters, previousRuneStartPosition, ref xmlTagStream, ref m_htmlTag))
                            {
                                previousRuneStartPosition = rawCharacters.NextRuneByteIndex;
                                continue;
                            }
                            else
                                rawCharacters.GotoByteIndex(previousRuneStartPosition);
                        }
                        previousRuneStartPosition = rawCharacters.NextRuneByteIndex;
                    }
                    xmlTagStream.EndForEachIndex();
                }
            }
            public struct TextHelperStruct
            {
                public XMLTag tag;
                public int    position;
                public int    tagLength;
            }
            public static void WriteCalliBytesToFile(string path, NativeArray<byte> callibyte)
            {
                if (callibyte.Length == 0)
                    return;
                StreamWriter writer = new StreamWriter(path, false);
                for (int i = 0, end = callibyte.Length; i < end; i++)
                {
                    var c = callibyte[i];
                    writer.WriteLine($"{c}");
                }
                writer.WriteLine();
                writer.Close();
            }
            public static void WriteCalliStringToFile(string path, NativeArray<TextHelperStruct> callibyte)
            {
                if (callibyte.Length == 0)
                    return;
                StreamWriter writer = new StreamWriter(path, false);
                for (int i = 0, end = callibyte.Length; i < end; i++)
                {
                    var c = callibyte[i];
                    if (c.tag.value.type == TagValueType.StringValue)
                        writer.WriteLine($"{c.position} {c.tagLength} {c.tag.tagType} {c.tag.value.valueStart} {c.tag.value.valueLength}");
                    else
                        writer.WriteLine($"{c.position} {c.tagLength} {c.tag.tagType} ");
                }
                writer.WriteLine();
                writer.Close();
            }
        }
    }
}

