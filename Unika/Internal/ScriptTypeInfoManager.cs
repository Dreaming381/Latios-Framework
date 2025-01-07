using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    internal struct ScriptTypeInfoManager
    {
        static short runtimeInterfaceCounter = 1;
        static short runtimeScriptCounter    = 1;

        static HashSet<System.Type>                     s_capturedInterfaces  = new HashSet<System.Type>();
        static Dictionary<System.Type, ulong>           s_stableTypeHashCache = new Dictionary<System.Type, ulong>();
        static HashSet<System.Type>                     s_noOffsetsCache      = new HashSet<System.Type>();
        static NativeList<TypeManager.EntityOffsetInfo> s_entityOffsetCache;
        static NativeList<TypeManager.EntityOffsetInfo> s_blobOffsetCache;
        static NativeList<TypeManager.EntityOffsetInfo> s_assetOffsetCache;
        static NativeList<TypeManager.EntityOffsetInfo> s_objectOffsetCache;

        public static void InitializeStatics()
        {
            s_entityOffsetCache = new NativeList<TypeManager.EntityOffsetInfo>(Allocator.Persistent);
            s_blobOffsetCache   = new NativeList<TypeManager.EntityOffsetInfo>(Allocator.Persistent);
            s_assetOffsetCache  = new NativeList<TypeManager.EntityOffsetInfo>(Allocator.Persistent);
            s_objectOffsetCache = new NativeList<TypeManager.EntityOffsetInfo>(Allocator.Persistent);

            ScriptStableHashToIdAndMaskMap.s_map.Data = new UnsafeHashMap<ulong, IdAndMask>(512, Allocator.Persistent);
            ScriptMetadata.s_metadataArray.Data       = new UnsafeList<ScriptMetadata>(512, Allocator.Persistent);
            ScriptMetadata.s_names.Data               = new UnsafeList<UnsafeText>(512, Allocator.Persistent);
            ScriptMetadata.s_offsets.Data             = new UnsafeList<TypeManager.EntityOffsetInfo>(4096, Allocator.Persistent);

            ScriptMetadata.s_metadataArray.Data.Add(default);
            string invalid     = "Null Script";
            var    invalidText = new UnsafeText(System.Text.Encoding.UTF8.GetByteCount(invalid), Allocator.Persistent);
            invalidText.CopyFrom(invalid);
            ScriptMetadata.s_names.Data.Add(invalidText);
        }

        public static void DisposeStatics()
        {
            s_entityOffsetCache.Dispose();
            s_blobOffsetCache.Dispose();
            s_assetOffsetCache.Dispose();
            s_objectOffsetCache.Dispose();

            foreach (var t in ScriptMetadata.s_names.Data)
                t.Dispose();

            ScriptStableHashToIdAndMaskMap.s_map.Data.Dispose();
            ScriptMetadata.s_metadataArray.Data.Dispose();
            ScriptMetadata.s_names.Data.Dispose();
            ScriptMetadata.s_offsets.Data.Dispose();
        }

        public static void InitializeInterface<T>() where T : IUnikaInterface
        {
            var type = typeof(T);
            if (!s_capturedInterfaces.Add(type))
                return;

            ScriptInterfaceInfoLookup<T>.s_runtimeTypeIndex.Data.runtimeId = runtimeInterfaceCounter;
            ScriptInterfaceInfoLookup<T>.s_runtimeTypeIndex.Data.bloomMask = BloomMaskOf<T>();
            runtimeInterfaceCounter++;
        }

        public static unsafe void InitializeScriptType<T>(System.ReadOnlySpan<IdAndMask> runtimeInterfacesImplemented) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            UnityEngine.Assertions.Assert.IsTrue((ulong)runtimeScriptCounter <= ScriptHeader.kMaxTypeIndex);

            ulong mask = 0;
            foreach (var i in runtimeInterfacesImplemented)
            {
                mask |= i.bloomMask;
            }
            var idAndMask = new IdAndMask { bloomMask = mask, runtimeId = runtimeScriptCounter };
            ScriptTypeInfoLookup<T>.s_runtimeTypeIndex.Data             = idAndMask;
            runtimeScriptCounter++;

            var stableHash = TypeHash.CalculateStableTypeHash(typeof(T), s_stableTypeHashCache);
            ScriptStableHashToIdAndMaskMap.s_map.Data.Add(stableHash, idAndMask);

            ScriptTypeExtraction.AddExtractorType<T>();
            UnityEngine.Assertions.Assert.IsTrue(runtimeScriptCounter == ScriptTypeExtraction.extractors.Count);

            s_entityOffsetCache.Clear();
            s_blobOffsetCache.Clear();
            s_assetOffsetCache.Clear();
            s_objectOffsetCache.Clear();

            EntityRemapUtility.CalculateFieldOffsetsUnmanaged(typeof(T),
                                                              out var hasEntityRefs,
                                                              out var hasBlobRefs,
                                                              out var hasWeakAssetRefs,
                                                              out var hasUnityObjectRefs,
                                                              ref s_entityOffsetCache,
                                                              ref s_blobOffsetCache,
                                                              ref s_assetOffsetCache,
                                                              ref s_objectOffsetCache,
                                                              s_noOffsetsCache);

            var name       = typeof(T).FullName;
            var nameCount  = System.Text.Encoding.UTF8.GetByteCount(name);
            var unsafeName = new UnsafeText(nameCount, Allocator.Persistent);
            unsafeName.CopyFrom(name);
            ScriptMetadata.s_names.Data.Add(unsafeName);
            var entityStart = ScriptMetadata.s_offsets.Data.Length;
            var entityCount = s_entityOffsetCache.Length;
            ScriptMetadata.s_offsets.Data.AddRange(*s_entityOffsetCache.GetUnsafeList());
            var blobStart = ScriptMetadata.s_offsets.Data.Length;
            var blobCount = s_blobOffsetCache.Length;
            ScriptMetadata.s_offsets.Data.AddRange(*s_blobOffsetCache.GetUnsafeList());
            var assetStart = ScriptMetadata.s_offsets.Data.Length;
            var assetCount = s_assetOffsetCache.Length;
            ScriptMetadata.s_offsets.Data.AddRange(*s_assetOffsetCache.GetUnsafeList());
            var objectStart = ScriptMetadata.s_offsets.Data.Length;
            var objectCount = s_objectOffsetCache.Length;
            ScriptMetadata.s_offsets.Data.AddRange(*s_objectOffsetCache.GetUnsafeList());

            ScriptMetadata.s_metadataArray.Data.Add(new ScriptMetadata
            {
                stableHash  = stableHash,
                bloomMask   = mask,
                size        = UnsafeUtility.SizeOf<T>(),
                alignment   = UnsafeUtility.AlignOf<T>(),
                entityStart = entityStart,
                entityCount = entityCount,
                blobStart   = blobStart,
                blobCount   = blobCount,
                assetStart  = assetStart,
                assetCount  = assetCount,
                objectStart = objectStart,
                objectCount = objectCount,
            });
        }

        public struct IdAndMask
        {
            public ulong bloomMask;
            public short runtimeId;
        }

        public static IdAndMask GetInterfaceRuntimeIdAndMask<T>() where T : IUnikaInterface
        {
            return ScriptInterfaceInfoLookup<T>.s_runtimeTypeIndex.Data;
        }

        public static IdAndMask GetScriptRuntimeIdAndMask<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            return ScriptTypeInfoLookup<T>.s_runtimeTypeIndex.Data;
        }

        public static bool TryGetRuntimeIdAndMask(ulong stableHash, out IdAndMask result)
        {
            return ScriptStableHashToIdAndMaskMap.s_map.Data.TryGetValue(stableHash, out result);
        }

        public static int scriptTypeCount => ScriptMetadata.s_metadataArray.Data.Length;

        public static ulong GetBloomMask(short runtimeId)
        {
            return ScriptMetadata.s_metadataArray.Data[runtimeId].bloomMask;
        }

        public static ulong GetStableHash(short runtimeId)
        {
            return ScriptMetadata.s_metadataArray.Data[runtimeId].stableHash;
        }

        public static int2 GetSizeAndAlignement(short runtimeId)
        {
            var meta = ScriptMetadata.s_metadataArray.Data[runtimeId];
            return new int2(meta.size, meta.alignment);
        }

        public static UnsafeText GetName(short runtimeId)
        {
            return ScriptMetadata.s_names.Data[runtimeId];
        }

        public static unsafe System.Span<TypeManager.EntityOffsetInfo> GetEntityRemap(short runtimeId)
        {
            var meta = ScriptMetadata.s_metadataArray.Data[runtimeId];
            var data = ScriptMetadata.s_offsets.Data;
            var span = new System.Span<TypeManager.EntityOffsetInfo>(data.Ptr, data.Length);
            return span.Slice(meta.entityStart, meta.entityCount);
        }

        public static unsafe System.Span<TypeManager.EntityOffsetInfo> GetBlobRemap(short runtimeId)
        {
            var meta = ScriptMetadata.s_metadataArray.Data[runtimeId];
            var data = ScriptMetadata.s_offsets.Data;
            var span = new System.Span<TypeManager.EntityOffsetInfo>(data.Ptr, data.Length);
            return span.Slice(meta.blobStart, meta.blobCount);
        }

        public static unsafe System.Span<TypeManager.EntityOffsetInfo> GetAssetRemap(short runtimeId)
        {
            var meta = ScriptMetadata.s_metadataArray.Data[runtimeId];
            var data = ScriptMetadata.s_offsets.Data;
            var span = new System.Span<TypeManager.EntityOffsetInfo>(data.Ptr, data.Length);
            return span.Slice(meta.assetStart, meta.assetCount);
        }

        public static unsafe System.Span<TypeManager.EntityOffsetInfo> GetObjectRemap(short runtimeId)
        {
            var meta = ScriptMetadata.s_metadataArray.Data[runtimeId];
            var data = ScriptMetadata.s_offsets.Data;
            var span = new System.Span<TypeManager.EntityOffsetInfo>(data.Ptr, data.Length);
            return span.Slice(meta.objectStart, meta.objectCount);
        }

        private struct ScriptInterfaceInfoLookup<T> where T : IUnikaInterface
        {
            public static readonly SharedStatic<IdAndMask> s_runtimeTypeIndex = SharedStatic<IdAndMask>.GetOrCreate<ScriptTypeInfoManager, T>();
        }

        private struct ScriptTypeInfoLookup<T> where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            public static readonly SharedStatic<IdAndMask> s_runtimeTypeIndex = SharedStatic<IdAndMask>.GetOrCreate<ScriptTypeInfoManager, T>();
        }

        private unsafe struct ScriptStableHashToIdAndMaskMap
        {
            public static readonly SharedStatic<UnsafeHashMap<ulong, IdAndMask> > s_map =
                SharedStatic<UnsafeHashMap<ulong, IdAndMask> >.GetOrCreate<ScriptStableHashToIdAndMaskMap>();
        }

        private unsafe struct ScriptMetadata
        {
            public ulong stableHash;
            public ulong bloomMask;
            public int   size;
            public int   alignment;
            public int   entityStart;
            public int   entityCount;
            public int   blobStart;
            public int   blobCount;
            public int   assetStart;
            public int   assetCount;
            public int   objectStart;
            public int   objectCount;

            public static readonly SharedStatic<UnsafeList<ScriptMetadata> > s_metadataArray = SharedStatic<UnsafeList<ScriptMetadata> >.GetOrCreate<ScriptMetadata,
                                                                                                                                                     ulong>();
            public static readonly SharedStatic<UnsafeList<UnsafeText> > s_names = SharedStatic<UnsafeList<UnsafeText> >.GetOrCreate<ScriptMetadata,
                                                                                                                                     UnsafeText>();
            public static readonly SharedStatic<UnsafeList<TypeManager.EntityOffsetInfo> > s_offsets =
                SharedStatic<UnsafeList<TypeManager.EntityOffsetInfo> >.GetOrCreate<ScriptMetadata, TypeManager.EntityOffsetInfo>();
        }

        static ulong BloomMaskOf<T>() where T : IUnikaInterface
        {
            // This is largely derived from TypeManager, except we use Rng instead of Random.
            // Todo: Since we don't require the bitwise-or'ing from queries (at least not in the common case),
            // we may not need as much in the bloom filter. Perhaps we could get by with fewer bits? Would doing
            // so improve performance at all?

            const int k        = 5;
            const int maxShift = 8 * sizeof(ulong);

            long  typeHash = BurstRuntime.GetHashCode64<T>();
            var   rng      = new Rng((uint)(typeHash & 0xffffffff)).GetSequence((int)(typeHash >> 32));
            ulong mask     = 0;
            for (int i = 0; i < k; i++)
            {
                mask |= 1ul << rng.NextInt(0, maxShift);
            }
            return mask;
        }
    }
}

