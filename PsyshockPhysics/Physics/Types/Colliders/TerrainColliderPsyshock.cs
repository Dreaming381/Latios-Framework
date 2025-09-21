using System;
using System.Runtime.InteropServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A heightmap terrain collider shape that can be scaled, stretched, and offset in local space efficiently.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct TerrainCollider
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

    /// <summary>
    /// The definition of precomputed terrain data and associated acceleration structures
    /// </summary>
    public struct TerrainColliderBlob
    {
        /// <summary>
        /// Constructs a Blob Asset for the specified heightmap terrain information. The user is responsible for the lifecycle
        /// of the resulting blob asset. Calling in a Baker may not result in correct incremental behavior.
        /// </summary>
        /// <param name="builder">The initialized BlobBuilder to create the blob asset with</param>
        /// <param name="quadsPerRow">The number of terrain quads along the local x-axis of the terrain.</param>
        /// <param name="heightsRowMajor">The heights of the terrain. This must be a multiple of (quadsPerRow + 1)</param>
        /// <param name="quadTriangleSplitParitiesRowMajor">The parity of the split corners of each quad.
        /// For the very first quad, if the triangle split edge goes from (0, 0) to (1, 1), then the parity is zero (sum the ordinates of a single vertex).
        /// If the triangle split edge goes from (0, 1) to (1, 0), then the parity is one.
        /// The length of this array must provide enough bits such that there is a bit per quad. There is no end-of-row padding.</param>
        /// <param name="trianglesValid">Each two bits corresponds to the two triangles in the quad. If the bit is set, the triangle is solid.
        /// If the bit is cleared, the triangle is absent (a hole). The triangle with the lower x-axis centerpoint comes first.
        /// The length of this array should match the length of the trianglesValid array.</param>
        /// <param name="name">The name of the terrain which will be stored in the blob</param>
        /// <param name="allocator">The allocator used for the finally BlobAsset, typically Persistent</param>
        /// <returns>A reference to the created Blob Asset which is user-owned</returns>
        public static BlobAssetReference<TerrainColliderBlob> BuildBlob(ref BlobBuilder builder,
                                                                        int quadsPerRow,
                                                                        ReadOnlySpan<short>              heightsRowMajor,
                                                                        ReadOnlySpan<BitField32>         quadTriangleSplitParitiesRowMajor,
                                                                        ReadOnlySpan<BitField64>         trianglesValid,
                                                                        in FixedString128Bytes name,
                                                                        AllocatorManager.AllocatorHandle allocator)
        {
            ref var root        = ref builder.ConstructRoot<TerrainColliderBlob>();
            root.minHeight      = short.MaxValue;
            root.maxHeight      = short.MinValue;
            root.quadsPerRow    = (short)quadsPerRow;
            root.quadRows       = (short)((heightsRowMajor.Length / (quadsPerRow + 1)) - 1);
            root.patchesPerRow  = (short)(CollectionHelper.Align(root.quadsPerRow, 8) / 8);
            var patchRows       = (short)(CollectionHelper.Align(root.quadRows, 8) / 8);
            root.tilesPerRow    = (short)(CollectionHelper.Align(root.patchesPerRow, 8) / 8);
            var tileRows        = (short)(CollectionHelper.Align(patchRows, 8) / 8);
            root.quartersPerRow = (short)(CollectionHelper.Align(root.tilesPerRow, 8) / 8);
            var quarterRows     = (short)(CollectionHelper.Align(tileRows, 8) / 8);
            root.sectionsPerRow = (short)(CollectionHelper.Align(root.sectionsPerRow, 8) / 8);
            var sectionRows     = (short)(CollectionHelper.Align(quarterRows, 8) / 8);

            var heightsArray = builder.Allocate(ref root.heights, heightsRowMajor.Length);
            for (int i = 0; i < heightsRowMajor.Length; i++)
                heightsArray[i] = heightsRowMajor[i];

            var patchesArray = builder.Allocate(ref root.patches, root.patchesPerRow * patchRows);
            for (int patchRow = 0; patchRow < patchRows; patchRow++)
            {
                for (int patchInRow = 0; patchInRow < root.patchesPerRow; patchInRow++)
                {
                    ref var patch                          = ref patchesArray[patchRow * root.patchesPerRow + patchInRow];
                    patch.min                              = short.MaxValue;
                    patch.max                              = short.MinValue;
                    patch.startX                           = (short)(8 * patchInRow);
                    patch.startY                           = (short)(8 * patchRow);
                    patch.triangleSplitParity              = 0;
                    patch.isFirstTriangleOrChildPatchValid = 0;
                    patch.isSecondTriangleValid            = 0;
                    for (int quadRow = 0; quadRow < 8; quadRow++)
                    {
                        ref var min8 = ref patch.minA;
                        ref var max8 = ref patch.maxA;
                        switch (quadRow)
                        {
                            case 0:
                                break;
                            case 1:
                                min8 = ref patch.minB;
                                max8 = ref patch.maxB;
                                break;
                            case 2:
                                min8 = ref patch.minC;
                                max8 = ref patch.maxC;
                                break;
                            case 3:
                                min8 = ref patch.minD;
                                max8 = ref patch.maxD;
                                break;
                            case 4:
                                min8 = ref patch.minE;
                                max8 = ref patch.maxE;
                                break;
                            case 5:
                                min8 = ref patch.minF;
                                max8 = ref patch.maxF;
                                break;
                            case 6:
                                min8 = ref patch.minG;
                                max8 = ref patch.maxG;
                                break;
                            case 7:
                                min8 = ref patch.minH;
                                max8 = ref patch.maxH;
                                break;
                        }

                        for (int quadInRow = 0; quadInRow < 8; quadInRow++)
                        {
                            var  quadInPatch = 8 * quadRow + quadInRow;
                            var  quadOffset  = (patch.startY + quadRow) * root.quadsPerRow + math.min(patch.startX + quadInRow, root.quadsPerRow - 1);
                            bool valid       = patch.startX + quadInRow < root.quadsPerRow;

                            var quadElement = quadOffset / 32;
                            var parityBit   = quadOffset % 32;
                            Bits.SetBit(ref patch.triangleSplitParity,              quadInPatch, quadTriangleSplitParitiesRowMajor[quadElement].IsSet(parityBit));
                            var triangleFirstBit = parityBit * 2;
                            Bits.SetBit(ref patch.isFirstTriangleOrChildPatchValid, quadInPatch, valid && trianglesValid[quadElement].IsSet(triangleFirstBit));
                            Bits.SetBit(ref patch.isSecondTriangleValid,            quadInPatch, valid && trianglesValid[quadElement].IsSet(triangleFirstBit + 1));

                            var heightA    = heightsRowMajor[quadOffset];
                            var heightB    = heightsRowMajor[quadOffset + 1];
                            var heightC    = heightsRowMajor[quadOffset + root.quadsPerRow];
                            var heightD    = heightsRowMajor[quadOffset + root.quadsPerRow + 1];
                            var min        = (short)math.min(math.min(heightA, heightB), math.min(heightC, heightD));
                            var max        = (short)math.max(math.max(heightA, heightB), math.max(heightC, heightD));
                            patch.min      = (short)math.min(patch.min, min);
                            patch.max      = (short)math.max(patch.max, max);
                            root.minHeight = (short)math.min(root.minHeight, min);
                            root.maxHeight = (short)math.max(root.maxHeight, max);
                            switch (quadInRow)
                            {
                                case 0:
                                    min8.SShort0 = min;
                                    max8.SShort0 = max;
                                    break;
                                case 1:
                                    min8.SShort1 = min;
                                    max8.SShort1 = max;
                                    break;
                                case 2:
                                    min8.SShort2 = min;
                                    max8.SShort2 = max;
                                    break;
                                case 3:
                                    min8.SShort3 = min;
                                    max8.SShort3 = max;
                                    break;
                                case 4:
                                    min8.SShort4 = min;
                                    max8.SShort4 = max;
                                    break;
                                case 5:
                                    min8.SShort5 = min;
                                    max8.SShort5 = max;
                                    break;
                                case 6:
                                    min8.SShort6 = min;
                                    max8.SShort6 = max;
                                    break;
                                case 7:
                                    min8.SShort7 = min;
                                    max8.SShort7 = max;
                                    break;
                            }
                        }
                    }
                    patch.reservedA = default;
                    patch.reservedB = default;
                }
            }

            static void PopulateLevel(BlobBuilderArray<Patch> currentLevel,
                                      BlobBuilderArray<Patch> previousLevel,
                                      short currentPerRow,
                                      short currentRows,
                                      short previousPerRow,
                                      short previousRows)
            {
                for (int currentRow = 0; currentRow < currentRows; currentRow++)
                {
                    for (int currentInRow = 0; currentInRow < currentPerRow; currentInRow++)
                    {
                        ref var patch                          = ref currentLevel[currentRow * currentPerRow + currentInRow];
                        patch.min                              = short.MaxValue;
                        patch.max                              = short.MinValue;
                        patch.startX                           = (short)(8 * currentInRow);
                        patch.startY                           = (short)(8 * currentRow);
                        patch.triangleSplitParity              = 0;
                        patch.isFirstTriangleOrChildPatchValid = 0;
                        for (int quadRow = 0; quadRow < 8; quadRow++)
                        {
                            ref var min8 = ref patch.minA;
                            ref var max8 = ref patch.maxA;
                            switch (quadRow)
                            {
                                case 0:
                                    break;
                                case 1:
                                    min8 = ref patch.minB;
                                    max8 = ref patch.maxB;
                                    break;
                                case 2:
                                    min8 = ref patch.minC;
                                    max8 = ref patch.maxC;
                                    break;
                                case 3:
                                    min8 = ref patch.minD;
                                    max8 = ref patch.maxD;
                                    break;
                                case 4:
                                    min8 = ref patch.minE;
                                    max8 = ref patch.maxE;
                                    break;
                                case 5:
                                    min8 = ref patch.minF;
                                    max8 = ref patch.maxF;
                                    break;
                                case 6:
                                    min8 = ref patch.minG;
                                    max8 = ref patch.maxG;
                                    break;
                                case 7:
                                    min8 = ref patch.minH;
                                    max8 = ref patch.maxH;
                                    break;
                            }

                            for (int quadInRow = 0; quadInRow < 8; quadInRow++)
                            {
                                var     quadInPatch    = 8 * quadRow + quadInRow;
                                var     quadOffset     = (patch.startY + quadRow) * previousPerRow + math.min(patch.startX + quadInRow, previousPerRow - 1);
                                ref var lowerQuadPatch = ref previousLevel[quadOffset];
                                bool    valid          = patch.startX + quadInRow < previousPerRow;

                                Bits.SetBit(ref patch.isFirstTriangleOrChildPatchValid, quadInPatch,
                                            valid && (lowerQuadPatch.isFirstTriangleOrChildPatchValid | lowerQuadPatch.isSecondTriangleValid) != 0);

                                patch.min = (short)math.min(patch.min, lowerQuadPatch.min);
                                patch.max = (short)math.max(patch.max, lowerQuadPatch.max);
                                switch (quadInRow)
                                {
                                    case 0:
                                        min8.SShort0 = lowerQuadPatch.min;
                                        max8.SShort0 = lowerQuadPatch.max;
                                        break;
                                    case 1:
                                        min8.SShort1 = lowerQuadPatch.min;
                                        max8.SShort1 = lowerQuadPatch.max;
                                        break;
                                    case 2:
                                        min8.SShort2 = lowerQuadPatch.min;
                                        max8.SShort2 = lowerQuadPatch.max;
                                        break;
                                    case 3:
                                        min8.SShort3 = lowerQuadPatch.min;
                                        max8.SShort3 = lowerQuadPatch.max;
                                        break;
                                    case 4:
                                        min8.SShort4 = lowerQuadPatch.min;
                                        max8.SShort4 = lowerQuadPatch.max;
                                        break;
                                    case 5:
                                        min8.SShort5 = lowerQuadPatch.min;
                                        max8.SShort5 = lowerQuadPatch.max;
                                        break;
                                    case 6:
                                        min8.SShort6 = lowerQuadPatch.min;
                                        max8.SShort6 = lowerQuadPatch.max;
                                        break;
                                    case 7:
                                        min8.SShort7 = lowerQuadPatch.min;
                                        max8.SShort7 = lowerQuadPatch.max;
                                        break;
                                }
                            }
                        }
                        patch.isSecondTriangleValid = 0;
                        patch.reservedA             = default;
                        patch.reservedB             = default;
                    }
                }
            }

            var tilesArray = builder.Allocate(ref root.tiles, root.tilesPerRow * tileRows);
            PopulateLevel(tilesArray,    patchesArray,  root.tilesPerRow,    tileRows,    root.patchesPerRow,  patchRows);
            var quartersArray = builder.Allocate(ref root.quarters, root.quartersPerRow * quarterRows);
            PopulateLevel(quartersArray, tilesArray,    root.quartersPerRow, quarterRows, root.tilesPerRow,    tileRows);
            var sectionsArray = builder.Allocate(ref root.sections, root.sectionsPerRow * sectionRows);
            PopulateLevel(sectionsArray, quartersArray, root.sectionsPerRow, sectionRows, root.quartersPerRow, quarterRows);

            root.name = name;

            return builder.CreateBlobAssetReference<TerrainColliderBlob>(allocator);
        }

        internal short minHeight;
        internal short maxHeight;
        internal short quadRows;
        internal short quadsPerRow;
        internal short patchesPerRow;
        internal short tilesPerRow;
        internal short quartersPerRow;
        internal short sectionsPerRow;
        // Row-major order
        internal BlobArray<short> heights;
        internal BlobArray<Patch> patches;  // 8x8
        internal BlobArray<Patch> tiles;  // 64x64
        internal BlobArray<Patch> quarters;  // 512x512
        internal BlobArray<Patch> sections;  // 4096x4096

        public FixedString128Bytes name;

        internal interface IFindTrianglesProcessor
        {
            ulong FilterPatch(ref Patch patch, ulong borderMask, short quadsPerBit);
            void Execute(ref TerrainColliderBlob blob, int3 triangleHeightIndices, int triangleIndex);
        }

        [StructLayout(LayoutKind.Explicit, Size = 320)]  // 5 cache lines
        internal struct Patch
        {
            [FieldOffset(0)]   public short min;
            [FieldOffset(2)]   public short max;
            [FieldOffset(4)]   public short startX;
            [FieldOffset(6)]   public short startY;
            [FieldOffset(8)]   public ulong triangleSplitParity;  // (0, 0) is bottom left, (1, 1) is top right, which both have parity 0, thus bl -> tr split edge is parity 0.
            [FieldOffset(16)]  public ulong isFirstTriangleOrChildPatchValid;
            [FieldOffset(24)]  public ulong isSecondTriangleValid;
            [FieldOffset(32)]  public v128  minA;
            [FieldOffset(48)]  public v128  minC;  // Ordering is a little weird to optimize AVX2
            [FieldOffset(64)]  public v128  maxA;
            [FieldOffset(80)]  public v128  maxC;
            [FieldOffset(96)]  public v128  minB;
            [FieldOffset(112)] public v128  minD;
            [FieldOffset(128)] public v128  maxB;
            [FieldOffset(144)] public v128  maxD;
            [FieldOffset(160)] public v128  minE;
            [FieldOffset(176)] public v128  minG;
            [FieldOffset(192)] public v128  maxE;
            [FieldOffset(208)] public v128  maxG;
            [FieldOffset(224)] public v128  minF;
            [FieldOffset(240)] public v128  minH;
            [FieldOffset(256)] public v128  maxF;
            [FieldOffset(272)] public v128  maxH;
            [FieldOffset(288)] public v128  reservedA;
            [FieldOffset(304)] public v128  reservedB;

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
                    // 18 intrinsics, 8 loads, 2 scalar loads, and 5 scalar ops
                    var mins    = X86.Avx.mm256_set1_epi16(minHeight);
                    var maxs    = X86.Avx.mm256_set1_epi16(maxHeight);
                    var a       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minA, minC), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxA, maxC)));
                    var b       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minB, minD), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxB, maxD)));
                    var result  = (ulong)X86.Avx2.mm256_movemask_epi8(X86.Avx2.mm256_packs_epi16(a, b));
                    var c       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minE, minG), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxE, maxG)));
                    var d       = X86.Avx2.mm256_or_si256(X86.Avx2.mm256_cmpgt_epi16(new v256(minF, minH), maxs), X86.Avx2.mm256_cmpgt_epi16(mins, new v256(maxF, maxH)));
                    result     |= ((ulong)X86.Avx2.mm256_movemask_epi8(X86.Avx2.mm256_packs_epi16(c, d))) << 32;
                    result      = ~result;
                    return result & (isFirstTriangleOrChildPatchValid | isSecondTriangleValid);
                }
                else if (X86.Sse2.IsSse2Supported)
                {
                    // 34 intrinsics, 16 loads, 2 scalar loads and 9 scalar ops
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
                    result      = ~result;
                    return result & (isFirstTriangleOrChildPatchValid | isSecondTriangleValid);
                }
                else if (Arm.Neon.IsNeonSupported)
                {
                    // 40 intrinsics, 16 loads, 1 constant load, 2 scalar loads, and 3 scalar ops
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
                    var  result   = ~Arm.Neon.vmovn_s16(abcdefgh).ULong0;
                    return result & (isFirstTriangleOrChildPatchValid | isSecondTriangleValid);
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
                    return result & (isFirstTriangleOrChildPatchValid | isSecondTriangleValid);
                }
            }

            public ulong GetBorderMask(int minX, int minY, int maxX, int maxY)
            {
                var result  = kBorderMasks[math.clamp(minX - startX, 0, 7)];
                result     |= kBorderMasks[8 + math.clamp(7 + startX - maxX, 0, 7)];
                result     |= kBorderMasks[16 + math.clamp(minY - startY, 0, 7)];
                result     |= kBorderMasks[24 + math.clamp(7 + startY - maxY, 0, 7)];
                return result;
            }
        }

        internal void FindTriangles<T>(int minX, int minY, int maxX, int maxY, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            var packedSearchDomain = new int4(minX, minY, maxX, maxY);
            packedSearchDomain     = math.clamp(packedSearchDomain, 0, new int4(quadsPerRow - 1, quadRows - 1, quadsPerRow - 1, quadRows - 1));
            if (math.all(packedSearchDomain.xy == packedSearchDomain.zw))
            {
                var     quadIndex      = minY * quadsPerRow + minX;
                var     patchIndex2D   = packedSearchDomain.xy / 8;
                ref var patch          = ref patches[patchIndex2D.y * patchesPerRow + patchIndex2D.x];
                var     offsetsInPatch = packedSearchDomain.xy % 8;
                var     bitIndex       = offsetsInPatch.y * 8 + offsetsInPatch.x;
                if ((patch.isFirstTriangleOrChildPatchValid & (1ul << bitIndex)) != 0)
                    processor.Execute(ref this, GetTriangle(quadIndex * 2), quadIndex * 2);
                if ((patch.isSecondTriangleValid & (1ul << bitIndex)) != 0)
                    processor.Execute(ref this, GetTriangle(quadIndex * 2 + 1), quadIndex * 2 + 1);
                return;
            }
            var patchIndices = packedSearchDomain / 8;
            if (math.all(patchIndices.xy == patchIndices.zw))
            {
                ref var patch = ref patches[patchIndices.y * patchesPerRow + patchIndices.x];
                DoFinal(ref patch, packedSearchDomain, ref processor);
                return;
            }
            var tileIndices = patchIndices / 8;
            if (math.all(tileIndices.xy == patchIndices.zw))
            {
                ref var tile = ref tiles[tileIndices.y * tilesPerRow + tileIndices.x];
                DoLevel(ref tile, packedSearchDomain, ref processor, 1);
                return;
            }
            var quarterIndices = tileIndices / 8;
            if (math.all(quarterIndices.xy == quarterIndices.zw))
            {
                ref var quarter = ref quarters[quarterIndices.y * quartersPerRow + quarterIndices.x];
                DoLevel(ref quarter, packedSearchDomain, ref processor, 2);
                return;
            }
            var sectionIndices = quarterIndices / 8;
            if (math.all(sectionIndices.xy == sectionIndices.zw))
            {
                ref var section = ref sections[sectionIndices.y * sectionsPerRow + sectionIndices.x];
                DoLevel(ref section, packedSearchDomain, ref processor, 3);
                return;
            }
            for (int i = 0; i < sections.Length; i++)
            {
                DoLevel(ref sections[i], packedSearchDomain, ref processor, 3);
            }
        }

        internal int3 GetTriangle(int triangleIndex)
        {
            var     quadIndex             = triangleIndex / 2;
            var     quadRow               = quadIndex / quadsPerRow;
            var     quadIndexInRow        = quadIndex % quadsPerRow;
            var     patchRow              = quadRow / 8;
            var     patchIndexInRow       = quadIndexInRow / 8;
            var     patchIndex            = patchRow * patchesPerRow + patchIndexInRow;
            ref var patch                 = ref patches[patchIndex];
            var     quadRowInPatch        = quadRow % 8;
            var     quadIndexInRowInPatch = quadIndexInRow % 8;
            var     bitInPatch            = quadRowInPatch * 8 + quadIndexInRowInPatch;
            var     baseCoordinates       = new int2(quadIndexInRow, quadRow);
            if ((patch.triangleSplitParity & (1ul << bitInPatch)) != 0)
            {
                if ((triangleIndex & 1) == 0)
                    return new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(0, 1)));
                else
                    return new int3(ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(0, 1)));
            }
            else
            {
                if ((triangleIndex & 1) == 0)
                    return new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(0, 1)));
                else
                    return new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1)));
            }
        }

        internal bool GetSplitParity(int quadIndex)
        {
            var     quadRow               = quadIndex / quadsPerRow;
            var     quadIndexInRow        = quadIndex % quadsPerRow;
            var     patchRow              = quadRow / 8;
            var     patchIndexInRow       = quadIndexInRow / 8;
            var     patchIndex            = patchRow * patchesPerRow + patchIndexInRow;
            ref var patch                 = ref patches[patchIndex];
            var     quadRowInPatch        = quadRow % 8;
            var     quadIndexInRowInPatch = quadIndexInRow % 8;
            var     bitInPatch            = quadRowInPatch * 8 + quadIndexInRowInPatch;
            return (patch.triangleSplitParity & (1ul << bitInPatch)) != 0;
        }

        internal int ToHeight1D(int2 height2D) => height2D.y * (quadsPerRow + 1) + height2D.x;

        void DoFinal<T>(ref Patch patch, int4 packedSearchDomain, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            var borderMask = patch.GetBorderMask(packedSearchDomain.x, packedSearchDomain.y, packedSearchDomain.z, packedSearchDomain.w);
            var quadMask   = processor.FilterPatch(ref patch, borderMask, 1);
            if (quadMask == 0)
                return;
            for (var i = math.tzcnt(quadMask); i < 64; quadMask ^= 1ul << i, i = math.tzcnt(quadMask))
            {
                int2 baseCoordinates    = new int2(patch.startX + (i & 0x7), patch.startY + (i >> 3));
                int  firstTriangleIndex = baseCoordinates.y * quadsPerRow + baseCoordinates.x;

                if ((patch.triangleSplitParity & (1ul << i)) != 0)
                {
                    if ((patch.isFirstTriangleOrChildPatchValid & (1ul << i)) != 0)
                    {
                        var coordinates = new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(0, 1)));
                        processor.Execute(ref this, coordinates, firstTriangleIndex);
                    }
                    if ((patch.isSecondTriangleValid & (1ul << i)) != 0)
                    {
                        var coordinates =
                            new int3(ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(0, 1)));
                        processor.Execute(ref this, coordinates, firstTriangleIndex + 1);
                    }
                }
                else
                {
                    if ((patch.isFirstTriangleOrChildPatchValid & (1ul << i)) != 0)
                    {
                        var coordinates = new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 1)), ToHeight1D(baseCoordinates + new int2(0, 1)));
                        processor.Execute(ref this, coordinates, firstTriangleIndex);
                    }
                    if ((patch.isSecondTriangleValid & (1ul << i)) != 0)
                    {
                        var coordinates = new int3(ToHeight1D(baseCoordinates), ToHeight1D(baseCoordinates + new int2(1, 0)), ToHeight1D(baseCoordinates + new int2(1, 1)));
                        processor.Execute(ref this, coordinates, firstTriangleIndex + 1);
                    }
                }
            }
        }

        void DoLevel<T>(ref Patch patch, int4 packedSearchDomain, ref T processor, int level) where T : unmanaged, IFindTrianglesProcessor
        {
            var domain     = packedSearchDomain >> (level * 3);
            var borderMask = patch.GetBorderMask(domain.x, domain.y, domain.z, domain.w);
            var quadMask   = processor.FilterPatch(ref patch, borderMask, 1);
            if (quadMask == 0)
                return;

            ref var targetPatchArray = ref patches;
            var     dimension        = patchesPerRow;
            if (level == 2)
            {
                targetPatchArray = ref tiles;
                dimension        = tilesPerRow;
            }
            if (level == 3)
            {
                targetPatchArray = ref quarters;
                dimension        = quartersPerRow;
            }

            for (var i = math.tzcnt(quadMask); i < 64; quadMask ^= 1ul << i, i = math.tzcnt(quadMask))
            {
                int2    baseCoordinates = new int2(patch.startX + (i & 0x7), patch.startY + (i >> 3));
                ref var targetPatch     = ref targetPatchArray[baseCoordinates.y * dimension + baseCoordinates.x];
                if (level == 1)
                    DoFinal(ref targetPatch, packedSearchDomain, ref processor);
                else
                    DoLevel(ref targetPatch, packedSearchDomain, ref processor, level - 1);
            }
        }
    }
}

