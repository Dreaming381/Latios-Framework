using System;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

//This is a modified version of StateVariableFilter from the DSPGraph samples
namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    public struct StateVariableFilterNode : IAudioKernel<StateVariableFilterNode.Parameters, StateVariableFilterNode.Providers>
    {
        public enum FilterType
        {
            Lowpass,
            Highpass,
            Bandpass,
            Bell,
            Notch,
            Lowshelf,
            Highshelf
        }

        public static DSPNode Create(DSPCommandBlock block, FilterType type, float cutoff, float q, float gainInDbs, int channelsPerPort)
        {
            var node = block.CreateDSPNode<Parameters, Providers, StateVariableFilterNode>();
            block.AddInletPort(node, channelsPerPort);
            block.AddOutletPort(node, channelsPerPort);
            block.SetFloat<Parameters, Providers, StateVariableFilterNode>(node, Parameters.FilterType, (float)type);
            block.SetFloat<Parameters, Providers, StateVariableFilterNode>(node, Parameters.Cutoff,     cutoff);
            block.SetFloat<Parameters, Providers, StateVariableFilterNode>(node, Parameters.Q,          q);
            block.SetFloat<Parameters, Providers, StateVariableFilterNode>(node, Parameters.GainInDBs,  gainInDbs);

            return node;
        }

        struct Coefficients
        {
            public float A, g, k, a1, a2, a3, m0, m1, m2;
        }

        static Coefficients DesignBell(float fc, float quality, float linearGain)
        {
            var A                      = linearGain;
            var g                      = math.tan(math.PI * fc);
            var k                      = 1 / (quality * A);
            var a1                     = 1 / (1 + g * (g + k));
            var a2                     = g * a1;
            var a3                     = g * a2;
            var m0                     = 1;
            var m1                     = k * (A * A - 1);
            var m2                     = 0;
            return new Coefficients {A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2};
        }

        static Coefficients DesignLowpass(float normalizedFrequency, float Q, float linearGain)
        {
            var A                      = linearGain;
            var g                      = math.tan(math.PI * normalizedFrequency);
            var k                      = 1 / Q;
            var a1                     = 1 / (1 + g * (g + k));
            var a2                     = g * a1;
            var a3                     = g * a2;
            var m0                     = 0;
            var m1                     = 0;
            var m2                     = 1;
            return new Coefficients {A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2};
        }

        static Coefficients DesignBandpass(float normalizedFrequency, float Q, float linearGain)
        {
            var coefficients = Design(FilterType.Lowpass, normalizedFrequency, Q, linearGain);
            coefficients.m1  = 1;
            coefficients.m2  = 0;
            return coefficients;
        }

        static Coefficients DesignHighpass(float normalizedFrequency, float Q, float linearGain)
        {
            var coefficients = Design(FilterType.Lowpass, normalizedFrequency, Q, linearGain);
            coefficients.m0  = 1;
            coefficients.m1  = -coefficients.k;
            coefficients.m2  = -1;
            return coefficients;
        }

        static Coefficients DesignNotch(float normalizedFrequency, float Q, float linearGain)
        {
            var coefficients = DesignLowpass(normalizedFrequency, Q, linearGain);
            coefficients.m0  = 1;
            coefficients.m1  = -coefficients.k;
            coefficients.m2  = 0;
            return coefficients;
        }

        static Coefficients DesignLowshelf(float normalizedFrequency, float Q, float linearGain)
        {
            var A                      = linearGain;
            var g                      = math.tan(math.PI * normalizedFrequency) / math.sqrt(A);
            var k                      = 1 / Q;
            var a1                     = 1 / (1 + g * (g + k));
            var a2                     = g * a1;
            var a3                     = g * a2;
            var m0                     = 1;
            var m1                     = k * (A - 1);
            var m2                     = A * A - 1;
            return new Coefficients {A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2};
        }

        static Coefficients DesignHighshelf(float normalizedFrequency, float Q, float linearGain)
        {
            var A                      = linearGain;
            var g                      = math.tan(math.PI * normalizedFrequency) / math.sqrt(A);
            var k                      = 1 / Q;
            var a1                     = 1 / (1 + g * (g + k));
            var a2                     = g * a1;
            var a3                     = g * a2;
            var m0                     = A * A;
            var m1                     = k * (1 - A) * A;
            var m2                     = 1 - A * A;
            return new Coefficients {A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2};
        }

        static Coefficients Design(FilterType type, float normalizedFrequency, float Q, float linearGain)
        {
            switch (type)
            {
                case FilterType.Lowpass: return DesignLowpass(normalizedFrequency, Q, linearGain);
                case FilterType.Highpass: return DesignHighpass(normalizedFrequency, Q, linearGain);
                case FilterType.Bandpass: return DesignBandpass(normalizedFrequency, Q, linearGain);
                case FilterType.Bell: return DesignBell(normalizedFrequency, Q, linearGain);
                case FilterType.Notch: return DesignNotch(normalizedFrequency, Q, linearGain);
                case FilterType.Lowshelf: return DesignLowshelf(normalizedFrequency, Q, linearGain);
                case FilterType.Highshelf: return DesignHighshelf(normalizedFrequency, Q, linearGain);
                default:
                    throw new ArgumentException("Unknown filter type", nameof(type));
            }
        }

        static Coefficients Design(FilterType filterType, float cutoff, float Q, float gainInDBs, float sampleRate)
        {
            var linearGain = math.pow(10, gainInDBs / 20);
            switch (filterType)
            {
                case FilterType.Lowpass:
                    return DesignLowpass(cutoff / sampleRate, Q, linearGain);
                case FilterType.Highpass:
                    return DesignHighpass(cutoff / sampleRate, Q, linearGain);
                case FilterType.Bandpass:
                    return DesignBandpass(cutoff / sampleRate, Q, linearGain);
                case FilterType.Bell:
                    return DesignBell(cutoff / sampleRate, Q, linearGain);
                case FilterType.Notch:
                    return DesignNotch(cutoff / sampleRate, Q, linearGain);
                case FilterType.Lowshelf:
                    return DesignLowshelf(cutoff / sampleRate, Q, linearGain);
                case FilterType.Highshelf:
                    return DesignHighshelf(cutoff / sampleRate, Q, linearGain);
                default:
                    throw new ArgumentException("Unknown filter type", nameof(filterType));
            }
        }

        public struct Channel
        {
            public float z1, z2;
        }

        FixedList512Bytes<Channel> m_channels;

        public enum Parameters
        {
            [ParameterDefault((float)StateVariableFilterNode.FilterType.Lowpass)]
            [ParameterRange((float)StateVariableFilterNode.FilterType.Lowpass,
                            (float)StateVariableFilterNode.FilterType.Highshelf)]
            FilterType,
            [ParameterDefault(5000.0f)][ParameterRange(10.0f, 22000.0f)]
            Cutoff,
            [ParameterDefault(1.0f)][ParameterRange(1.0f, 100.0f)]
            Q,
            [ParameterDefault(0.0f)][ParameterRange(-80.0f, 0.0f)]
            GainInDBs
        }

        public enum Providers
        {
        }

        public void Initialize()
        {
        }

        public void Execute(ref ExecuteContext<Parameters, Providers> context)
        {
            var input        = context.Inputs.GetSampleBuffer(0);
            var output       = context.Outputs.GetSampleBuffer(0);
            var channelCount = output.Channels;
            var sampleFrames = output.Samples;

            while (m_channels.Length < channelCount)
            {
                m_channels.Add(default);
            }

            var parameters   = context.Parameters;
            var filterType   = (FilterType)parameters.GetFloat(Parameters.FilterType, 0);
            var cutoff       = parameters.GetFloat(Parameters.Cutoff, 0);
            var q            = parameters.GetFloat(Parameters.Q, 0);
            var gain         = parameters.GetFloat(Parameters.GainInDBs, 0);
            var coefficients = Design(filterType, cutoff, q, gain, context.SampleRate);

            for (var c = 0; c < channelCount; c++)
            {
                var inputBuffer  = input.GetBuffer(c);
                var outputBuffer = output.GetBuffer(c);

                var z1 = m_channels[c].z1;
                var z2 = m_channels[c].z2;

                for (var i = 0; i < sampleFrames; ++i)
                {
                    var x           = inputBuffer[i];
                    var v3          = x - z2;
                    var v1          = coefficients.a1 * z1 + coefficients.a2 * v3;
                    var v2          = z2 + coefficients.a2 * z1 + coefficients.a3 * v3;
                    z1              = 2 * v1 - z1;
                    z2              = 2 * v2 - z2;
                    outputBuffer[i] = coefficients.A * (coefficients.m0 * x + coefficients.m1 * v1 + coefficients.m2 * v2);
                }

                m_channels[c] = new Channel {z1 = z1, z2 = z2};
            }
        }

        public void Dispose()
        {
        }
    }
}

