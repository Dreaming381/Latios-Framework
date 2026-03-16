using System.Runtime.InteropServices;
using Latios.Calligraphics.HarfBuzz;
using UnityEngine.TextCore;

namespace Latios.Calligraphics
{
    /// <summary> Dimensions of glyph according to harfbuzz definition (y is top to bottom ). Invert height for use in a coordinate systems that grows up.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphExtents
    {        
        public int x_bearing;   //Distance from the x-origin to the left extremum of the glyph.
        public int y_bearing;   //Distance from the top extremum of the glyph to the y-origin.
        public int width;       //Distance from the left extremum of the glyph to the right extremum.
        public int height;      //Distance from the top extremum of the glyph to the bottom extremum.           
        public void InvertY()
        {
            height = -height; //Invert height for use in a coordinate systems that grows up.</ summary >
        }
        public GlyphRect GetPaddedAtlasRect(int x, int y, int padding)
        {
            var doublePadding = 2 * padding;
            return new GlyphRect(x, y, width + doublePadding, height + doublePadding);
        }
        public BBox ClipRect
        {
            get { return new BBox ( x_bearing, y_bearing - height, x_bearing + width, y_bearing); }
        }

        public override string ToString()
        {
            return $"Bearing (x,y) {x_bearing}, {y_bearing} width {width} height {height}";
        }
    }    
}

