using System;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Latios.Myri.Driver
{
    internal class MyriManagedDriver : MonoBehaviour
    {
        private unsafe void OnAudioFilterRead(float[] data, int channels)
        {
            var           span       = data.AsSpan();
            int           spanLength = span.Length;
            fixed (float* dst        = span)
            {
                var graphs = DriverManager.GetLockableManagedGraphs();
                lock (graphs)
                {
                    for (int i = 0; i < graphs.Count; i++)
                    {
                        var graph = graphs[i];
                        if (!graph.Valid)
                            continue;
                        MyriManagedDriverUpdater.Update(&graph, dst, spanLength, channels);
                    }
                }
            }
        }
    }

    [BurstCompile]
    static unsafe class MyriManagedDriverUpdater
    {
        [BurstCompile]
        public static void Update(DSPGraph* graph, float* dstBufferAdditive, int managedFloatCount, int channelCount)
        {
            var bufferA         = stackalloc float[managedFloatCount];
            var array           = CollectionHelper.ConvertExistingDataToNativeArray<float>(bufferA, managedFloatCount, Allocator.None, true);
            int samplesPerFrame = managedFloatCount / channelCount;
            graph->BeginMix(samplesPerFrame, DSPGraph.ExecutionMode.Synchronous);
            graph->ReadMix(array, samplesPerFrame, channelCount);

            // Interleave
            for (int i = 0; i < samplesPerFrame; i++)
            {
                for (int c = 0; c < channelCount; c++)
                {
                    var outputIndex                 = i * channelCount + c;
                    var inputIndex                  = c * samplesPerFrame + i;
                    dstBufferAdditive[outputIndex] += array[inputIndex];
                }
            }
        }
    }
}

