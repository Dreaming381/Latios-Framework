namespace Latios.Calligraphics.RichText
{
    // Often referred to as "attributes" though we refrain from that term due to aliasing
    // with both graphics attributes and C# language attributes.
    internal struct RichTextTagIdentifier
    {
        public int             nameHashCode;
        public int             valueHashCode;
        public int             valueStartIndex;  //bytes position, not char!
        public int             valueLength;  //byte length, not char!
        public TagUnitType     unitType;
        public TagValueType    valueType;

        public static RichTextTagIdentifier Empty => new RichTextTagIdentifier
        {
            nameHashCode    = 0,
            valueHashCode   = 0,
            valueStartIndex = 0,
            valueLength     = 0,
            valueType       = TagValueType.None,
            unitType        = TagUnitType.Pixels,
        };
    }
}

