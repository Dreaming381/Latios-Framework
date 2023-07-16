using Unity.Collections;
using Unity.Entities;

namespace Latios.Calligraphics.RichText.Parsing
{
    internal static class RichTextParser
    {
        internal static void ParseTags(ref NativeList<RichTextTag> richTextTags, in DynamicBuffer<CalliByte> calliBytes)
        {
            richTextTags.Clear();

            var enumerator       = new CalliString(calliBytes).GetEnumerator();
            int currentCharIndex = -1;
            while (enumerator.MoveNext())
            {
                currentCharIndex++;
                if (TryParseTag(ref enumerator, currentCharIndex, out var tagName, out var tagValue, out var endIndex, out var isEndTag))
                {
                    RichTextTag tag = new RichTextTag
                    {
                        isEndTag               = isEndTag,
                        nextInfluenceTagOffset = 1,
                        startTagStartIndex     = currentCharIndex,
                        startScopeOffset       = (sbyte)(endIndex - currentCharIndex + 1),
                        endTagOffset           = -1
                    };
                    //Align
                    if (tagName == AlignParser.tagName && AlignParser.TryParseTagTypeAndValue(tagValue, ref tag))
                    {
                        richTextTags.Add(tag);
                    }
                    //Alpha
                    else if (tagName == AlphaParser.tagName && AlphaParser.TryParseTagTypeAndValue(tagValue, ref tag))
                    {
                        richTextTags.Add(tag);
                    }
                    //Color
                    else if (tagName == ColorParser.tagName && ColorParser.TryParseTagTypeAndValue(tagValue, ref tag))
                    {
                        richTextTags.Add(tag);
                    }
                    else
                    {
                        tag.tagType  = RichTextTagType.INVALID;
                        tag.isEndTag = false;
                        richTextTags.Add(tag);
                    }

                    currentCharIndex = endIndex;
                }
            }

            //Link start and end tags
            for (int i = richTextTags.Length - 1; i >= 0; i--)
            {
                var endTag = richTextTags[i];

                if (endTag.isEndTag)
                {
                    //Find the start tag that matches
                    int nestCount = 0;
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var startTag = richTextTags[j];
                        if (startTag.isEndTag && startTag.tagType == endTag.tagType)
                        {
                            nestCount++;
                        }
                        if (!startTag.isEndTag && startTag.tagType == endTag.tagType)
                        {
                            if (nestCount > 0)
                            {
                                nestCount--;
                            }
                            else
                            {
                                startTag.endTagStartIndex = endTag.startTagStartIndex;
                                startTag.endTagOffset     = (sbyte)(endTag.startScopeOffset - 1);
                                richTextTags[j]           = startTag;
                                break;
                            }
                        }
                    }
                    // Todo: Add RemoveIfEnumerator to Core and switch this to that using forward iteration.
                    // That approach also needs same-type stack batching since color tags may be spammed without end tags.
                    richTextTags.RemoveAt(i);
                }
            }
        }

        internal static bool TryParseTag(ref CalliString.Enumerator enumerator,
                                         int currentIndex,
                                         out FixedString32Bytes tagName,
                                         out FixedString64Bytes tagValue,
                                         out int endIndex,
                                         out bool isEndTag)
        {
            var state = ParserState.FindingTagName;
            tagName   = new FixedString32Bytes();
            tagValue  = new FixedString64Bytes();
            endIndex  = -1;
            isEndTag  = false;

            bool valueEnclosedInQuotes = false;

            var copyEnumerator = enumerator;
            int i              = currentIndex;

            while (true)
            {
                var c = copyEnumerator.Current;

                switch (state)
                {
                    case ParserState.FindingTagName:
                        if (c == '<')
                        {
                            if (!copyEnumerator.MoveNext())
                            {
                                return false;
                            }
                            i++;

                            if (copyEnumerator.Current == '/')
                            {
                                isEndTag = true;
                                if (!copyEnumerator.MoveNext())
                                {
                                    return false;
                                }
                                i++;
                            }

                            while (true)
                            {
                                c = copyEnumerator.Current;
                                if (c == ' ')
                                {
                                    state = ParserState.FindingAssignment;
                                    break;
                                }
                                else if (c == '=')
                                {
                                    state = ParserState.FindingAssignment;
                                    copyEnumerator.MovePrevious();
                                    i--;
                                    break;
                                }
                                else if (c == '>')
                                {
                                    endIndex = i;
                                    if (tagName.Length > 0)
                                    {
                                        enumerator = copyEnumerator;
                                    }
                                    return tagName.Length > 0;
                                }
                                else if (c == '/')
                                {
                                    return false;
                                }
                                else
                                {
                                    tagName.Append(c);
                                }

                                if (!copyEnumerator.MoveNext())
                                {
                                    return false;
                                }
                                i++;
                            }
                        }
                        else
                        {
                            return false;
                        }
                        break;
                    case ParserState.FindingAssignment:
                        if (c == '=')
                        {
                            state = ParserState.FindingValue;
                            if (!copyEnumerator.MoveNext())
                            {
                                return false;
                            }
                            if (copyEnumerator.Current == '"')
                            {
                                i++;
                                valueEnclosedInQuotes = true;
                            }
                            else
                            {
                                copyEnumerator.MovePrevious();
                            }
                        }
                        else if (c != ' ')
                        {
                            return false;
                        }
                        break;
                    case ParserState.FindingValue:
                        //for (int j = i; j < calliBytes.Length; j++)
                        while (true)
                        {
                            c = copyEnumerator.Current;
                            if (c == '"' && valueEnclosedInQuotes)
                            {
                                state = ParserState.FindingTagClose;
                                break;
                            }
                            if (c == '>')
                            {
                                endIndex   = i;
                                enumerator = copyEnumerator;
                                return true;
                            }
                            else
                            {
                                tagValue.Append(c);
                            }

                            if (!copyEnumerator.MoveNext())
                            {
                                return false;
                            }
                            i++;
                        }
                        break;
                    case ParserState.FindingTagClose:
                        if (c == '>')
                        {
                            endIndex   = i;
                            enumerator = copyEnumerator;
                            return true;
                        }
                        else if (c != ' ')
                        {
                            return false;
                        }
                        break;
                }

                if (!copyEnumerator.MoveNext())
                {
                    return false;
                }
                i++;
            }
        }

        private enum ParserState
        {
            FindingTagName,
            FindingAssignment,
            FindingValue,
            FindingTagClose
        }
    }
}

