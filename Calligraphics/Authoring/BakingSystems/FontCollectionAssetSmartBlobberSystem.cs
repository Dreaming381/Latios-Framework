using Latios.Authoring;
using Latios.Authoring.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Calligraphics.Authoring
{
    public static class FontCollectionSmartBlobberExtensions
    {
        public static SmartBlobberHandle<FontLoadDescriptionsBlob> RequestCreateBlobAsset(this IBaker baker, FontCollectionAsset fontCollectionAsset)
        {
            return baker.RequestCreateBlobAsset<FontLoadDescriptionsBlob, FontCollectionAssetBakeData>(new FontCollectionAssetBakeData { fontCollection = fontCollectionAsset });
        }
    }

    public struct FontCollectionAssetBakeData : ISmartBlobberRequestFilter<FontLoadDescriptionsBlob>
    {
        public UnityObjectRef<FontCollectionAsset> fontCollection;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (!fontCollection.IsValid())
                return false;
            baker.AddComponent(blobBakingEntity, new FontCollectionLoadDescriptionBlobBakeData { fontCollection = fontCollection });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct FontCollectionLoadDescriptionBlobBakeData : IComponentData
    {
        public UnityObjectRef<FontCollectionAsset> fontCollection;
    }
}

namespace Latios.Calligraphics.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public partial struct FontCollectionAssetSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<FontLoadDescriptionsBlob>().Register(state.World);
        }

        public void OnUpdate(ref SystemState state)
        {
            var map = new NativeHashMap<UnityObjectRef<FontCollectionAsset>, UnsafeUntypedBlobAssetReference>(8, state.WorldUpdateAllocator);

            foreach ((var bakeData, var result) in SystemAPI.Query<FontCollectionLoadDescriptionBlobBakeData,
                                                                   RefRW<SmartBlobberResult> >().WithOptions(EntityQueryOptions.IncludePrefab |
                                                                                                             EntityQueryOptions.IncludeDisabledEntities))
            {
                if (map.TryGetValue(bakeData.fontCollection, out var cachedBlob))
                {
                    result.ValueRW.blob = cachedBlob;
                    continue;
                }

                var     builder    = new BlobBuilder(Allocator.Temp);
                ref var root       = ref builder.ConstructRoot<FontLoadDescriptionsBlob>();
                var     collection = bakeData.fontCollection.Value;

                builder.ConstructFromList(ref root.descriptions, collection.fontReferences);
                root.collectionAssetName = collection.name;
                var blob                 = UnsafeUntypedBlobAssetReference.Create(builder.CreateBlobAssetReference<FontLoadDescriptionsBlob>(Allocator.Persistent));
                result.ValueRW.blob      = blob;
                map.Add(bakeData.fontCollection, blob);
            }
        }
    }
}

