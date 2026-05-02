using System;
using Latios.AuxEcs;
using Latios.Myri.DSP;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public unsafe partial struct StereoOutputBuffers : IAuxDisposable, IVInterface
    {
        float*                           m_samples;
        int                              m_samplesPerChannel;
        AllocatorManager.AllocatorHandle m_allocator;

        public Span<float> left => new Span<float>(m_samples, m_samplesPerChannel);
        public Span<float> right => new Span<float>(m_samples + m_samplesPerChannel, m_samplesPerChannel);

        public void Clear()
        {
            var span = new Span<float>(m_samples, m_samplesPerChannel * 2);
            span.Clear();
        }

        public StereoOutputBuffers(int samplesPerChannel, AllocatorManager.AllocatorHandle allocator)
        {
            m_samples           = AllocatorManager.Allocate<float>(allocator, samplesPerChannel * 2);
            m_samplesPerChannel = samplesPerChannel;
            m_allocator         = allocator;
        }

        public void Dispose()
        {
            if (m_samples != null)
            {
                AllocatorManager.Free(m_allocator, m_samples, m_samplesPerChannel * 2);
                this = default;
            }
        }
    }

    public unsafe partial struct StereoOutputBrickwallLimiter : IVInterface, IAuxDisposable
    {
        public BrickwallLimiter brickwallLimiter;

        public void Dispose()
        {
            if (brickwallLimiter.isCreated)
                brickwallLimiter.Dispose();
            this = default;
        }
    }
}

