using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DrawState
    {
        private int path_open; //hb_bool_t is 4 bytes!

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

        public bool PathOpen => path_open != 0;
    }
}
