using System;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock.Authoring
{
    public static class TerrainColliderSmartBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a BlobAssetReference<TerrainColliderBlob> that contains unscaled heightmap terrain data
        /// </summary>
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
        /// <returns>A handle to the pending blob asset that can be resolved later</returns>
        public static SmartBlobberHandle<TerrainColliderBlob> RequestCreateTerrainBlobAsset(this IBaker baker,
                                                                                            int quadsPerRow,
                                                                                            NativeArray<short>      heightsRowMajor,
                                                                                            NativeArray<BitField32> quadTriangleSplitParitiesRowMajor,
                                                                                            NativeArray<BitField64> trianglesValid,
                                                                                            in FixedString128Bytes name)
        {
            return baker.RequestCreateBlobAsset<TerrainColliderBlob, TerrainColliderBakeData>(new TerrainColliderBakeData
            {
                quadsPerRow                       = quadsPerRow,
                heightsRowMajor                   = heightsRowMajor,
                quadTriangleSplitParitiesRowMajor = quadTriangleSplitParitiesRowMajor,
                trianglesValid                    = trianglesValid,
                name                              = name
            });
        }
    }

    public struct TerrainColliderBakeData : ISmartBlobberRequestFilter<TerrainColliderBlob>
    {
        public int                     quadsPerRow;
        public NativeArray<short>      heightsRowMajor;
        public NativeArray<BitField32> quadTriangleSplitParitiesRowMajor;
        public NativeArray<BitField64> trianglesValid;
        public FixedString128Bytes     name;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (heightsRowMajor.Length % (quadsPerRow + 1) != 0)
            {
                UnityEngine.Debug.LogError(
                    $"Psyshock failed to bake the terrain {name}. The number of heights {heightsRowMajor.Length} is not a multiple of (quads per row {quadsPerRow} + 1)");
                return false;
            }
            var rowCount         = heightsRowMajor.Length / (quadsPerRow + 1);
            var quadCount        = quadsPerRow * rowCount;
            var expectedElements = CollectionHelper.Align(quadCount, 32) / 32;
            if (quadTriangleSplitParitiesRowMajor.Length < expectedElements)
            {
                UnityEngine.Debug.LogError(
                    $"Psyshock failed to bake the terrain {name}. The number of triangle split parity bitfields {quadTriangleSplitParitiesRowMajor.Length} is less than the required amount of {expectedElements}");
                return false;
            }
            if (trianglesValid.Length < expectedElements)
            {
                UnityEngine.Debug.LogError(
                    $"Psyshock failed to bake the terrain {name}. The number of triangle validity bitfields {trianglesValid.Length} is less than the required amount of {expectedElements}");
                return false;
            }

            baker.AddComponent(blobBakingEntity, new TerrainToBake { name = name, quadsPerRow = quadsPerRow });
            baker.AddBuffer<TerrainHeightToBake>(            blobBakingEntity).Reinterpret<short>().AddRange(heightsRowMajor);
            baker.AddBuffer<TerrainSplitParitiesToBake>(     blobBakingEntity).Reinterpret<BitField32>().AddRange(quadTriangleSplitParitiesRowMajor);
            baker.AddBuffer<TerrainTriangleValiditiesToBake>(blobBakingEntity).Reinterpret<BitField64>().AddRange(trianglesValid);

            return true;
        }
    }

    [TemporaryBakingType]
    [InternalBufferCapacity(0)]
    internal struct TerrainHeightToBake : IBufferElementData
    {
        public short height;
    }

    [TemporaryBakingType]
    [InternalBufferCapacity(0)]
    internal struct TerrainSplitParitiesToBake : IBufferElementData
    {
        public BitField32 bits;
    }

    [TemporaryBakingType]
    [InternalBufferCapacity(0)]
    internal struct TerrainTriangleValiditiesToBake : IBufferElementData
    {
        public BitField64 bits;
    }

    [TemporaryBakingType]
    internal struct TerrainToBake : IComponentData
    {
        public FixedString128Bytes name;
        public int                 quadsPerRow;
    }
}

namespace Latios.Psyshock.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    [BurstCompile]
    public partial struct TerrainColliderSmartBlobberSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new SmartBlobberTools<TerrainColliderBlob>().Register(state.World);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new Job().ScheduleParallel();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct Job : IJobEntity
        {
            public void Execute(ref SmartBlobberResult result,
                                in TerrainToBake terrain,
                                in DynamicBuffer<TerrainHeightToBake>             heightsBuffer,
                                in DynamicBuffer<TerrainSplitParitiesToBake>      paritiesBuffer,
                                in DynamicBuffer<TerrainTriangleValiditiesToBake> validitiesBuffer)
            {
                var builder    = new BlobBuilder(Allocator.Temp);
                var heights    = heightsBuffer.AsNativeArray().Reinterpret<short>().AsReadOnlySpan();
                var parities   = paritiesBuffer.AsNativeArray().Reinterpret<BitField32>().AsReadOnlySpan();
                var validities = validitiesBuffer.AsNativeArray().Reinterpret<BitField64>().AsReadOnlySpan();
                var blob       = TerrainColliderBlob.BuildBlob(ref builder, terrain.quadsPerRow, heights, parities, validities, in terrain.name, Allocator.Persistent);
                result.blob    = UnsafeUntypedBlobAssetReference.Create(blob);
            }
        }
    }
}

