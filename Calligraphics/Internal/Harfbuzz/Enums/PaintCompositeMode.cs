namespace Latios.Calligraphics.HarfBuzz
{
    internal enum PaintCompositeMode
    {
        CLEAR,      // r = 0
        SRC,        // r = s
        DEST,       // r = d
        SRC_OVER,   // r = s + (1-sa)*d
        DEST_OVER,  // r = d + (1-da)*s
        SRC_IN,     // r = s * da
        DEST_IN,    // r = d * sa
        SRC_OUT,    // r = s * (1-da)
        DEST_OUT,   // r = d * (1-sa)
        SRC_ATOP,   // r = s*da + d*(1-sa)
        DEST_ATOP,  // r = d*sa + s*(1-da)
        XOR,        // r = s*(1-da) + d*(1-sa)
        PLUS,       // r = min(s + d, 1)
        //MODULATE,      // r = s*d
        SCREEN,     // r = s + d - s*d
        OVERLAY,    // multiply or screen, depending on destination
        DARKEN,     // rc = s + d - max(s*da, d*sa), ra = kSrcOver
        LIGHTEN,    // rc = s + d - min(s*da, d*sa), ra = kSrcOver
        COLOR_DODGE,// s / (1 - d)      brighten destination to reflect source
        COLOR_BURN, // 1 - (1 - s) / d darken destination to reflect source
        HARD_LIGHT, // multiply or screen, depending on source
        SOFT_LIGHT, // lighten or darken, depending on source
        DIFFERENCE, // rc = s + d - 2*(min(s*da, d*sa)), ra = kSrcOver
        EXCLUSION,  // rc = s + d - 2*(min(s*da, d*sa)), ra = kSrcOver
        MULTIPLY,   // r = s*(1-da) + d*(1-sa) + s*d
        HSL_HUE,        // hue of source with saturation and luminosity of destination
        HSL_SATURATION, // saturation of source with hue and luminosity of destination
        HSL_COLOR,      // hue and saturation of source with luminosity of destination
        HSL_LUMINOSITY  // luminosity of source with hue and saturation of destination
    }
}
