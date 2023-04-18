using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Media.Utilities;

namespace Latios.Myri
{
    //This is just DefaultDSPGraphDriver from DspGraph except with the scheduling mode changed to be on-thread.
    [BurstCompile(CompileSynchronously = true)]
    public struct LatiosDSPGraphDriver : IAudioOutput
    {
        public DSPGraph Graph;
        int             m_ChannelCount;
        [NativeDisableContainerSafetyRestriction]
        NativeArray<float> m_DeinterleavedBuffer;
        private bool       m_FirstMix;

        public void Initialize(int channelCount, SoundFormat format, int sampleRate, long dspBufferSize)
        {
            m_ChannelCount        = channelCount;
            m_DeinterleavedBuffer = new NativeArray<float>((int)(dspBufferSize * channelCount), Allocator.AudioKernel, NativeArrayOptions.UninitializedMemory);
            m_FirstMix            = true;
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
#if UNITY_2020_2_OR_NEWER
            // Interleaving happens in the output hook manager
            Graph.OutputMixer.ReadMix(output,                frames, m_ChannelCount);
#else
            Graph.OutputMixer.ReadMix(m_DeinterleavedBuffer, frames, m_ChannelCount);
            Utility.InterleaveAudioStream((float*)m_DeinterleavedBuffer.GetUnsafeReadOnlyPtr(), (float*)output.GetUnsafePtr(), frames, m_ChannelCount);
#endif
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

            // TODO: This currently throws, needs yet another fix in unity
            if (m_DeinterleavedBuffer.IsCreated)
            {
                m_DeinterleavedBuffer.Dispose();
            }
        }
    }
}

