using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    public readonly partial struct TextRendererAspect : IAspect
    {
        readonly DynamicBuffer<CalliByte>     m_string;
        readonly RefRW<TextBaseConfiguration> m_baseConfig;

        public CalliString text => m_string;

        public float fontSize
        {
            get => m_baseConfig.ValueRO.fontSize;
            set => m_baseConfig.ValueRW.fontSize = value;
        }

        public UnityEngine.Color32 color
        {
            get => m_baseConfig.ValueRO.color;
            set => m_baseConfig.ValueRW.color = value;
        }
    }
}

