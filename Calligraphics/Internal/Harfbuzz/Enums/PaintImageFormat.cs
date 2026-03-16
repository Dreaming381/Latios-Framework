namespace Latios.Calligraphics.HarfBuzz
{
    internal enum PaintImageFormat
    {
        PNG = ('p' << 24) | ('n' << 16) | ('g' << 8) | ' ', //better would be HB.HB_TAG('c', 'p', 'c', 't'), but this does not work in C Sharp,
        SVG = ('s' << 24) | ('v' << 16) | ('g' << 8) | ' ',
        BGRA = ('B' << 24) | ('G' << 16) | ('R' << 8) | 'A',
    }
}
