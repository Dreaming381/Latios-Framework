namespace Latios.Calligraphics.HarfBuzz
{
    internal enum AxisTag : uint
    {
        ITALIC = ('i' << 24) | ('t' << 16) | ('a' << 8) | 'l', //better would be HB.HB_TAG('i', 't', 'a', 'l'), but this does not work in C Sharp
        OPTICAL_SIZE = ('o' << 24) | ('p' << 16) | ('s' << 8) | 'z',
        SLANT = ('s' << 24) | ('l' << 16) | ('n' << 8) | 't',
        WIDTH = ('w' << 24) | ('d' << 16) | ('t' << 8) | 'h',
        WEIGHT = ('w' << 24) | ('g' << 16) | ('h' << 8) | 't',
    }
}