using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawState
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool path_open;

        public float path_start_x;
        public float path_start_y;

        public float current_x;
        public float current_y;

        /*< private >*/
        int reserved1;
        int reserved2;
        int reserved3;
        int reserved4;
        int reserved5;
        int reserved6;
        int reserved7;
    }
}
