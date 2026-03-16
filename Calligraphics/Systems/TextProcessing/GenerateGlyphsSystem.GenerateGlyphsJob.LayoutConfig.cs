using Latios.Calligraphics.RichText;
using UnityEngine;

namespace Latios.Calligraphics.Systems
{
    public partial struct GenerateGlyphsSystem
    {
        partial struct GenerateRenderGlyphsJob
        {
            struct LayoutConfig
            {
                //metrics
                public float                     m_fontScaleMultiplier;  // Used for handling of superscript and subscript.
                public float                     m_currentFontSize;
                public FixedStack512Bytes<float> m_sizeStack;

                public FontStyles m_fontStyles;

                public float                     fontWeight;
                public FixedStack512Bytes<float> fontWeightStack;

                public float                     fontWidth;
                public FixedStack512Bytes<float> fontWidthStack;

                public HorizontalAlignmentOptions                     m_lineJustification;
                public FixedStack512Bytes<HorizontalAlignmentOptions> m_lineJustificationStack;

                public float                     m_baselineOffset;
                public FixedStack512Bytes<float> m_baselineOffsetStack;

                public Color32                     m_htmlColor;
                public FixedStack512Bytes<Color32> m_htmlColorStack;
                public Color32                     m_underlineColor;
                public FixedStack512Bytes<Color32> m_underlineColorStack;
                public Color32                     m_strikethroughColor;
                public FixedStack512Bytes<Color32> m_strikethroughColorStack;

                public bool                                  useGradient;
                public TextColorGradient                     m_gradient;
                public FixedStack512Bytes<TextColorGradient> m_gradientStack;

                public float m_lineOffset;
                public float m_lineHeight;

                public float m_cSpacing;
                public float m_monoSpacing;
                public float m_xAdvance;

                public float                     m_tagLineIndent;
                public float                     m_tagIndent;
                public FixedStack512Bytes<float> m_indentStack;
                public bool                      m_tagNoParsing;

                public bool m_isNonBreakingSpace;

                public short m_fxRotationAngleCCW_degree;
                public float m_fxScale;

                public LayoutConfig(in TextBaseConfiguration textBaseConfiguration)
                {
                    m_fontScaleMultiplier = 1;

                    m_fontStyles = textBaseConfiguration.fontStyles;

                    fontWeight      = textBaseConfiguration.fontWeight.Value();
                    fontWeightStack = default;
                    fontWeightStack.Add(fontWeight);

                    fontWidth      = textBaseConfiguration.fontWidth.Value();
                    fontWidthStack = default;
                    fontWidthStack.Add(fontWidth);

                    m_currentFontSize = textBaseConfiguration.fontSize;
                    m_sizeStack       = default;
                    m_sizeStack.Add(m_currentFontSize);

                    m_lineJustification      = textBaseConfiguration.lineJustification;
                    m_lineJustificationStack = default;
                    m_lineJustificationStack.Add(m_lineJustification);

                    m_baselineOffset      = 0;
                    m_baselineOffsetStack = default;
                    m_baselineOffsetStack.Add(m_baselineOffset);

                    m_htmlColor          = textBaseConfiguration.color;
                    m_underlineColor     = Color.white;
                    m_strikethroughColor = Color.white;

                    useGradient      = false;
                    m_gradient       = default;
                    m_gradientStack  = default;
                    m_htmlColorStack = default;
                    m_htmlColorStack.Add(m_htmlColor);
                    m_underlineColorStack = default;
                    m_underlineColorStack.Add(m_htmlColor);
                    m_strikethroughColorStack = default;
                    m_strikethroughColorStack.Add(m_htmlColor);

                    m_lineOffset = 0;  // Amount of space between lines (font line spacing + m_linespacing).
                    m_lineHeight = float.MinValue;  //TMP_Math.FLOAT_UNSET -->is there a better way to do this?

                    m_cSpacing    = 0;  // Amount of space added between characters as a result of the use of the <cspace> tag.
                    m_monoSpacing = 0;
                    m_xAdvance    = 0;  // Used to track the position of each character.

                    m_tagLineIndent = 0;  // Used for indentation of text.
                    m_tagIndent     = 0;
                    m_indentStack   = default;
                    m_indentStack.Add(m_tagIndent);
                    m_tagNoParsing = false;

                    m_isNonBreakingSpace = false;

                    m_fxRotationAngleCCW_degree = 0;
                    m_fxScale                   = 1;
                }
                public void Reset(in TextBaseConfiguration textBaseConfiguration)
                {
                    m_fontScaleMultiplier = 1;

                    m_fontStyles = textBaseConfiguration.fontStyles;

                    fontWeight = textBaseConfiguration.fontWeight.Value();
                    fontWeightStack.Clear();
                    fontWeightStack.Add(fontWeight);

                    fontWidth = textBaseConfiguration.fontWidth.Value();
                    fontWidthStack.Clear();
                    fontWidthStack.Add(fontWidth);

                    m_currentFontSize = textBaseConfiguration.fontSize;
                    m_sizeStack.Clear();
                    m_sizeStack.Add(m_currentFontSize);

                    m_lineJustification = textBaseConfiguration.lineJustification;
                    m_lineJustificationStack.Clear();
                    m_lineJustificationStack.Add(m_lineJustification);

                    m_baselineOffset = 0;
                    m_baselineOffsetStack.Clear();
                    m_baselineOffsetStack.Add(0);

                    m_htmlColor          = textBaseConfiguration.color;
                    m_underlineColor     = Color.white;
                    m_strikethroughColor = Color.white;
                    useGradient          = false;
                    m_gradient           = default;
                    m_gradientStack      = default;

                    m_htmlColorStack.Clear();
                    m_htmlColorStack.Add(m_htmlColor);
                    m_underlineColorStack.Clear();
                    m_underlineColorStack.Add(m_htmlColor);
                    m_strikethroughColorStack.Clear();
                    m_strikethroughColorStack.Add(m_htmlColor);

                    m_lineOffset = 0;  // Amount of space between lines (font line spacing + m_linespacing).
                    m_lineHeight = float.MinValue;  //TMP_Math.FLOAT_UNSET -->is there a better way to do this?

                    m_cSpacing    = 0;  // Amount of space added between characters as a result of the use of the <cspace> tag.
                    m_monoSpacing = 0;
                    m_xAdvance    = 0;  // Used to track the position of each character.

                    m_tagLineIndent = 0;  // Used for indentation of text.
                    m_tagIndent     = 0;
                    m_indentStack.Clear();
                    m_indentStack.Add(m_tagIndent);
                    m_tagNoParsing = false;

                    m_isNonBreakingSpace = false;

                    m_fxRotationAngleCCW_degree = 0;
                    m_fxScale                   = 1;
                }
                public void Update(ref XMLTag tag, in TextBaseConfiguration textBaseConfiguration, ref TextColorGradientArray textColorGradients)
                {
                    switch (tag.tagType)
                    {
                        //case TagType.Subscript: //redundant to opentype feature set during shaping. only purpose is to simulate missing subscript glyphs, but unclear how to determine this
                        //    if (!tag.isClosing)
                        //        m_fontStyles |= FontStyles.Subscript;
                        //    else
                        //        m_fontStyles &= ~FontStyles.Subscript;
                        //    return;
                        //case TagType.Superscript: //redundant to opentype feature set during shaping. only purpose is to simulate missing subscript glyphs, but unclear how to determine this
                        //    if (!tag.isClosing)
                        //        m_fontStyles |= FontStyles.Superscript;
                        //    else
                        //        m_fontStyles &= ~FontStyles.Superscript;
                        //    return;
                        case TagType.Italic:
                            if (!tag.isClosing)
                                m_fontStyles |= FontStyles.Italic;
                            else
                                m_fontStyles &= ~FontStyles.Italic;
                            return;
                        case TagType.Bold:
                            if (!tag.isClosing)
                            {
                                fontWeight = FontWeight.Bold.Value();
                                fontWeightStack.Add(fontWeight);
                            }
                            else
                                fontWeight = fontWeightStack.RemoveExceptRoot();
                            return;
                        case TagType.FontWeight:
                            if (!tag.isClosing)
                            {
                                fontWeight = tag.value.NumericalValue;
                                fontWeightStack.Add(fontWeight);
                            }
                            else
                                fontWeight = fontWeightStack.RemoveExceptRoot();
                            return;
                        case TagType.FontWidth:
                            if (!tag.isClosing)
                            {
                                fontWidth = tag.value.NumericalValue;
                                fontWidthStack.Add(fontWidth);
                            }
                            else
                                fontWidth = fontWidthStack.RemoveExceptRoot();
                            return;
                        case TagType.NoBr:
                            if (!tag.isClosing)
                                m_isNonBreakingSpace = true;
                            else
                                m_isNonBreakingSpace = false;
                            return;
                        case TagType.Size:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_currentFontSize = tag.value.NumericalValue;
                                        m_sizeStack.Add(m_currentFontSize);
                                        return;
                                    case TagUnitType.FontUnits:
                                        m_currentFontSize = textBaseConfiguration.fontSize * tag.value.NumericalValue;
                                        m_sizeStack.Add(m_currentFontSize);
                                        return;
                                    case TagUnitType.Percentage:
                                        m_currentFontSize = textBaseConfiguration.fontSize * tag.value.NumericalValue / 100;
                                        m_sizeStack.Add(m_currentFontSize);
                                        return;
                                }
                            }
                            else
                            {
                                m_currentFontSize = m_sizeStack.RemoveExceptRoot();
                            }
                            return;
                        case TagType.Space:
                            switch (tag.value.unit)
                            {
                                case TagUnitType.Pixels:
                                    m_xAdvance += (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                    return;
                                case TagUnitType.FontUnits:
                                    m_xAdvance += (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                    return;
                                case TagUnitType.Percentage:
                                    // Not applicable
                                    return;
                            }
                            return;
                        case TagType.Alpha:
                            if (tag.value.valueLength != 3)
                                return;
                            m_htmlColor.a = ((Color32)tag.value.ColorValue).a;
                            return;
                        case TagType.Align:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.stringValue)
                                {
                                    case StringValue.left:  // <align=left>
                                        m_lineJustification = HorizontalAlignmentOptions.Left;
                                        break;
                                    case StringValue.right:  // <align=right>
                                        m_lineJustification = HorizontalAlignmentOptions.Right;
                                        break;
                                    case StringValue.center:  // <align=center>
                                        m_lineJustification = HorizontalAlignmentOptions.Center;
                                        break;
                                    case StringValue.justified:  // <align=justified>
                                        m_lineJustification = HorizontalAlignmentOptions.Justified;
                                        break;
                                        //case StringValue.flush:  // <align=flush>
                                        //    m_lineJustification = HorizontalAlignmentOptions.Flush;
                                        //    break;
                                }
                                m_lineJustificationStack.Add(m_lineJustification);
                                return;
                            }
                            else
                                m_lineJustification = m_lineJustificationStack.RemoveExceptRoot();
                            return;
                        case TagType.Gradient:
                            if (!tag.isClosing)
                            {
                                var gradientNameHash = tag.value.valueHash;
                                for (int i = 0, ii = textColorGradients.Length; i < ii; i++)
                                {
                                    var gradient = textColorGradients[i];
                                    if (gradient.nameHash == gradientNameHash)
                                    {
                                        m_gradient = gradient;
                                        m_gradientStack.Add(m_gradient);
                                        useGradient = true;
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                useGradient = false;
                                if (m_gradientStack.Length == 1)
                                {
                                    m_gradientStack.Pop();
                                    Debug.Log($"{m_htmlColor}");
                                }
                                else
                                    m_gradient = m_gradientStack.RemoveExceptRoot();
                            }
                            return;
                        case TagType.Color:
                            if (!tag.isClosing)
                            {
                                useGradient = false;
                                if (tag.value.type == TagValueType.ColorValue)
                                    m_htmlColor = tag.value.ColorValue;
                                else if (tag.value.type == TagValueType.StringValue)
                                {
                                    switch (tag.value.stringValue)
                                    {
                                        case StringValue.red:  // <color=red>
                                            m_htmlColor = Color.red;
                                            break;
                                        case StringValue.lightblue:  // <color=lightblue>
                                            m_htmlColor = new Color32(173, 216, 230, 255);
                                            break;
                                        case StringValue.blue:  // <color=blue>
                                            m_htmlColor = Color.blue;
                                            break;
                                        case StringValue.grey:  // <color=grey>
                                            m_htmlColor = new Color32(128, 128, 128, 255);
                                            break;
                                        case StringValue.black:  // <color=black>
                                            m_htmlColor = Color.black;
                                            break;
                                        case StringValue.green:  // <color=green>
                                            m_htmlColor = Color.green;
                                            break;
                                        case StringValue.white:  // <color=white>
                                            m_htmlColor = Color.white;
                                            break;
                                        case StringValue.orange:  // <color=orange>
                                            m_htmlColor = new Color32(255, 128, 0, 255);
                                            break;
                                        case StringValue.purple:  // <color=purple>
                                            m_htmlColor = new Color32(160, 32, 240, 255);
                                            break;
                                        case StringValue.yellow:  // <color=yellow>
                                            m_htmlColor = Color.yellow;
                                            break;
                                    }
                                }
                                m_htmlColorStack.Add(m_htmlColor);
                                return;
                            }
                            else
                            {
                                m_htmlColor = m_htmlColorStack.RemoveExceptRoot();
                                return;
                            }
                        case TagType.CSpace:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_cSpacing = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        return;
                                    case TagUnitType.FontUnits:
                                        m_cSpacing = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        return;
                                    case TagUnitType.Percentage:
                                        return;
                                }
                            }
                            else
                                m_cSpacing = 0;
                            return;
                        case TagType.Mspace:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_monoSpacing = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        return;
                                    case TagUnitType.FontUnits:
                                        m_monoSpacing = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        return;
                                    case TagUnitType.Percentage:
                                        return;
                                }
                            }
                            else
                                m_monoSpacing = 0;
                            return;
                        case TagType.Indent:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_tagIndent = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.FontUnits:
                                        m_tagIndent = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.Percentage:
                                        //m_tagIndent = m_marginWidth * tag.value.NumericalValue / 100;
                                        break;
                                }
                                m_indentStack.Add(m_tagIndent);
                                m_xAdvance = m_tagIndent;
                            }
                            else
                                m_tagIndent = m_indentStack.RemoveExceptRoot();
                            return;
                        case TagType.LineIndent:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_tagLineIndent = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.FontUnits:
                                        m_tagLineIndent = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.Percentage:
                                        //m_tagIndent = m_marginWidth * tag.value.NumericalValue / 100;
                                        break;
                                }
                                m_xAdvance += m_tagLineIndent;
                            }
                            else
                                m_tagLineIndent = 0;
                            return;
                        case TagType.LineHeight:
                            if (!tag.isClosing)
                            {
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_lineHeight = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.FontUnits:
                                        m_lineHeight = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.Percentage:
                                        //fontScale = (richtextAdjustments.m_currentFontSize / m_currentFontAsset.faceInfo.pointSize * m_currentFontAsset.faceInfo.scale * (richtextAdjustments.m_isOrthographic ? 1 : 0.1f));
                                        //richtextAdjustments.m_lineHeight = m_fontAsset.faceInfo.lineHeight * value / 100 * fontScale;
                                        break;
                                }
                            }
                            else
                                m_lineHeight = float.MinValue; //TMP_Math.FLOAT_UNSET -->is there a better way to do this?
                            return;
                        case TagType.Rotate:
                            if (!tag.isClosing)
                            {
                                m_fxRotationAngleCCW_degree = (short)-tag.value.NumericalValue;
                            }
                            else
                                m_fxRotationAngleCCW_degree = 0;
                            return;
                        case TagType.VOffset:
                            if (!tag.isClosing)
                            {
                                var tmp = m_baselineOffset;
                                switch (tag.value.unit)
                                {
                                    case TagUnitType.Pixels:
                                        m_baselineOffset = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.FontUnits:
                                        m_baselineOffset = (textBaseConfiguration.isOrthographic ? 1 : 0.1f) * m_currentFontSize * tag.value.NumericalValue;
                                        break;
                                    case TagUnitType.Percentage:
                                        //m_tagIndent = m_marginWidth * tag.value.NumericalValue / 100;
                                        break;
                                }
                                m_baselineOffsetStack.Add(m_baselineOffset);
                                //Debug.Log($"Set from {tmp} to {m_baselineOffset}");
                            }
                            else
                            {
                                var tmp          = m_baselineOffset;
                                m_baselineOffset = m_baselineOffsetStack.RemoveExceptRoot();
                                //Debug.Log($"revert from {tmp} to {m_baselineOffset}");
                            }

                            return;
                    }
                }
            }
        }
    }
}

