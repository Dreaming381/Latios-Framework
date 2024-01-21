using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make public on release
    internal struct StateVariableFilter
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

        #region Instance API
        public StateVariableFilter(FilterType filterType, float cutoff, float Q, float gainInDBs, int sampleRate)
        {
            m_leftChannel  = default;
            m_rightChannel = default;
            m_coefficients = default;
            m_sampleRate   = sampleRate;

            SetFilter(filterType, cutoff, Q, gainInDBs);
        }

        public void SetFilter(FilterType filterType, float cutoff, float Q, float gainInDBs)
        {
            m_coefficients = Design(filterType, cutoff, Q, gainInDBs, m_sampleRate);
        }

        public float ProcessLeftSample(float leftSample)
        {
            var v3           = leftSample - m_leftChannel.z2;
            var v1           = m_coefficients.a1 * m_leftChannel.z1 + m_coefficients.a2 * v3;
            var v2           = m_leftChannel.z2 + m_coefficients.a2 * m_leftChannel.z1 + m_coefficients.a3 * v3;
            m_leftChannel.z1 = 2 * v1 - m_leftChannel.z1;
            m_leftChannel.z2 = 2 * v2 - m_leftChannel.z2;
            return m_coefficients.A * (m_coefficients.m0 * leftSample + m_coefficients.m1 * v1 + m_coefficients.m2 * v2);
        }

        public float ProcessRightSample(float rightSample)
        {
            var v3            = rightSample - m_rightChannel.z2;
            var v1            = m_coefficients.a1 * m_rightChannel.z1 + m_coefficients.a2 * v3;
            var v2            = m_rightChannel.z2 + m_coefficients.a2 * m_rightChannel.z1 + m_coefficients.a3 * v3;
            m_rightChannel.z1 = 2 * v1 - m_rightChannel.z1;
            m_rightChannel.z2 = 2 * v2 - m_rightChannel.z2;
            return m_coefficients.A * (m_coefficients.m0 * rightSample + m_coefficients.m1 * v1 + m_coefficients.m2 * v2);
        }

        public void ProcessFrame(ref SampleFrame frame, bool outputConnected)
        {
            if (!outputConnected || !frame.connected)
            {
                frame.connected = false;

                for (int i = 0; i < frame.length; i++)
                {
                    ProcessLeftSample(0f);
                    ProcessRightSample(0f);
                }
            }
            else
            {
                var left  = frame.left;
                var right = frame.right;

                for (int i = 0; i < frame.length; i++)
                {
                    left[i]  = ProcessLeftSample(left[i]);
                    right[i] = ProcessRightSample(right[i]);
                }
            }
        }
        #endregion

        #region Static API
        public struct Coefficients
        {
            internal float A, g, k, a1, a2, a3, m0, m1, m2;
        }

        public struct Channel
        {
            internal float z1;
            internal float z2;

            public void Reset() => this = default;
        }

        public static Coefficients CreateFilterCoefficients(FilterType filterType, float cutoff, float Q, float gainInDBs, int sampleRate)
        {
            return Design(filterType, cutoff, Q, gainInDBs, sampleRate);
        }

        public static float ProcessSample(ref Channel channel, in Coefficients coefficients, float sample)
        {
            var v3     = sample - channel.z2;
            var v1     = coefficients.a1 * channel.z1 + coefficients.a2 * v3;
            var v2     = channel.z2 + coefficients.a2 * channel.z1 + coefficients.a3 * v3;
            channel.z1 = 2 * v1 - channel.z1;
            channel.z2 = 2 * v2 - channel.z2;
            return coefficients.A * (coefficients.m0 * sample + coefficients.m1 * v1 + coefficients.m2 * v2);
        }
        #endregion

        Channel      m_leftChannel;
        Channel      m_rightChannel;
        Coefficients m_coefficients;
        float        m_sampleRate;

        static Coefficients DesignBell(float fc, float quality, float linearGain)
        {
            var A                       = linearGain;
            var g                       = math.tan(math.PI * fc);
            var k                       = 1 / (quality * A);
            var a1                      = 1 / (1 + g * (g + k));
            var a2                      = g * a1;
            var a3                      = g * a2;
            var m0                      = 1;
            var m1                      = k * (A * A - 1);
            var m2                      = 0;
            return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
        }

        static Coefficients DesignLowpass(float normalizedFrequency, float Q, float linearGain)
        {
            var A                       = linearGain;
            var g                       = math.tan(math.PI * normalizedFrequency);
            var k                       = 1 / Q;
            var a1                      = 1 / (1 + g * (g + k));
            var a2                      = g * a1;
            var a3                      = g * a2;
            var m0                      = 0;
            var m1                      = 0;
            var m2                      = 1;
            return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
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
            var A                       = linearGain;
            var g                       = math.tan(math.PI * normalizedFrequency) / math.sqrt(A);
            var k                       = 1 / Q;
            var a1                      = 1 / (1 + g * (g + k));
            var a2                      = g * a1;
            var a3                      = g * a2;
            var m0                      = 1;
            var m1                      = k * (A - 1);
            var m2                      = A * A - 1;
            return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
        }

        static Coefficients DesignHighshelf(float normalizedFrequency, float Q, float linearGain)
        {
            var A                       = linearGain;
            var g                       = math.tan(math.PI * normalizedFrequency) / math.sqrt(A);
            var k                       = 1 / Q;
            var a1                      = 1 / (1 + g * (g + k));
            var a2                      = g * a1;
            var a3                      = g * a2;
            var m0                      = A * A;
            var m1                      = k * (1 - A) * A;
            var m2                      = 1 - A * A;
            return new Coefficients { A = A, g = g, k = k, a1 = a1, a2 = a2, a3 = a3, m0 = m0, m1 = m1, m2 = m2 };
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
                    throw new System.ArgumentException("Unknown filter type", nameof(type));
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
                    throw new System.ArgumentException("Unknown filter type", nameof(filterType));
            }
        }
    }
}

