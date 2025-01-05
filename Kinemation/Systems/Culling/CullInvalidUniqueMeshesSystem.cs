using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CullInvalidUniqueMeshesSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().With<UniqueMeshConfig>(false).With<MaterialMeshInfo>(true).With<ChunkPerCameraCullingMask>(false, true).Build();
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var cullingContext = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>();
            var meshPool       = latiosWorld.worldBlackboardEntity.GetCollectionComponent<UniqueMeshPool>(false);
            if (cullingContext.cullIndexThisFrame == 0)
                state.Dependency = new NewFrameJob { meshPool = meshPool }.Schedule(state.Dependency);

            var changeRequests = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<ChangeRequest>(), 256, state.WorldUpdateAllocator);
            state.Dependency   = new CullJob
            {
                entityHandle   = GetEntityTypeHandle(),
                mmiHandle      = GetComponentTypeHandle<MaterialMeshInfo>(true),
                positionHandle = GetBufferTypeHandle<UniqueMeshPosition>(true),
                normalHandle   = GetBufferTypeHandle<UniqueMeshNormal>(true),
                tangentHandle  = GetBufferTypeHandle<UniqueMeshTangent>(true),
                colorHandle    = GetBufferTypeHandle<UniqueMeshColor>(true),
                uv0xyHandle    = GetBufferTypeHandle<UniqueMeshUv0xy>(true),
                uv3xyzHandle   = GetBufferTypeHandle<UniqueMeshUv3xyz>(true),
                indexHandle    = GetBufferTypeHandle<UniqueMeshIndex>(true),
                submeshHandle  = GetBufferTypeHandle<UniqueMeshSubmesh>(true),
                meshPool       = meshPool,
                configHandle   = GetComponentTypeHandle<UniqueMeshConfig>(false),
                maskHandle     = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                changeRequests = changeRequests
            }.ScheduleParallel(m_query, state.Dependency);

            state.Dependency = new UpdateRequestsJob
            {
                changeRequests = changeRequests,
                meshPool       = meshPool,
            }.Schedule(state.Dependency);
        }

        struct ChangeRequest
        {
            public BatchMeshID id;
            public bool        isInvalid;
        }

        [BurstCompile]
        struct NewFrameJob : IJob
        {
            public UniqueMeshPool meshPool;

            public void Execute() => meshPool.meshesPrevalidatedThisFrame.Clear();
        }

        [BurstCompile]
        struct CullJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                      entityHandle;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo> mmiHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshPosition>  positionHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshNormal>    normalHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshTangent>   tangentHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshColor>     colorHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv0xy>     uv0xyHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv3xyz>    uv3xyzHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshIndex>     indexHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshSubmesh>   submeshHandle;
            [ReadOnly] public UniqueMeshPool                        meshPool;

            public ComponentTypeHandle<UniqueMeshConfig>          configHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingMask> maskHandle;
            public UnsafeParallelBlockList                        changeRequests;

            [NativeSetThreadIndex]
            int threadIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ref var    chunkMask            = ref chunk.GetChunkComponentRefRW(ref maskHandle);
                var        mmis                 = (MaterialMeshInfo*)chunk.GetRequiredComponentDataPtrRO(ref mmiHandle);
                var        configurations       = (UniqueMeshConfig*)chunk.GetRequiredComponentDataPtrRO(ref configHandle);
                var        configuredBits       = chunk.GetEnabledMask(ref configHandle);
                var        enumerator           = new ChunkEntityEnumerator(true, new v128(chunkMask.lower.Value, chunkMask.upper.Value), chunk.Count);
                BitField64 furtherEvaluateLower = default, furtherEvaluateUpper = default;
                while (enumerator.NextEntityIndex(out var entityIndex))
                {
                    if (configuredBits[entityIndex])
                    {
                        if (!configurations[entityIndex].disableEmptyAndInvalidMeshCulling && !meshPool.meshesPrevalidatedThisFrame.Contains(mmis[entityIndex].MeshID))
                        {
                            if (entityIndex < 64)
                                furtherEvaluateLower.SetBits(entityIndex, true);
                            else
                                furtherEvaluateUpper.SetBits(entityIndex - 64, true);
                        }
                    }
                    else if (meshPool.invalidMeshesToCull.Contains(mmis[entityIndex].MeshID))
                    {
                        chunkMask.ClearBitAtIndex(entityIndex);
                    }
                }
                if ((furtherEvaluateLower.Value | furtherEvaluateUpper.Value) == 0)
                    return;

                // New configurations to validate
                var validator = new UniqueMeshValidator
                {
                    configurations  = configurations,
                    entities        = chunk.GetEntityDataPtrRO(entityHandle),
                    positionBuffers = chunk.GetBufferAccessor(ref positionHandle),
                    normalBuffers   = chunk.GetBufferAccessor(ref normalHandle),
                    tangentBuffers  = chunk.GetBufferAccessor(ref tangentHandle),
                    colorBuffers    = chunk.GetBufferAccessor(ref colorHandle),
                    uv0xyBuffers    = chunk.GetBufferAccessor(ref uv0xyHandle),
                    uv3xyzBuffers   = chunk.GetBufferAccessor(ref uv3xyzHandle),
                    indexBuffers    = chunk.GetBufferAccessor(ref indexHandle),
                    submeshBuffers  = chunk.GetBufferAccessor(ref submeshHandle),
                };
                validator.Init();

                enumerator = new ChunkEntityEnumerator(true, new v128(furtherEvaluateLower.Value, furtherEvaluateUpper.Value), chunk.Count);
                while (enumerator.NextEntityIndex(out var entityIndex))
                {
                    if (!validator.IsEntityIndexValidMesh(entityIndex))
                    {
                        // Mark the entity as configured now so that we don't try to process it again until the user fixes the problem.
                        configuredBits[entityIndex] = false;
                        // Mark the entity as invalid so that we can cull it in future updates, but only if the status changed.
                        if (meshPool.invalidMeshesToCull.Contains(mmis[entityIndex].MeshID))
                        {
                            changeRequests.Write(new ChangeRequest
                            {
                                id        = mmis[entityIndex].MeshID,
                                isInvalid = true
                            }, threadIndex);
                        }
                        // Mark the entity as culled
                        chunkMask.ClearBitAtIndex(entityIndex);
                    }
                    else
                    {
                        // The mesh is now valid. Report it so that we don't cull it in the future. We must always report to add to the per-frame validation list.
                        changeRequests.Write(new ChangeRequest
                        {
                            id        = mmis[entityIndex].MeshID,
                            isInvalid = false
                        }, threadIndex);
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateRequestsJob : IJob
        {
            public UnsafeParallelBlockList changeRequests;
            public UniqueMeshPool          meshPool;

            public void Execute()
            {
                var enumerator = changeRequests.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var request = enumerator.GetCurrent<ChangeRequest>();
                    if (request.isInvalid)
                    {
                        meshPool.invalidMeshesToCull.Add(request.id);
                    }
                    else
                    {
                        meshPool.invalidMeshesToCull.Remove(request.id);
                        meshPool.meshesPrevalidatedThisFrame.Add(request.id);
                    }
                }
            }
        }
    }

    internal unsafe struct UniqueMeshValidator
    {
        public UniqueMeshConfig*                  configurations;
        public Entity*                            entities;
        public BufferAccessor<UniqueMeshPosition> positionBuffers;
        public BufferAccessor<UniqueMeshNormal>   normalBuffers;
        public BufferAccessor<UniqueMeshTangent>  tangentBuffers;
        public BufferAccessor<UniqueMeshColor>    colorBuffers;
        public BufferAccessor<UniqueMeshUv0xy>    uv0xyBuffers;
        public BufferAccessor<UniqueMeshUv3xyz>   uv3xyzBuffers;
        public BufferAccessor<UniqueMeshIndex>    indexBuffers;
        public BufferAccessor<UniqueMeshSubmesh>  submeshBuffers;

        bool hasPositions;
        bool hasNormals;
        bool hasTangents;
        bool hasColors;
        bool hasUv0xys;
        bool hasUv3xyzs;
        bool hasIndices;
        bool hasSubmehes;

        public void Init()
        {
            hasPositions = positionBuffers.Length > 0;
            hasNormals   = normalBuffers.Length > 0;
            hasTangents  = tangentBuffers.Length > 0;
            hasColors    = colorBuffers.Length > 0;
            hasUv0xys    = uv0xyBuffers.Length > 0;
            hasUv3xyzs   = uv3xyzBuffers.Length > 0;
            hasIndices   = indexBuffers.Length > 0;
            hasSubmehes  = submeshBuffers.Length > 0;
        }

        public bool IsEntityIndexValidMesh(int entityIndex)
        {
            var  config = configurations[entityIndex];
            bool failed = false;

            // Validate buffer size matches
            int vertexCode  = -1;
            int vertexCount = -1;
            if (hasPositions)
            {
                vertexCode  = 0;
                vertexCount = positionBuffers[entityIndex].Length;
            }
            if (!config.calculateNormals && hasNormals)
            {
                if (vertexCode < 0)
                {
                    vertexCode  = 1;
                    vertexCount = normalBuffers[entityIndex].Length;
                }
                else if (vertexCount != normalBuffers[entityIndex].Length)
                {
                    UnityEngine.Debug.LogError(
                        $"{entities[entityIndex].ToFixedString()} has {vertexCount} positions and {normalBuffers[entityIndex].Length} tangents. These must match.");
                    failed = true;
                }
            }
            if (!config.calculateTangents && hasTangents)
            {
                if (vertexCode < 0)
                {
                    vertexCode  = 2;
                    vertexCount = tangentBuffers[entityIndex].Length;
                }
                else if (vertexCount != tangentBuffers[entityIndex].Length)
                {
                    UnityEngine.Debug.LogError(
                        $"{entities[entityIndex].ToFixedString()} has {vertexCount} {GetNameFromVertexCode(vertexCode)} and {tangentBuffers[entityIndex].Length} tangents. These must match.");
                    failed = true;
                }
            }
            if (hasColors)
            {
                if (vertexCode < 0)
                {
                    vertexCode  = 3;
                    vertexCount = colorBuffers[entityIndex].Length;
                }
                else if (vertexCount != colorBuffers[entityIndex].Length)
                {
                    UnityEngine.Debug.LogError(
                        $"{entities[entityIndex].ToFixedString()} has {vertexCount} {GetNameFromVertexCode(vertexCode)} and {colorBuffers[entityIndex].Length} colors. These must match.");
                    failed = true;
                }
            }
            if (hasUv0xys)
            {
                if (vertexCode < 0)
                {
                    vertexCode  = 4;
                    vertexCount = uv0xyBuffers[entityIndex].Length;
                }
                else if (vertexCount != uv0xyBuffers[entityIndex].Length)
                {
                    UnityEngine.Debug.LogError(
                        $"{entities[entityIndex].ToFixedString()} has {vertexCount} {GetNameFromVertexCode(vertexCode)} and {uv0xyBuffers[entityIndex].Length} UV0 xy values. These must match.");
                    failed = true;
                }
            }
            if (hasUv3xyzs)
            {
                if (vertexCode < 0)
                {
                    vertexCode  = 4;
                    vertexCount = uv3xyzBuffers[entityIndex].Length;
                }
                else if (vertexCount != uv3xyzBuffers[entityIndex].Length)
                {
                    UnityEngine.Debug.LogError(
                        $"{entities[entityIndex].ToFixedString()} has {vertexCount} {GetNameFromVertexCode(vertexCode)} and {uv3xyzBuffers[entityIndex].Length} UV3 xyz values. These must match.");
                    failed = true;
                }
            }

            // Validate config options
            if (config.calculateNormals && !hasPositions)
            {
                UnityEngine.Debug.LogError($"Cannot calculate normals without positions for {entities[entityIndex].ToFixedString()}.");
                failed = true;
            }
            if (config.calculateTangents && !hasPositions && !hasUv0xys && !(hasNormals || config.calculateNormals))
            {
                UnityEngine.Debug.LogError($"Cannot calculate tangents without positions, normals, and UV0 values for {entities[entityIndex].ToFixedString()}.");
                failed = true;
            }

            // Validate submeshes
            if (hasSubmehes)
            {
                int indexCount   = hasIndices ? indexBuffers[entityIndex].Length : vertexCount;
                int submeshIndex = 0;
                foreach (var submesh in submeshBuffers[entityIndex])
                {
                    if (submesh.indexStart + submesh.indexCount > indexCount)
                    {
                        UnityEngine.Debug.LogError(
                            $"In {entities[entityIndex].ToFixedString()}, submesh {submeshIndex} with indexStart {submesh.indexStart} and indexCount {submesh.indexCount} exceeds {indexCount} total indices in the mesh.");
                        failed = true;
                    }
                    if (submesh.topology == UniqueMeshSubmesh.Topology.Triangles && submesh.indexCount % 3 != 0)
                    {
                        UnityEngine.Debug.LogError(
                            $"In {entities[entityIndex].ToFixedString()}, submesh has triangle topology and indexCount {submesh.indexCount} which is not divisible by 3.");
                        failed = true;
                    }
                    if (submesh.topology == UniqueMeshSubmesh.Topology.Lines && submesh.indexCount % 2 != 0)
                    {
                        UnityEngine.Debug.LogError(
                            $"In {entities[entityIndex].ToFixedString()}, submesh has line topology and indexCount {submesh.indexCount} which is not divisible by 2. Did you intend to use LineStrip instead?");
                        failed = true;
                    }
                    submeshIndex++;
                }
            }

            // Validate indices only if we haven't failed already
            int totalIndexCount = 0;
            if (!failed && hasIndices)
            {
                foreach (var index in indexBuffers[entityIndex])
                {
                    if (index.index < 0 || index.index >= vertexCount)
                    {
                        UnityEngine.Debug.LogError(
                            $"In {entities[entityIndex].ToFixedString()}, index value {index.index} at position {totalIndexCount} in the index buffer is outside the vertex range [0, {vertexCount})");
                        failed = true;
                        break;
                    }
                    totalIndexCount++;
                }
            }
            // If the mesh is just empty, silently fail.
            if (vertexCount == 0 && totalIndexCount == 0)
                failed = true;

            return !failed;
        }

        FixedString32Bytes GetNameFromVertexCode(int code)
        {
            switch (code)
            {
                case 0: return "positions";
                case 1: return "normals";
                case 2: return "tangents";
                case 3: return "colors";
                case 4: return "UV0 xy values";
                case 5: return "UV3 xyz values";
                default: return default;
            }
        }
    }
}

