using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Calligraphics.RichText
{    
    internal static class RichTextParser
    {
        private enum ParserState : byte
        {
            Zero,
            One,
            Two,
        }
        internal static bool GetTag(
            in CalliString calliStringRaw,            
            ref CalliString.Enumerator enumerator,
            int position,
            ref NativeStream.Writer xmlTagStream, 
            ref FixedString128Bytes m_htmlTag) 
        {
            int tagCharCount = 0;
            int tagByteCount = 0;
            int startByteIndex = enumerator.NextRuneByteIndex;
            ParserState tagIndentifierFlag = ParserState.Zero;

            var tag = new XMLTag();
            ref var tagValue = ref tag.value;
            int nameHashCode = default;
            int valueHashCode = default;


            bool isTagSet = false;
            bool isValidHtmlTag = false;
            bool success = false;

            Unicode.Rune unicode = Unicode.BadRune;
            while (enumerator.MoveNext() && (unicode = enumerator.Current) != Unicode.BadRune && unicode != '<')
            {
                if (unicode == '>')  // ASCII Code of End HTML tag '>'
                {
                    isValidHtmlTag = true;
                    GetTagType(nameHashCode, ref tag);
                    //calliStringRaw.GetSubString(ref m_htmlTag, startByteIndex, tagByteCount);
                    //Debug.Log($"{m_htmlTag} {nameHashCode}");
                    tag.startID = position;
                    tag.endID = enumerator.NextRuneByteIndex - unicode.LengthInUtf8Bytes(); //could also do -1 as '<' is known to be just 1 byte           
                    switch (tagValue.type)
                    {                        
                        case TagValueType.NumericalValue:
                            calliStringRaw.GetSubString(ref m_htmlTag, tagValue.valueStart, tagValue.valueLength);
                            success = (ConvertToFloat(ref m_htmlTag, out float value) == ParseError.None);
                            if (success)
                                tagValue.NumericalValue = value;
                            //Debug.Log($"NumericalValue: {m_htmlTag} {tagValue.valueLength} {tagValue.NumericalValue}");
                            else
                                return  false;
                            break;
                        case TagValueType.ColorValue:
                            calliStringRaw.GetSubString(ref m_htmlTag, tagValue.valueStart, tagValue.valueLength);
                            tagValue.ColorValue = HexCharsToColor(m_htmlTag, tagValue.valueLength);
                            //Debug.Log($"Color: {m_htmlTag} {tagValue.valueLength} {(Color32)tagValue.ColorValue}");
                            break;
                        case TagValueType.StringValue:
                            tag.value.valueHash = valueHashCode;
                            GetStringValueTagType(valueHashCode, ref tag);
                            //Debug.Log($"StringValue: {tag.tagType} {tag.value.stringValue}");
                            break;
                        default:
                            if (tag.tagType == TagType.Unknown)
                            {
                                calliStringRaw.GetSubString(ref m_htmlTag, startByteIndex, tagByteCount);                                
                                if (m_htmlTag[0] == 35)//special handling of tags that specify color without any prefix
                                {                                     
                                    tag.tagType = TagType.Color;
                                    tagValue.type = TagValueType.ColorValue;
                                    tagValue.ColorValue = HexCharsToColor(m_htmlTag, tagByteCount);
                                    //Debug.Log($"Color: {m_htmlTag} {(Color32)tagValue.ColorValue}");
                                    break;                                    
                                }
                                //Debug.Log($"Unknown value: {m_htmlTag} {tag.tagType} {tagByteCount - startByteIndex}");
                            }
                            break;
                    }
                    //Debug.Log($"Unexpected: {tag.tagType} {tag.value.NumericalValue}");
                    break;
                }

                int byteCount = unicode.LengthInUtf8Bytes();
                tagCharCount += 1;
                tagByteCount += byteCount;

                if (tagIndentifierFlag == ParserState.One)
                {
                    if (tagValue.type == TagValueType.None)
                    {
                        // Check for tagIndentifier type
                        if (unicode == '+' || unicode == '-' || unicode == '.' || Unicode.Rune.IsDigit(unicode)) 
                        {
                            //To-Do: distinguish between "+" and "-". Needed to enable relative font size increase (12pt "+6pt")...could store as NumericalValue.Plus and NumericalValue.Minus
                            //Debug.Log($"Found digit tag {(char)unicode.value}");
                            tagValue.unit = TagUnitType.Pixels;
                            tagValue.type = TagValueType.NumericalValue;
                            tagValue.valueStart = enumerator.NextRuneByteIndex - unicode.LengthInUtf8Bytes();
                            tagValue.valueLength += byteCount;
                        }
                        else if (unicode == '#')
                        {
                            //Debug.Log($"Found color tag {(char)unicode.value}");
                            tagValue.unit = TagUnitType.Pixels;
                            tagValue.type = TagValueType.ColorValue;
                            tagValue.valueStart = enumerator.NextRuneByteIndex - unicode.LengthInUtf8Bytes();
                            tagValue.valueLength += byteCount;
                        }
                        else if (unicode == '"')
                        {
                            //Debug.Log($"Found string tag {(char)unicode.value}");
                            tagValue.unit = TagUnitType.Pixels;
                            tagValue.type = TagValueType.StringValue;
                            tagValue.valueStart = enumerator.NextRuneByteIndex;
                        }
                        else
                        {
                            //Debug.Log($"Found other tag {(char)unicode.value}");
                            tagValue.unit = TagUnitType.Pixels;
                            tagValue.type = TagValueType.StringValue;
                            tagValue.valueStart = enumerator.NextRuneByteIndex - unicode.LengthInUtf8Bytes();
                            valueHashCode = (valueHashCode << 5) + valueHashCode ^ unicode.value;
                            tagValue.valueLength += byteCount;
                        }
                    }
                    else
                    {
                        if (tagValue.type == TagValueType.NumericalValue)
                        {
                            // Check for termination of numerical value.
                            if (unicode == 'p' || unicode == 'e' || unicode == '%' || unicode == ' ')
                            {
                                tagIndentifierFlag = ParserState.Two;
                                switch (unicode.value)
                                {
                                    case 'e':
                                        tagValue.unit = TagUnitType.FontUnits;
                                        break;
                                    case '%':
                                        tagValue.unit = TagUnitType.Percentage;
                                        break;
                                    default:
                                        tagValue.unit = TagUnitType.Pixels;
                                        break;
                                }
                                //parsing is done, but continue to determine complete length of tag
                            }
                            else if (tagIndentifierFlag != ParserState.Two)
                            {
                                tagValue.valueLength += byteCount;
                            }
                        }
                        else if (tagValue.type == TagValueType.ColorValue)
                        {
                            if (unicode != ' ')
                            {
                                tagValue.valueLength += byteCount;
                            }
                            else
                            {
                                tagIndentifierFlag = ParserState.Two;
                                //parsing is done, but continue to determine complete length of tag
                            }
                        }
                        else if (tagValue.type == TagValueType.StringValue)
                        {
                            // Compute HashCode value for the named tag.
                            if (unicode != '"')
                            {
                                valueHashCode = (valueHashCode << 5) + valueHashCode ^ unicode.value;
                                tagValue.valueLength += byteCount;
                            }
                            else
                            {
                                tagIndentifierFlag = ParserState.Two;
                                //parsing is done, but continue to determine complete length of tag
                            }
                        }
                    }
                }

                if (unicode == '=') // '='
                    tagIndentifierFlag = ParserState.One;

                // Compute HashCode for the name of the tagIndentifier
                if (tagIndentifierFlag == ParserState.Zero && unicode == ' ')
                {
                    if (isTagSet) //found '=' 2 times in same tag --> invalid tag
                        return false;

                    isTagSet = true;
                    tagIndentifierFlag = ParserState.Two;
                    //parsing is done, but continue to determine complete length of tag
                }

                if (tagIndentifierFlag == ParserState.Zero)
                    nameHashCode = (nameHashCode << 3) - nameHashCode + unicode.value;

                if (tagIndentifierFlag == ParserState.Two && unicode == ' ')
                    tagIndentifierFlag = ParserState.Zero;
            }

            if (!isValidHtmlTag)
            {
                return false;
            }
            if (tag.tagType != TagType.Unknown)
                xmlTagStream.Write(tag);

            return true;            
        }
        
        internal static void GetTagType(int tagHash, ref XMLTag tag)
        {
            switch (tagHash)
            {
                case 98:  // <b>
                case 66:  // <B>
                    tag.tagType = TagType.Bold;
                    tag.isClosing = false;
                    return;
                case 427:  // </b>
                case 395:  // </B>
                    tag.tagType = TagType.Bold;
                    tag.isClosing = true;
                    return;
                case 105:  // <i>
                case 73:  // <I>
                    tag.tagType = TagType.Italic;
                    tag.isClosing = false;
                    return;
                case 434:  // </i>
                case 402:  // </I>
                    tag.tagType = TagType.Italic;
                    tag.isClosing = true;
                    return;
                case 115:  // <s>
                case 83:  // <S>
                    tag.tagType = TagType.Strikethrough;
                    tag.isClosing = false;
                    return;
                case 444:  // </s>
                case 412:  // </S>
                    tag.tagType = TagType.Strikethrough;
                    tag.isClosing = true;
                    return;
                case 117:  // <u>
                case 85:  // <U>
                    tag.tagType = TagType.Underline;
                    tag.isClosing = false;
                    return;
                case 446:  // </u>
                case 414:  // </U>
                    tag.tagType = TagType.Underline;
                    tag.isClosing = true;
                    return;
                case 43045:  // <mark=#FF00FF80>
                case 30245:  // <MARK>
                    tag.tagType = TagType.Mark;
                    tag.isClosing = false;
                    return;
                case 155892:  // </mark>
                case 143092:  // </MARK>                        
                    tag.tagType = TagType.Mark;
                    tag.isClosing = true;
                    return;
                case 41350: // <frac>
                case 28550: // <FRAC>
                    tag.tagType = TagType.Fraction;
                    tag.isClosing = false;
                    return;
                case 154197: // </frac>
                case 141397: // </FRAC>
                    tag.tagType = TagType.Fraction;
                    tag.isClosing = true;
                    return;
                case 6552:  // <sub>
                case 4728:  // <SUB>
                    tag.tagType = TagType.Subscript;
                    tag.isClosing = false;
                    return;
                case 22673:  // </sub>
                case 20849:  // </SUB>
                    tag.tagType = TagType.Subscript;
                    tag.isClosing = true;
                    return;
                case 6566:  // <sup>
                case 4742:  // <SUP>
                    tag.tagType = TagType.Superscript;
                    tag.isClosing = false;
                    return;
                case 22687:  // </sup>
                case 20863:  // </SUP>
                    tag.tagType = TagType.Superscript;
                    tag.isClosing = true;
                    return;
                case -330774850:  // <font-weight>
                case 2012149182:  // <FONT-WEIGHT>
                    tag.tagType = TagType.FontWeight;
                    tag.isClosing = false;
                    return;
                case -1885698441:  // </font-weight>
                case 457225591:  // </FONT-WEIGHT>
                    tag.tagType = TagType.FontWeight;
                    tag.isClosing = true;
                    return;
                case 566314408:  // <font-width>
                case -939682424:  // <FONT-WIDTH>
                    tag.tagType = TagType.FontWidth;
                    tag.isClosing = false;
                    return;
                case 957749223:  // </font-width>
                case -548247609:  // </FONT-WIDTH>
                    tag.tagType = TagType.FontWidth;
                    tag.isClosing = true;
                    return;
                case 43969:  // <nobr>
                case 31169:  // <NOBR>
                    tag.tagType = TagType.NoBr;
                    tag.isClosing = false;
                    return;
                case 156816:  // </nobr>
                case 144016:  // </NOBR>
                    tag.tagType = TagType.NoBr;
                    tag.isClosing = true;
                    return;
                case 45545:  // <size=>
                case 32745:  // <SIZE>
                    tag.tagType = TagType.Size;
                    tag.isClosing = false;
                    return;
                case 158392:  // </size>
                case 145592:  // </SIZE>
                    tag.tagType = TagType.Size;
                    tag.isClosing = true;
                    return;
                case 41311:  // <font=xx>
                case 28511:  // <FONT>
                    tag.tagType = TagType.Font;
                    tag.isClosing = false;
                    return;
                case 154158:  // </font>
                case 141358:  // </FONT>
                    tag.tagType = TagType.Font;
                    tag.isClosing = true;
                    return;
                case 320078:  // <space=000.00>
                case 230446:  // <SPACE>
                    tag.tagType = TagType.Space;
                    tag.isClosing = false;
                    return;
                case 276254:  // <alpha=#FF>
                case 186622:  // <ALPHA>
                    tag.tagType = TagType.Alpha;
                    tag.isClosing = false;
                    return;
                case 43066: // <link="name">
                case 30266: // <LINK>
                    tag.tagType = TagType.Link;
                    tag.isClosing = false;
                    return;
                case 155913: // </link>
                case 143113: // </LINK>
                    tag.tagType = TagType.Link;
                    tag.isClosing = true;
                    return;
                case 275917:  // <align=>
                case 186285:  // <ALIGN>
                    tag.tagType = TagType.Align;
                    tag.isClosing = false;
                    return;
                case 1065846:  // </align>
                case 976214:  // </ALIGN>
                    tag.tagType = TagType.Align;
                    tag.isClosing = true;
                    return;
                case 100149144:  // <gradient>
                case 69403544:    // <GRADIENT>
                    tag.tagType = TagType.Gradient;
                    tag.isClosing = false;
                    return;
                case 371094791:  // </gradient>
                case 340349191:  // </GRADIENT>
                    tag.tagType = TagType.Gradient;
                    tag.isClosing = true;
                    return;
                case 281955:  // <color> <color=#FF00FF> or <color=#FF00FF00>
                case 192323:  // <COLOR=#FF00FF>
                    tag.tagType = TagType.Color;
                    tag.isClosing = false;
                    return;
                case 1071884:  // </color>
                case 982252:  // </COLOR>
                    tag.tagType = TagType.Color;
                    tag.isClosing = true;
                    return;
                case 1983971:  // <cspace=xx.x>
                case 1356515:  // <CSPACE>
                    tag.tagType = TagType.CSpace;
                    tag.isClosing = false;
                    return;
                case 7513474:  // </cspace>
                case 6886018:  // </CSPACE>
                    tag.tagType = TagType.CSpace;
                    tag.isClosing = true;
                    return;
                case 2152041:  // <mspace=xx.x>
                case 1524585:  // <MSPACE>
                    tag.tagType = TagType.Mspace;
                    tag.isClosing = false;
                    return;
                case 7681544:  // </mspace>
                case 7054088:  // </MSPACE>
                    tag.tagType = TagType.Mspace;
                    tag.isClosing = true;
                    return;
                case 2068980:  // <indent=10px> <indent=10em> <indent=50%>
                case 1441524:  // <INDENT>
                    tag.tagType = TagType.Indent;
                    tag.isClosing = false;
                    return;
                case 7598483:  // </indent>
                case 6971027:  // </INDENT>
                    tag.tagType = TagType.Indent;
                    tag.isClosing = true;
                    return;
                case 1109386397:  // <line-indent>
                case -842656867:  // <LINE-INDENT>
                    tag.tagType = TagType.LineIndent;
                    tag.isClosing = false;
                    return;
                case -445537194:  // </line-indent>
                case 1897386838:  // </LINE-INDENT>
                    tag.tagType = TagType.LineIndent;
                    tag.isClosing = true;
                    return;
                case 2246877: // <sprite=x>
                case 1619421: // <SPRITE>
                    tag.tagType = TagType.Sprite;
                    tag.isClosing = false;
                    return;
                case 730022849:  // <lowercase>
                case 514803617:  // <LOWERCASE>
                    tag.tagType = TagType.Lowercase;
                    tag.isClosing = false;
                    return;
                case -1668324918:  // </lowercase>
                case -1883544150:  // </LOWERCASE>
                    tag.tagType = TagType.Lowercase;
                    tag.isClosing = true;
                    return;
                case 13526026:  // <allcaps>
                case 9133802:  // <ALLCAPS>
                    tag.tagType = TagType.AllCaps;
                    tag.isClosing = false;
                    return;
                case 781906058:  // <uppercase>
                case 566686826:  // <UPPERCASE>
                    tag.tagType = TagType.Uppercase;
                    tag.isClosing = false;
                    return;
                case 52232547:  // </allcaps>
                case 47840323:  // </ALLCAPS>
                    tag.tagType = TagType.AllCaps;
                    tag.isClosing = true;
                    return;
                case -1616441709:  // </uppercase>
                case -1831660941:  // </UPPERCASE>
                    tag.tagType = TagType.Uppercase;
                    tag.isClosing = true;
                    return;
                case 766244328:  // <smallcaps>
                case 551025096:  // <SMALLCAPS>
                    tag.tagType = TagType.SmallCaps;
                    tag.isClosing = false;
                    return;
                case -1632103439:  // </smallcaps>
                case -1847322671:  // </SMALLCAPS>
                    tag.tagType = TagType.SmallCaps;
                    tag.isClosing = true;
                    return;
                case 1109349752:  // <line-height=xx.x>
                case -842693512:  // <LINE-HEIGHT>
                    tag.tagType = TagType.LineHeight;
                    tag.isClosing = false;
                    return;
                case -445573839:  // </line-height>
                case 1897350193:  // </LINE-HEIGHT>
                    tag.tagType = TagType.LineHeight;
                    tag.isClosing = true;
                    return;
                case 15115642:  // <noparse>
                case 10723418:  // <NOPARSE>
                    tag.tagType = TagType.NoParse;
                    tag.isClosing = false;
                    return;
                case 53822163:  // </noparse>
                case 49429939:  // </NOPARSE>
                    tag.tagType = TagType.NoParse;
                    tag.isClosing = true;
                    return;
                case 2227963:  // <rotate=xx.x>
                case 1600507:  // <ROTATE=xx.x>
                    tag.tagType = TagType.Rotate;
                    tag.isClosing = false;
                    return;
                case 7757466:  // </rotate>
                case 7130010:  // </ROTATE>
                    tag.tagType = TagType.Rotate;
                    tag.isClosing = true;
                    return;
                case 16034505:  // <voffset>
                case 11642281:  // <VOFFSET>
                    tag.tagType = TagType.VOffset;
                    tag.isClosing = false;
                    return;
                case 54741026:  // </voffset>
                case 50348802:  // </VOFFSET>
                    tag.tagType = TagType.VOffset;
                    tag.isClosing = true;
                    return;
                default:
                    tag.tagType = TagType.Unknown;
                    tag.isClosing = false;
                    return;
            }
        }
        internal static void GetStringValueTagType(int tagHash, ref XMLTag tag)
        {
            switch (tagHash)
            {
                case 764638571:  // default
                case 523367755:  // Default
                    tag.value.stringValue = StringValue.Default;
                    return;
                case 3774683:  // <align=left>
                    tag.value.stringValue = StringValue.left;
                    return;
                case 136703040:  // <align=right>
                    tag.value.stringValue = StringValue.right;
                    return;
                case -458210101:  // <align=center>
                    tag.value.stringValue = StringValue.center;
                    return;
                case -523808257:  // <align=justified>
                    tag.value.stringValue = StringValue.justified;
                    return;
                case 122383428:  // <align=flush>
                    tag.value.stringValue = StringValue.flush;
                    return;
                case 125395:  // <color=red>
                    tag.value.stringValue = StringValue.red;
                    return;
                case -992792864:  // <color=lightblue>
                    tag.value.stringValue = StringValue.lightblue;
                    return;
                case 3573310:  // <color=blue>
                    tag.value.stringValue = StringValue.blue;
                    return;
                case 3680713:  // <color=grey>
                    tag.value.stringValue = StringValue.grey;
                    return;
                case 117905991:  // <color=black>
                    tag.value.stringValue = StringValue.black;
                    return;
                case 121463835:  // <color=green>
                    tag.value.stringValue = StringValue.green;
                    return;
                case 140357351:  // <color=white>
                    tag.value.stringValue = StringValue.white;
                    return;
                case 26556144:  // <color=orange>
                    tag.value.stringValue = StringValue.orange;
                    return;
                case -36881330:  // <color=purple>
                    tag.value.stringValue = StringValue.purple;
                    return;
                case 554054276:  // <color=yellow>
                    tag.value.stringValue = StringValue.yellow;
                    return;                
                default:
                    tag.value.stringValue = StringValue.Unknown;
                    return;
            }
        }
        // <summary>
        /// Extracts a float value from char[] given a start index and length.
        /// </summary>
        /// <param name="chars"></param> The Char[] containing the numerical sequence.
        /// <returns></returns>
        static ParseError ConvertToFloat(ref FixedString128Bytes htmlTag, out float value)
        {
            value = 0;
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
            if (tagCount == 3)//Alpha <#FF> 2 Hex values alpha only
            {
                byte a = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[2]));
                return new Color32(0, 0, 0, a);
            } 
            else if (tagCount == 4) //Color <#FFF> 3 Hex values (short form)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[1]));
                byte g = (byte)(HexToInt((char)htmlTag[2]) * 16 + HexToInt((char)htmlTag[2]));
                byte b = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[3]));

                return new Color32(r, g, b, 255);
            }
            else if (tagCount == 5) //Color <#FFF7> 4 Hex values with alpha (short form)
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[1]));
                byte g = (byte)(HexToInt((char)htmlTag[2]) * 16 + HexToInt((char)htmlTag[2]));
                byte b = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[3]));
                byte a = (byte)(HexToInt((char)htmlTag[4]) * 16 + HexToInt((char)htmlTag[4]));

                return new Color32(r, g, b, a);
            }
            else if (tagCount == 7) //Color <#FF00FF>
            {
                byte r = (byte)(HexToInt((char)htmlTag[1]) * 16 + HexToInt((char)htmlTag[2]));
                byte g = (byte)(HexToInt((char)htmlTag[3]) * 16 + HexToInt((char)htmlTag[4]));
                byte b = (byte)(HexToInt((char)htmlTag[5]) * 16 + HexToInt((char)htmlTag[6]));

                return new Color32(r, g, b, 255);
            }
            else if (tagCount == 9) //Color <#FF00FF00> with alpha
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
    }
}
