using UnityEngine;

namespace Latios.Calligraphics.RichText
{
    internal enum StringValue : byte
    {
        Unknown, // Not a real tag, used to indicate unknown string, which needs to be fetched from calliBytesRaw
        Default,
        red,
        lightblue,
        blue,
        grey,
        black,
        green,
        white,
        orange,
        purple,
        yellow,
        left,
        right,
        center,
        justified,
        flush
    }
}
