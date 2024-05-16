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
            material.SetFloat("_WeightNormal", font.regularStyleWeight);
            material.SetFloat("_WeightBold",   font.boldStyleWeight);
            float materialPadding = material.GetPaddingForText(false, false);

            var          builder             = new BlobBuilder(Allocator.Temp);
            ref FontBlob fontBlobRoot        = ref builder.ConstructRoot<FontBlob>();
            fontBlobRoot.name                = font.name;
            fontBlobRoot.scale               = font.faceInfo.scale;
            fontBlobRoot.pointSize           = font.faceInfo.pointSize;
            fontBlobRoot.baseLine            = font.faceInfo.baseline;
            fontBlobRoot.ascentLine          = font.faceInfo.ascentLine;
            fontBlobRoot.descentLine         = font.faceInfo.descentLine;
            fontBlobRoot.capLine             = font.faceInfo.capLine;
            fontBlobRoot.meanLine            = font.faceInfo.meanLine;
            fontBlobRoot.lineHeight          = font.faceInfo.lineHeight;
            fontBlobRoot.subscriptOffset     = font.faceInfo.subscriptOffset;
            fontBlobRoot.subscriptSize       = font.faceInfo.subscriptSize;
            fontBlobRoot.superscriptOffset   = font.faceInfo.superscriptOffset;
            fontBlobRoot.superscriptSize     = font.faceInfo.superscriptSize;
            fontBlobRoot.tabWidth            = font.faceInfo.tabWidth;
            fontBlobRoot.tabMultiple         = font.tabMultiple;
            fontBlobRoot.regularStyleSpacing = font.regularStyleSpacing;
            fontBlobRoot.regularStyleWeight  = font.regularStyleWeight;
            fontBlobRoot.boldStyleSpacing    = font.boldStyleSpacing;
            fontBlobRoot.boldStyleWeight     = font.boldStyleWeight;
            fontBlobRoot.italicsStyleSlant   = font.italicStyleSlant;
            fontBlobRoot.atlasWidth          = font.atlasWidth;
            fontBlobRoot.atlasHeight         = font.atlasHeight;
            fontBlobRoot.materialPadding     = materialPadding;

            var       adjustmentCacheBefore      = new NativeList<int2>(Allocator.TempJob);
            var       adjustmentCacheAfter       = new NativeList<int2>(Allocator.TempJob);
            var       glyphPairAdjustmentsSource = font.GetGlyphPairAdjustmentRecords();
            Span<int> hashCounts                 = stackalloc int[64];
            hashCounts.Clear();
            // Todo: Currently, we allocate a glyph per character and leave characters with null glyphs uninitialized.
            // We should rework that to only allocate glyphs to save memory.
            var characterLookupTable = font.characterLookupTable;
            BlobBuilderArray<GlyphBlob>      glyphBuilder    = builder.Allocate(ref fontBlobRoot.characters, characterLookupTable.Count);
            BlobBuilderArray<AdjustmentPair> adjustmentPairs = builder.Allocate(ref fontBlobRoot.adjustmentPairs, glyphPairAdjustmentsSource.Count);
      
            for (int i = 0; i < glyphPairAdjustmentsSource.Count; i++)
            {
                var kerningPair = glyphPairAdjustmentsSource[i];
                if (GlyphIndexToUnicode(kerningPair.firstAdjustmentRecord.glyphIndex, characterLookupTable, out int firstUnicode) &&
                    GlyphIndexToUnicode(kerningPair.secondAdjustmentRecord.glyphIndex, characterLookupTable, out int secondUnicode))                    
                {
                    adjustmentPairs[i] = new AdjustmentPair
                    {
                        firstAdjustment = new GlyphAdjustment
                        {
                            xPlacement = kerningPair.firstAdjustmentRecord.glyphValueRecord.xPlacement,
                            yPlacement = kerningPair.firstAdjustmentRecord.glyphValueRecord.yPlacement,
                            xAdvance   = kerningPair.firstAdjustmentRecord.glyphValueRecord.xAdvance,
                            yAdvance   = kerningPair.firstAdjustmentRecord.glyphValueRecord.yAdvance,
                        },
                        secondAdjustment = new GlyphAdjustment
                        {
                            xPlacement = kerningPair.secondAdjustmentRecord.glyphValueRecord.xPlacement,
                            yPlacement = kerningPair.secondAdjustmentRecord.glyphValueRecord.yPlacement,
                            xAdvance   = kerningPair.secondAdjustmentRecord.glyphValueRecord.xAdvance,
                            yAdvance   = kerningPair.secondAdjustmentRecord.glyphValueRecord.yAdvance,
                        },
                        fontFeatureLookupFlags = kerningPair.featureLookupFlags,
                        firstUnicode           = firstUnicode,
                        secondUnicode          = secondUnicode
                    };
                }
            }

            int characterCount = 0;
            foreach (var character in characterLookupTable.Values)
            { 
                var glyph 	  = character.glyph;
                if (glyph == null)
                    continue;
                var unicode = math.asint(character.unicode);

                ref GlyphBlob glyphBlob = ref glyphBuilder[characterCount++];

                glyphBlob.unicode      = unicode;
                glyphBlob.glyphScale   = glyph.scale;
                glyphBlob.glyphMetrics = glyph.metrics;
                glyphBlob.glyphRect    = glyph.glyphRect;

                //Add kerning adjustments
                adjustmentCacheBefore.Clear();
                adjustmentCacheAfter.Clear();
                for (int j = 0; j < adjustmentPairs.Length; j++)
                {
                    ref var adj = ref adjustmentPairs[j];
                    if (adj.firstUnicode == unicode)
                        adjustmentCacheAfter.Add(new int2(adj.secondUnicode, j));
                    if (adj.secondUnicode == unicode)
                        adjustmentCacheBefore.Add(new int2(adj.firstUnicode, j));
                }
                adjustmentCacheBefore.Sort(new XSorter());
                var bk = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.beforeKeys, adjustmentCacheBefore.Length);
                var bv = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.beforeIndices, adjustmentCacheBefore.Length);
                for (int j = 0; j < bk.Length; j++)
                {
                    var d = adjustmentCacheBefore[j];
                    bk[j] = d.x;  //unicode
                    bv[j] = d.y;
                }
                adjustmentCacheAfter.Sort(new XSorter());
                var ak = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.afterKeys, adjustmentCacheAfter.Length);
                var av = builder.Allocate(ref glyphBlob.glyphAdjustmentsLookup.afterIndices, adjustmentCacheAfter.Length);
                for (int j = 0; j < ak.Length; j++)
                {
                    var d = adjustmentCacheAfter[j];
                    ak[j] = d.x;  //unicode
                    av[j] = d.y;
                }

                hashCounts[BlobTextMeshGlyphExtensions.GetGlyphHash(glyphBlob.unicode)]++;
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

            fontBlobRoot = result.Value;

            return result;
        }
        static bool GlyphIndexToUnicode(uint glyphIndex, Dictionary<uint, Character> characterLookupTable, out int unicode)
        {
            unicode = default;
            foreach (var character in characterLookupTable.Values)
            {
                if (character.glyphIndex == glyphIndex)
                {
                    unicode = math.asint(character.unicode);
                    return true;
                }
            }
            return false;
        }
        unsafe struct HashArray
        {
            public GlyphLookup* hashArray;
        }
        struct XSorter : IComparer<int2>
        {
            public int Compare(int2 x, int2 y) => x.x.CompareTo(y.x);
        }
    }
}

