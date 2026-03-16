namespace Latios.Calligraphics.HarfBuzz
{
    internal enum BufferFlag :uint
    {
        DEFAULT = 0x00000000u,
        BOT = 0x00000001u, /* Beginning-of-text */
        EOT = 0x00000002u, /* End-of-text */
        PRESERVE_DEFAULT_IGNORABLES = 0x00000004u,
        REMOVE_DEFAULT_IGNORABLES = 0x00000008u,
        DO_NOT_INSERT_DOTTED_CIRCLE = 0x00000010u,
        VERIFY = 0x00000020u,
        PRODUCE_UNSAFE_TO_CONCAT = 0x00000040u,
        PRODUCE_SAFE_TO_INSERT_TATWEEL = 0x00000080u,

        DEFINED = 0x000000FFu
    }
}
