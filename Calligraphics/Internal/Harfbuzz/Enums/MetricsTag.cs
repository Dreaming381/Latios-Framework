namespace Latios.Calligraphics.HarfBuzz
{
    internal enum MetricTag
    {
        HORIZONTAL_ASCENDER = ('h' << 24) | ('a' << 16) | ('s' << 8) | 'c', //better would be HB.HB_TAG('c', 'p', 'c', 't'), but this does not work in C Sharp,
        HORIZONTAL_DESCENDER = ('h' << 24) | ('d' << 16) | ('s' << 8) | 'c',
        HORIZONTAL_LINE_GAP = ('h' << 24) | ('l' << 16) | ('g' << 8) | 'p',
        HORIZONTAL_CLIPPING_ASCENT = ('h' << 24) | ('c' << 16) | ('l' << 8) | 'a',
        HORIZONTAL_CLIPPING_DESCENT = ('h' << 24) | ('c' << 16) | ('l' << 8) | 'd',
        VERTICAL_ASCENDER = ('v' << 24) | ('a' << 16) | ('s' << 8) | 'c',
        VERTICAL_DESCENDER = ('v' << 24) | ('d' << 16) | ('s' << 8) | 'c',
        VERTICAL_LINE_GAP = ('v' << 24) | ('l' << 16) | ('g' << 8) | 'p',
        HORIZONTAL_CARET_RISE = ('h' << 24) | ('c' << 16) | ('r' << 8) | 's',
        HORIZONTAL_CARET_RUN = ('h' << 24) | ('c' << 16) | ('r' << 8) | 'n',
        HORIZONTAL_CARET_OFFSET = ('h' << 24) | ('c' << 16) | ('o' << 8) | 'f',
        VERTICAL_CARET_RISE = ('v' << 24) | ('c' << 16) | ('r' << 8) | 's',
        VERTICAL_CARET_RUN = ('v' << 24) | ('c' << 16) | ('r' << 8) | 'n',
        VERTICAL_CARET_OFFSET = ('v' << 24) | ('c' << 16) | ('o' << 8) | 'f',
        X_HEIGHT = ('x' << 24) | ('h' << 16) | ('g' << 8) | 't',
        CAP_HEIGHT = ('c' << 24) | ('p' << 16) | ('h' << 8) | 't',
        SUBSCRIPT_EM_X_SIZE = ('s' << 24) | ('b' << 16) | ('x' << 8) | 's',
        SUBSCRIPT_EM_Y_SIZE = ('s' << 24) | ('b' << 16) | ('y' << 8) | 's',
        SUBSCRIPT_EM_X_OFFSET = ('s' << 24) | ('b' << 16) | ('x' << 8) | 'o',
        SUBSCRIPT_EM_Y_OFFSET = ('s' << 24) | ('b' << 16) | ('y' << 8) | 'o',
        SUPERSCRIPT_EM_X_SIZE = ('s' << 24) | ('p' << 16) | ('x' << 8) | 's',
        SUPERSCRIPT_EM_Y_SIZE = ('s' << 24) | ('p' << 16) | ('y' << 8) | 's',
        SUPERSCRIPT_EM_X_OFFSET = ('s' << 24) | ('p' << 16) | ('x' << 8) | 'o',
        SUPERSCRIPT_EM_Y_OFFSET = ('s' << 24) | ('p' << 16) | ('y' << 8) | 'o',
        STRIKEOUT_SIZE = ('s' << 24) | ('t' << 16) | ('r' << 8) | 's',
        STRIKEOUT_OFFSET = ('s' << 24) | ('t' << 16) | ('r' << 8) | 'o',
        UNDERLINE_SIZE = ('u' << 24) | ('n' << 16) | ('d' << 8) | 's',
        UNDERLINE_OFFSET = ('u' << 24) | ('n' << 16) | ('d' << 8) | 'o',
    }
}
