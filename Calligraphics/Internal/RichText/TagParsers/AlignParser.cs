using Unity.Collections;

namespace Latios.Calligraphics.RichText.Parsing
{
    /// <summary>
    /// Rules:  If no end tag, then end tag is the end of the string.  If end tag, reverts to previous
    /// </summary>
    internal static class AlignParser
    {
        public const string tagName = "align";

        internal static bool TryParseTagTypeAndValue(in FixedString64Bytes tagValue, ref RichTextTag tag)
        {
            tag.tagType = RichTextTagType.Align;

            if (tag.isEndTag && tagValue == default)
            {
                return true;
            }

            if (tagValue == "left")
            {
                tag.alignType = AlignType.Left;
            }
            else if (tagValue == "center")
            {
                tag.alignType = AlignType.Center;
            }
            else if (tagValue == "right")
            {
                tag.alignType = AlignType.Right;
            }
            else if (tagValue == "justified")
            {
                tag.alignType = AlignType.Justified;
            }
            else if (tagValue == "flush")
            {
                tag.alignType = AlignType.Flush;
            }
            else
            {
                return false;
            }

            return true;
        }
    }
}

