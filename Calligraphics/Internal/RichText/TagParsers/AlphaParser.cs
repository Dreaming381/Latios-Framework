using Unity.Collections;

namespace Latios.Calligraphics.RichText.Parsing
{
    /// <summary>
    /// Rules:  If no end tag, then end tag is the end of the string.  If end tag, reverts to previous
    /// </summary>
    internal static class AlphaParser
    {
        private const byte m_hash = (byte)'#';

        public const string tagName = "alpha";

        internal static bool TryParseTagTypeAndValue(in FixedString64Bytes tagValue, ref RichTextTag tag)
        {
            tag.tagType = RichTextTagType.Alpha;

            if (tag.isEndTag && tagValue == default)
            {
                return true;
            }

            //TODO:  More validations
            if (tagValue.Length > 3)
                return false;
            if (tagValue[0] != m_hash)
                return false;

            tag.alpha = (byte)((tagValue[1] % 32 + 9) % 25 * 16 + (tagValue[2] % 32 + 9) % 25);

            return true;
        }
    }
}

