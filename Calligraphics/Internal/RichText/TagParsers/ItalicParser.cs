using Unity.Collections;

namespace Latios.Calligraphics.RichText.Parsing
{
    /// <summary>
    /// Rules:  If no end tag, then end tag is the end of the string.  If end tag, reverts to previous
    /// </summary>
    internal static class ItalicParser
    {
        public const string tagName = "i";

        internal static bool TryParseTagTypeAndValue(in FixedString64Bytes tagValue, ref RichTextTag tag)
        {
            tag.tagType = RichTextTagType.Italic;
            if (tagValue != default)
            {
                return false;
            }
            return true;
        }
    }
}

