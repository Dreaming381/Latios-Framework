namespace Latios.Calligraphics.RichText
{
    internal enum TagType : byte
    {
        Hyperlink,
        Align,
        AllCaps,
        Alpha,
        Bold,
        Br,
        Color,
        CSpace,
        Font,
        FontWeight,
        FontWidth,
        Fraction,
        Gradient,
        Italic,
        Indent,
        LineHeight,
        LineIndent,
        Link,
        Lowercase,
        Mark,
        Mspace,
        NoBr,
        NoParse,
        Rotate,
        Strikethrough,
        Size,
        SmallCaps,
        Space,
        Sprite,
        Style,
        Subscript,
        Superscript,
        Underline,
        Uppercase,
        VOffset,
        Unknown // Not a real tag, used to indicate an error

        // margin, pos, will not be supported
    }
}
