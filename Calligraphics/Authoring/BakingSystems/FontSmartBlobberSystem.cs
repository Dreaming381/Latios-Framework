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

        public static unsafe BlobAssetReference<FontBlob> BakeFont(FontAsset font, Material material)
        {
            float materialPadding = material.GetPaddingForText(false, false);

            var          builder             = new BlobBuilder(Allocator.Temp);
            ref FontBlob fontBlobRoot        = ref builder.ConstructRoot<FontBlob>();
            fontBlobRoot.scale               = font.faceInfo.scale;
            fontBlobRoot.pointSize           = font.faceInfo.pointSize;
            fontBlobRoot.baseLine            = font.faceInfo.baseline;
            fontBlobRoot.ascentLine          = font.faceInfo.ascentLine;
            fontBlobRoot.descentLine         = font.faceInfo.descentLine;
            fontBlobRoot.lineHeight          = font.faceInfo.lineHeight;
            fontBlobRoot.regularStyleSpacing = font.regularStyleSpacing;
            fontBlobRoot.regularStyleWeight  = font.regularStyleWeight;
            fontBlobRoot.boldStyleSpacing    = font.boldStyleSpacing;
            fontBlobRoot.boldStyleWeight     = font.boldStyleWeight;
            fontBlobRoot.italicsStyleSlant   = font.italicStyleSlant;
            fontBlobRoot.capLine             = font.faceInfo.capLine;
            fontBlobRoot.atlasWidth          = font.atlasWidth;
            fontBlobRoot.atlasHeight         = font.atlasHeight;
            fontBlobRoot.materialPadding     = materialPadding;

            var       adjustmentCacheBefore      = new NativeList<int2>(Allocator.TempJob);
            var       adjustmentCacheAfter       = new NativeList<int2>(Allocator.TempJob);
            var       glyphToCharacterMap        = new NativeHashMap<int, int>(font.characterTable.Count, Allocator.TempJob);
            var       glyphPairAdjustmentsSource = font.GetGlyphPairAdjustmentRecords();
            Span<int> hashCounts                 = stackalloc int[64];
            hashCounts.Clear();
            // Todo: Currently, we allocate a glyph per character and leave characters with null glyphs uninitialized.
            // We should rework that to only allocate glyphs to save memory.
            BlobBuilderArray<GlyphBlob>      glyphBuilder    = builder.Allocate(ref fontBlobRoot.characters, font.characterTable.Count);
            BlobBuilderArray<AdjustmentPair> adjustmentPairs = builder.Allocate(ref fontBlobRoot.adjustmentPairs, glyphPairAdjustmentsSource.Count);

            for (int i = 0; i < font.characterTable.Count; i++)
            {
                var c = font.characterTable[i];
                if (c.glyph != null)
                    glyphToCharacterMap.Add((int)c.glyph.index, i);
            }

            for (int i = 0; i < glyphPairAdjustmentsSource.Count; i++)
            {
                var src            = glyphPairAdjustmentsSource[i];
                adjustmentPairs[i] = new AdjustmentPair
                {
                    firstAdjustment = new GlyphAdjustment
                    {
                        xPlacement = src.firstAdjustmentRecord.glyphValueRecord.xPlacement,
                        yPlacement = src.firstAdjustmentRecord.glyphValueRecord.yPlacement,
                        xAdvance   = src.firstAdjustmentRecord.glyphValueRecord.xAdvance,
                        yAdvance   = src.firstAdjustmentRecord.glyphValueRecord.yAdvance,
                    },
                    secondAdjustment = new GlyphAdjustment
                    {
                        xPlacement = src.secondAdjustmentRecord.glyphValueRecord.xPlacement,
                        yPlacement = src.secondAdjustmentRecord.glyphValueRecord.yPlacement,
                        xAdvance   = src.secondAdjustmentRecord.glyphValueRecord.xAdvance,
                        yAdvance   = src.secondAdjustmentRecord.glyphValueRecord.yAdvance,
                    },
                    fontFeatureLookupFlags = src.featureLookupFlags,
                    firstGlyphIndex        = glyphToCharacterMap[(int)src.firstAdjustmentRecord.glyphIndex],
                    secondGlyphIndex       = glyphToCharacterMap[(int)src.secondAdjustmentRecord.glyphIndex]
                };
            }

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

                    //Add kerning adjustments
                    adjustmentCacheBefore.Clear();
                    adjustmentCacheAfter.Clear();
                    for (int j = 0; j < adjustmentPairs.Length; j++)
                    {
                        ref var adj = ref adjustmentPairs[j];
                        if (adj.firstGlyphIndex == i)
                            adjustmentCacheAfter.Add(new int2(adj.secondGlyphIndex, j));
                        if (adj.secondGlyphIndex == i)
                            adjustmentCacheBefore.Add(new int2(adj.firstGlyphIndex, j));
                    }
                    adjustmentCacheBefore.Sort(new XSorter());
                    var bk = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.beforeKeys, adjustmentCacheBefore.Length);
                    var bv = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.beforeIndices, adjustmentCacheBefore.Length);
                    for (int j = 0; j < bk.Length; j++)
                    {
                        var d = adjustmentCacheBefore[j];
                        bk[j] = d.x;
                        bv[j] = d.y;
                    }
                    adjustmentCacheAfter.Sort(new XSorter());
                    var ak = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.afterKeys, adjustmentCacheAfter.Length);
                    var av = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.afterIndices, adjustmentCacheAfter.Length);
                    for (int j = 0; j < ak.Length; j++)
                    {
                        var d = adjustmentCacheAfter[j];
                        ak[j] = d.x;
                        av[j] = d.y;
                    }

                    //Get vertices and uvs
                    var                      vertices        = GetVertices(character.glyph.metrics, materialPadding, font.atlasPadding / 2f, fontBlobRoot.baseScale);
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

                    hashCounts[BlobTextMeshGlyphExtensions.GetGlyphHash(glyphBlob.unicode)]++;
                }
            }

            var             hashes     = builder.Allocate(ref fontBlobRoot.glyphLookupMap, 64);
            Span<HashArray> hashArrays = stackalloc HashArray[64];
            for (int i = 0; i < hashes.Length; i++)
            {
                hashArrays[i] = new HashArray
                {
                    hashArray = (GlyphLookup*)builder.Allocate(ref hashes[i], hashCounts[i]).GetUnsafePtr()
                };
                hashCounts[i] = 0;
            }

            for (int i = 0; i < glyphBuilder.Length; i++)
            {
                if (glyphBuilder[i].unicode == 0) // Is this the right way to rule out null glyphs?
                    continue;
                var hash                                     = BlobTextMeshGlyphExtensions.GetGlyphHash(glyphBuilder[i].unicode);
                hashArrays[hash].hashArray[hashCounts[hash]] = new GlyphLookup { unicode = glyphBuilder[i].unicode, index = i };
                hashCounts[hash]++;
            }

            var result = builder.CreateBlobAssetReference<FontBlob>(Allocator.Persistent);
            builder.Dispose();
            adjustmentCacheBefore.Dispose();
            adjustmentCacheAfter.Dispose();
            glyphToCharacterMap.Dispose();

            fontBlobRoot = result.Value;

            return result;
        }

        unsafe struct HashArray
        {
            public GlyphLookup* hashArray;
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

        struct XSorter : IComparer<int2>
        {
            public int Compare(int2 x, int2 y) => x.x.CompareTo(y.x);
        }
    }
}

