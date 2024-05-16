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
        public float                              baseLine;
        public float                              ascentLine;
        public float                              descentLine;
        public float                              capLine;
        public float                              meanLine;
        public float                              lineHeight;
        public float                              pointSize;
        public float                              scale;

        public float atlasWidth;
        public float atlasHeight;

        public float regularStyleSpacing;
        public float regularStyleWeight;
        public float boldStyleSpacing;
        public float boldStyleWeight;
        public byte  italicsStyleSlant;

        public float subscriptOffset;
        public float subscriptSize;
        public float superscriptOffset;
        public float superscriptSize;

        public float tabWidth;
        public float tabMultiple;

        //public float underlineOffset;

        /// <summary>
        /// Padding that is read from material properties
        /// </summary>
        public float materialPadding;
    }

    public struct GlyphBlob
    {
        public int          unicode;
        public GlyphMetrics glyphMetrics;
        public GlyphRect    glyphRect;
        public float        glyphScale;

        public AdjustmentPairLookupByUnicode glyphAdjustmentsLookup;
    }

    public struct GlyphLookup
    {
        public int unicode;
        public int index;
    }

    public struct AdjustmentPair
    {
        public int                    firstUnicode;
        public int                    secondUnicode;
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

    public struct AdjustmentPairLookupByUnicode
    {
        public BlobArray<int> beforeKeys;  //unicode
        public BlobArray<int> beforeIndices;
        public BlobArray<int> afterKeys;  //unicode
        public BlobArray<int> afterIndices;

        public unsafe bool TryGetAdjustmentPairIndexForUnicodeBefore(int otherUnicodeBefore, out int index)
        {
            index = -1;
            if (beforeKeys.Length == 0)
                return false;

            var found = BinarySearchFirstGreaterOrEqual((int*)beforeKeys.GetUnsafePtr(), beforeKeys.Length, otherUnicodeBefore);
            if (found < beforeKeys.Length && beforeKeys[found] == otherUnicodeBefore)
            {
                index = beforeIndices[found];
                return true;
            }
            return false;
        }

        public unsafe bool TryGetAdjustmentPairIndexForUnicodeAfter(int otherUnicodeAfter, out int index)
        {
            index = -1;
            if (afterKeys.Length == 0)
                return false;

            var found = BinarySearchFirstGreaterOrEqual((int*)afterKeys.GetUnsafePtr(), afterKeys.Length, otherUnicodeAfter);
            if (found < afterKeys.Length && afterKeys[found] == otherUnicodeAfter)
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
        public static bool TryGetCharacterIndex(ref this FontBlob font, Unicode.Rune character, out int index)
        {
            return TryGetCharacterIndex(ref font, character.value, out index);
        }

        public static bool TryGetCharacterIndex(ref this FontBlob font, int unicode, out int index)
        {
            ref var hashArray = ref font.glyphLookupMap[GetGlyphHash(unicode)];
            index             = -1;

            for (int i = 0; i < hashArray.Length; i++)
            {
                if (hashArray[i].unicode == unicode)
                {
                    index = hashArray[i].index;
                    return true;
                }
            }

            return false;
        }

        public static int GetGlyphHash(int unicode)
        {
            return unicode & 0x3f;
        }
    }
}

