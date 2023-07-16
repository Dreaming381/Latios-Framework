using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

// If you are providing a frontend, listen up!
// Your objective is to query for all entities with two components:
// RenderGlyph and TextRenderControl.
// Write to them in PresentationSystemGroup before KinemationRenderUpdateSuperSystem
// which is inside UpdatePresentationSystemGroup.
// If you change anything on any of the RenderGlyphs, or resize the buffer,
// you must add the Dirty flag to TextRenderControl. The other flags are optional
// and are simply there so you can defer some calculations to the GPU (they're effectively free).
// For baking, you must bake whatever data you require to populate the RenderGlyphs.
// In the namespace Latios.Kinemation.TextBackend.Authoring, call the IBaker extension
// method BakeTextBackendMeshAndMaterial() to set up the rendering side. This will add
// the required RenderGlyph and TextRenderControl components as well as internal rendering
// components.
//
// How it works:
// In the Kinemation Resources directory, there is a special Mesh baked which contains dummy
// vertex attributes, and has multiple submeshes. Each submesh contains the triangle vertex indices
// for various glyph counts. Inside KienmationRenderUpdateSuperSystem, for any TextRenderControl
// with the dirty flag, Kinemation will set the appropriate submesh on the entity's MaterialMeshInfo.
// It will also recalculate the RenderBounds (local-space bounds).
// In the culling loop, Kinemation will update any visible text glyphs to an upload GraphicsBuffer.
// The uploaded glyphs need to be transferred to a persistent GraphicsBuffer which is done via
// a ComputeShader. Because this ComputeShader is primarily a memory-transfer operation, most of the
// time the GPU is doing NOPs waiting on memory. We try to replace those NOPs with useful calculations
// that the CPU would otherwise have to do, such as color space conversion. The culling loop will also
// set the TextShaderIndex material property.
// Lastly, the Latios Text Shader Graph node will parse the glyphs from the persistent buffer based on
// the vertex ID. If the vertex ID does not map to any glyph (because the string is short), the node
// will instead return a vertex that the GPU will discard based on the same mechanism VFX Graph uses.
// Otherwise, the returned vertex will contain all the information the vertex needs to provide to
// TextMeshPro. The glyph stays compressed in its 96 byte form on the GPU and is decoded directly in
// the vertex shader.

namespace Latios.Kinemation.TextBackend
{
    [MaterialProperty("_latiosTextGlyphBase")]
    public struct TextShaderIndex : IComponentData
    {
        public uint firstGlyphIndex;
        public uint glyphCount;
    }

    public struct RenderGlyph : IBufferElementData
    {
        public float2 blPosition;
        public float2 trPosition;
        public float2 blUVA;
        public float2 trUVA;

        public float2 blUVB;
        public float2 tlUVB;
        public float2 trUVB;
        public float2 brUVB;

        // Assign a UnityEngine.Color32 to these.
        public PackedColor blColor;
        public PackedColor tlColor;
        public PackedColor trColor;
        public PackedColor brColor;

        public uint  unicode;
        public float shear;  // Should be equal to topLeft.x - bottomLeft.x
        public float scale;
        public float rotationCCW;  // Radians
    }

    public struct PackedColor
    {
        public uint packedColor;
        
        public uint a
        {
            get => packedColor >> 24;
            set => packedColor = (packedColor & 0x00ffffff) | (value << 24);
        }
        
        public uint b
        {
            get => (packedColor >> 16) & 0xff;
            set => packedColor = (packedColor & 0xff00ffff) | (value << 16);
        }
        
        public uint g
        {
            get => (packedColor >> 8) & 0xff;
            set => packedColor = (packedColor & 0xffff00ff) | (value << 8);
        }
        
        public uint r
        {
            get => packedColor & 0xff;
            set => packedColor = (packedColor & 0xffffff00) | value;
        }

        public static implicit operator PackedColor(UnityEngine.Color32 unityColor)
        {
            uint result                           = (uint)unityColor.a << 24;
            result                               |= (uint)unityColor.b << 16;
            result                               |= (uint)unityColor.g << 8;
            result                               |= (uint)unityColor.r;
            return new PackedColor { packedColor  = result };
        }

        public static implicit operator UnityEngine.Color32(PackedColor packedColor)
        {
            return new UnityEngine.Color32
            {
                r = (byte)(packedColor.packedColor & 0xff),
                g = (byte)((packedColor.packedColor >> 8) & 0xff),
                b = (byte)((packedColor.packedColor >> 16) & 0xff),
                a = (byte)((packedColor.packedColor >> 24) & 0xff)
            };
        }
    }

    public struct TextRenderControl : IComponentData
    {
        public enum Flags : byte
        {
            None = 0,
            Dirty = 1 << 0,
            ConvertColorGammaToLinear = 1 << 1,
            ApplyShearToPositions = 1 << 2,
            // Todo: What other flags would you want?
        }

        public Flags flags;
    }
}

