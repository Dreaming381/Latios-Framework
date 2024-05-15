using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics.RichText
{
    internal static class RichTextParser
    {
        /// <summary>
        /// Function to identify and validate the rich tag. Returns the byte position of the > if the tag was valid.
        /// </summary>
        /// <param name="chars"></param>
        /// <param name="startIndex"></param>
        /// <param name="endIndex"></param>
        /// <returns></returns>
        internal static bool ValidateHtmlTag(
            in CalliString calliString,
            ref CalliString.Enumerator enumerator,
            ref FontMaterialSet fontMaterialSet,
            in TextBaseConfiguration baseConfiguration,
            ref TextConfiguration textConfiguration,
            ref FixedList512Bytes<RichTextTagIdentifier> richTextTagIndentifiers)  //this is just a cache to avoid allocation
        {
            richTextTagIndentifiers.Clear();
            int         tagCharCount       = 0;
            int         tagByteCount       = 0;
            int         startByteIndex     = enumerator.CurrentByteIndex;
            ParserState tagIndentifierFlag = ParserState.Zero;

            int tagIndentifierIndex = richTextTagIndentifiers.Length;
            richTextTagIndentifiers.Add(RichTextTagIdentifier.Empty);
            ref var      currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(tagIndentifierIndex);
            TagValueType tagValueType          = currentTagIndentifier.valueType = TagValueType.None;
            TagUnitType  tagUnitType           = currentTagIndentifier.unitType = TagUnitType.Pixels;

            bool isTagSet       = false;
            bool isValidHtmlTag = false;

            Unicode.Rune unicode                     = Unicode.BadRune;
            int          charCount                   = 0;
            while (enumerator.MoveNext() && (unicode = enumerator.Current) != Unicode.BadRune && unicode != '<')
            {
                if (unicode == '>')  // ASCII Code of End HTML tag '>'
                {
                    isValidHtmlTag = true;
                    break;
                }

                int byteCount  = unicode.LengthInUtf8Bytes();
                tagCharCount  += 1;
                tagByteCount  += byteCount;

                if (tagIndentifierFlag == ParserState.One)
                {
                    if (tagValueType == TagValueType.None)
                    {
                        // Check for tagIndentifier type
                        if (unicode == '+' || unicode == '-' || unicode == '.' || Unicode.Rune.IsDigit(unicode))
                        {
                            tagUnitType                            = TagUnitType.Pixels;
                            tagValueType                           = currentTagIndentifier.valueType = TagValueType.NumericalValue;
                            currentTagIndentifier.valueStartIndex  = enumerator.CurrentByteIndex - unicode.LengthInUtf8Bytes();
                            currentTagIndentifier.valueLength     += byteCount;
                        }
                        else if (unicode == '#')
                        {
                            tagUnitType                            = TagUnitType.Pixels;
                            tagValueType                           = currentTagIndentifier.valueType = TagValueType.ColorValue;
                            currentTagIndentifier.valueStartIndex  = enumerator.CurrentByteIndex - unicode.LengthInUtf8Bytes();
                            currentTagIndentifier.valueLength     += byteCount;
                        }
                        else if (unicode == '"')
                        {
                            tagUnitType                           = TagUnitType.Pixels;
                            tagValueType                          = currentTagIndentifier.valueType = TagValueType.StringValue;
                            currentTagIndentifier.valueStartIndex = enumerator.CurrentByteIndex;
                        }
                        else
                        {
                            tagUnitType                            = TagUnitType.Pixels;
                            tagValueType                           = currentTagIndentifier.valueType = TagValueType.StringValue;
                            currentTagIndentifier.valueStartIndex  = enumerator.CurrentByteIndex - unicode.LengthInUtf8Bytes();
                            currentTagIndentifier.valueHashCode    = (currentTagIndentifier.valueHashCode << 5) + currentTagIndentifier.valueHashCode ^ unicode.value;
                            currentTagIndentifier.valueLength     += byteCount;
                        }
                    }
                    else
                    {
                        if (tagValueType == TagValueType.NumericalValue)
                        {
                            // Check for termination of numerical value.
                            if (unicode == 'p' || unicode == 'e' || unicode == '%' || unicode == ' ')
                            {
                                tagIndentifierFlag = ParserState.Two;
                                tagValueType       = TagValueType.None;

                                switch (unicode.value)
                                {
                                    case 'e':
                                        currentTagIndentifier.unitType = tagUnitType = TagUnitType.FontUnits;
                                        break;
                                    case '%':
                                        currentTagIndentifier.unitType = tagUnitType = TagUnitType.Percentage;
                                        break;
                                    default:
                                        currentTagIndentifier.unitType = tagUnitType = TagUnitType.Pixels;
                                        break;
                                }

                                tagIndentifierIndex += 1;
                                richTextTagIndentifiers.Add(RichTextTagIdentifier.Empty);
                                currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(tagIndentifierIndex);
                            }
                            else if (tagIndentifierFlag != ParserState.Two)
                            {
                                currentTagIndentifier.valueLength += byteCount;
                            }
                        }
                        else if (tagValueType == TagValueType.ColorValue)
                        {
                            if (unicode != ' ')
                            {
                                currentTagIndentifier.valueLength += byteCount;
                            }
                            else
                            {
                                tagIndentifierFlag   = ParserState.Two;
                                tagValueType         = TagValueType.None;
                                tagUnitType          = TagUnitType.Pixels;
                                tagIndentifierIndex += 1;
                                richTextTagIndentifiers.Add(RichTextTagIdentifier.Empty);
                                currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(tagIndentifierIndex);
                            }
                        }
                        else if (tagValueType == TagValueType.StringValue)
                        {
                            // Compute HashCode value for the named tag.
                            if (unicode != '"')
                            {
                                currentTagIndentifier.valueHashCode  = (currentTagIndentifier.valueHashCode << 5) + currentTagIndentifier.valueHashCode ^ unicode.value;
                                currentTagIndentifier.valueLength   += byteCount;
                            }
                            else
                            {
                                tagIndentifierFlag   = ParserState.Two;
                                tagValueType         = TagValueType.None;
                                tagUnitType          = TagUnitType.Pixels;
                                tagIndentifierIndex += 1;
                                richTextTagIndentifiers.Add(RichTextTagIdentifier.Empty);
                                currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(tagIndentifierIndex);
                            }
                        }
                    }
                }

                if (unicode == '=') // '='
                    tagIndentifierFlag = ParserState.One;

                // Compute HashCode for the name of the tagIndentifier
                if (tagIndentifierFlag == ParserState.Zero && unicode == ' ')
                {
                    if (isTagSet)
                        return false;

                    isTagSet           = true;
                    tagIndentifierFlag = ParserState.Two;

                    tagValueType         = TagValueType.None;
                    tagUnitType          = TagUnitType.Pixels;
                    tagIndentifierIndex += 1;
                    richTextTagIndentifiers.Add(RichTextTagIdentifier.Empty);
                    currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(tagIndentifierIndex);
                }

                if (tagIndentifierFlag == ParserState.Zero)
                    currentTagIndentifier.nameHashCode = (currentTagIndentifier.nameHashCode << 3) - currentTagIndentifier.nameHashCode + unicode.value;

                if (tagIndentifierFlag == ParserState.Two && unicode == ' ')
                    tagIndentifierFlag = ParserState.Zero;
            }

            if (!isValidHtmlTag)
            {
                return false;
            }

            ref var firstTagIndentifier = ref richTextTagIndentifiers.ElementAt(0);
            calliString.GetSubString(ref textConfiguration.m_htmlTag, startByteIndex, tagByteCount);

            //#region Rich Text Tag Processing
            //#if !RICH_TEXT_ENABLED
            // Special handling of the no parsing tag </noparse> </NOPARSE> tag
            if (textConfiguration.tag_NoParsing && (firstTagIndentifier.nameHashCode != 53822163 && firstTagIndentifier.nameHashCode != 49429939))
                return false;
            else if (firstTagIndentifier.nameHashCode == 53822163 || firstTagIndentifier.nameHashCode == 49429939)
            {
                textConfiguration.tag_NoParsing = false;
                return true;
            }

            // Color tag just starting with hex (no assignment)
            if (textConfiguration.m_htmlTag[0] == 35)
            {
                // tagCharCount == 4: Color <#FFF> 3 Hex values (short form)
                // tagCharCount == 5: Color <#FFF7> 4 Hex values with alpha (short form)
                // tagCharCount == 7: Color <#FF00FF>
                // tagCharCount == 9: Color <#FF00FF00> with alpha
                if (tagCharCount == 4 || tagCharCount == 5 || tagCharCount == 7 || tagCharCount == 9)
                {
                    textConfiguration.m_htmlColor = HexCharsToColor(textConfiguration.m_htmlTag, tagCharCount);
                    textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                    return true;
                }
            }
            else
            {
                float value = 0;
                float fontScale;

                ref var currentFont = ref fontMaterialSet[textConfiguration.m_currentFontMaterialIndex];

                switch (firstTagIndentifier.nameHashCode)
                {
                    case 98:  // <b>
                    case 66:  // <B>
                        textConfiguration.m_fontStyleInternal |= FontStyles.Bold;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.Bold);

                        textConfiguration.m_fontWeightInternal = FontWeight.Bold;
                        return true;
                    case 427:  // </b>
                    case 395:  // </B>
                        if ((baseConfiguration.fontStyle & FontStyles.Bold) != FontStyles.Bold)
                        {
                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Bold) == 0)
                            {
                                textConfiguration.m_fontStyleInternal  &= ~FontStyles.Bold;
                                textConfiguration.m_fontWeightInternal  = textConfiguration.m_fontWeightStack.Peek();
                            }
                        }
                        return true;
                    case 105:  // <i>
                    case 73:  // <I>
                        textConfiguration.m_fontStyleInternal |= FontStyles.Italic;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.Italic);

                        if (richTextTagIndentifiers.Length > 1 && (richTextTagIndentifiers[1].nameHashCode == 276531 || richTextTagIndentifiers[1].nameHashCode == 186899))
                        {
                            // Reject tag if value is invalid.
                            calliString.GetSubString(ref textConfiguration.m_htmlTag, richTextTagIndentifiers[1].valueStartIndex, richTextTagIndentifiers[1].valueLength);

                            if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                                return false;
                            textConfiguration.m_italicAngle = (short)value;

                            // Make sure angle is within valid range.
                            if (textConfiguration.m_italicAngle < -180 || textConfiguration.m_italicAngle > 180)
                                return false;
                        }
                        else
                            textConfiguration.m_italicAngle = currentFont.italicsStyleSlant;

                        textConfiguration.m_italicAngleStack.Add(textConfiguration.m_italicAngle);

                        return true;
                    case 434:  // </i>
                    case 402:  // </I>
                        if ((baseConfiguration.fontStyle & FontStyles.Italic) != FontStyles.Italic)
                        {
                            textConfiguration.m_italicAngle = textConfiguration.m_italicAngleStack.RemoveExceptRoot();

                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Italic) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Italic;
                        }
                        return true;
                    case 115:  // <s>
                    case 83:  // <S>
                        textConfiguration.m_fontStyleInternal |= FontStyles.Strikethrough;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.Strikethrough);

                        if (richTextTagIndentifiers.Length > 1 && (richTextTagIndentifiers[1].nameHashCode == 281955 || richTextTagIndentifiers[1].nameHashCode == 192323))
                        {
                            calliString.GetSubString(ref textConfiguration.m_htmlTag, richTextTagIndentifiers[1].valueStartIndex, richTextTagIndentifiers[1].valueLength);
                            charCount                                = richTextTagIndentifiers[1].valueLength - richTextTagIndentifiers[1].valueStartIndex;
                            textConfiguration.m_strikethroughColor   = HexCharsToColor(textConfiguration.m_htmlTag, charCount);
                            textConfiguration.m_strikethroughColor.a = textConfiguration.m_htmlColor.a <
                                                                       textConfiguration.m_strikethroughColor.a ? (byte)(textConfiguration.m_htmlColor.a) : (byte)(textConfiguration
                                                                                                                                                                   .
                                                                                                                                                                   m_strikethroughColor
                                                                                                                                                                   .a);
                        }
                        else
                            textConfiguration.m_strikethroughColor = textConfiguration.m_htmlColor;

                        textConfiguration.m_strikethroughColorStack.Add(textConfiguration.m_strikethroughColor);

                        return true;
                    case 444:  // </s>
                    case 412:  // </S>
                        if ((baseConfiguration.fontStyle & FontStyles.Strikethrough) != FontStyles.Strikethrough)
                        {
                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Strikethrough) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Strikethrough;
                        }

                        textConfiguration.m_strikethroughColor = textConfiguration.m_strikethroughColorStack.RemoveExceptRoot();
                        return true;
                    case 117:  // <u>
                    case 85:  // <U>
                        textConfiguration.m_fontStyleInternal |= FontStyles.Underline;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.Underline);
                        if (richTextTagIndentifiers.Length > 1 && (richTextTagIndentifiers[1].nameHashCode == 281955 || richTextTagIndentifiers[1].nameHashCode == 192323))
                        {
                            calliString.GetSubString(ref textConfiguration.m_htmlTag, richTextTagIndentifiers[1].valueStartIndex, richTextTagIndentifiers[1].valueLength);
                            charCount                            = richTextTagIndentifiers[1].valueLength - richTextTagIndentifiers[1].valueStartIndex;
                            textConfiguration.m_underlineColor   = HexCharsToColor(textConfiguration.m_htmlTag, charCount);
                            textConfiguration.m_underlineColor.a = textConfiguration.m_htmlColor.a <
                                                                   textConfiguration.m_underlineColor.a ? (byte)(textConfiguration.m_htmlColor.a) : (byte)(textConfiguration.
                                                                                                                                                           m_underlineColor.a);
                        }
                        else
                            textConfiguration.m_underlineColor = textConfiguration.m_htmlColor;

                        textConfiguration.m_underlineColorStack.Add(textConfiguration.m_underlineColor);

                        return true;
                    case 446:  // </u>
                    case 414:  // </U>
                        if ((baseConfiguration.fontStyle & FontStyles.Underline) != FontStyles.Underline)
                        {
                            textConfiguration.m_underlineColor = textConfiguration.m_underlineColorStack.RemoveExceptRoot();

                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Underline) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Underline;
                        }

                        textConfiguration.m_underlineColor = textConfiguration.m_underlineColorStack.RemoveExceptRoot();
                        return true;
                    case 43045:  // <mark=#FF00FF80>
                    case 30245:  // <MARK>
                        textConfiguration.m_fontStyleInternal |= FontStyles.Highlight;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.Highlight);

                        Color32     highlightColor   = new Color32(255, 255, 0, 64);
                        RectOffsets highlightPadding = RectOffsets.zero;

                        // Handle Mark Tag and potential tagIndentifiers
                        for (int i = 0; i < richTextTagIndentifiers.Length && richTextTagIndentifiers[i].nameHashCode != 0; i++)
                        {
                            int nameHashCode = richTextTagIndentifiers[i].nameHashCode;

                            switch (nameHashCode)
                            {
                                // Mark tag
                                case 43045:
                                case 30245:
                                    if (richTextTagIndentifiers[i].valueType == TagValueType.ColorValue)
                                    {
                                        //is this a bug in TMP Pro? -->should be richTextTagIndentifiers[i] and not firstTagIndentifier
                                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                                        charCount      = firstTagIndentifier.valueLength - firstTagIndentifier.valueStartIndex;
                                        highlightColor = HexCharsToColor(textConfiguration.m_htmlTag, charCount);
                                    }
                                    break;

                                // Color tagIndentifier
                                case 281955:
                                    calliString.GetSubString(ref textConfiguration.m_htmlTag, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength);
                                    charCount      = richTextTagIndentifiers[i].valueLength - richTextTagIndentifiers[i].valueStartIndex;
                                    highlightColor = HexCharsToColor(textConfiguration.m_htmlTag, charCount);
                                    break;

                                // Padding tagIndentifier
                                case 15087385:
                                    //int paramCount = GetTagIndentifierParameters(calliString, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength, ref m_tagIndentifierParameterValues);
                                    //if (paramCount != 4) return false;

                                    //highlightPadding = new Calli_Offset(m_tagIndentifierParameterValues[0], m_tagIndentifierParameterValues[1], m_tagIndentifierParameterValues[2], m_tagIndentifierParameterValues[3]);
                                    //highlightPadding *= baseConfiguration.fontSize * 0.01f * (richtextAdjustments.m_isOrthographic ? 1 : 0.1f);
                                    break;
                            }
                        }

                        highlightColor.a = textConfiguration.m_htmlColor.a < highlightColor.a ? (byte)(textConfiguration.m_htmlColor.a) : (byte)(highlightColor.a);

                        HighlightState state = new HighlightState(highlightColor, highlightPadding);
                        textConfiguration.m_highlightStateStack.Add(state);

                        return true;
                    case 155892:  // </mark>
                    case 143092:  // </MARK>
                        if ((baseConfiguration.fontStyle & FontStyles.Highlight) != FontStyles.Highlight)
                        {
                            textConfiguration.m_highlightStateStack.RemoveExceptRoot();

                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Highlight) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Highlight;
                        }
                        return true;
                    case 6552:  // <sub>
                    case 4728:  // <SUB>
                        textConfiguration.m_fontScaleMultiplier *= currentFont.subscriptSize > 0 ? currentFont.subscriptSize : 1;
                        textConfiguration.m_baselineOffsetStack.Add(textConfiguration.m_baselineOffset);
                        fontScale =
                            (textConfiguration.m_currentFontSize / currentFont.pointSize * currentFont.scale * (baseConfiguration.isOrthographic ? 1 : 0.1f));
                        textConfiguration.m_baselineOffset += currentFont.subscriptOffset * fontScale * textConfiguration.m_fontScaleMultiplier;

                        textConfiguration.m_fontStyleStack.Add(FontStyles.Subscript);
                        textConfiguration.m_fontStyleInternal |= FontStyles.Subscript;
                        return true;
                    case 22673:  // </sub>
                    case 20849:  // </SUB>
                        if ((textConfiguration.m_fontStyleInternal & FontStyles.Subscript) == FontStyles.Subscript)
                        {
                            if (textConfiguration.m_fontScaleMultiplier < 1)
                            {
                                textConfiguration.m_baselineOffset       = textConfiguration.m_baselineOffsetStack.RemoveExceptRoot();
                                textConfiguration.m_fontScaleMultiplier /= currentFont.subscriptSize > 0 ? currentFont.subscriptSize : 1;
                            }

                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Subscript) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Subscript;
                        }
                        return true;
                    case 6566:  // <sup>
                    case 4742:  // <SUP>
                        textConfiguration.m_fontScaleMultiplier *= currentFont.superscriptSize > 0 ? currentFont.superscriptSize : 1;
                        textConfiguration.m_baselineOffsetStack.Add(textConfiguration.m_baselineOffset);
                        fontScale =
                            (textConfiguration.m_currentFontSize / currentFont.pointSize * currentFont.scale * (baseConfiguration.isOrthographic ? 1 : 0.1f));
                        textConfiguration.m_baselineOffset += currentFont.superscriptOffset * fontScale * textConfiguration.m_fontScaleMultiplier;

                        textConfiguration.m_fontStyleStack.Add(FontStyles.Superscript);
                        textConfiguration.m_fontStyleInternal |= FontStyles.Superscript;
                        return true;
                    case 22687:  // </sup>
                    case 20863:  // </SUP>
                        if ((textConfiguration.m_fontStyleInternal & FontStyles.Superscript) == FontStyles.Superscript)
                        {
                            if (textConfiguration.m_fontScaleMultiplier < 1)
                            {
                                textConfiguration.m_baselineOffset       = textConfiguration.m_baselineOffsetStack.RemoveExceptRoot();
                                textConfiguration.m_fontScaleMultiplier /= currentFont.superscriptSize > 0 ? currentFont.superscriptSize : 1;
                            }

                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.Superscript) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.Superscript;
                        }
                        return true;
                    case -330774850:  // <font-weight>
                    case 2012149182:  // <FONT-WEIGHT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch ((int)value)
                        {
                            case 100:
                                textConfiguration.m_fontWeightInternal = FontWeight.Thin;
                                break;
                            case 200:
                                textConfiguration.m_fontWeightInternal = FontWeight.ExtraLight;
                                break;
                            case 300:
                                textConfiguration.m_fontWeightInternal = FontWeight.Light;
                                break;
                            case 400:
                                textConfiguration.m_fontWeightInternal = FontWeight.Regular;
                                break;
                            case 500:
                                textConfiguration.m_fontWeightInternal = FontWeight.Medium;
                                break;
                            case 600:
                                textConfiguration.m_fontWeightInternal = FontWeight.SemiBold;
                                break;
                            case 700:
                                textConfiguration.m_fontWeightInternal = FontWeight.Bold;
                                break;
                            case 800:
                                textConfiguration.m_fontWeightInternal = FontWeight.Heavy;
                                break;
                            case 900:
                                textConfiguration.m_fontWeightInternal = FontWeight.Black;
                                break;
                        }

                        textConfiguration.m_fontWeightStack.Add(textConfiguration.m_fontWeightInternal);

                        return true;
                    case -1885698441:  // </font-weight>
                    case 457225591:  // </FONT-WEIGHT>
                        textConfiguration.m_fontWeightStack.RemoveExceptRoot();

                        if (textConfiguration.m_fontStyleInternal == FontStyles.Bold)
                            textConfiguration.m_fontWeightInternal = FontWeight.Bold;
                        else
                            textConfiguration.m_fontWeightInternal = textConfiguration.m_fontWeightStack.Peek();

                        return true;
                    case 6380:  // <pos=000.00px> <pos=0em> <pos=50%>
                    case 4556:  // <POS>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_xAdvance = value * (baseConfiguration.isOrthographic ? 1.0f : 0.1f);
                                return true;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_xAdvance = value * textConfiguration.m_currentFontSize * (baseConfiguration.isOrthographic ? 1.0f : 0.1f);
                                return true;
                            case TagUnitType.Percentage:
                                textConfiguration.m_xAdvance = textConfiguration.m_marginWidth * value / 100;
                                return true;
                        }
                        return false;
                    case 22501:  // </pos>
                    case 20677:  // </POS>
                        return true;
                    case 16034505:  // <voffset>
                    case 11642281:  // <VOFFSET>
                        // Reject tag if value is invalid.
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_baselineOffset = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                return true;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_baselineOffset = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                return true;
                            case TagUnitType.Percentage:
                                //m_baselineOffset = m_marginHeight * val / 100;
                                return false;
                        }
                        return false;
                    case 54741026:  // </voffset>
                    case 50348802:  // </VOFFSET>
                        textConfiguration.m_baselineOffset = 0;
                        return true;
                    //case 43991: // <page>
                    //case 31191: // <PAGE>
                    //            // This tag only works when Overflow - Page mode is used.
                    //    if (m_overflowMode == TextOverflowModes.Page)
                    //    {
                    //        m_xAdvance = 0 + tag_LineIndent + tag_Indent;
                    //        m_lineOffset = 0;
                    //        m_pageNumber += 1;
                    //        m_isNewPage = true;
                    //    }
                    //    return true;
                    //// <BR> tag is now handled inline where it is replaced by a linefeed or \n.
                    ////case 544: // <BR>
                    ////case 800: // <br>
                    ////    m_forceLineBreak = true;
                    ////    return true;
                    case 43969:  // <nobr>
                    case 31169:  // <NOBR>
                        textConfiguration.m_isNonBreakingSpace = true;
                        return true;
                    case 156816:  // </nobr>
                    case 144016:  // </NOBR>
                        textConfiguration.m_isNonBreakingSpace = false;
                        return true;
                    case 45545:  // <size=>
                    case 32745:  // <SIZE>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                if (calliString[5] == 43)  // <size=+00>
                                {
                                    textConfiguration.m_currentFontSize = baseConfiguration.fontSize + value;
                                    textConfiguration.m_sizeStack.Add(textConfiguration.m_currentFontSize);
                                    return true;
                                }
                                else if (calliString[5] == 45)  // <size=-00>
                                {
                                    textConfiguration.m_currentFontSize = baseConfiguration.fontSize + value;
                                    textConfiguration.m_sizeStack.Add(textConfiguration.m_currentFontSize);
                                    return true;
                                }
                                else  // <size=00.0>
                                {
                                    textConfiguration.m_currentFontSize = value;
                                    textConfiguration.m_sizeStack.Add(textConfiguration.m_currentFontSize);
                                    return true;
                                }
                            case TagUnitType.FontUnits:
                                textConfiguration.m_currentFontSize = baseConfiguration.fontSize * value;
                                textConfiguration.m_sizeStack.Add(textConfiguration.m_currentFontSize);
                                return true;
                            case TagUnitType.Percentage:
                                textConfiguration.m_currentFontSize = baseConfiguration.fontSize * value / 100;
                                textConfiguration.m_sizeStack.Add(textConfiguration.m_currentFontSize);
                                return true;
                        }
                        return false;
                    case 158392:  // </size>
                    case 145592:  // </SIZE>
                        textConfiguration.m_currentFontSize = textConfiguration.m_sizeStack.RemoveExceptRoot();
                        return true;
                    case 41311:  // <font=xx>
                    case 28511:  // <FONT>
                        int fontHashCode                   = firstTagIndentifier.valueHashCode;

                        // Special handling for <font=default> or <font=Default>
                        if (fontHashCode == 764638571 || fontHashCode == 523367755)
                        {
                            textConfiguration.m_currentFontMaterialIndex = 0;
                            textConfiguration.m_fontMaterialIndexStack.Add(0);
                            return true;                        }

                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);

                        for (int i = 0; i < fontMaterialSet.length; i++)
                        {
                            ref var candidateFont = ref fontMaterialSet[i];
                            if (textConfiguration.m_htmlTag.Equals(candidateFont.name))
                            {
                                textConfiguration.m_currentFontMaterialIndex = i;
                                textConfiguration.m_fontMaterialIndexStack.Add(i);
                                return true;
                            }
                        }
                        return false;
                    case 154158:  // </font>
                    case 141358:  // </FONT>
                    {                        
                        textConfiguration.m_currentFontMaterialIndex = textConfiguration.m_fontMaterialIndexStack.RemoveExceptRoot();
                        return true;
                    }
                    //case 103415287: // <material="material name">
                    //case 72669687: // <MATERIAL>
                    //    materialHashCode = firstTagIndentifier.valueHashCode;

                    //    // Special handling for <material=default> or <material=Default>
                    //    if (materialHashCode == 764638571 || materialHashCode == 523367755)
                    //    {
                    //        // Check if material font atlas texture matches that of the current font asset.
                    //        //if (m_currentFontAsset.atlas.GetInstanceID() != m_currentMaterial.GetTexture(ShaderUtilities.ID_MainTex).GetInstanceID()) return false;

                    //        m_currentMaterial = m_materialReferences[0].material;
                    //        m_currentMaterialIndex = 0;

                    //        m_materialReferenceStack.Add(m_materialReferences[0]);

                    //        return true;
                    //    }

                    //    // Check if material
                    //    if (MaterialReferenceManager.TryGetMaterial(materialHashCode, out tempMaterial))
                    //    {
                    //        // Check if material font atlas texture matches that of the current font asset.
                    //        //if (m_currentFontAsset.atlas.GetInstanceID() != tempMaterial.GetTexture(ShaderUtilities.ID_MainTex).GetInstanceID()) return false;

                    //        m_currentMaterial = tempMaterial;

                    //        m_currentMaterialIndex = MaterialReference.AddMaterialReference(m_currentMaterial, m_currentFontAsset, ref m_materialReferences, m_materialReferenceIndexLookup);

                    //        m_materialReferenceStack.Add(m_materialReferences[m_currentMaterialIndex]);
                    //    }
                    //    else
                    //    {
                    //        // Load new material
                    //        tempMaterial = Resources.Load<Material>(TMP_Settings.defaultFontAssetPath + new string(calliString, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength));

                    //        if (tempMaterial == null)
                    //            return false;

                    //        // Check if material font atlas texture matches that of the current font asset.
                    //        //if (m_currentFontAsset.atlas.GetInstanceID() != tempMaterial.GetTexture(ShaderUtilities.ID_MainTex).GetInstanceID()) return false;

                    //        // Add new reference to this material in the MaterialReferenceManager
                    //        MaterialReferenceManager.AddFontMaterial(materialHashCode, tempMaterial);

                    //        m_currentMaterial = tempMaterial;

                    //        m_currentMaterialIndex = MaterialReference.AddMaterialReference(m_currentMaterial, m_currentFontAsset, ref m_materialReferences, m_materialReferenceIndexLookup);

                    //        m_materialReferenceStack.Add(m_materialReferences[m_currentMaterialIndex]);
                    //    }
                    //    return true;
                    //case 374360934: // </material>
                    //case 343615334: // </MATERIAL>
                    //    {
                    //        //if (m_currentMaterial.GetTexture(ShaderUtilities.ID_MainTex).GetInstanceID() != m_materialReferenceStack.PreviousItem().material.GetTexture(ShaderUtilities.ID_MainTex).GetInstanceID())
                    //        //    return false;

                    //        MaterialReference materialReference = m_materialReferenceStack.Remove();

                    //        m_currentMaterial = materialReference.material;
                    //        m_currentMaterialIndex = materialReference.index;

                    //        return true;
                    //    }
                    case 320078:  // <space=000.00>
                    case 230446:  // <SPACE>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_xAdvance += value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                return true;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_xAdvance += value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                return true;
                            case TagUnitType.Percentage:
                                // Not applicable
                                return false;
                        }
                        return false;
                    case 276254:  // <alpha=#FF>
                    case 186622:  // <ALPHA>
                        if (firstTagIndentifier.valueLength != 3)
                            return false;

                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        textConfiguration.m_htmlColor.a = (byte)(HexToInt((char)textConfiguration.m_htmlTag[1]) * 16 + HexToInt((char)textConfiguration.m_htmlTag[2]));
                        return true;

                    //case 1750458: // <a name=" ">
                    //    return false;
                    //case 426: // </a>
                    //    return true;
                    //case 43066: // <link="name">
                    //case 30266: // <LINK>
                    //    if (m_isParsingText && !m_isCalculatingPreferredValues)
                    //    {
                    //        int index = m_textInfo.linkCount;

                    //        if (index + 1 > m_textInfo.linkInfo.Length)
                    //            TMP_TextInfo.Resize(ref m_textInfo.linkInfo, index + 1);

                    //        m_textInfo.linkInfo[index].textComponent = this;
                    //        m_textInfo.linkInfo[index].hashCode = firstTagIndentifier.valueHashCode;
                    //        m_textInfo.linkInfo[index].linkTextfirstCharacterIndex = m_characterCount;

                    //        m_textInfo.linkInfo[index].linkIdFirstCharacterIndex = startIndex + firstTagIndentifier.valueStartIndex;
                    //        m_textInfo.linkInfo[index].linkIdLength = firstTagIndentifier.valueLength;
                    //        m_textInfo.linkInfo[index].SetLinkID(calliString, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                    //    }
                    //    return true;
                    //case 155913: // </link>
                    //case 143113: // </LINK>
                    //    if (m_isParsingText && !m_isCalculatingPreferredValues)
                    //    {
                    //        if (m_textInfo.linkCount < m_textInfo.linkInfo.Length)
                    //        {
                    //            m_textInfo.linkInfo[m_textInfo.linkCount].linkTextLength = m_characterCount - m_textInfo.linkInfo[m_textInfo.linkCount].linkTextfirstCharacterIndex;

                    //            m_textInfo.linkCount += 1;
                    //        }
                    //    }
                    //    return true;
                    //case 275917:  // <align=>
                    //case 186285:  // <ALIGN>
                    //    switch (firstTagIndentifier.valueHashCode)
                    //    {
                    //        case 3774683:  // <align=left>
                    //            textConfiguration.m_lineJustification = HorizontalAlignmentOptions.Left;
                    //            textConfiguration.m_lineJustificationStack.Add(textConfiguration.m_lineJustification);
                    //            return true;
                    //        case 136703040:  // <align=right>
                    //            textConfiguration.m_lineJustification = HorizontalAlignmentOptions.Right;
                    //            textConfiguration.m_lineJustificationStack.Add(textConfiguration.m_lineJustification);
                    //            return true;
                    //        case -458210101:  // <align=center>
                    //            textConfiguration.m_lineJustification = HorizontalAlignmentOptions.Center;
                    //            textConfiguration.m_lineJustificationStack.Add(textConfiguration.m_lineJustification);
                    //            return true;
                    //        case -523808257:  // <align=justified>
                    //            textConfiguration.m_lineJustification = HorizontalAlignmentOptions.Justified;
                    //            textConfiguration.m_lineJustificationStack.Add(textConfiguration.m_lineJustification);
                    //            return true;
                    //        case 122383428:  // <align=flush>
                    //            textConfiguration.m_lineJustification = HorizontalAlignmentOptions.Flush;
                    //            textConfiguration.m_lineJustificationStack.Add(textConfiguration.m_lineJustification);
                    //            return true;
                    //    }
                    //    return false;
                    //case 1065846:  // </align>
                    //case 976214:  // </ALIGN>
                    //    textConfiguration.m_lineJustification = textConfiguration.m_lineJustificationStack.RemoveExceptRoot();
                    //    return true;
                    case 327550:  // <width=xx>
                    case 237918:  // <WIDTH>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_width = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                return false;
                            //break;
                            case TagUnitType.Percentage:
                                textConfiguration.m_width = textConfiguration.m_marginWidth * value / 100;
                                break;
                        }
                        return true;
                    case 1117479:  // </width>
                    case 1027847:  // </WIDTH>
                        textConfiguration.m_width = -1;
                        return true;
                    case 281955:  // <color> <color=#FF00FF> or <color=#FF00FF00>
                    case 192323:  // <COLOR=#FF00FF>
                                  // <color=#FFF> 3 Hex (short hand)
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        tagCharCount -= 6;  //remove "color=" from char count
                        if (textConfiguration.m_htmlTag[0] == 35 && tagCharCount == 4)
                        {
                            textConfiguration.m_htmlColor = HexCharsToColor(textConfiguration.m_htmlTag, tagCharCount);
                            textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                            return true;
                        }
                        // <color=#FFF7> 4 Hex (short hand)
                        else if (textConfiguration.m_htmlTag[0] == 35 && tagCharCount == 5)
                        {
                            textConfiguration.m_htmlColor = HexCharsToColor(textConfiguration.m_htmlTag, tagCharCount);
                            textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                            return true;
                        }
                        // <color=#FF00FF> 3 Hex pairs
                        if (textConfiguration.m_htmlTag[0] == 35 && tagCharCount == 7)
                        {
                            textConfiguration.m_htmlColor = HexCharsToColor(textConfiguration.m_htmlTag, tagCharCount);
                            textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                            return true;
                        }
                        // <color=#FF00FF00> 4 Hex pairs
                        else if (textConfiguration.m_htmlTag[0] == 35 && tagCharCount == 9)
                        {
                            textConfiguration.m_htmlColor = HexCharsToColor(textConfiguration.m_htmlTag, tagCharCount);
                            textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                            return true;
                        }

                        // <color=name>
                        switch (firstTagIndentifier.valueHashCode)
                        {
                            case 125395:  // <color=red>
                                textConfiguration.m_htmlColor = Color.red;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case -992792864:  // <color=lightblue>
                                textConfiguration.m_htmlColor = new Color32(173, 216, 230, 255);
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 3573310:  // <color=blue>
                                textConfiguration.m_htmlColor = Color.blue;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 3680713:  // <color=grey>
                                textConfiguration.m_htmlColor = new Color32(128, 128, 128, 255);
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 117905991:  // <color=black>
                                textConfiguration.m_htmlColor = Color.black;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 121463835:  // <color=green>
                                textConfiguration.m_htmlColor = Color.green;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 140357351:  // <color=white>
                                textConfiguration.m_htmlColor = Color.white;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 26556144:  // <color=orange>
                                textConfiguration.m_htmlColor = new Color32(255, 128, 0, 255);
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case -36881330:  // <color=purple>
                                textConfiguration.m_htmlColor = new Color32(160, 32, 240, 255);
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                            case 554054276:  // <color=yellow>
                                textConfiguration.m_htmlColor = Color.yellow;
                                textConfiguration.m_colorStack.Add(textConfiguration.m_htmlColor);
                                return true;
                        }
                        return false;

                    //case 100149144: //<gradient>
                    //case 69403544:  // <GRADIENT>
                    //    firstTagIndentifier.tagType = RichTextTagType.Gradient;
                    //    calliString.GetSubString(ref richtextAdjustments.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                    //    int gradientPresetHashCode = firstTagIndentifier.valueHashCode;
                    //    //TMP_ColorGradient tempColorGradientPreset;

                    //    //// Check if Color Gradient Preset has already been loaded.
                    //    //if (MaterialReferenceManager.TryGetColorGradientPreset(gradientPresetHashCode, out tempColorGradientPreset))
                    //    //{
                    //    //    m_colorGradientPreset = tempColorGradientPreset;
                    //    //}
                    //    //else
                    //    //{
                    //    //    // Load Color Gradient Preset
                    //    //    if (tempColorGradientPreset == null)
                    //    //    {
                    //    //        tempColorGradientPreset = Resources.Load<TMP_ColorGradient>(TMP_Settings.defaultColorGradientPresetsPath + new string(calliString, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength));
                    //    //    }

                    //    //    if (tempColorGradientPreset == null)
                    //    //        return false;

                    //    //    MaterialReferenceManager.AddColorGradientPreset(gradientPresetHashCode, tempColorGradientPreset);
                    //    //    m_colorGradientPreset = tempColorGradientPreset;
                    //    //}

                    //    richtextAdjustments.m_colorGradientPresetIsTinted = false;

                    //    // Check TagIndentifiers
                    //    for (int i = 1; i < richTextTagIndentifiers.Length && richTextTagIndentifiers[i].nameHashCode != 0; i++)
                    //    {
                    //        // Get tagIndentifier name
                    //        int nameHashCode = richTextTagIndentifiers[i].nameHashCode;

                    //        switch (nameHashCode)
                    //        {
                    //            case 45819: // tint
                    //            case 33019: // TINT
                    //                calliString.GetSubString(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength);
                    //                if (ConvertToFloat(ref richtextAdjustments.m_htmlTag, out value) != ParseError.None)
                    //                    richtextAdjustments.m_colorGradientPresetIsTinted = value != 0;
                    //                break;
                    //        }
                    //    }
                    //    richtextAdjustments.m_colorGradientStack.Add(m_colorGradientPreset);

                    //    // TODO : Add support for defining preset in the tag itself

                    //    return true;

                    //case 371094791: // </gradient>
                    //case 340349191: // </GRADIENT>
                    //    m_colorGradientPreset = m_colorGradientStack.Remove();
                    //    return true;

                    case 1983971:  // <cspace=xx.x>
                    case 1356515:  // <CSPACE>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_cSpacing = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_cSpacing = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                return false;
                        }
                        return true;
                    case 7513474:  // </cspace>
                    case 6886018:  // </CSPACE>
                        if (!textConfiguration.m_isParsingText)
                            return true;

                        // Adjust xAdvance to remove extra space from last character.
                        if (textConfiguration.m_characterCount > 0)
                        {
                            textConfiguration.m_xAdvance -= textConfiguration.m_cSpacing;
                            //m_textInfo.characterInfo[m_characterCount - 1].xAdvance = m_xAdvance;
                        }
                        textConfiguration.m_cSpacing = 0;
                        return true;
                    case 2152041:  // <mspace=xx.x>
                    case 1524585:  // <MSPACE>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_monoSpacing = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_monoSpacing = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                return false;
                        }
                        return true;
                    case 7681544:  // </mspace>
                    case 7054088:  // </MSPACE>
                        textConfiguration.m_monoSpacing = 0;
                        return true;
                    case 280416:  // <class="name">
                        return false;
                    case 1071884:  // </color>
                    case 982252:  // </COLOR>
                        textConfiguration.m_htmlColor = textConfiguration.m_colorStack.RemoveExceptRoot();
                        return true;
                    case 2068980:  // <indent=10px> <indent=10em> <indent=50%>
                    case 1441524:  // <INDENT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                                    textConfiguration.tag_Indent = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.tag_Indent = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                textConfiguration.tag_Indent = textConfiguration.m_marginWidth * value / 100;
                                break;
                        }
                        textConfiguration.m_indentStack.Add(textConfiguration.tag_Indent);

                        textConfiguration.m_xAdvance = textConfiguration.tag_Indent;
                        return true;
                    case 7598483:  // </indent>
                    case 6971027:  // </INDENT>
                        textConfiguration.tag_Indent = textConfiguration.m_indentStack.RemoveExceptRoot();
                        //m_xAdvance = tag_Indent;
                        return true;
                    case 1109386397:  // <line-indent>
                    case -842656867:  // <LINE-INDENT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.tag_LineIndent = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.tag_LineIndent = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                textConfiguration.tag_LineIndent = textConfiguration.m_marginWidth * value / 100;
                                break;
                        }

                        textConfiguration.m_xAdvance += textConfiguration.tag_LineIndent;
                        return true;
                    case -445537194:  // </line-indent>
                    case 1897386838:  // </LINE-INDENT>
                        textConfiguration.tag_LineIndent = 0;
                        return true;
                    //case 2246877: // <sprite=x>
                    //case 1619421: // <SPRITE>
                    //    int spriteAssetHashCode = firstTagIndentifier.valueHashCode;
                    //    TMP_SpriteAsset tempSpriteAsset;
                    //    m_spriteIndex = -1;

                    //    // CHECK TAG FORMAT
                    //    if (firstTagIndentifier.valueType == TagValueType.None || firstTagIndentifier.valueType == TagValueType.NumericalValue)
                    //    {
                    //        // No Sprite Asset is assigned to the text object
                    //        if (m_spriteAsset != null)
                    //        {
                    //            m_currentSpriteAsset = m_spriteAsset;
                    //        }
                    //        else if (m_defaultSpriteAsset != null)
                    //        {
                    //            m_currentSpriteAsset = m_defaultSpriteAsset;
                    //        }
                    //        else if (m_defaultSpriteAsset == null)
                    //        {
                    //            if (TMP_Settings.defaultSpriteAsset != null)
                    //                m_defaultSpriteAsset = TMP_Settings.defaultSpriteAsset;
                    //            else
                    //                m_defaultSpriteAsset = Resources.Load<TMP_SpriteAsset>("Sprite Assets/Default Sprite Asset");

                    //            m_currentSpriteAsset = m_defaultSpriteAsset;
                    //        }

                    //        // No valid sprite asset available
                    //        if (m_currentSpriteAsset == null)
                    //            return false;
                    //    }
                    //    else
                    //    {
                    //        // A Sprite Asset has been specified
                    //        if (MaterialReferenceManager.TryGetSpriteAsset(spriteAssetHashCode, out tempSpriteAsset))
                    //        {
                    //            m_currentSpriteAsset = tempSpriteAsset;
                    //        }
                    //        else
                    //        {
                    //            // Load Sprite Asset
                    //            if (tempSpriteAsset == null)
                    //            {
                    //                //
                    //                tempSpriteAsset = OnSpriteAssetRequest?.Invoke(spriteAssetHashCode, new string(calliString, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength));

                    //                if (tempSpriteAsset == null)
                    //                    tempSpriteAsset = Resources.Load<TMP_SpriteAsset>(TMP_Settings.defaultSpriteAssetPath + new string(calliString, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength));
                    //            }

                    //            if (tempSpriteAsset == null)
                    //                return false;

                    //            //Debug.Log("Loading & assigning new Sprite Asset: " + tempSpriteAsset.name);
                    //            MaterialReferenceManager.AddSpriteAsset(spriteAssetHashCode, tempSpriteAsset);
                    //            m_currentSpriteAsset = tempSpriteAsset;
                    //        }
                    //    }

                    //    // Handling of <sprite=index> legacy tag format.
                    //    if (firstTagIndentifier.valueType == TagValueType.NumericalValue) // <sprite=index>
                    //    {
                    //        int index = (int)ConvertToFloat(ref richtextAdjustments.m_htmlTag, out value);

                    //        // Reject tag if value is invalid.
                    //        if (index == Int16.MinValue) return false;

                    //        // Check to make sure sprite index is valid
                    //        if (index > m_currentSpriteAsset.spriteCharacterTable.Count - 1) return false;

                    //        m_spriteIndex = index;
                    //    }

                    //    m_spriteColor = s_colorWhite;
                    //    m_tintSprite = false;

                    //    // Handle Sprite Tag TagIndentifiers
                    //    for (int i = 0; i < richTextTagIndentifiers.Length && richTextTagIndentifiers[i].nameHashCode != 0; i++)
                    //    {
                    //        //Debug.Log("TagIndentifier[" + i + "].nameHashCode=" + richTextTagIndentifiers[i].nameHashCode + "   Value:" + ConvertToFloat(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength));
                    //        int nameHashCode = richTextTagIndentifiers[i].nameHashCode;
                    //        int index = 0;

                    //        switch (nameHashCode)
                    //        {
                    //            case 43347: // <sprite name="">
                    //            case 30547: // <SPRITE NAME="">
                    //                m_currentSpriteAsset = TMP_SpriteAsset.SearchForSpriteByHashCode(m_currentSpriteAsset, richTextTagIndentifiers[i].valueHashCode, true, out index);
                    //                if (index == -1) return false;

                    //                m_spriteIndex = index;
                    //                break;
                    //            case 295562: // <sprite index=>
                    //            case 205930: // <SPRITE INDEX=>
                    //                index = (int)ConvertToFloat(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[1].valueStartIndex, richTextTagIndentifiers[1].valueLength);

                    //                // Reject tag if value is invalid.
                    //                if (index == Int16.MinValue) return false;

                    //                // Check to make sure sprite index is valid
                    //                if (index > m_currentSpriteAsset.spriteCharacterTable.Count - 1) return false;

                    //                m_spriteIndex = index;
                    //                break;
                    //            case 45819: // tint
                    //            case 33019: // TINT
                    //                m_tintSprite = ConvertToFloat(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength) != 0;
                    //                break;
                    //            case 281955: // color=#FF00FF80
                    //            case 192323: // COLOR
                    //                m_spriteColor = HexCharsToColor(calliString, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength);
                    //                break;
                    //            case 39505: // anim="0,16,12"  start, end, fps
                    //            case 26705: // ANIM
                    //                        //Debug.Log("Start: " + richTextTagIndentifiers[i].valueStartIndex + "  Length: " + richTextTagIndentifiers[i].valueLength);
                    //                int paramCount = GetTagIndentifierParameters(calliString, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength, ref m_tagIndentifierParameterValues);
                    //                if (paramCount != 3) return false;

                    //                m_spriteIndex = (int)m_tagIndentifierParameterValues[0];

                    //                if (m_isParsingText)
                    //                {
                    //                    // TODO : fix this!
                    //                    // It is possible for a sprite to get animated when it ends up being truncated.
                    //                    // Should consider moving the animation of the sprite after text geometry upload.

                    //                    spriteAnimator.DoSpriteAnimation(m_characterCount, m_currentSpriteAsset, m_spriteIndex, (int)m_tagIndentifierParameterValues[1], (int)m_tagIndentifierParameterValues[2]);
                    //                }

                    //                break;
                    //            //case 45545: // size
                    //            //case 32745: // SIZE

                    //            //    break;
                    //            default:
                    //                if (nameHashCode != 2246877 && nameHashCode != 1619421)
                    //                    return false;
                    //                break;
                    //        }
                    //    }

                    //    if (m_spriteIndex == -1) return false;

                    //    // Material HashCode for the Sprite Asset is the Sprite Asset Hash Code
                    //    m_currentMaterialIndex = MaterialReference.AddMaterialReference(m_currentSpriteAsset.material, m_currentSpriteAsset, ref m_materialReferences, m_materialReferenceIndexLookup);

                    //    m_textElementType = TMP_TextElementType.Sprite;
                    //    return true;
                    case 730022849:  // <lowercase>
                    case 514803617:  // <LOWERCASE>
                        textConfiguration.m_fontStyleInternal |= FontStyles.LowerCase;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.LowerCase);
                        return true;
                    case -1668324918:  // </lowercase>
                    case -1883544150:  // </LOWERCASE>
                        if ((baseConfiguration.fontStyle & FontStyles.LowerCase) != FontStyles.LowerCase)
                        {
                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.LowerCase) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.LowerCase;
                        }
                        return true;
                    case 13526026:  // <allcaps>
                    case 9133802:  // <ALLCAPS>
                    case 781906058:  // <uppercase>
                    case 566686826:  // <UPPERCASE>
                        textConfiguration.m_fontStyleInternal |= FontStyles.UpperCase;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.UpperCase);
                        return true;
                    case 52232547:  // </allcaps>
                    case 47840323:  // </ALLCAPS>
                    case -1616441709:  // </uppercase>
                    case -1831660941:  // </UPPERCASE>
                        if ((baseConfiguration.fontStyle & FontStyles.UpperCase) != FontStyles.UpperCase)
                        {
                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.UpperCase) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.UpperCase;
                        }
                        return true;
                    case 766244328:  // <smallcaps>
                    case 551025096:  // <SMALLCAPS>
                        textConfiguration.m_fontStyleInternal |= FontStyles.SmallCaps;
                        textConfiguration.m_fontStyleStack.Add(FontStyles.SmallCaps);
                        return true;
                    case -1632103439:  // </smallcaps>
                    case -1847322671:  // </SMALLCAPS>
                        if ((baseConfiguration.fontStyle & FontStyles.SmallCaps) != FontStyles.SmallCaps)
                        {
                            if (textConfiguration.m_fontStyleStack.Remove(FontStyles.SmallCaps) == 0)
                                textConfiguration.m_fontStyleInternal &= ~FontStyles.SmallCaps;
                        }
                        return true;
                    case 2109854:  // <margin=00.0> <margin=00em> <margin=50%>
                    case 1482398:  // <MARGIN>
                                   // Check value type
                        switch (firstTagIndentifier.valueType)
                        {
                            case TagValueType.NumericalValue:
                                calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                                // Reject tag if value is invalid.
                                if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                                    return false;

                                // Determine tag unit type
                                switch (tagUnitType)
                                {
                                    case TagUnitType.Pixels:
                                        textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                        break;
                                    case TagUnitType.FontUnits:
                                        textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                        break;
                                    case TagUnitType.Percentage:
                                        textConfiguration.m_marginLeft =
                                            (textConfiguration.m_marginWidth - (textConfiguration.m_width != -1 ? textConfiguration.m_width : 0)) * value / 100;
                                        break;
                                }
                                textConfiguration.m_marginLeft  = textConfiguration.m_marginLeft >= 0 ? textConfiguration.m_marginLeft : 0;
                                textConfiguration.m_marginRight = textConfiguration.m_marginLeft;
                                return true;

                            case TagValueType.None:
                                for (int i = 1; i < richTextTagIndentifiers.Length && richTextTagIndentifiers[i].nameHashCode != 0; i++)
                                {
                                    currentTagIndentifier = ref richTextTagIndentifiers.ElementAt(i);
                                    // Get tagIndentifier name
                                    int nameHashCode = richTextTagIndentifiers[i].nameHashCode;

                                    switch (nameHashCode)
                                    {
                                        case 42823:  // <margin left=value>
                                            calliString.GetSubString(ref textConfiguration.m_htmlTag, currentTagIndentifier.valueStartIndex, currentTagIndentifier.valueLength);
                                            // Reject tag if value is invalid.
                                            if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                                                return false;

                                            switch (richTextTagIndentifiers[i].unitType)
                                            {
                                                case TagUnitType.Pixels:
                                                    textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                                    break;
                                                case TagUnitType.FontUnits:
                                                    textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                                    break;
                                                case TagUnitType.Percentage:
                                                    textConfiguration.m_marginLeft =
                                                        (textConfiguration.m_marginWidth - (textConfiguration.m_width != -1 ? textConfiguration.m_width : 0)) * value / 100;
                                                    break;
                                            }
                                            textConfiguration.m_marginLeft = textConfiguration.m_marginLeft >= 0 ? textConfiguration.m_marginLeft : 0;
                                            break;

                                        case 315620:  // <margin right=value>
                                            calliString.GetSubString(ref textConfiguration.m_htmlTag, currentTagIndentifier.valueStartIndex, currentTagIndentifier.valueLength);
                                            // Reject tag if value is invalid.
                                            if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                                                return false;

                                            switch (richTextTagIndentifiers[i].unitType)
                                            {
                                                case TagUnitType.Pixels:
                                                    textConfiguration.m_marginRight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                                    break;
                                                case TagUnitType.FontUnits:
                                                    textConfiguration.m_marginRight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                                    break;
                                                case TagUnitType.Percentage:
                                                    textConfiguration.m_marginRight =
                                                        (textConfiguration.m_marginWidth - (textConfiguration.m_width != -1 ? textConfiguration.m_width : 0)) * value / 100;
                                                    break;
                                            }
                                            textConfiguration.m_marginRight = textConfiguration.m_marginRight >= 0 ? textConfiguration.m_marginRight : 0;
                                            break;
                                    }
                                }
                                return true;
                        }

                        return false;
                    case 7639357:  // </margin>
                    case 7011901:  // </MARGIN>
                        textConfiguration.m_marginLeft  = 0;
                        textConfiguration.m_marginRight = 0;
                        return true;
                    case 1100728678:  // <margin-left=xx.x>
                    case -855002522:  // <MARGIN-LEFT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_marginLeft = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                textConfiguration.m_marginLeft =
                                    (textConfiguration.m_marginWidth - (textConfiguration.m_width != -1 ? textConfiguration.m_width : 0)) * value / 100;
                                break;
                        }
                        textConfiguration.m_marginLeft = textConfiguration.m_marginLeft >= 0 ? textConfiguration.m_marginLeft : 0;
                        return true;
                    case -884817987:  // <margin-right=xx.x>
                    case -1690034531:  // <MARGIN-RIGHT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_marginRight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_marginRight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                textConfiguration.m_marginRight =
                                    (textConfiguration.m_marginWidth - (textConfiguration.m_width != -1 ? textConfiguration.m_width : 0)) * value / 100;
                                break;
                        }
                        textConfiguration.m_marginRight = textConfiguration.m_marginRight >= 0 ? textConfiguration.m_marginRight : 0;
                        return true;
                    case 1109349752:  // <line-height=xx.x>
                    case -842693512:  // <LINE-HEIGHT>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        switch (tagUnitType)
                        {
                            case TagUnitType.Pixels:
                                textConfiguration.m_lineHeight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f);
                                break;
                            case TagUnitType.FontUnits:
                                textConfiguration.m_lineHeight = value * (baseConfiguration.isOrthographic ? 1 : 0.1f) * textConfiguration.m_currentFontSize;
                                break;
                            case TagUnitType.Percentage:
                                //fontScale = (richtextAdjustments.m_currentFontSize / m_currentFontAsset.faceInfo.pointSize * m_currentFontAsset.faceInfo.scale * (richtextAdjustments.m_isOrthographic ? 1 : 0.1f));
                                //richtextAdjustments.m_lineHeight = m_fontAsset.faceInfo.lineHeight * value / 100 * fontScale;
                                break;
                        }
                        return true;
                    case -445573839:  // </line-height>
                    case 1897350193:  // </LINE-HEIGHT>
                        textConfiguration.m_lineHeight = float.MinValue;  //TMP_Math.FLOAT_UNSET -->is there a better way to do this?
                        return true;
                    case 15115642:  // <noparse>
                    case 10723418:  // <NOPARSE>
                        textConfiguration.tag_NoParsing = true;
                        return true;
                    //case 1913798: // <action>
                    //case 1286342: // <ACTION>
                    //    int actionID = firstTagIndentifier.valueHashCode;

                    //    if (m_isParsingText)
                    //    {
                    //        m_actionStack.Add(actionID);

                    //        Debug.Log("Action ID: [" + actionID + "] First character index: " + m_characterCount);

                    //    }
                    //    //if (m_isParsingText)
                    //    //{
                    //    // TMP_Action action = TMP_Action.GetAction(firstTagIndentifier.valueHashCode);
                    //    //}
                    //    return true;
                    //case 7443301: // </action>
                    //case 6815845: // </ACTION>
                    //    if (m_isParsingText)
                    //    {
                    //        Debug.Log("Action ID: [" + m_actionStack.CurrentItem() + "] Last character index: " + (m_characterCount - 1));
                    //    }

                    //    m_actionStack.Remove();
                    //    return true;
                    case 315682:  // <scale=xx.x>
                    case 226050:  // <SCALE=xx.x>
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        textConfiguration.m_FXScale = new Vector3(value, 1, 1);

                        return true;
                    case 1105611:  // </scale>
                    case 1015979:  // </SCALE>
                        textConfiguration.m_FXScale = 1;
                        return true;
                    case 2227963:  // <rotate=xx.x>
                    case 1600507:  // <ROTATE=xx.x>
                                   // TODO: Add ability to use Random Rotation
                        calliString.GetSubString(ref textConfiguration.m_htmlTag, firstTagIndentifier.valueStartIndex, firstTagIndentifier.valueLength);
                        // Reject tag if value is invalid.
                        if (ConvertToFloat(ref textConfiguration.m_htmlTag, out value) != ParseError.None)
                            return false;

                        textConfiguration.m_FXRotationAngle = math.radians(value);

                        return true;
                    case 7757466:  // </rotate>
                    case 7130010:  // </ROTATE>
                        textConfiguration.m_FXRotationAngle = 0;
                        return true;
                    case 317446:  // <table>
                    case 227814:  // <TABLE>
                        //switch (richTextTagIndentifiers[1].nameHashCode)
                        //{
                        //    case 327550: // width
                        //        float tableWidth = ConvertToFloat(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[1].valueStartIndex, richTextTagIndentifiers[1].valueLength);

                        //        // Reject tag if value is invalid.
                        //        if (tableWidth == Int16.MinValue) return false;

                        //        switch (tagUnitType)
                        //        {
                        //            case TagUnitType.Pixels:
                        //                Debug.Log("Table width = " + tableWidth + "px.");
                        //                break;
                        //            case TagUnitType.FontUnits:
                        //                Debug.Log("Table width = " + tableWidth + "em.");
                        //                break;
                        //            case TagUnitType.Percentage:
                        //                Debug.Log("Table width = " + tableWidth + "%.");
                        //                break;
                        //        }
                        //        break;
                        //}
                        return false;
                    case 1107375:  // </table>
                    case 1017743:  // </TABLE>
                        return true;
                    case 926:  // <tr>
                    case 670:  // <TR>
                        return true;
                    case 3229:  // </tr>
                    case 2973:  // </TR>
                        return true;
                    case 916:  // <th>
                    case 660:  // <TH>
                               // Set style to bold and center alignment
                        return true;
                    case 3219:  // </th>
                    case 2963:  // </TH>
                        return true;
                    case 912:  // <td>
                    case 656:  // <TD>
                        // Style options
                        //for (int i = 1; i < richTextTagIndentifiers.Length && richTextTagIndentifiers[i].nameHashCode != 0; i++)
                        //{
                        //    switch (richTextTagIndentifiers[i].nameHashCode)
                        //    {
                        //        case 327550: // width
                        //            float tableWidth = ConvertToFloat(ref richtextAdjustments.m_htmlTag, richTextTagIndentifiers[i].valueStartIndex, richTextTagIndentifiers[i].valueLength);

                        //            switch (tagUnitType)
                        //            {
                        //                case TagUnitType.Pixels:
                        //                    Debug.Log("Table width = " + tableWidth + "px.");
                        //                    break;
                        //                case TagUnitType.FontUnits:
                        //                    Debug.Log("Table width = " + tableWidth + "em.");
                        //                    break;
                        //                case TagUnitType.Percentage:
                        //                    Debug.Log("Table width = " + tableWidth + "%.");
                        //                    break;
                        //            }
                        //            break;
                        //        case 275917: // align
                        //            switch (richTextTagIndentifiers[i].valueHashCode)
                        //            {
                        //                case 3774683: // left
                        //                    Debug.Log("TD align=\"left\".");
                        //                    break;
                        //                case 136703040: // right
                        //                    Debug.Log("TD align=\"right\".");
                        //                    break;
                        //                case -458210101: // center
                        //                    Debug.Log("TD align=\"center\".");
                        //                    break;
                        //                case -523808257: // justified
                        //                    Debug.Log("TD align=\"justified\".");
                        //                    break;
                        //            }
                        //            break;
                        //    }
                        //}

                        return false;
                    case 3215:  // </td>
                    case 2959:  // </TD>
                        return false;
                }
            }
            //#endif
            //            #endregion

            return false;
        }

        /// <summary>
        /// Extracts a float value from char[] given a start index and length.
        /// </summary>
        /// <param name="chars"></param> The Char[] containing the numerical sequence.
        /// <returns></returns>
        static ParseError ConvertToFloat(ref FixedString128Bytes htmlTag, out float value)
        {
            value         = 0;
            int subOffset = 0;
            return htmlTag.Parse(ref subOffset, ref value);
        }
        /// <summary>
        /// Method to convert Hex color values to Color32. Accessing CalliString via indexer[]
        /// works only when CalliString consists ONLY of 1 byte chars!!!
        /// </summary>
        /// <param name="htmlTag"></param>
        /// <param name="tagCount"></param>
        /// <returns></returns>
        static Color32 HexCharsToColor(in FixedString128Bytes htmlTag, int tagCount)
        {
            if (tagCount == 4)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[1]));
                byte g = (byte)(HexToInt((char)htmlTag[2]) * 16 + HexToInt((char)htmlTag[2]));
                byte b = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[3]));

                return new Color32(r, g, b, 255);
            }
            else if (tagCount == 5)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[1]));
                byte g = (byte)(HexToInt((char)htmlTag[2]) * 16 + HexToInt((char)htmlTag[2]));
                byte b = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[3]));
                byte a = (byte)(HexToInt((char)htmlTag[4]) * 16 + HexToInt((char)htmlTag[4]));

                return new Color32(r, g, b, a);
            }
            else if (tagCount == 7)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[2]));
                byte g = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[4]));
                byte b = (byte)(HexToInt((char)htmlTag[5]) * 16 + HexToInt((char)htmlTag[6]));

                return new Color32(r, g, b, 255);
            }
            else if (tagCount == 9)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[2]));
                byte g = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[4]));
                byte b = (byte)(HexToInt((char)htmlTag[5]) * 16 + HexToInt((char)htmlTag[6]));
                byte a = (byte)(HexToInt((char)htmlTag[7]) * 16 + HexToInt((char)htmlTag[8]));

                return new Color32(r, g, b, a);
            }
            return new Color32(255, 255, 255, 255);
        }
        /// <summary>
        /// Method to convert Hex to Int
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        static int HexToInt(char hex)
        {
            switch (hex)
            {
                case '0': return 0;
                case '1': return 1;
                case '2': return 2;
                case '3': return 3;
                case '4': return 4;
                case '5': return 5;
                case '6': return 6;
                case '7': return 7;
                case '8': return 8;
                case '9': return 9;
                case 'A': return 10;
                case 'B': return 11;
                case 'C': return 12;
                case 'D': return 13;
                case 'E': return 14;
                case 'F': return 15;
                case 'a': return 10;
                case 'b': return 11;
                case 'c': return 12;
                case 'd': return 13;
                case 'e': return 14;
                case 'f': return 15;
            }
            return 15;
        }
        private enum ParserState : byte
        {
            Zero,
            One,
            Two,
        }
    }
}

