namespace Latios.Calligraphics.RichText
{
    public struct RichTextAttribute
    {
        public RichTextTagType tagType;
        public int nameHashCode;
        public int valueHashCode;
        public TagValueType valueType;
        public int valueStartIndex; //bytes position, not char!
        public int valueLength;     //byte length, not char!
        public TagUnitType unitType;
        public static RichTextAttribute Empty => new RichTextAttribute
        {
            tagType = RichTextTagType.INVALID,
            nameHashCode = 0,
            valueHashCode = 0,
            valueStartIndex = 0,
            valueLength = 0,
            valueType = TagValueType.None,
            unitType = TagUnitType.Pixels,
        };
    }
}

