using Unity.Audio;
using Unity.Burst;
using Unity.Collections;

namespace Latios.Myri.Driver
{
    [BurstCompile(CompileSynchronously = true)]
    public struct MyriBurstDriver : IAudioOutput
    {
        public DSPGraph Graph;
        int             m_ChannelCount;
        private bool    m_FirstMix;

        public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
        {
            m_ChannelCount = channelCount;
            m_FirstMix     = true;
        }

        public void BeginMix(int frameCount)
        {
            if (!m_FirstMix)
                return;
            m_FirstMix = false;
            Graph.OutputMixer.BeginMix(frameCount, DSPGraph.ExecutionMode.Synchronous);
        }

        public unsafe void EndMix(NativeArray<float> output, int frames)
        {
            // Interleaving happens in the output hook manager
            Graph.OutputMixer.ReadMix(output, frames, m_ChannelCount);
            // Todo: Would we get lower latency by always calling this in BeginMix since we operate synchronously?
            Graph.OutputMixer.BeginMix(frames, DSPGraph.ExecutionMode.Synchronous);
        }

        public void Dispose()
        {
            //UnityEngine.Debug.Log("Driver.Dispose");

            if (Graph.Valid)
            {
                //UnityEngine.Debug.Log("Disposing graph");
                Graph.Dispose();
            }
        }
    }
}

