using System;
using System.Collections.Generic;
using System.Linq;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics.Authoring
{
    public static class MecanimBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of an MecanimControllerLayerBlob Blob Asset
        /// </summary>
        /// <param name="animatorController">An animatorController whose layer to bake.</param>
        /// <param name="layerIndex">The index of the layer to bake.</param>
        public static SmartBlobberHandle<FontBlob> RequestCreateBlobAsset(this IBaker baker, FontAsset font, Material material)
        {
            return baker.RequestCreateBlobAsset<FontBlob, TextMeshFontBakeData>(new TextMeshFontBakeData
            {
                font     = font,
                material = material,
            });
        }
    }

    /// <summary>
    /// Input for the Font Asset Smart Blobber
    /// </summary>
    public struct TextMeshFontBakeData : ISmartBlobberRequestFilter<FontBlob>
    {
        /// <summary>
        /// The UnityEngine.TextCore.Text.Font to bake as a constituent into a blob asset reference.
        /// </summary>
        public FontAsset font;

        /// <summary>
        /// The material that will be baked along with the font asset
        /// </summary>
        public Material material;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            baker.AddComponent(blobBakingEntity, new FontBlobRequest
            {
                font     = new UnityObjectRef<FontAsset> { Value = font },
                material                                         = new UnityObjectRef<Material> { Value = material },
            });

            return true;
        }
    }

    [TemporaryBakingType]
    internal struct FontBlobRequest : IComponentData, IEquatable<FontBlobRequest>
    {
        public UnityObjectRef<FontAsset> font;
        public UnityObjectRef<Material>  material;

        public bool Equals(FontBlobRequest other)
        {
            return font.Equals(other.font) && material.Equals(other.material);
        }

        public override int GetHashCode()
        {
            return font.GetHashCode() ^ material.GetHashCode();
        }
    }
}

namespace Latios.Calligraphics.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public partial class FontSmartBlobberSystem : SystemBase
    {
        protected override void OnCreate()
        {
            new SmartBlobberTools<FontBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            Entities.ForEach((ref FontBlobRequest request, ref SmartBlobberResult result) =>
            {
                var font = request.font.Value;
                font.ReadFontAssetDefinition();
                var FontBlob = BakeFont(font, request.material.Value);
                result.blob  = UnsafeUntypedBlobAssetReference.Create(FontBlob);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).WithoutBurst().Run();
        }

        public static BlobAssetReference<FontBlob> BakeFont(FontAsset font, Material material)
        {
            float materialPadding = material.GetPaddingForText(false, false);

            var          builder             = new BlobBuilder(Allocator.Temp);
            ref FontBlob FontBlobFont        = ref builder.ConstructRoot<FontBlob>();
            FontBlobFont.scale               = font.faceInfo.scale;
            FontBlobFont.pointSize           = font.faceInfo.pointSize;
            FontBlobFont.baseLine            = font.faceInfo.baseline;
            FontBlobFont.ascentLine          = font.faceInfo.ascentLine;
            FontBlobFont.descentLine         = font.faceInfo.descentLine;
            FontBlobFont.lineHeight          = font.faceInfo.lineHeight;
            FontBlobFont.regularStyleSpacing = font.regularStyleSpacing;
            FontBlobFont.regularStyleWeight  = font.regularStyleWeight;
            FontBlobFont.boldStyleSpacing    = font.boldStyleSpacing;
            FontBlobFont.boldStyleWeight     = font.boldStyleWeight;
            FontBlobFont.italicsStyleSlant   = font.italicStyleSlant;
            FontBlobFont.capLine             = font.faceInfo.capLine;
            FontBlobFont.atlasWidth          = font.atlasWidth;
            FontBlobFont.atlasHeight         = font.atlasHeight;
            FontBlobFont.materialPadding     = materialPadding;

            var glyphPairAdjustments = font.GetGlyphPairAdjustmentRecordLookup();

            BlobBuilderArray<GlyphBlob> glyphBuilder = builder.Allocate(ref FontBlobFont.characters, font.characterTable.Count);
            for (int i = 0; i < font.characterTable.Count; i++)
            {
                var character = font.characterTable[i];

                if (character.glyph != null)
                {
                    ref GlyphBlob glyphBlob = ref glyphBuilder[i];

                    glyphBlob.unicode            = character.unicode;
                    glyphBlob.width              = character.glyph.metrics.width;
                    glyphBlob.height             = character.glyph.metrics.height;
                    glyphBlob.horizontalAdvance  = character.glyph.metrics.horizontalAdvance;
                    glyphBlob.horizontalBearingX = character.glyph.metrics.horizontalBearingX;
                    glyphBlob.horizontalBearingY = character.glyph.metrics.horizontalBearingY;
                    glyphBlob.scale              = character.glyph.scale;

                    Dictionary<uint, GlyphPairAdjustmentRecord> glyphAdjustments =
                        new Dictionary<uint, GlyphPairAdjustmentRecord>();

                    //Add kerning adjustments
                    for (int j = 0; j < font.characterTable.Count; j++)
                    {
                        if (j == i)
                            continue;

                        uint key = (uint)(i << 16 | j);
                        if (glyphPairAdjustments.TryGetValue(key, out var adjustmentPair))
                        {
                            var pairedCharacter = font.characterTable[j];
                            glyphAdjustments.Add(pairedCharacter.unicode, adjustmentPair);
                        }
                    }

                    BlobBuilderArray<GlyphBlob.AdjustmentPair> glyphAdjustmentsBuilder =
                        builder.Allocate(ref glyphBlob.glyphAdjustments, glyphAdjustments.Count);

                    for (int j = 0; j < glyphAdjustments.Count; j++)
                    {
                        var adjustmentPair         = glyphAdjustments.ElementAt(j);
                        glyphAdjustmentsBuilder[j] = new GlyphBlob.AdjustmentPair
                        {
                            firstAdjustment = new GlyphBlob.GlyphAdjustment
                            {
                                glyphUnicode = character.unicode,
                                xPlacement   = adjustmentPair.Value.firstAdjustmentRecord.glyphValueRecord.xPlacement,
                                yPlacement   = adjustmentPair.Value.firstAdjustmentRecord.glyphValueRecord.yPlacement,
                                xAdvance     = adjustmentPair.Value.firstAdjustmentRecord.glyphValueRecord.xAdvance,
                                yAdvance     = adjustmentPair.Value.firstAdjustmentRecord.glyphValueRecord.yAdvance,
                            },
                            secondAdjustment = new GlyphBlob.GlyphAdjustment
                            {
                                glyphUnicode = adjustmentPair.Key,
                                xPlacement   = adjustmentPair.Value.secondAdjustmentRecord.glyphValueRecord.xPlacement,
                                yPlacement   = adjustmentPair.Value.secondAdjustmentRecord.glyphValueRecord.yPlacement,
                                xAdvance     = adjustmentPair.Value.secondAdjustmentRecord.glyphValueRecord.xAdvance,
                                yAdvance     = adjustmentPair.Value.secondAdjustmentRecord.glyphValueRecord.yAdvance,
                            }
                        };
                    }

                    //Get vertices and uvs
                    var                      vertices        = GetVertices(character.glyph.metrics, materialPadding, font.atlasPadding / 2f, FontBlobFont.baseScale);
                    BlobBuilderArray<float2> verticesBuilder = builder.Allocate(ref glyphBlob.vertices, vertices.Length);
                    for (int j = 0; j < vertices.Length; j++)
                    {
                        verticesBuilder[j] = vertices[j];
                    }

                    var                      uvs       = GetUV0s(font, character.glyph, materialPadding, font.atlasPadding / 2f);
                    BlobBuilderArray<float2> uvBuilder = builder.Allocate(ref glyphBlob.uv, uvs.Length);
                    for (int j = 0; j < uvs.Length; j++)
                    {
                        uvBuilder[j] = uvs[j];
                    }

                    var                      uv2s       = GetUV2s(character.glyph, FontStyles.Normal, false);
                    BlobBuilderArray<float2> uv2Builder = builder.Allocate(ref glyphBlob.uv2, uv2s.Length);
                    for (int j = 0; j < uv2s.Length; j++)
                    {
                        uv2Builder[j] = uv2s[j];
                    }

                    glyphBuilder[i] = glyphBlob;
                }
            }

            var result = builder.CreateBlobAssetReference<FontBlob>(Allocator.Persistent);
            builder.Dispose();

            FontBlobFont = result.Value;

            return result;
        }

        private static FixedList64Bytes<float2> GetVertices(GlyphMetrics glyphMetrics, float materialPadding, float stylePadding, float currentElementScale)
        {
            float2 topLeft;
            topLeft.x = (glyphMetrics.horizontalBearingX - materialPadding - stylePadding) * currentElementScale;
            topLeft.y = (glyphMetrics.horizontalBearingY + materialPadding) * currentElementScale;

            float2 bottomLeft;
            bottomLeft.x = topLeft.x;
            bottomLeft.y = topLeft.y - (glyphMetrics.height + materialPadding * 2) * currentElementScale;

            float2 topRight;
            topRight.x = bottomLeft.x + (glyphMetrics.width + materialPadding * 2 + stylePadding * 2) * currentElementScale;
            topRight.y = topLeft.y;

            float2 bottomRight;
            bottomRight.x = topRight.x;
            bottomRight.y = bottomLeft.y;

            FixedList64Bytes<float2> vertices = new FixedList64Bytes<float2>();

            vertices.Add(bottomLeft);
            vertices.Add(topLeft);
            vertices.Add(topRight);
            vertices.Add(bottomRight);

            return vertices;
        }

        private static FixedList64Bytes<float2> GetUV0s(FontAsset font, Glyph glyph, float materialPadding, float stylePadding)
        {
            var    glyphRect = glyph.glyphRect;
            float2 bottomLeft;
            bottomLeft.x = (glyphRect.x - materialPadding - stylePadding) / font.atlasWidth;
            bottomLeft.y = (glyphRect.y - materialPadding - stylePadding) / font.atlasHeight;

            float2 topLeft;
            topLeft.x = bottomLeft.x;
            topLeft.y = (glyphRect.y + materialPadding + stylePadding + glyphRect.height) / font.atlasHeight;

            float2 topRight;
            topRight.x = (glyphRect.x + materialPadding + stylePadding + glyphRect.width) / font.atlasWidth;
            topRight.y = topLeft.y;

            float2 bottomRight;
            bottomRight.x = topRight.x;
            bottomRight.y = bottomLeft.y;

            FixedList64Bytes<float2> uvs = new FixedList64Bytes<float2>();

            uvs.Add(bottomLeft);
            uvs.Add(topLeft);
            uvs.Add(topRight);
            uvs.Add(bottomRight);

            return uvs;
        }

        private static FixedList64Bytes<float2> GetUV2s(Glyph glyph, FontStyles fontStyle, bool isUsingAlternateTypeface)
        {
            float2 bottomLeft;
            bottomLeft.x = 0;
            bottomLeft.y = 0;

            float2 topLeft;
            topLeft.x = 0;
            topLeft.y = 1;

            float2 topRight;
            topRight.x = 1;
            topRight.y = 1;

            float2 bottomRight;
            bottomRight.x = 1;
            bottomRight.y = 0;

            // var xScale = glyph.scale;
            // if (!isUsingAlternateTypeface && (fontStyle & TMPro.FontStyles.Bold) == TMPro.FontStyles.Bold)
            // {
            //     xScale *= -1;
            // }
            //
            // float x0 = bottomLeft.x;
            // float y0 = bottomLeft.y;
            // float x1 = topRight.x;
            // float y1 = topRight.y;
            //
            // float dx = (int)x0;
            // float dy = (int)y0;
            //
            // x0 = x0 - dx;
            // x1 = x1 - dx;
            // y0 = y0 - dy;
            // y1 = y1 - dy;
            //
            // // Optimization to avoid having a vector2 returned from the Pack UV function.
            // bottomLeft.x = PackUV(x0, y0);
            // bottomLeft.y = xScale;
            // topLeft.x = PackUV(x0, y1);
            // topLeft.y = xScale;
            // topRight.x = PackUV(x1, y1);
            // topRight.y = xScale;
            // bottomRight.x = PackUV(x1, y0);
            // bottomRight.y = xScale;

            FixedList64Bytes<float2> uv2s = new FixedList64Bytes<float2>();

            uv2s.Add(bottomLeft);
            uv2s.Add(topLeft);
            uv2s.Add(topRight);
            uv2s.Add(bottomRight);

            return uv2s;
        }

        private static float GetFontBaseScale(FontAsset font, float smallCapsMultiplier, bool isOrthographic = false)
        {
            //TODO:  Smallcaps multiplier is 1 for most font styles, but .8 for smallcaps style
            return smallCapsMultiplier / font.faceInfo.pointSize * font.faceInfo.scale * (isOrthographic ? 1 : 0.1f);
        }

        private static float PackUV(float x, float y)
        {
            double x0 = (int)(x * 511);
            double y0 = (int)(y * 511);

            return (float)((x0 * 4096) + y0);
        }
    }
}

