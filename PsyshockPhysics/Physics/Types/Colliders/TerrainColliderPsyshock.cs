using System;
using System.Runtime.InteropServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct TerrainCollider
    {
        /// <summary>
        /// The blob asset containing the raw terrain hull data
        /// </summary>
        [FieldOffset(24)] public BlobAssetReference<TerrainColliderBlob> terrainColliderBlob;
        /// <summary>
        /// The premultiplied scale and stretch in local space to be applied on raw integer coordinates
        /// </summary>
        [FieldOffset(0)] public float3 scale;
        /// <summary>
        /// An offset which should be applied to the raw height values before being scaled
        /// </summary>
        [FieldOffset(12)] public int baseHeightOffset;

        /// <summary>
        /// Creates a new TerrainCollider
        /// </summary>
        /// <param name="terrainColliderBlob">The blob asset containing the raw terrain hull data</param>
        /// <param name="scale">The premultiplied scale and stretch in local space to be applied on raw integer coordinates</param>
        /// <param name="baseHeightOffset">An offset which should be applied to the raw height values before being scaled</param>
        public TerrainCollider(BlobAssetReference<TerrainColliderBlob> terrainColliderBlob, float3 scale, int baseHeightOffset)
        {
            this.terrainColliderBlob = terrainColliderBlob;
            this.scale               = scale;
            this.baseHeightOffset    = baseHeightOffset;
        }
    }

    internal struct TerrainColliderBlob
    {
        internal short minHeight;
        internal short maxHeight;
        internal short quadsPerAxis;
        internal short patchesPerAxis;
        internal short tilesPerAxis;
        internal short quartersPerAxis;
        internal short sectionsPerAxis;
        // Row-major order
        internal BlobArray<short> heights;
        internal BlobArray<Patch> patches;  // 8x8
        internal BlobArray<Patch> tiles;  // 64x64
        internal BlobArray<Patch> quarters;  // 512x512
        internal BlobArray<Patch> sections;  // 4096x4096

        internal interface IFindTrianglesProcessor
        {
            v128 FilterPatch(ref Patch patch, ulong borderMask, short quadsPerBit);
            void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices);
        }

        [StructLayout(LayoutKind.Explicit, Size = 320)]  // 5 cache lines
        internal struct Patch
        {
            [FieldOffset(0)] internal short  min;
            [FieldOffset(2)] internal short  max;
            [FieldOffset(4)] internal short  startX;
            [FieldOffset(6)] internal short  startY;
            [FieldOffset(8)] internal ulong  triangleSplitParity;
            [FieldOffset(16)] internal v128  minA;
            [FieldOffset(32)] internal v128  minC;  // Ordering is a little weird to optimize AVX2
            [FieldOffset(48)] internal v128  maxA;
            [FieldOffset(64)] internal v128  maxC;
            [FieldOffset(80)] internal v128  minB;
            [FieldOffset(96)] internal v128  minD;
            [FieldOffset(112)] internal v128 maxB;
            [FieldOffset(128)] internal v128 maxD;
            [FieldOffset(144)] internal v128 minE;
            [FieldOffset(160)] internal v128 minG;
            [FieldOffset(176)] internal v128 maxE;
            [FieldOffset(192)] internal v128 maxG;
            [FieldOffset(208)] internal v128 minF;
            [FieldOffset(224)] internal v128 minH;
            [FieldOffset(240)] internal v128 maxF;
            [FieldOffset(256)] internal v128 maxH;
            [FieldOffset(272)] internal v128 isValid;
            [FieldOffset(288)] internal v128 normalXPositive;
            [FieldOffset(304)] internal v128 normalYPositive;

            static readonly ulong[] kBorderMasks = new ulong[]
            {
                // Left
                0xffffffffffffffff, 0x7f7f7f7f7f7f7f7f, 0x3f3f3f3f3f3f3f3f, 0x1f1f1f1f1f1f1f1f, 0x0f0f0f0f0f0f0f0f, 0x0707070707070707, 0x0303030303030303, 0x0101010101010101,
                // Right
                0xffffffffffffffff, 0xfefefefefefefefe, 0xfcfcfcfcfcfcfcfc, 0xf8f8f8f8f8f8f8f8, 0xf0f0f0f0f0f0f0f0, 0xe0e0e0e0e0e0e0e0, 0xc0c0c0c0c0c0c0c0, 0x8080808080808080,
                // Top
                0xffffffffffffffff, 0x00ffffffffffffff, 0x0000ffffffffffff, 0x000000ffffffffff, 0x00000000ffffffff, 0x0000000000ffffff, 0x000000000000ffff, 0x00000000000000ff,
                // Bottom
                0xffffffffffffffff, 0xffffffffffffff00, 0xffffffffffff0000, 0xffffffffff000000, 0xffffffff00000000, 0xffffff0000000000, 0xffff000000000000, 0xff00000000000000
            };

            public ulong GetFilteredQuadMaskFromHeights(short minHeight, short maxHeight)
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    // 18 intrinsics, 8 loads, and 3 scalar ops
                    var mins    = X86.Avx.mm256_set1_epi16(minHeight);
                    var maxs    = X86.Avx.mm256_set1_epi16(maxHeight);
                    var a       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minA, minC), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxA, maxC)));
                    var b       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minB, minD), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxB, maxD)));
                    var result  = (ulong)X86.Avx2.mm256_movemask_epi8(X86.Avx2.mm256_packs_epi16(a, b));
                    var c       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minE, minG), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxE, maxG)));
                    var d       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minF, minH), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxF, maxH)));
                    result     |= ((ulong)X86.Avx2.mm256_movemask_epi8(X86.Avx2.mm256_packs_epi16(c, d))) << 32;
                    return ~result;
                }
                else if (X86.Sse2.IsSse2Supported)
                {
                    // 34 intrinsics, 16 loads, and 7 scalar ops
                    var mins    = X86.Sse2.set1_epi16(minHeight);
                    var maxs    = X86.Sse2.set1_epi16(maxHeight);
                    var a       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minA, maxs), X86.Sse2.cmplt_epi16(maxA, mins));
                    var b       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minB, maxs), X86.Sse2.cmplt_epi16(maxB, mins));
                    var result  = (ulong)X86.Sse2.movemask_epi8(X86.Sse2.packs_epi16(a, b));
                    var c       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minC, maxs), X86.Sse2.cmplt_epi16(maxC, mins));
                    var d       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minD, maxs), X86.Sse2.cmplt_epi16(maxD, mins));
                    result     |= ((ulong)X86.Sse2.movemask_epi8(X86.Sse2.packs_epi16(c, d))) << 16;
                    var e       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minE, maxs), X86.Sse2.cmplt_epi16(maxE, mins));
                    var f       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minF, maxs), X86.Sse2.cmplt_epi16(maxF, mins));
                    result     |= ((ulong)X86.Sse2.movemask_epi8(X86.Sse2.packs_epi16(e, f))) << 32;
                    var g       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minG, maxs), X86.Sse2.cmplt_epi16(maxG, mins));
                    var h       = X86.Sse2.or_si128(X86.Sse2.cmpgt_epi16(minH, maxs), X86.Sse2.cmplt_epi16(maxH, mins));
                    result     |= ((ulong)X86.Sse2.movemask_epi8(X86.Sse2.packs_epi16(g, h))) << 48;
                    return ~result;
                }
                else if (Arm.Neon.IsNeonSupported)
                {
                    // 40 intrinsics, 16 loads, 1 constant load, and 1 scalar op
                    var  mins     = Arm.Neon.vmovq_n_s16(minHeight);
                    var  maxs     = Arm.Neon.vmovq_n_s16(maxHeight);
                    v128 mask     = new v128(0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80);
                    var  a        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minA, maxs), Arm.Neon.vcltq_s16(maxA, mins)), mask);
                    var  b        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minB, maxs), Arm.Neon.vcltq_s16(maxB, mins)), mask);
                    var  c        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minC, maxs), Arm.Neon.vcltq_s16(maxC, mins)), mask);
                    var  d        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minD, maxs), Arm.Neon.vcltq_s16(maxD, mins)), mask);
                    var  e        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minE, maxs), Arm.Neon.vcltq_s16(maxE, mins)), mask);
                    var  f        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minF, maxs), Arm.Neon.vcltq_s16(maxF, mins)), mask);
                    var  g        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minG, maxs), Arm.Neon.vcltq_s16(maxG, mins)), mask);
                    var  h        = Arm.Neon.vandq_s16(Arm.Neon.vorrq_s16(Arm.Neon.vcgtq_s16(minH, maxs), Arm.Neon.vcltq_s16(maxH, mins)), mask);
                    var  ab       = Arm.Neon.vpaddq_s16(a, b);
                    var  cd       = Arm.Neon.vpaddq_s16(c, d);
                    var  ef       = Arm.Neon.vpaddq_s16(e, f);
                    var  gh       = Arm.Neon.vpaddq_s16(g, h);
                    var  abcd     = Arm.Neon.vpaddq_s16(ab, cd);
                    var  efgh     = Arm.Neon.vpaddq_s16(ef, gh);
                    var  abcdefgh = Arm.Neon.vpaddq_s16(abcd, efgh);
                    return ~Arm.Neon.vmovn_s16(abcdefgh).ULong0;
                }
                else
                {
                    static void Eval8(ref ulong bits, ref ulong bitSetter, short smin, short smax, v128 vmin, v128 vmax)
                    {
                        bool set    = smax >= vmin.SShort0 && smin <= vmax.SShort0;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort1 && smin <= vmax.SShort1;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort2 && smin <= vmax.SShort2;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort3 && smin <= vmax.SShort3;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort4 && smin <= vmax.SShort4;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort5 && smin <= vmax.SShort5;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort6 && smin <= vmax.SShort6;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                        set         = smax >= vmin.SShort7 && smin <= vmax.SShort7;
                        bits       |= math.select(0, bitSetter, set);
                        bitSetter <<= 1;
                    }

                    ulong result    = 0;
                    ulong bitSetter = 1;
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minA, maxA);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minB, maxB);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minC, maxC);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minD, maxD);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minE, maxE);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minF, maxF);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minG, maxG);
                    Eval8(ref result, ref bitSetter, minHeight, maxHeight, minH, maxH);
                    return result;
                }
            }

            internal ulong GetBorderMask(int minX, int minY, int maxX, int maxY)
            {
                var result  = kBorderMasks[math.clamp(minX - startX, 0, 7)];
                result     |= kBorderMasks[8 + math.clamp(7 + startX - maxX, 0, 7)];
                result     |= kBorderMasks[16 + math.clamp(minY - startY, 0, 7)];
                result     |= kBorderMasks[24 + math.clamp(7 + startY - maxY, 0, 7)];
                return result;
            }

            public v128 ExpandAndMaskTriangles(ulong quadMask)
            {
                // BMI2 pdep and pext run terribly on Zen1, and there's not a good way to detect
                // that processor and avoid those at runtime. So that's not an option.
                // Unfortunately, Burst doesn't have carryless multiplication for X86 exposed.
                // Otherwise it would be:
                // var result = clmul(quadMask, quadMask);
                // return result | (result >> 1);

                static ulong Expand32(ulong m)
                {
                    m = (m | (m << 32)) & 0x00000000ffffffff;
                    m = (m | (m << 16)) & 0x0000ffff0000ffff;
                    m = (m | (m << 8)) & 0x00ff00ff00ff00ff;
                    m = (m | (m << 4)) & 0x0f0f0f0f0f0f0f0f;
                    m = (m | (m << 2)) & 0x3333333333333333;
                    m = (m | (m << 1)) & 0x5555555555555555;
                    return m | (m << 1);
                }

                var a      = Expand32(quadMask & 0x00000000ffffffff);
                var b      = Expand32(quadMask >> 32);
                var result = new v128(a, b);
                return AndWithMask(result, isValid);
            }

            public v128 FilterPositiveXNormalUpside(v128 mask) => AndWithMask(mask, normalXPositive);
            public v128 FilterNegativeXNormalUpside(v128 mask) => AndNotWithMask(mask, normalXPositive);
            public v128 FilterPositiveYNormalUpside(v128 mask) => AndWithMask(mask, normalYPositive);
            public v128 FilterNegativeYNormalUpside(v128 mask) => AndNotWithMask(mask, normalYPositive);
        }

        static v128 AndWithMask(v128 a, v128 b)
        {
            if (X86.Sse2.IsSse2Supported)
            {
                return X86.Sse2.and_si128(a, b);
            }
            else
            {
                return new v128(a.ULong0 & b.ULong0, a.ULong1 & b.ULong1);
            }
        }

        static v128 AndNotWithMask(v128 a, v128 bnot)
        {
            if (X86.Sse2.IsSse2Supported)
            {
                return X86.Sse2.andnot_si128(bnot, a);
            }
            else
            {
                return new v128(a.ULong0 & ~bnot.ULong0, a.ULong1 & ~bnot.ULong1);
            }
        }

        internal void FindTriangles<T>(int minX, int minY, int maxX, int maxY, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            var packedSearchDomain = new int4(minX, minY, maxX, maxY);
            packedSearchDomain     = math.clamp(packedSearchDomain, 0, quadsPerAxis - 1);
            var patchIndices       = packedSearchDomain / 8;
            if (math.all(patchIndices.xy == patchIndices.zw))
            {
                ref var patch = ref patches[patchIndices.y * patchesPerAxis + patchIndices.x];
                DoFinal(ref patch, packedSearchDomain, ref processor);
                return;
            }
            var tileIndices = patchIndices / 8;
            if (math.all(tileIndices.xy == patchIndices.zw))
            {
                ref var tile = ref tiles[tileIndices.y * tilesPerAxis + tileIndices.x];
                DoLevel(ref tile, packedSearchDomain, ref processor, 1);
                return;
            }
            var quarterIndices = tileIndices / 8;
            if (math.all(quarterIndices.xy == quarterIndices.zw))
            {
                ref var quarter = ref quarters[quarterIndices.y * quartersPerAxis + quarterIndices.x];
                DoLevel(ref quarter, packedSearchDomain, ref processor, 2);
                return;
            }
            var sectionIndices = quarterIndices / 8;
            if (math.all(sectionIndices.xy == sectionIndices.zw))
            {
                ref var section = ref sections[sectionIndices.y * sectionsPerAxis + sectionIndices.x];
                DoLevel(ref section, packedSearchDomain, ref processor, 3);
                return;
            }
            for (int i = 0; i < sections.Length; i++)
            {
                DoLevel(ref sections[i], packedSearchDomain, ref processor, 3);
            }
        }

        int ToHeight1D(int2 height2D) => height2D.y * quadsPerAxis + height2D.x;

        void DoFinal<T>(ref Patch patch, int4 packedSearchDomain, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            var borderMask   = patch.GetBorderMask(packedSearchDomain.x, packedSearchDomain.y, packedSearchDomain.z, packedSearchDomain.w);
            var triangleMask = processor.FilterPatch(ref patch, borderMask, 1);
            if ((triangleMask.ULong0 | triangleMask.ULong1) == 0)
                return;
            var lower = triangleMask.ULong0;
            for (var i = math.tzcnt(lower); i < 64; lower ^= 1ul << i, i = math.tzcnt(lower))
            {
                var  quad            = i / 2;
                int2 baseCoordinates = new int2(patch.startX + (quad & 0x7), patch.startY + (quad >> 3));
                bool isTlbr          = (patch.triangleSplitParity & (1ul << quad)) != 0;
                bool isSecondary     = (i & 1) == 1;

                var coordinates = (isTlbr, isSecondary) switch
                {
                    (false, false) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(1, 0))),
                    (false, true) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1))),
                    (true, false) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(0, 1))),
                    (true, true) => new int3(ToHeight1D(baseCoordinates + new int2(1, 0)),
                                             ToHeight1D(baseCoordinates + new int2(1, 1)),
                                             ToHeight1D(baseCoordinates + new int2(1, 0)))
                };
                processor.Execute(ref this, coordinates);
            }

            var upper = triangleMask.ULong0;
            for (var i = math.tzcnt(upper); i < 64; upper ^= 1ul << i, i = math.tzcnt(upper))
            {
                var  quad            = (i / 2) + 32;
                int2 baseCoordinates = new int2(patch.startX + (quad & 0x7), patch.startY + (quad >> 3));
                bool isTlbr          = (patch.triangleSplitParity & (1ul << quad)) != 0;
                bool isSecondary     = (i & 1) == 1;

                var coordinates = (isTlbr, isSecondary) switch
                {
                    (false, false) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(1, 0))),
                    (false, true) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1))),
                    (true, false) => new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(0, 1))),
                    (true, true) => new int3(ToHeight1D(baseCoordinates + new int2(1, 0)),
                                             ToHeight1D(baseCoordinates + new int2(1, 1)),
                                             ToHeight1D(baseCoordinates + new int2(1, 0)))
                };
                processor.Execute(ref this, coordinates);
            }
        }

        void DoLevel<T>(ref Patch patch, int4 packedSearchDomain, ref T processor, int level) where T : unmanaged, IFindTrianglesProcessor
        {
            var domain       = packedSearchDomain >> (level * 3);
            var borderMask   = patch.GetBorderMask(domain.x, domain.y, domain.z, domain.w);
            var triangleMask = processor.FilterPatch(ref patch, borderMask, 1);
            if ((triangleMask.ULong0 | triangleMask.ULong1) == 0)
                return;

            ref var targetPatchArray = ref patches;
            var     dimension        = patchesPerAxis;
            if (level == 2)
            {
                targetPatchArray = ref tiles;
                dimension        = tilesPerAxis;
            }
            if (level == 3)
            {
                targetPatchArray = ref quarters;
                dimension        = quartersPerAxis;
            }

            triangleMask = AndWithMask(triangleMask, new v128(0x5555555555555555, 0x5555555555555555));
            var lower    = triangleMask.ULong0;
            for (var i = math.tzcnt(lower); i < 64; lower ^= 1ul << i, i = math.tzcnt(lower))
            {
                var     quad            = i / 2;
                int2    baseCoordinates = new int2(patch.startX + (quad & 0x7), patch.startY + (quad >> 3));
                ref var targetPatch     = ref targetPatchArray[baseCoordinates.y * dimension + baseCoordinates.x];
                if (level == 1)
                    DoFinal(ref targetPatch, packedSearchDomain, ref processor);
                else
                    DoLevel(ref targetPatch, packedSearchDomain, ref processor, level - 1);
            }

            var upper = triangleMask.ULong0;
            for (var i = math.tzcnt(upper); i < 64; upper ^= 1ul << i, i = math.tzcnt(upper))
            {
                var     quad            = (i / 2) + 32;
                int2    baseCoordinates = new int2(patch.startX + (quad & 0x7), patch.startY + (quad >> 3));
                ref var targetPatch     = ref targetPatchArray[baseCoordinates.y * dimension + baseCoordinates.x];
                if (level == 1)
                    DoFinal(ref targetPatch, packedSearchDomain, ref processor);
                else
                    DoLevel(ref targetPatch, packedSearchDomain, ref processor, level - 1);
            }
        }
    }
}

