using Latios.Calligraphics.RichText;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore.Text;

// As a general rule, GlyphGeneration should never modify the ActiveTextConfiguration or TextConfigurationStack, only read them.
// They should be driven by the RichTextParser.

namespace Latios.Calligraphics
{
    internal struct TextGenerationStateCommands
    {
        public float xAdvanceChange;
        public bool  xAdvanceIsOverwrite;  // False is additive

        public void Reset()
        {
            xAdvanceChange      = 0f;
            xAdvanceIsOverwrite = false;
        }
    }

    internal struct ActiveTextConfiguration
    {
        public float      m_fontScaleMultiplier;  // Used for handling of superscript and subscript.
        public float      m_currentFontSize;
        public FontStyles m_fontStyleInternal;
        //public FontWeight                 m_fontWeightInternal;
        public int                        m_currentFontMaterialIndex;
        public HorizontalAlignmentOptions m_lineJustification;
        public float                      m_baselineOffset;
        public Color32                    m_htmlColor;
        //public Color32                    m_underlineColor;
        //public Color32                    m_strikethroughColor;
        public short m_italicAngle;
        public float m_lineOffset;
        //public float                      m_lineHeight;
        public float m_cSpacing;
        public float m_monoSpacing;
        //public float                      m_xAdvance;
        //public float                      m_tagLineIndent;
        //public float                      m_tagIndent;
        //public float                      m_marginWidth;
        //public float                      m_marginHeight;
        //public float                      m_marginLeft;
        //public float                      m_marginRight;
        //public float                      m_width;
        //public bool                       m_isNonBreakingSpace;
        public float  m_fxRotationAngleCCW;
        public float3 m_fxScale;

        //// The following are derived values
        //public float baseScale;
        //
        //public void CalculateDerived(in TextBaseConfiguration baseConfiguration, ref FontBlob font)
        //{
        //    baseScale = m_currentFontSize / font.pointSize * font.scale * (baseConfiguration.isOrthographic ? 1 : 0.1f);
        //}
    }

    internal struct TextConfigurationStack
    {
        // These top two are scratchpads for RichTextParser.
        public FixedString128Bytes                      m_htmlTag;
        public FixedList512Bytes<RichTextTagIdentifier> richTextTagIndentifiers;

        //metrics
        public float                     m_fontScaleMultiplier;  // Used for handling of superscript and subscript.
        public float                     m_currentFontSize;
        public FixedStack512Bytes<float> m_sizeStack;

        public FontStyles                     m_fontStyleInternal;
        public FontWeight                     m_fontWeightInternal;
        public FontStyleStack                 m_fontStyleStack;
        public FixedStack512Bytes<FontWeight> m_fontWeightStack;

        public int                     m_currentFontMaterialIndex;
        public FixedStack512Bytes<int> m_fontMaterialIndexStack;

        public HorizontalAlignmentOptions                     m_lineJustification;
        public FixedStack512Bytes<HorizontalAlignmentOptions> m_lineJustificationStack;

        public float                     m_baselineOffset;
        public FixedStack512Bytes<float> m_baselineOffsetStack;

        public Color32 m_htmlColor;
        public Color32 m_underlineColor;
        public Color32 m_strikethroughColor;

        public FixedStack512Bytes<Color32> m_colorStack;
        public FixedStack512Bytes<Color32> m_strikethroughColorStack;
        public FixedStack512Bytes<Color32> m_underlineColorStack;

        public short                     m_italicAngle;
        public FixedStack512Bytes<short> m_italicAngleStack;

        public float m_lineOffset;
        public float m_lineHeight;

        public float m_cSpacing;
        public float m_monoSpacing;

        public float                     m_tagLineIndent;
        public float                     m_tagIndent;
        public FixedStack512Bytes<float> m_indentStack;
        public bool                      m_tagNoParsing;

        public float m_marginWidth;
        public float m_marginHeight;
        public float m_marginLeft;
        public float m_marginRight;
        public float m_width;

        public bool m_isNonBreakingSpace;

        public bool m_isParsingText;

        public float  m_fxRotationAngleCCW;
        public float3 m_fxScale;

        public FixedStack512Bytes<HighlightState> m_highlightStateStack;

        public void Reset(TextBaseConfiguration textBaseConfiguration)
        {
            m_htmlTag.Clear();

            m_fontScaleMultiplier = 1;
            m_currentFontSize     = textBaseConfiguration.fontSize;
            m_sizeStack.Clear();
            m_sizeStack.Add(m_currentFontSize);

            m_fontStyleInternal  = textBaseConfiguration.fontStyle;
            m_fontWeightInternal = (m_fontStyleInternal & FontStyles.Bold) == FontStyles.Bold ? FontWeight.Bold : textBaseConfiguration.fontWeight;
            m_fontWeightStack.Clear();
            m_fontWeightStack.Add(m_fontWeightInternal);
            m_fontStyleStack.Clear();

            m_currentFontMaterialIndex = 0;
            m_fontMaterialIndexStack.Clear();
            m_fontMaterialIndexStack.Add(0);

            m_lineJustification = textBaseConfiguration.lineJustification;
            m_lineJustificationStack.Clear();
            m_lineJustificationStack.Add(m_lineJustification);

            m_baselineOffset = 0;
            m_baselineOffsetStack.Clear();
            m_baselineOffsetStack.Add(0);

            m_htmlColor          = textBaseConfiguration.color;
            m_underlineColor     = Color.white;
            m_strikethroughColor = Color.white;

            m_colorStack.Clear();
            m_colorStack.Add(m_htmlColor);
            m_underlineColorStack.Clear();
            m_underlineColorStack.Add(m_htmlColor);
            m_strikethroughColorStack.Clear();
            m_strikethroughColorStack.Add(m_htmlColor);

            m_italicAngle = 0;
            m_italicAngleStack.Clear();

            m_lineOffset = 0;  // Amount of space between lines (font line spacing + m_linespacing).
            m_lineHeight = float.MinValue;  //TMP_Math.FLOAT_UNSET -->is there a better way to do this?

            m_cSpacing    = 0;  // Amount of space added between characters as a result of the use of the <cspace> tag.
            m_monoSpacing = 0;

            m_tagLineIndent = 0;  // Used for indentation of text.
            m_tagIndent     = 0;
            m_indentStack.Clear();
            m_indentStack.Add(m_tagIndent);
            m_tagNoParsing = false;

            m_marginWidth  = 0;
            m_marginHeight = 0;
            m_marginLeft   = 0;
            m_marginRight  = 0;
            m_width        = -1;

            m_isNonBreakingSpace = false;

            m_isParsingText      = false;
            m_fxRotationAngleCCW = 0;
            m_fxScale            = 1;

            m_highlightStateStack.Clear();
        }

        public ActiveTextConfiguration GetActiveConfiguration()
        {
            return new ActiveTextConfiguration
            {
                m_baselineOffset           = m_baselineOffset,
                m_cSpacing                 = m_cSpacing,
                m_currentFontMaterialIndex = m_currentFontMaterialIndex,
                m_currentFontSize          = m_currentFontSize,
                m_fontScaleMultiplier      = m_fontScaleMultiplier,
                m_fontStyleInternal        = m_fontStyleInternal,
                //m_fontWeightInternal       = m_fontWeightInternal,
                m_fxRotationAngleCCW = m_fxRotationAngleCCW,
                m_fxScale            = m_fxScale,
                m_htmlColor          = m_htmlColor,
                //m_isNonBreakingSpace       = m_isNonBreakingSpace,
                m_italicAngle = m_italicAngle,
                //m_lineHeight               = m_lineHeight,
                m_lineJustification = m_lineJustification,
                m_lineOffset        = m_lineOffset,
                //m_marginHeight             = m_marginHeight,
                //m_marginLeft               = m_marginLeft,
                //m_marginRight              = m_marginRight,
                //m_marginWidth              = m_marginWidth,
                m_monoSpacing = m_monoSpacing,
                //m_strikethroughColor       = m_strikethroughColor,
                //m_underlineColor           = m_underlineColor,
                //m_width                    = m_width,
                //m_xAdvance                 = m_xAdvance,
                //m_tagIndent                 = m_tagIndent,
                //m_tagLineIndent             = m_tagLineIndent,
            };
        }
    }
}

