namespace Latios.Calligraphics.HarfBuzz
{
    internal enum LayoutBaselineTag
    {
        ROMAN = ('r' << 24) | ('o' << 16) | ('m' << 8) | 'n', //better would be HB.HB_TAG('c', 'p', 'c', 't'), but this does not work in C Sharp,
        HANGING = ('h' << 24) | ('a' << 16) | ('n' << 8) | 'g',
        IDEO_FACE_BOTTOM_OR_LEFT = ('i' << 24) | ('c' << 16) | ('f' << 8) | 'b',
        IDEO_FACE_TOP_OR_RIGHT = ('i' << 24) | ('c' << 16) | ('f' << 8) | 't',
        IDEO_FACE_CENTRAL = ('I' << 24) | ('c' << 16) | ('f' << 8) | 'c',
        IDEO_EMBOX_BOTTOM_OR_LEFT = ('i' << 24) | ('d' << 16) | ('e' << 8) | 'o',
        IDEO_EMBOX_TOP_OR_RIGHT = ('i' << 24) | ('d' << 16) | ('t' << 8) | 'p',
        IDEO_EMBOX_CENTRAL = ('I' << 24) | ('d' << 16) | ('t' << 8) | 'p',
        MATH = ('m' << 24) | ('a' << 16) | ('t' << 8) | 'h',
    }
}
