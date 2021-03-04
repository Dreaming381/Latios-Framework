using Unity.Collections;
using Unity.Mathematics;

namespace Latios
{
    internal interface IRadixSortableInt
    {
        int GetKey();
    }

    //X is MSB
    internal interface IRadixSortableInt3
    {
        int3 GetKey3();
    }

    internal static class RadixSort
    {
        #region Int
        public static void RankSortInt<T>(NativeArray<int> ranks, NativeArray<T> src) where T : struct, IRadixSortableInt
        {
            int count = src.Length;

            NativeArray<int> counts1    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<Indexer32> frontArray = new NativeArray<Indexer32>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Indexer32> backArray  = new NativeArray<Indexer32>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            //Counts
            for (int i = 0; i < count; i++)
            {
                var keys            = Keys(src[i].GetKey());
                counts1[keys.byte1] = counts1[keys.byte1] + 1;
                counts2[keys.byte2] = counts2[keys.byte2] + 1;
                counts3[keys.byte3] = counts3[keys.byte3] + 1;
                counts4[keys.byte4] = counts4[keys.byte4] + 1;
                frontArray[i]       = new Indexer32 { key = keys, index = i };
            }

            //Sums
            calculatePrefixSum(counts1, prefixSum1);
            calculatePrefixSum(counts2, prefixSum2);
            calculatePrefixSum(counts3, prefixSum3);
            calculatePrefixSum(counts4, prefixSum4);

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].key.byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].key.byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].key.byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key        = backArray[i].key.byte4;
                int  dest       = prefixSum4[key];
                ranks[dest]     = backArray[i].index;
                prefixSum4[key] = prefixSum4[key] + 1;
            }
        }

        private struct Indexer32
        {
            public UintAsBytes key;
            public int         index;
        }

        private struct UintAsBytes
        {
            public byte byte1;
            public byte byte2;
            public byte byte3;
            public byte byte4;
        }

        private static UintAsBytes Keys(int val)
        {
            uint        key = math.asuint(val ^ 0x80000000);
            UintAsBytes result;
            result.byte1 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte2 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte3 = (byte)(key & 0x000000FF);
            key          = key >> 8;
            result.byte4 = (byte)(key & 0x000000FF);
            return result;
        }
        #endregion

        #region Int3
        public static void RankSortInt3<T>(NativeArray<int> ranks, NativeArray<T> src) where T : struct, IRadixSortableInt3
        {
            int count = src.Length;

            NativeArray<int> counts1     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts2     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts3     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts4     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts5     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts6     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts7     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts8     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts9     = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts10    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts11    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> counts12    = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<int> prefixSum1  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum2  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum3  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum4  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum5  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum6  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum7  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum8  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum9  = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum10 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum11 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> prefixSum12 = new NativeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            NativeArray<IndexerInt3> frontArray = new NativeArray<IndexerInt3>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<IndexerInt3> backArray  = new NativeArray<IndexerInt3>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            //Counts
            for (int i = 0; i < count; i++)
            {
                var         keysInt3  = src[i].GetKey3();
                var         keysUint3 = math.asuint(keysInt3) ^ 0x80000000;
                IndexerInt3 keys;
                keys.index  = i;
                var lsb     = keysUint3 & 0x000000FF;
                var slsb    = (keysUint3 >> 8) & 0x000000FF;
                var smsb    = (keysUint3 >> 16) & 0x000000FF;
                var msb     = (keysUint3 >> 24) & 0x000000FF;
                keys.byte1  = (byte)lsb.z;
                keys.byte2  = (byte)slsb.z;
                keys.byte3  = (byte)smsb.z;
                keys.byte4  = (byte)msb.z;
                keys.byte5  = (byte)lsb.y;
                keys.byte6  = (byte)slsb.y;
                keys.byte7  = (byte)smsb.y;
                keys.byte8  = (byte)msb.y;
                keys.byte9  = (byte)lsb.z;
                keys.byte10 = (byte)slsb.z;
                keys.byte11 = (byte)smsb.z;
                keys.byte12 = (byte)msb.z;

                counts1[keys.byte1]   = counts1[keys.byte1] + 1;
                counts2[keys.byte2]   = counts2[keys.byte2] + 1;
                counts3[keys.byte3]   = counts3[keys.byte3] + 1;
                counts4[keys.byte4]   = counts4[keys.byte4] + 1;
                counts5 [keys.byte5 ] = counts5 [keys.byte5 ] + 1;
                counts6 [keys.byte6 ] = counts6 [keys.byte6 ] + 1;
                counts7 [keys.byte7 ] = counts7 [keys.byte7 ] + 1;
                counts8 [keys.byte8 ] = counts8 [keys.byte8 ] + 1;
                counts9 [keys.byte9 ] = counts9 [keys.byte9 ] + 1;
                counts10[keys.byte10] = counts10[keys.byte10] + 1;
                counts11[keys.byte11] = counts11[keys.byte11] + 1;
                counts12[keys.byte12] = counts12[keys.byte12] + 1;
                frontArray[i]         = keys;
            }

            //Sums
            calculatePrefixSum(counts1,  prefixSum1);
            calculatePrefixSum(counts2,  prefixSum2);
            calculatePrefixSum(counts3,  prefixSum3);
            calculatePrefixSum(counts4,  prefixSum4);
            calculatePrefixSum(counts5,  prefixSum5 );
            calculatePrefixSum(counts6,  prefixSum6 );
            calculatePrefixSum(counts7,  prefixSum7 );
            calculatePrefixSum(counts8,  prefixSum8 );
            calculatePrefixSum(counts9,  prefixSum9 );
            calculatePrefixSum(counts10, prefixSum10);
            calculatePrefixSum(counts11, prefixSum11);
            calculatePrefixSum(counts12, prefixSum12);

            //Z
            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].byte1;
                int  dest       = prefixSum1[key];
                backArray[dest] = frontArray[i];
                prefixSum1[key] = prefixSum1[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte2;
                int  dest        = prefixSum2[key];
                frontArray[dest] = backArray[i];
                prefixSum2[key]  = prefixSum2[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].byte3;
                int  dest       = prefixSum3[key];
                backArray[dest] = frontArray[i];
                prefixSum3[key] = prefixSum3[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte4;
                int  dest        = prefixSum4[key];
                frontArray[dest] = backArray[i];
                prefixSum4[key]  = prefixSum4[key] + 1;
            }

            //Y
            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].byte5;
                int  dest       = prefixSum5[key];
                backArray[dest] = frontArray[i];
                prefixSum5[key] = prefixSum5[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte6;
                int  dest        = prefixSum6[key];
                frontArray[dest] = backArray[i];
                prefixSum6[key]  = prefixSum6[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].byte7;
                int  dest       = prefixSum7[key];
                backArray[dest] = frontArray[i];
                prefixSum7[key] = prefixSum7[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte8;
                int  dest        = prefixSum8[key];
                frontArray[dest] = backArray[i];
                prefixSum8[key]  = prefixSum8[key] + 1;
            }

            //X
            for (int i = 0; i < count; i++)
            {
                byte key        = frontArray[i].byte9;
                int  dest       = prefixSum9[key];
                backArray[dest] = frontArray[i];
                prefixSum9[key] = prefixSum9[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte10;
                int  dest        = prefixSum10[key];
                frontArray[dest] = backArray[i];
                prefixSum10[key] = prefixSum10[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = frontArray[i].byte11;
                int  dest        = prefixSum11[key];
                backArray[dest]  = frontArray[i];
                prefixSum11[key] = prefixSum11[key] + 1;
            }

            for (int i = 0; i < count; i++)
            {
                byte key         = backArray[i].byte12;
                int  dest        = prefixSum12[key];
                ranks[dest]      = backArray[i].index;
                prefixSum12[key] = prefixSum12[key] + 1;
            }
        }

        struct IndexerInt3
        {
            public int  index;
            public byte byte1;
            public byte byte2;
            public byte byte3;
            public byte byte4;
            public byte byte5;
            public byte byte6;
            public byte byte7;
            public byte byte8;
            public byte byte9;
            public byte byte10;
            public byte byte11;
            public byte byte12;
        }
        #endregion

        private static void calculatePrefixSum(NativeArray<int> counts, NativeArray<int> sums)
        {
            sums[0] = 0;
            for (int i = 0; i < counts.Length - 1; i++)
            {
                sums[i + 1] = sums[i] + counts[i];
            }
        }
    }
}

