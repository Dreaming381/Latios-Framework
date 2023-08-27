using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    /// <summary>
    /// An aspect for working with renderable text
    /// </summary>
    public readonly partial struct TextRendererAspect : IAspect
    {
        readonly DynamicBuffer<CalliByte>     m_string;
        readonly RefRW<TextBaseConfiguration> m_baseConfig;

        /// <summary>
        /// The text string this text renderer should display
        /// </summary>
        public CalliString text => m_string;

        /// <summary>
        /// The size of the font, in font sizes
        /// </summary>
        public float fontSize
        {
            get => m_baseConfig.ValueRO.fontSize;
            set => m_baseConfig.ValueRW.fontSize = value;
        }

        /// <summary>
        /// The line width of the font, in world units.
        /// </summary>
        public float maxLineWidth
        {
            get => m_baseConfig.ValueRO.maxLineWidth;
            set => m_baseConfig.ValueRW.maxLineWidth = value;
        }

        // <summary>
        /// The vertex colors of the rendered text
        /// </summary>
        public UnityEngine.Color32 color
        {
            get => m_baseConfig.ValueRO.color;
            set => m_baseConfig.ValueRW.color = value;
        }

        /// <summary>
        /// The horizontal and vertical alignment modes of the text
        /// </summary>
        public AlignMode alignMode
        {
            get => m_baseConfig.ValueRO.alignMode;
            set => m_baseConfig.ValueRW.alignMode = value;
        }
    }
}

