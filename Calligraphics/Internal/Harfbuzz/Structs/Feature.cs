using System.Runtime.InteropServices;

namespace Latios.Calligraphics.HarfBuzz
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Feature
    {
        public uint tag;
        public uint value;
        public uint start;
        public uint end;
        //public Blob(string feature)
        //{
        //    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(feature + "\0"); //IMPORTANT! interop with c++ requieres null terminated char*
        //    unsafe
        //    {
        //        Debug.Log($"Last bytes is NULL? {bytes[^1] == 0} {bytes[^1]}");
        //        fixed (byte* text = bytes)
        //        {
        //            bool result = HB.hb_feature_from_string(text, -1, out this);
        //            Debug.Log(System.Text.Encoding.UTF8.GetString(text, bytes.Length));
        //        }
        //    }
        //}
        public Feature(string feature)
        {
            bool result = Harfbuzz.hb_feature_from_string(feature, -1, out this);
        }
        public Feature(uint tag, uint value, uint start, uint end)
        {
            this.tag = tag;
            this.value = value;
            this.start = start;
            this.end = end;
        }
        
    }
}
