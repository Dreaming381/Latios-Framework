using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// How Rendering works:
// When the DynamicBuffer<CalliByte> is updated, a pipeline will process it, convert it into a set
// of glyphs, and add any glyphs it hasn't seen before to a global internal table. The result of
// this is the DynamicBuffer<RenderGlyph>. As a user, you can override this buffer by adding
// DynamicBuffer<AnimatedRenderGlyph> and populating the values using modified elements read from
// DynamicBuffer<RenderGlyph>.
//
// In the Kinemation Resources directory, there is a special Mesh baked which contains dummy
// vertex attributes, and has multiple submeshes. Each submesh contains the triangle vertex indices
// for various glyph counts. The submesh is with with the least number of triangles needed is chosen
// to render the set of glyphs contained by the text renderer entity.
//
// A material override property _TextShaderIndex is sent to the GPU. This contains a start index and
// count as integers into a global buffer containing all glyphs. This global buffer has two regions,
// a resident region and a dynamic region. The first time an entity with glyphs is rendered, the
// glyphs are allocated in the dynamic region and forgotten about the next frame. If the second frame
// also sees the same entity rendered without changes to glyphs, it will allocate the glyphs in a
// resident region, allowing future renders of the entity to not require uploading glyphs. The GPU
// buffer can be accessed and decoded using the included GlobalsApi.hlsl file. Extra triangles that
// don't have glyphs will have NaN assigned to the positions. This causes the GPU to discard the
// triangle.
//
// Similarly, global texture arrays are used to store SDFs for character glyphs and bitmaps for
// sprites and rasterized emojis. Access APIs can also be found in the hlsl file. Any shader including
// that file will have access to these global resources.

namespace Latios.Calligraphics
{
    /// <summary>
    /// The glyphs to be rendered based on the processed CalliByte buffer.
    /// Copy this buffer to AnimatedRenderGlyph to apply animation to the data.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct RenderGlyph : IBufferElementData
    {
        public float2 blPosition;  //0
        public float2 brPosition;  //8
        public float2 tlPosition;  //16
        public float2 trPosition;  //24

        public float2 blUVB;  //32
        public float2 brUVB;  //40
        public float2 tlUVB;  //48
        public float2 trUVB;  //56

        public half4 blColor;  //64
        public half4 brColor;  //72
        public half4 tlColor;  //80
        public half4 trColor;  //88

        // These should be normalized relative to the padded bounding box extents of [0, 1]
        // The uploader will patch these with the atlas coordinates using math.lerp()
        public float2 blUVA;  //96
        public float2 trUVA;  //104

        public uint  arrayIndex;  //112  Converted to float in upload shader
        public uint  glyphEntryId;  //116
        public float scale;  //120
        public uint  reserved;  //124
                               //128 bytes total size
    }

    /// <summary>
    /// When this buffer is present, it overrides the RenderGlyph buffer for rendering purposes.
    /// Copy the RenderGlyph buffer into this buffer and then modify the glyphs for animation purposes
    /// within AnimateGlyphsSuperSystem.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct AnimatedRenderGlyph : IBufferElementData
    {
        public RenderGlyph glyph;
    }

    [MaterialProperty("_TextShaderIndex")]
    public struct TextShaderIndex : IComponentData
    {
        public uint firstGlyphIndex;
        public uint glyphCount;
    }

    internal struct GpuState : IComponentData, IEnableableComponent  // Enabled to request dispatch
    {
        internal enum State : byte
        {
            Uncommitted,
            Dynamic,
            DynamicPromoteToResident,
            Resident,
            ResidentUncommitted
        }
        internal State state;
    }

    [InternalBufferCapacity(0)]
    internal struct PreviousRenderGlyph : ICleanupBufferElementData
    {
        public RenderGlyph glyph;
    }

    internal struct ResidentRange : ICleanupComponentData
    {
        public uint start;
        public uint count;
    }

    internal partial struct NewEntitiesList : ICollectionComponent
    {
        public NativeList<Entity> newGlyphEntities;

        public JobHandle TryDispose(JobHandle inputDeps) => newGlyphEntities.Dispose(inputDeps);
    }
}

