using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.TextCore.LowLevel;

namespace Latios.Calligraphics
{
    //TODO: Underlay, Bold, Smallcaps
    public struct FontBlob
    {
        public BlobArray<GlyphBlob>               characters;
        public BlobArray<BlobArray<GlyphLookup> > glyphLookupMap;
        public float                              ascentLine;
        public float                              descentLine;
        public float                              lineHeight;
        public float                              pointSize;
        public float                              scale;

        public float baseLine;
        public float atlasWidth;
        public float atlasHeight;

        public float regularStyleSpacing;
        public float regularStyleWeight;
        public float boldStyleSpacing;
        public float boldStyleWeight;
        public byte  italicsStyleSlant;

        public float capLine;

        public float subscriptOffset;
        public float subscriptSize;
        public float superscriptOffset;
        public float superscriptSize;

        //public float underlineOffset;

        /// <summary>
        /// Padding that is read from material properties
        /// </summary>
        public float materialPadding;

        public float baseScale => 1f / pointSize * scale * .1f;

        public const float smallCapsScaleMultiplier    = .8f;
        public const float orthographicScaleMultiplier = 10f;
    }

    public struct GlyphBlob
    {
        public uint              glyphIndex;
        public uint              unicode;
        public BlobArray<float2> vertices;
        public BlobArray<float2> uv;
        public BlobArray<float2> uv2;

        public int bottomLeftIndex => 0;
        public int topLeftIndex => 1;
        public int topRightIndex => 2;
        public int bottomRightIndex => 3;
        public float2 bottomLeftVertex => vertices[bottomLeftIndex];
        public float2 topLeftVertex => vertices[topLeftIndex];
        public float2 topRightVertex => vertices[topRightIndex];
        public float2 bottomRightVertex => vertices[bottomRightIndex];

        public float2 bottomLeftUV => uv[bottomLeftIndex];
        public float2 topLeftUV => uv[topLeftIndex];
        public float2 topRightUV => uv[topRightIndex];
        public float2 bottomRightUV => uv[bottomRightIndex];

        public float2 bottomLeftUV2 => uv2[bottomLeftIndex];
        public float2 topLeftUV2 => uv2[topLeftIndex];
        public float2 topRightUV2 => uv2[topRightIndex];
        public float2 bottomRightUV2 => uv2[bottomRightIndex];

        public float horizontalAdvance;
        public float horizontalBearingX;
        public float horizontalBearingY;

        public BlobArray<AdjustmentPair> glyphAdjustments;

        public float scale;
        public float width;
        public float height;

        public struct AdjustmentPair
        {
            public FontFeatureLookupFlags fontFeatureLookupFlags;
            public GlyphAdjustment        firstAdjustment;
            public GlyphAdjustment        secondAdjustment;
        }

        public struct GlyphAdjustment
        {
            public uint  glyphUnicode;
            public float xPlacement;
            public float yPlacement;
            public float xAdvance;
            public float yAdvance;
            public static GlyphAdjustment operator +(GlyphAdjustment a, GlyphAdjustment b)
            => new GlyphAdjustment
            {
                xPlacement = a.xPlacement + b.xPlacement,
                yPlacement = a.yPlacement + b.yPlacement,
                xAdvance   = a.xAdvance + b.xAdvance,
                yAdvance   = a.yAdvance + b.yAdvance,
            };
        }
    }

    public struct GlyphLookup
    {
        public uint unicode;
        public int  index;
    }

    public static class BlobTextMeshGlyphExtensions
    {
        public static bool TryGetGlyphIndex(ref this FontBlob font, uint character, out int index)
        {
            ref var hashArray = ref font.glyphLookupMap[GetGlyphHash(character)];
            index             = -1;

            for (int i = 0; i < hashArray.Length; i++)
            {
                if (hashArray[i].unicode == character)
                {
                    index = hashArray[i].index;
                    return true;
                }
            }

            return false;
        }

        public static int GetGlyphHash(uint unicode)
        {
            return (int)(unicode & 0x3f);
        }
    }
}

