using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class Batching
    {
        [BurstCompile]
        public struct BatchJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeStream.Reader capturedSources;
            [ReadOnly] public NativeStream.Reader chunkChannelStreams;
            public NativeStream.Writer            channelStream;

            UnsafeList<HashedSource>         hashedSources;
            UnsafeHashMap<HashedSource, int> sourceToDeduplicatedIndexMap;

            public void Execute(int channelIndex)
            {
                int channelCount     = channelStream.ForEachCount;
                int chunkStreamCount = chunkChannelStreams.ForEachCount;

                // Sanity check because we are using pointers into this container.
                capturedSources.BeginForEachIndex(0);

                // Count sources
                int sourceCount = 0;
                for (int i = channelIndex; i < chunkStreamCount; i += channelCount)
                {
                    sourceCount += chunkChannelStreams.BeginForEachIndex(i);
                    // EndForEachIndex() is a sanity test to ensure we read all items and all data within each item.
                    // Since we aren't doing that in this first pass, we need to skip the call.
                    //chunkChannelStreams.EndForEachIndex();
                }
                if (sourceCount == 0)
                    return;

                // Set up allocations for local containers
                if (!hashedSources.IsCreated)
                {
                    hashedSources                = new UnsafeList<HashedSource>(sourceCount * 2, Allocator.Temp);
                    sourceToDeduplicatedIndexMap = new UnsafeHashMap<HashedSource, int>(sourceCount * 2, Allocator.Temp);
                }
                hashedSources.Clear();
                sourceToDeduplicatedIndexMap.Clear();
                hashedSources.Capacity                = math.max(hashedSources.Capacity, sourceCount);
                sourceToDeduplicatedIndexMap.Capacity = hashedSources.Capacity;

                // Build the hashedSources array and identify for each source the first index that shares the same batchable source data.
                // Mark the original so that we can find it after sorting.
                for (int streamIndex = channelIndex; streamIndex < chunkStreamCount; streamIndex += channelCount)
                {
                    int countInStream = chunkChannelStreams.BeginForEachIndex(streamIndex);
                    for (int i = 0; i < countInStream; i++)
                    {
                        var  hashedSource = new HashedSource(chunkChannelStreams.Read<ChannelStreamSource>());
                        bool isOriginal   = false;
                        if (!sourceToDeduplicatedIndexMap.TryGetValue(hashedSource, out var deduplicatedIndex))
                        {
                            deduplicatedIndex = hashedSources.Length;
                            sourceToDeduplicatedIndexMap.Add(hashedSource, deduplicatedIndex);
                            isOriginal = true;
                        }
                        hashedSource.SetDeduplicatedIndexAndOriginality(deduplicatedIndex, isOriginal);
                        hashedSources.Add(hashedSource);
                    }
                }

                // Sort the hashes by deduplicated index and then by ITD.
                hashedSources.Sort();

                // Generate deduplicated results
                channelStream.BeginForEachIndex(channelIndex);
                for (int start = 0; start < hashedSources.Length; )
                {
                    // Find the span of matching sources and mark which one is the original.
                    int originalOffset    = 0;
                    int deduplicatedIndex = hashedSources[start].deduplicatedIndex;
                    int count             = 1;
                    for (; start + count < hashedSources.Length; count++)
                    {
                        var source = hashedSources[start + count];
                        if (source.deduplicatedIndex != deduplicatedIndex)
                            break;

                        if (source.isOriginal)
                            originalOffset = count;
                    }

                    // Use the original as the base of all ITD batches. This means that captured source data
                    // used in batches are in the original capture stream order, which will hopefully provide
                    // some extra cache benefits. We still group by differing ITDs together though for added
                    // sample data cache coherency, which is probably just as big if not a bigger win.
                    var batchSource = hashedSources[start + originalOffset].source;

                    // Combine sources by matching ITD
                    var startSource                 = hashedSources[start].source;
                    batchSource.itdIndex            = startSource.itdIndex;
                    batchSource.sourceHeader.volume = startSource.sourceHeader.volume;
                    for (int i = 1; i < count; i++)
                    {
                        var iSource = hashedSources[start + i].source;
                        if (batchSource.itdIndex != iSource.itdIndex)
                        {
                            channelStream.Write(batchSource);
                            batchSource.itdIndex            = iSource.itdIndex;
                            batchSource.sourceHeader.volume = 0f;
                        }
                        batchSource.sourceHeader.volume += iSource.sourceHeader.volume;
                    }
                    channelStream.Write(batchSource);

                    start += count;
                }
                channelStream.EndForEachIndex();
            }

            unsafe struct HashedSource : IEquatable<HashedSource>, IComparable<HashedSource>
            {
                public ChannelStreamSource source;
                public int                 hashcode;
                private uint               m_packed;

                public int deduplicatedIndex => (int)(m_packed & 0x7fffffff);
                public bool isOriginal => (m_packed & 0x80000000) != 0;

                public void SetDeduplicatedIndexAndOriginality(int newIndex, bool original)
                {
                    m_packed = (uint)newIndex | math.select(0, 0x80000000, original);
                }

                public HashedSource(ChannelStreamSource input)
                {
                    source   = input;
                    hashcode = math.asint(Unity.Core.XXHash.Hash32(source.sourceDataPtr, source.batchingByteCount));
                    m_packed = 0;
                }

                public override int GetHashCode() => hashcode;

                public bool Equals(HashedSource other)
                {
                    if (hashcode != other.hashcode)
                        return false;

                    var aFeatures = source.sourceHeader.features & CapturedSourceHeader.Features.BatchingFeatures;
                    var bFeatures = other.source.sourceHeader.features & CapturedSourceHeader.Features.BatchingFeatures;
                    if (aFeatures != bFeatures)
                        return false;

                    if (source.batchingByteCount != other.source.batchingByteCount)
                        return false;

                    return UnsafeUtility.MemCmp(source.sourceDataPtr, other.source.sourceDataPtr, source.batchingByteCount) == 0;
                }

                public int CompareTo(HashedSource other)
                {
                    var result = deduplicatedIndex.CompareTo(other.deduplicatedIndex);
                    if (result == 0)
                        result = source.itdIndex.CompareTo(other.source.itdIndex);
                    return result;
                }
            }
        }
    }
}

