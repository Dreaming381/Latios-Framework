using Unity.Audio;
using Unity.Burst;
using Unity.Collections;

namespace Latios.Myri.Driver
{
    [BurstCompile(CompileSynchronously = true)]
    public struct MyriBurstDriver : IAudioOutput
    {
        public DSPGraph graph;
        int             m_channelCount;

        public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
        {
            m_channelCount = channelCount;
        }

        public void BeginMix(int frameCount)
        {
            graph.OutputMixer.BeginMix(frameCount, DSPGraph.ExecutionMode.Synchronous);
        }

        public unsafe void EndMix(NativeArray<float> output, int frames)
        {
            // Interleaving happens in the output hook manager
            graph.OutputMixer.ReadMix(output, frames, m_channelCount);
        }

        public void Dispose()
        {
            //UnityEngine.Debug.Log("Driver.Dispose");

            if (graph.Valid)
            {
                //UnityEngine.Debug.Log("Disposing graph");
                graph.Dispose();
            }
        }
    }
}

