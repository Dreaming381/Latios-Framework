using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace Latios.Calligraphics
{
    //TODO: Underlay, Bold, Smallcaps
    public struct FontBlob
    {
        public FixedString128Bytes                name;
        public BlobArray<GlyphBlob>               characters;
        public BlobArray<BlobArray<GlyphLookup> > glyphLookupMap;
        public BlobArray<AdjustmentPair>          adjustmentPairs;
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
        public uint         glyphIndex;
        public uint         unicode;
        public GlyphMetrics glyphMetrics;
        public GlyphRect    glyphRect;
        public float        glyphScale;

        public AdjustmentPairLookupByGlyph glyphAdjustmentsLookup;
    }

    public struct GlyphLookup
    {
        public uint unicode;
        public int  index;
    }

    public struct AdjustmentPair
    {
        public int                    firstGlyphIndex;
        public int                    secondGlyphIndex;
        public FontFeatureLookupFlags fontFeatureLookupFlags;
        public GlyphAdjustment        firstAdjustment;
        public GlyphAdjustment        secondAdjustment;
    }

    public struct GlyphAdjustment
    {
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

    public struct AdjustmentPairLookupByGlyph
    {
        public BlobArray<int> beforeKeys;
        public BlobArray<int> beforeIndices;
        public BlobArray<int> afterKeys;
        public BlobArray<int> afterIndices;

        public unsafe bool TryGetAdjustmentPairIndexForGlyphBefore(int otherGlyphBefore, out int index)
        {
            index = -1;
            if (beforeKeys.Length == 0)
                return false;

            var found = BinarySearchFirstGreaterOrEqual((int*)beforeKeys.GetUnsafePtr(), beforeKeys.Length, otherGlyphBefore);
            if (found < beforeKeys.Length && beforeKeys[found] == otherGlyphBefore)
            {
                index = beforeIndices[found];
                return true;
            }
            return false;
        }

        public unsafe bool TryGetAdjustmentPairIndexForGlyphAfter(int otherGlyphAfter, out int index)
        {
            index = -1;
            if (afterKeys.Length == 0)
                return false;

            var found = BinarySearchFirstGreaterOrEqual((int*)afterKeys.GetUnsafePtr(), afterKeys.Length, otherGlyphAfter);
            if (found < afterKeys.Length && afterKeys[found] == otherGlyphAfter)
            {
                index = afterIndices[found];
                return true;
            }
            return false;
        }

        // Returns count if nothing is greater or equal
        //   The following function is a C# and Burst adaptation of Paul-Virak Khuong and Pat Morin's
        //   optimized sequential order binary search: https://github.com/patmorin/arraylayout/blob/master/src/sorted_array.h
        //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
        private static unsafe int BinarySearchFirstGreaterOrEqual(int* array, [AssumeRange(0, short.MaxValue)] int count, int searchValue)
        {
            bool isBurst = true;
            SkipWithoutBurst(ref isBurst);
            if (isBurst)
            {
                for (int i = 1; i < count; i++)
                {
                    Hint.Assume(array[i] >= array[i - 1]);
                }
            }

            var  basePtr = array;
            uint n       = (uint)count;
            while (Hint.Likely(n > 1))
            {
                var half    = n / 2;
                n          -= half;
                var newPtr  = &basePtr[half];

                // As of Burst 1.8.0 prev 2
                // Burst never loads &basePtr[half] into a register for newPtr, and instead uses dual register addressing instead.
                // Because of this, instead of loading into the register, performing the comparison, using a cmov, and then a jump,
                // Burst immediately performs the comparison, conditionally jumps, uses a lea, and then a jump.
                // This is technically less instructions on average. But branch prediction may suffer as a result.
                basePtr = *newPtr < searchValue ? newPtr : basePtr;
            }

            if (*basePtr < searchValue)
                basePtr++;

            return (int)(basePtr - array);
        }

        [BurstDiscard]
        static void SkipWithoutBurst(ref bool isBurst) => isBurst = false;
    }

    public static class BlobTextMeshGlyphExtensions
    {
        public static bool TryGetGlyphIndex(ref this FontBlob font, Unicode.Rune character, out int index)
        {
            return TryGetGlyphIndex(ref font, math.asuint(character.value), out index);
        }

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

