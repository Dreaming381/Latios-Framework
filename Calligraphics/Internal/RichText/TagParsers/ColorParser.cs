using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.RichText.Parsing
{
    /// <summary>
    /// Rules:  If no end tag, then end tag is the end of the string.  If end tag, reverts to previous
    /// </summary>
    internal static class ColorParser
    {
        private const byte m_hash = (byte)'#';

        public const string tagName = "color";

        internal static unsafe bool TryParseTagTypeAndValue(in FixedString64Bytes tagValue, ref RichTextTag tag)
        {
            tag.tagType = RichTextTagType.Color;

            if (tag.isEndTag && tagValue == default)
            {
                return true;
            }

            var bytearray = stackalloc byte[3];

            if (tagValue.Length == 7 && tagValue[0] == m_hash)
            {
                for (int i = 1, j = 1; i < ((tagValue.Length - 1) / 2); i++, j += 2)
                    bytearray[i] = (byte)((tagValue[j] % 32 + 9) % 25 * 16 + (tagValue[j + 1] % 32 + 9) % 25);

                tag.color = new Color32(bytearray[1], bytearray[2], bytearray[3], 255);
                return true;
            }

            if (tagValue == "red")
            {
                tag.color = Color.red;
                return true;
            }
            if (tagValue == "black")
            {
                tag.color = Color.black;
                return true;
            }
            if (tagValue == "blue")
            {
                tag.color = Color.blue;
                return true;
            }
            if (tagValue == "green")
            {
                tag.color = Color.green;
                return true;
            }
            if (tagValue == "cyan")
            {
                tag.color = Color.cyan;
                return true;
            }
            if (tagValue == "gray")
            {
                tag.color = Color.gray;
                return true;
            }
            if (tagValue == "magenta")
            {
                tag.color = Color.magenta;
                return true;
            }
            if (tagValue == "white")
            {
                tag.color = Color.white;
                return true;
            }
            if (tagValue == "yellow")
            {
                tag.color = Color.yellow;
                return true;
            }

            return false;
        }
    }
}

