using System;
using Latios.Calligraphics.HarfBuzz.Bitmap;
using Unity.Collections;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Face : IDisposable
    {
        public IntPtr ptr;

        //cache a couple of face meta data to avoid fetching them upon every face access
        public SDFOrientation sdfOrientation;
        internal bool hasColor;
        public uint GlyphCount => Harfbuzz.hb_face_get_glyph_count(ptr);
        public bool HasVarData => Harfbuzz.hb_ot_var_has_data(ptr);
        public uint AxisCount => Harfbuzz.hb_ot_var_get_axis_count(ptr);
        public uint NamedInstanceCount => Harfbuzz.hb_ot_var_get_named_instance_count(ptr);        

        public Face(Blob blob, int index)
        {
            sdfOrientation= default;
            hasColor = default;
            ptr = Harfbuzz.hb_face_create(blob.ptr, (uint)index);
            hasColor = HasCOLR() || HasColorBitmap();
            sdfOrientation = HasTrueTypeOutlines() ? SDFOrientation.TRUETYPE : SDFOrientation.POSTSCRIPT;
        }
        public uint UnitsPerEM
        {
            get { return Harfbuzz.hb_face_get_upem(ptr); }
            set { Harfbuzz.hb_face_set_upem(ptr, value); }
        }
        public void GetSizeParams(out uint design_size, out uint subfamily_id, out uint subfamily_name_id, out uint range_start, out uint range_end)
        {
            Harfbuzz.hb_ot_layout_get_size_params(ptr, out design_size, out subfamily_id, out subfamily_name_id, out range_start, out range_end);
        }

        public FixedString128Bytes GetName(NameID name_id, Language language)
        {
            var result = new FixedString128Bytes();
            var textSize = (uint)result.Capacity;
            unsafe
            {
                Harfbuzz.hb_ot_name_get_utf8(ptr, name_id, language, ref textSize, result.GetUnsafePtr());
            }
            result.Length = (int)textSize;
            return result;
        }

        public NameID GetNamedInstanceSubFamilyNameID(int instanceIndex)
        {
            return Harfbuzz.hb_ot_var_named_instance_get_subfamily_name_id(ptr, (uint)instanceIndex);
        }
        public NameID GetNamedInstancePostscriptNameID(int instanceIndex)
        {
            return Harfbuzz.hb_ot_var_named_instance_get_subfamily_name_id(ptr, (uint)instanceIndex);
        }

        public void GetNamedInstanceDesignCoords(int instanceIndex, ref Span<float> coords, out uint coordLength)
        {
            coordLength = (uint)coords.Length;            
            uint axisCount = default;
            unsafe
            {
                fixed (float* spanPtr = coords)
                {
                    axisCount = Harfbuzz.hb_ot_var_named_instance_get_design_coords(ptr, (uint)instanceIndex, ref coordLength, spanPtr);
                }
            }
        }
        public void GetAxisInfos(int startOffset, int offset, ref Span<AxisInfo> axisInfos, out uint axisCount)
        {
            axisCount = (uint)axisInfos.Length;
            uint axisCount2 = default;
            unsafe
            {
                fixed (AxisInfo* axisInfoPtr = axisInfos)
                {
                    axisCount2 = Harfbuzz.hb_ot_var_get_axis_infos(ptr, (uint)startOffset, ref axisCount, axisInfoPtr);
                }
            }
        }

        public bool FindAxisInfo(AxisTag axisTag, out AxisInfo axisInfo)
        {
            return Harfbuzz.hb_ot_var_find_axis_info(ptr, axisTag, out axisInfo);
        }        

        bool HasReferenceTable(uint HB_TAG)
        {
            var blob = Harfbuzz.hb_face_reference_table(ptr, HB_TAG);
            var tableLength = Harfbuzz.hb_blob_get_length(blob);
            Harfbuzz.hb_blob_destroy(blob);
            return tableLength > 0;
        }
        public bool HasColorBitmap()
        {
            return HasReferenceTable(Harfbuzz.HB_TAG('C', 'B', 'D', 'T')) || 
                   HasReferenceTable(Harfbuzz.HB_TAG('s', 'b', 'i', 'x'));
        }
        public bool HasSVG()
        {
            return HasReferenceTable(Harfbuzz.HB_TAG('S', 'V', 'G', ' '));
        }
        public bool HasCOLR()
        {
            return HasReferenceTable(Harfbuzz.HB_TAG('C', 'O', 'L', 'R'));
        }
        public bool HasTrueTypeOutlines()
        {
            return HasReferenceTable(Harfbuzz.HB_TAG('g', 'l', 'y', 'f'));
        }
        public bool HasPostScriptOutlines()
        {
            return HasReferenceTable(Harfbuzz.HB_TAG('C', 'F', 'F', ' ')) || 
                   HasReferenceTable(Harfbuzz.HB_TAG('C', 'F', 'F', '2')); 
        }
        public bool IsImmutable() => Harfbuzz.hb_face_is_immutable(ptr);
        public void MakeImmutable()
        {
            Harfbuzz.hb_face_make_immutable(ptr);
        }
        public void Dispose()
        {
            Harfbuzz.hb_face_destroy(ptr);
        }
    }
}
