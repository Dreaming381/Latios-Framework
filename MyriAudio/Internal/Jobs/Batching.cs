using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Myri
{
    internal static class Batching
    {
        internal const int INITIAL_ALLOCATION_SIZE = 1024;

        [BurstCompile]
        public struct BatchOneshotsJob : IJob
        {
            [ReadOnly] public NativeArray<OneshotEmitter> emitters;
            [ReadOnly] public NativeStream.Reader         pairWeights;
            [ReadOnly] public NativeStream.Reader         listenerEmitterPairs;  //int2: listener, emitter

            public NativeList<ClipFrameLookup> clipFrameLookups;
            public NativeList<Weights>         batchedWeights;
            public NativeList<int>             targetListenerIndices;

            public void Execute()
            {
                var hashmap = new NativeHashMap<ClipFrameListener, int>(INITIAL_ALLOCATION_SIZE, Allocator.Temp);
                if (clipFrameLookups.Capacity < INITIAL_ALLOCATION_SIZE)
                    clipFrameLookups.Capacity = INITIAL_ALLOCATION_SIZE;
                if (batchedWeights.Capacity < INITIAL_ALLOCATION_SIZE)
                    batchedWeights.Capacity = INITIAL_ALLOCATION_SIZE;
                if (targetListenerIndices.Capacity < INITIAL_ALLOCATION_SIZE)
                    targetListenerIndices.Capacity = INITIAL_ALLOCATION_SIZE;

                int streamIndices = listenerEmitterPairs.ForEachCount;
                for (int streamIndex = 0; streamIndex < streamIndices; streamIndex++)
                {
                    int countInStream = listenerEmitterPairs.BeginForEachIndex(streamIndex);
                    pairWeights.BeginForEachIndex(streamIndex);

                    for (; countInStream > 0; countInStream--)
                    {
                        int2 listenerEmitterPairIndices = listenerEmitterPairs.Read<int2>();
                        var  pairWeight                 = pairWeights.Read<Weights>();

                        var e = emitters[listenerEmitterPairIndices.y];
                        if (!e.source.clip.IsCreated)
                            continue;

                        ClipFrameListener cfl = new ClipFrameListener
                        {
                            lookup        = new ClipFrameLookup { clip = e.source.clip, spawnFrameOrOffset = e.source.m_spawnedAudioFrame },
                            listenerIndex                                                                  = listenerEmitterPairIndices.x
                        };
                        if (hashmap.TryGetValue(cfl, out int foundIndex))
                        {
                            ref Weights w  = ref batchedWeights.ElementAt(foundIndex);
                            w             += pairWeight;
                        }
                        else
                        {
                            hashmap.Add(cfl, clipFrameLookups.Length);
                            clipFrameLookups.Add(cfl.lookup);
                            batchedWeights.Add(pairWeight);
                            targetListenerIndices.Add(cfl.listenerIndex);
                        }
                    }
                    listenerEmitterPairs.EndForEachIndex();
                    pairWeights.EndForEachIndex();
                }
            }
        }

        [BurstCompile]
        public struct BatchLoopedJob : IJob
        {
            [ReadOnly] public NativeArray<LoopedEmitter> emitters;
            [ReadOnly] public NativeStream.Reader        pairWeights;
            [ReadOnly] public NativeStream.Reader        listenerEmitterPairs;  //int2: listener, emitter

            public NativeList<ClipFrameLookup> clipFrameLookups;
            public NativeList<Weights>         batchedWeights;
            public NativeList<int>             targetListenerIndices;

            public void Execute()
            {
                var hashmap = new NativeHashMap<ClipFrameListener, int>(INITIAL_ALLOCATION_SIZE, Allocator.Temp);
                if (clipFrameLookups.Capacity < INITIAL_ALLOCATION_SIZE)
                    clipFrameLookups.Capacity = INITIAL_ALLOCATION_SIZE;
                if (batchedWeights.Capacity < INITIAL_ALLOCATION_SIZE)
                    batchedWeights.Capacity = INITIAL_ALLOCATION_SIZE;
                if (targetListenerIndices.Capacity < INITIAL_ALLOCATION_SIZE)
                    targetListenerIndices.Capacity = INITIAL_ALLOCATION_SIZE;

                int streamIndices = listenerEmitterPairs.ForEachCount;
                for (int streamIndex = 0; streamIndex < streamIndices; streamIndex++)
                {
                    int countInStream = listenerEmitterPairs.BeginForEachIndex(streamIndex);
                    pairWeights.BeginForEachIndex(streamIndex);

                    for (; countInStream > 0; countInStream--)
                    {
                        int2 listenerEmitterPairIndices = listenerEmitterPairs.Read<int2>();
                        var  pairWeight                 = pairWeights.Read<Weights>();

                        var e = emitters[listenerEmitterPairIndices.y];
                        if (!e.source.clip.IsCreated)
                            continue;
                        ClipFrameListener cfl = new ClipFrameListener
                        {
                            lookup        = new ClipFrameLookup { clip = e.source.clip, spawnFrameOrOffset = e.source.m_loopOffset },
                            listenerIndex                                                                  = listenerEmitterPairIndices.x
                        };
                        if (hashmap.TryGetValue(cfl, out int foundIndex))
                        {
                            ref Weights w  = ref batchedWeights.ElementAt(foundIndex);
                            w             += pairWeight;
                        }
                        else
                        {
                            hashmap.Add(cfl, clipFrameLookups.Length);
                            clipFrameLookups.Add(cfl.lookup);
                            batchedWeights.Add(pairWeight);
                            targetListenerIndices.Add(cfl.listenerIndex);
                        }
                    }
                    listenerEmitterPairs.EndForEachIndex();
                    pairWeights.EndForEachIndex();
                }
            }
        }

        private struct ClipFrameListener : IEquatable<ClipFrameListener>
        {
            public ClipFrameLookup lookup;
            public int             listenerIndex;

            public bool Equals(ClipFrameListener other)
            {
                return lookup.Equals(other.lookup) && listenerIndex.Equals(other.listenerIndex);
            }

            public unsafe override int GetHashCode()
            {
                return new int3((int)((ulong)lookup.clip.GetUnsafePtr() >> 4), lookup.spawnFrameOrOffset, listenerIndex).GetHashCode();
            }
        }
    }
}

