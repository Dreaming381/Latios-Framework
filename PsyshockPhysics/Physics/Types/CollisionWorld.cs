using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public struct CollisionWorld
    {
        internal CollisionLayer               layer;
        internal NativeList<short>            archetypeIndicesByBody;
        internal NativeList<EntityArchetype>  archetypesInLayer;
        internal NativeList<int2>             archetypeStartsAndCountsByBucket;  // Archetype is inner array
        internal NativeList<int>              archetypeBodyIndicesByBucket;  // Relative
        internal NativeList<IntervalTreeNode> archetypeIntervalTreesByBucket;
        internal byte                         worldIndex;

        internal CollisionWorld(CollisionLayerSettings settings, AllocatorManager.AllocatorHandle allocator, byte worldIndex)
        {
            layer                            = new CollisionLayer(settings, allocator);
            archetypeIndicesByBody           = new NativeList<short>(allocator);
            archetypesInLayer                = new NativeList<EntityArchetype>(allocator);
            archetypeStartsAndCountsByBucket = new NativeList<int2>(allocator);
            archetypeBodyIndicesByBucket     = new NativeList<int>(allocator);
            archetypeIntervalTreesByBucket   = new NativeList<IntervalTreeNode>(allocator);
            this.worldIndex                  = worldIndex;
        }

        /// <summary>
        /// Creates an empty CollisionWorld. This is useful when you just need an empty world in order to reuse some other codepath.
        /// However, if you need a normal world, you should use Physics.BuildCollisionWorld() instead.
        /// </summary>
        /// <param name="settings">The settings to use for the world. You typically want to match this with other layers when using FindPairs.</param>
        /// <param name="allocator">The Allocator to use for this world. Despite being empty, this world is still allocated and may require disposal.</param>
        /// <param name="worldIndex">An index allocated to the world which may be stored in a CollisionWorldIndex component on an entity</param>
        /// <returns>A CollisionWorld with zero bodies, but with the bucket distribution matching the specified settings</returns>
        public static CollisionWorld CreateEmptyCollisionWorld(CollisionLayerSettings settings, AllocatorManager.AllocatorHandle allocator, byte worldIndex = 1)
        {
            CheckWorldIndexIsValid(worldIndex);

            return new CollisionWorld
            {
                layer                            = CollisionLayer.CreateEmptyCollisionLayer(settings, allocator),
                archetypeIndicesByBody           = new NativeList<short>(allocator),
                archetypesInLayer                = new NativeList<EntityArchetype>(allocator),
                archetypeStartsAndCountsByBucket = new NativeList<int2>(allocator),
                archetypeBodyIndicesByBucket     = new NativeList<int>(allocator),
                archetypeIntervalTreesByBucket   = new NativeList<IntervalTreeNode>(allocator),
                worldIndex                       = worldIndex
            };
        }

        /// <summary>
        /// Disposes the layer immediately
        /// </summary>
        public void Dispose()
        {
            layer.Dispose();
            archetypeIndicesByBody.Dispose();
            archetypesInLayer.Dispose();
            archetypeStartsAndCountsByBucket.Dispose();
            archetypeBodyIndicesByBucket.Dispose();
            archetypeIntervalTreesByBucket.Dispose();
        }

        /// <summary>
        /// Disposes the layer using jobs
        /// </summary>
        /// <param name="inputDeps">A JobHandle to wait upon before disposing</param>
        /// <returns>The final jobHandle of the disposed layers</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            layer.worldSubdivisionsPerAxis = 0;
            return CollectionsExtensions.CombineDependencies(stackalloc JobHandle[]
            {
                layer.bucketStartsAndCounts.Dispose(inputDeps),
                layer.xmins.Dispose(inputDeps),
                layer.xmaxs.Dispose(inputDeps),
                layer.yzminmaxs.Dispose(inputDeps),
                layer.intervalTrees.Dispose(inputDeps),
                layer.bodies.Dispose(inputDeps),
                layer.srcIndices.Dispose(inputDeps),
                archetypeIndicesByBody.Dispose(inputDeps),
                archetypesInLayer.Dispose(inputDeps),
                archetypeStartsAndCountsByBucket.Dispose(inputDeps),
                archetypeBodyIndicesByBucket.Dispose(inputDeps),
                archetypeIntervalTreesByBucket.Dispose(inputDeps)
            });
        }

        /// <summary>
        /// The number of elements in the layer
        /// </summary>
        public int count => layer.xmins.Length;
        /// <summary>
        /// The number of cells in the layer, including the "catch-all" cell but ignoring the NaN cell
        /// </summary>
        public int bucketCount => layer.bucketCount;
        /// <summary>
        /// True if the CollisionLayer has been created
        /// </summary>
        public bool IsCreated => worldIndex != 0;
        /// <summary>
        /// Read-Only access to the collider bodiesArray stored in the CollisionLayer ordered by bodyIndex
        /// </summary>
        public NativeArray<ColliderBody>.ReadOnly colliderBodies => layer.colliderBodies;
        /// <summary>
        /// Read-Only access to the source indices corresponding to each bodyIndex. CollisionLayers
        /// reorder bodiesArray for better performance. The source indices specify the original index of
        /// each body in an EntityQuery or NativeArray of ColliderBody.
        /// </summary>
        public NativeArray<int>.ReadOnly sourceIndices => layer.sourceIndices;
        /// <summary>
        /// Gets an Aabb for an associated index in the collision layer ordered by bodyIndex
        /// </summary>
        public Aabb GetAabb(int index) => layer.GetAabb(index);
        /// <summary>
        /// The number of unique entity archetypes that were found in the CollisionWorld
        /// </summary>
        public int archetypeCount => archetypesInLayer.Length;
        /// <summary>
        /// Read-Only access to each body's archetype index within the CollisionWorld ordered by bodyIndex.
        /// </summary>
        public NativeArray<short>.ReadOnly archetypeIndices => archetypeIndicesByBody.AsReadOnly();

        /// <summary>
        /// The underlying CollisionLayer, intended for read-only access.
        /// </summary>
        public CollisionLayer collisionLayer => layer;

        /// <summary>
        /// Creates a mask that can be used when performing query operations on this CollisionWorld
        /// to only include entities which match the specified EntityQueryMask without filtering.
        /// This mask should typically be created from inside the job and cached.
        /// </summary>
        /// <param name="entityQueryMask">An EntityQueryMask which will identify entities in the CollisionWorld to be considered candidates in queries</param>
        /// <returns>A Mask which can be used as a parameter in various spatial query methods alongside this CollisionWorld.</returns>
        public Mask CreateMask(EntityQueryMask entityQueryMask)
        {
            Span<short> indices = stackalloc short[archetypesInLayer.Length];
            int         count   = 0;
            for (int i = 0; i < archetypesInLayer.Length; i++)
            {
                if (entityQueryMask.Matches(archetypesInLayer[i]))
                {
                    indices[count] = (short)i;
                    count++;
                }
            }
            return new Mask(indices.Slice(0, count));
        }

        /// <summary>
        /// Creates a mask that can be used when performing query operations on this CollisionWorld
        /// to only include entities which match the specified entity query description without filtering.
        /// This mask should typically be created from inside the job and cached.
        /// </summary>
        /// <param name="entityStorageInfoLookup">The EntityStorageInfoLookup used for fetching entities and ensuring safety</param>
        /// <param name="with">The component types that should be present (enabled states are not considered)</param>
        /// <param name="withAny">The component types where at least one should be present (enabled states are not considered)</param>
        /// <param name="without">The component types that should be absent (disabled components don't count as absent)</param>
        /// <param name="options">EntityQuery options to use. FilterWriteGroup and IgnoreComponentEnabledState are not acknowledged.</param>
        /// <returns>A Mask which can be used as a parameter in various spatial query methods alongside this CollisionWorld.</returns>
        public Mask CreateMask(EntityStorageInfoLookup entityStorageInfoLookup,
                               ComponentTypeSet with,
                               ComponentTypeSet withAny = default,
                               ComponentTypeSet without = default,
                               EntityQueryOptions options = EntityQueryOptions.Default)
        {
            return CreateMask(new TempQuery(default, entityStorageInfoLookup, with, withAny, without, options));
        }

        /// <summary>
        /// Creates a mask that can be used when performing query operations on this CollisionWorld
        /// to only include entities which match the specified TempQuery.
        /// This mask should typically be created from inside the job and cached.
        /// </summary>
        /// <param name="entityQueryMask">A TempQuery which will identify entities in the CollisionWorld to be considered candidates in queries.
        /// The TempQuery's internal array of EntityArchetype will be ignored, and constructing a TempQuery with that parameter defaulted is valid.</param>
        /// <returns>A Mask which can be used as a parameter in various spatial query methods alongside this CollisionWorld.</returns>
        public Mask CreateMask(in TempQuery tempQuery)
        {
            CheckValid(tempQuery.entityStorageInfoLookup);
            Span<short> indices = stackalloc short[archetypesInLayer.Length];
            int         count   = 0;
            for (int i = 0; i < archetypesInLayer.Length; i++)
            {
                if (tempQuery.MatchesArchetype(archetypesInLayer[i]))
                {
                    indices[count] = (short)i;
                    count++;
                }
            }
            return new Mask(indices.Slice(0, count));
        }

        /// <summary>
        /// A mask based on some kind of entity query which can select and filter entities in the CollisionWorld it was made from.
        /// Instances should be retrieved from the CollisionWorld within the same job and thread they are used.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public unsafe struct Mask
        {
            /// <summary>
            /// Returns false if this Mask is still the default value
            /// </summary>
            public bool isCreated => mode != Mode.Default;

            enum Mode
            {
                Default,
                Empty,
                Bitmask,
                ShortList,
                LongList
            }

            [FieldOffset(0)]
            short current;

            [FieldOffset(2)]
            byte modeAndShortListCount;

            Mode mode
            {
                get => (Mode)(modeAndShortListCount & 0x7);
                set => Bits.SetBits(ref modeAndShortListCount, 0, 3, (byte)value);
            }
            int shortCount
            {
                get => modeAndShortListCount >> 3;
                set => Bits.SetBits(ref modeAndShortListCount, 3, 5, (byte)value);
            }

            [FieldOffset(3)]
            byte maskShortIndex;  // Bitmask index or shortList next index

            [FieldOffset(4)]
            uint bitmaskFinal;

            [FieldOffset(8)]
            fixed ulong bitmask[7];

            [FieldOffset(4)]
            fixed short shortList[30];

            [FieldOffset(4)]
            short longListIndex;

            [FieldOffset(8)]
            UnsafeList<short> longList;

            public short CountArchetypes()
            {
                switch (mode)
                {
                    case Mode.Default:
                    case Mode.Empty: return 0;
                    case Mode.Bitmask:
                    {
                        int result = 0;
                        for (int i = 0; i < 7; i++)
                            result += math.countbits(bitmask[i]);
                        result     += math.countbits(bitmaskFinal);
                        return (short)result;
                    }
                    case Mode.ShortList: return (short)shortCount;
                    case Mode.LongList: return (short)longList.Length;
                    default: return 0;
                }
            }

            public Mask GetEnumerator() => this;

            public short Current => current;

            public bool MoveNext()
            {
                switch (mode)
                {
                    case Mode.Empty: return false;
                    case Mode.Bitmask:
                    {
                        while (maskShortIndex < 7)
                        {
                            if (bitmask[maskShortIndex] == 0)
                            {
                                maskShortIndex++;
                                continue;
                            }
                            var tz                   = math.tzcnt(bitmask[maskShortIndex]);
                            current                  = (short)(64 * maskShortIndex + tz);
                            bitmask[maskShortIndex] ^= 1ul << tz;
                            return true;
                        }
                        if (bitmaskFinal != 0)
                        {
                            var tz        = math.tzcnt(bitmaskFinal);
                            current       = (short)(64 * 7 + tz);
                            bitmaskFinal ^= 1u << tz;
                            return true;
                        }
                        return false;
                    }
                    case Mode.ShortList:
                    {
                        if (maskShortIndex < shortCount)
                        {
                            current = shortList[maskShortIndex];
                            maskShortIndex++;
                            return true;
                        }
                        return false;
                    }
                    case Mode.LongList:
                    {
                        if (longListIndex < longList.Length)
                        {
                            current = longList[longListIndex];
                            longListIndex++;
                            return true;
                        }
                        return false;
                    }
                    default: return false;
                }
            }

            internal Mask(ReadOnlySpan<short> indices)
            {
                this = default;
                if (indices.Length == 0)
                {
                    return;
                }

                // Always prefer the shortlist over anything else since it is the cheapest
                if (indices.Length <= 30)
                {
                    maskShortIndex = 0;
                    mode           = Mode.ShortList;
                    shortCount     = indices.Length;
                    for (int i = 0; i < indices.Length; i++)
                        shortList[i] = indices[i];
                }
                else if (indices[indices.Length - 1] < 7 * 64 + 32)
                {
                    maskShortIndex = 0;
                    mode           = Mode.Bitmask;
                    foreach (var index in indices)
                    {
                        var ul  = index >> 6;
                        var bit = index & 0x3f;
                        if (ul == 7)
                            bitmaskFinal |= 1u << bit;
                        else
                            bitmask[ul] |= 1ul << bit;
                    }
                }
                else
                {
                    longListIndex = 0;
                    mode          = Mode.LongList;
                    longList      = new UnsafeList<short>(indices.Length, Allocator.Temp);
                    foreach (var index in indices)
                        longList.AddNoResize(index);
                }
            }
        }

        internal WorldBucket GetBucket(int bucketIndex)
        {
            int start = layer.bucketStartsAndCounts[bucketIndex].x;
            int count = layer.bucketStartsAndCounts[bucketIndex].y;
            return new WorldBucket
            {
                slices                   = layer.GetBucketSlices(bucketIndex),
                archetypeStartsAndCounts = archetypeStartsAndCountsByBucket.AsArray().GetSubArray(archetypesInLayer.Length * bucketIndex, archetypesInLayer.Length),
                archetypeBodyIndices     = archetypeBodyIndicesByBucket.AsArray().GetSubArray(start, count),
                archetypeIntervalTrees   = archetypeIntervalTreesByBucket.AsArray().GetSubArray(start, count)
            };
        }

        [Conditional("ENABLE_UNITY_COLLECTION_CHECKS")]
        internal static void CheckWorldIndexIsValid(byte worldIndex)
        {
            if (worldIndex == 0)
                throw new ArgumentOutOfRangeException("The worldIndex must be greater than 0 in a CollisionWorld");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckValid(EntityStorageInfoLookup esil) => esil.Exists(Entity.Null);
    }

    internal struct WorldBucket
    {
        public BucketSlices                  slices;
        public NativeArray<int2>             archetypeStartsAndCounts;
        public NativeArray<int>              archetypeBodyIndices;
        public NativeArray<IntervalTreeNode> archetypeIntervalTrees;
    }
}

