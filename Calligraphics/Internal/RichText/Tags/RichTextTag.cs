using System.Runtime.InteropServices;
using UnityEngine;

namespace Latios.Calligraphics.RichText
{
    [StructLayout(LayoutKind.Explicit)]
    internal partial struct RichTextTag
    {
        /// <summary>
        /// The behavior that the tag will indicate
        /// </summary>
        [FieldOffset(0)]
        public RichTextTagType tagType;

        /// <summary>
        /// Offset from the start of the start tag to the start of the scope
        /// </summary>
        [FieldOffset(1)]
        public sbyte startScopeOffset;
        /// <summary>
        /// Offset from the end of the start of the end tag to the end of the scope
        /// </summary>
        [FieldOffset(2)]
        public sbyte endTagOffset;

        [FieldOffset(3)]
        public bool isEndTag;

        /// <summary>
        /// The start index of the end tag
        /// </summary>
        [FieldOffset(4)]
        public int endTagStartIndex;
        /// <summary>
        /// The start index of the start tag
        /// </summary>
        [FieldOffset(8)]
        public int startTagStartIndex;

        /// <summary>
        /// The offset from the current index to the next influencing tag
        /// </summary>
        [FieldOffset(12)]
        public int nextInfluenceTagOffset;

        public int endTagEndIndex
        {
            get { return endTagStartIndex + endTagOffset; }
            set { endTagOffset = (sbyte)(endTagStartIndex - value); }
        }

        public int scopeStartIndex
        {
            get { return startTagStartIndex + startScopeOffset; }
            set { startScopeOffset = (sbyte)(value - startTagStartIndex); }
        }

        public int scopeEndIndex
        {
            get => endTagStartIndex - 1;
            set => endTagStartIndex = value + 1;
        }

        #region ColorTag
        [FieldOffset(16)]
        public Color32 color;
        #endregion

        #region AlphaTag
        [FieldOffset(16)]
        public byte alpha;
        #endregion

        #region GradientTag
        [FieldOffset(16)]
        public Color32 blColor;
        [FieldOffset(20)]
        public Color32 tlColor;
        [FieldOffset(24)]
        public Color32 trColor;
        [FieldOffset(28)]
        public Color32 brColor;
        #endregion

        #region AlignTag
        [FieldOffset(16)]
        public AlignType alignType;
        #endregion

        #region CharacterSpacingTag
        [FieldOffset(16)]
        public short spacing;
        #endregion

        #region FontWeightTag
        [FieldOffset(16)]
        public short fontWeight;
        #endregion

        #region FontSizeTag
        [FieldOffset(16)]
        public short fontSize;
        #endregion
    }
}

