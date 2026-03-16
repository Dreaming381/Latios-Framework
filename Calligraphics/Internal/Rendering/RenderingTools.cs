using Unity.Rendering;

namespace Latios.Calligraphics
{
    internal static class RenderingTools
    {
        public static void SetSubMesh(int glyphCount, ref MaterialMeshInfo mmi)
        {
            switch (glyphCount)
            {
                case int _ when glyphCount <= 4:
                    mmi.SubMesh = 0; break;
                case int _ when glyphCount <= 8:
                    mmi.SubMesh = 1; break;
                case int _ when glyphCount <= 16:
                    mmi.SubMesh = 2; break;
                case int _ when glyphCount <= 24:
                    mmi.SubMesh = 3; break;
                case int _ when glyphCount <= 32:
                    mmi.SubMesh = 4; break;
                case int _ when glyphCount <= 64:
                    mmi.SubMesh = 5; break;
                case int _ when glyphCount <= 256:
                    mmi.SubMesh = 6; break;
                case int _ when glyphCount <= 1024:
                    mmi.SubMesh = 7; break;
                case int _ when glyphCount <= 4096:
                    mmi.SubMesh = 8; break;
                case int _ when glyphCount <= 16384:
                    mmi.SubMesh = 9; break;
                default:
                    mmi.SubMesh = 9;
                    UnityEngine.Debug.LogWarning("Glyphs in RenderGlyph buffer exceeds max capacity of 16384 and will be truncated.");
                    break;
            }
        }
    }
}

