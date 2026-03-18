using Latios.Kinemation.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct UploadUniqueMeshesSystem : ISystem, ICullingComputeDispatchSystem<UploadUniqueMeshesSystem.CollectState,  UploadUniqueMeshesSystem.WriteState>
    {
        LatiosWorldUnmanaged                                 latiosWorld;
        EntityQuery                                          m_query;
        CullingComputeDispatchData<CollectState, WriteState> m_data;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().With<MaterialMeshInfo>(false).WithEnabled<UniqueMeshConfig>(false).Build();
            m_data      = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            var chunkCount      = m_query.CalculateChunkCountWithoutFiltering();
            var collectedChunks = new NativeList<CollectedChunk>(chunkCount, state.WorldUpdateAllocator);
            collectedChunks.Resize(chunkCount, NativeArrayOptions.ClearMemory);
            var meshPool = latiosWorld.worldBlackboardEntity.GetCollectionComponent<UniqueMeshPool>(false);

            state.Dependency = new FindAndValidateMeshesJob
            {
                collectedChunks = collectedChunks,
                colorHandle     = GetBufferTypeHandle<UniqueMeshColor>(true),
                configHandle    = GetComponentTypeHandle<UniqueMeshConfig>(false),
                entityHandle    = GetEntityTypeHandle(),
                indexHandle     = GetBufferTypeHandle<UniqueMeshIndex>(true),
                meshPool        = meshPool,
                mmiHandle       = GetComponentTypeHandle<MaterialMeshInfo>(false),
                normalHandle    = GetBufferTypeHandle<UniqueMeshNormal>(true),
                positionHandle  = GetBufferTypeHandle<UniqueMeshPosition>(true),
                submeshHandle   = GetBufferTypeHandle<UniqueMeshSubmesh>(true),
                tangentHandle   = GetBufferTypeHandle<UniqueMeshTangent>(true),
                trackedHandle   = GetComponentTypeHandle<TrackedUniqueMesh>(true),
                uv0xyHandle     = GetBufferTypeHandle<UniqueMeshUv0xy>(true),
                uv3xyzHandle    = GetBufferTypeHandle<UniqueMeshUv3xyz>(true),
            }.ScheduleParallel(m_query, state.Dependency);

            var meshesNeeded = new NativeReference<int>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            state.Dependency = new OrganizeMeshesJob
            {
                collectedChunks = collectedChunks,
                meshPool        = meshPool,
                meshesNeeded    = meshesNeeded
            }.Schedule(state.Dependency);

            return new CollectState
            {
                collectedChunks = collectedChunks,
                meshesNeeded    = meshesNeeded
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collectState)
        {
            var meshCount = collectState.meshesNeeded.Value;
            if (meshCount == 0)
                return default;
            var meshesToUpload =
                CollectionHelper.CreateNativeArray<UnityObjectRef<UnityEngine.Mesh> >(meshCount, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var meshDataArray = UnityEngine.Mesh.AllocateWritableMeshData(meshCount);
            var meshPool      = latiosWorld.worldBlackboardEntity.GetCollectionComponent<UniqueMeshPool>(true);

            state.Dependency = new WriteMeshesJob
            {
                collectedChunks = collectState.collectedChunks.AsDeferredJobArray(),
                colorHandle     = GetBufferTypeHandle<UniqueMeshColor>(false),
                configHandle    = GetComponentTypeHandle<UniqueMeshConfig>(false),
                indexHandle     = GetBufferTypeHandle<UniqueMeshIndex>(false),
                meshDataArray   = meshDataArray,
                meshesToUpload  = meshesToUpload,
                meshPool        = meshPool,
                mmiHandle       = GetComponentTypeHandle<MaterialMeshInfo>(true),
                normalHandle    = GetBufferTypeHandle<UniqueMeshNormal>(false),
                positionHandle  = GetBufferTypeHandle<UniqueMeshPosition>(false),
                submeshHandle   = GetBufferTypeHandle<UniqueMeshSubmesh>(false),
                tangentHandle   = GetBufferTypeHandle<UniqueMeshTangent>(false),
                trackedHandle   = GetComponentTypeHandle<TrackedUniqueMesh>(true),
                uv0xyHandle     = GetBufferTypeHandle<UniqueMeshUv0xy>(false),
                uv3xyzHandle    = GetBufferTypeHandle<UniqueMeshUv3xyz>(false),
            }.Schedule(collectState.collectedChunks, 1, state.Dependency);

            return new WriteState
            {
                meshCount      = meshCount,
                meshDataArray  = meshDataArray,
                meshesToUpload = meshesToUpload
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState writeState)
        {
            if (writeState.meshCount == 0)
                return;

            //var flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;
            var flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontRecalculateBounds;
            GraphicsUnmanaged.ApplyAndDisposeWritableMeshData(writeState.meshDataArray, writeState.meshesToUpload, flags);
        }

        public struct CollectState
        {
            internal NativeList<CollectedChunk> collectedChunks;
            internal NativeReference<int>       meshesNeeded;
        }

        public struct WriteState
        {
            internal int                                            meshCount;
            internal UnityEngine.Mesh.MeshDataArray                 meshDataArray;
            internal NativeArray<UnityObjectRef<UnityEngine.Mesh> > meshesToUpload;
        }

        internal struct CollectedChunk
        {
            public ArchetypeChunk chunk;
            public BitField64     lower;
            public BitField64     upper;
            public int            prefixSum;
        }

        [BurstCompile]
        struct FindAndValidateMeshesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshPosition>   positionHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshNormal>     normalHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshTangent>    tangentHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshColor>      colorHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv0xy>      uv0xyHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshUv3xyz>     uv3xyzHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshIndex>      indexHandle;
            [ReadOnly] public BufferTypeHandle<UniqueMeshSubmesh>    submeshHandle;
            [ReadOnly] public ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            [ReadOnly] public UniqueMeshPool                         meshPool;

            public ComponentTypeHandle<UniqueMeshConfig>                            configHandle;
            public ComponentTypeHandle<MaterialMeshInfo>                            mmiHandle;
            [NativeDisableParallelForRestriction] public NativeList<CollectedChunk> collectedChunks;  // Preallocated to query chunk count without filtering

            [NativeSetThreadIndex]
            int threadIndex;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // 1) Get config info
                var configurations = (UniqueMeshConfig*)chunk.GetRequiredComponentDataPtrRO(ref configHandle);
                var configuredBits = chunk.GetEnabledMask(ref configHandle);
                var mmis           = chunk.GetComponentDataPtrRO(ref mmiHandle);
                var tracked        = chunk.GetComponentDataPtrRO(ref trackedHandle);
                var entities       = chunk.GetEntityDataPtrRO(entityHandle);

                // 2) Validate meshes
                BitField64 lowerToProcess = default;
                BitField64 upperToProcess = default;
                var        validator      = new UniqueMeshValidator
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

                bool reservedMmiWrite = false;
                var  enumerator       = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var entityIndex))
                {
                    if (!validator.IsEntityIndexValidMesh(entityIndex))
                    {
                        // Mark the entity as configured now so that we don't try to process it again until the user fixes the problem.
                        configuredBits[entityIndex] = false;
                        // Mark the entity as invalid so that we can cull it in future updates, but only if the status changed.

                        if (mmis[entityIndex].Mesh != 0)
                        {
                            if (!reservedMmiWrite)
                            {
                                chunk.GetComponentDataPtrRW(ref mmiHandle);
                                reservedMmiWrite = true;
                            }
                            mmis[entityIndex].Mesh = 0;
                        }
                    }
                    else
                    {
                        if (entityIndex < 64)
                            lowerToProcess.SetBits(entityIndex, true);
                        else
                            upperToProcess.SetBits(entityIndex - 64, true);
                        if (mmis[entityIndex].Mesh == 0)
                        {
                            if (!reservedMmiWrite)
                            {
                                chunk.GetComponentDataPtrRW(ref mmiHandle);
                                reservedMmiWrite = true;
                            }
                            // All of these checks should pass at this point, but I don't really want to crash Unity if they fail for some reason.
                            if (tracked != null)
                            {
                                if (meshPool.meshToIdMap.TryGetValue(tracked[entityIndex].mesh, out var id))
                                    mmis[entityIndex].Mesh = id;
                            }
                        }
                    }
                }
                if ((lowerToProcess.Value | upperToProcess.Value) == 0)
                    return;

                // 5) Export chunk
                collectedChunks[unfilteredChunkIndex] = new CollectedChunk
                {
                    chunk = chunk,
                    lower = lowerToProcess,
                    upper = upperToProcess,
                };
            }
        }

        [BurstCompile]
        struct OrganizeMeshesJob : IJob
        {
            public NativeList<CollectedChunk> collectedChunks;
            public UniqueMeshPool             meshPool;
            public NativeReference<int>       meshesNeeded;

            public void Execute()
            {
                int prefixSum  = 0;
                int writeIndex = 0;
                for (int i = 0; i < collectedChunks.Length; i++)
                {
                    var chunk = collectedChunks[i];
                    if (chunk.chunk == default)
                        continue;

                    chunk.prefixSum              = prefixSum;
                    prefixSum                   += chunk.lower.CountBits() + chunk.upper.CountBits();
                    collectedChunks[writeIndex]  = chunk;
                    writeIndex++;
                }
                collectedChunks.Length = writeIndex;
                meshesNeeded.Value     = prefixSum;
            }
        }

        [BurstCompile]
        struct WriteMeshesJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<CollectedChunk>            collectedChunks;
            [ReadOnly] public ComponentTypeHandle<MaterialMeshInfo>  mmiHandle;
            [ReadOnly] public ComponentTypeHandle<TrackedUniqueMesh> trackedHandle;
            [ReadOnly] public UniqueMeshPool                         meshPool;

            public ComponentTypeHandle<UniqueMeshConfig> configHandle;
            public BufferTypeHandle<UniqueMeshPosition>  positionHandle;
            public BufferTypeHandle<UniqueMeshNormal>    normalHandle;
            public BufferTypeHandle<UniqueMeshTangent>   tangentHandle;
            public BufferTypeHandle<UniqueMeshColor>     colorHandle;
            public BufferTypeHandle<UniqueMeshUv0xy>     uv0xyHandle;
            public BufferTypeHandle<UniqueMeshUv3xyz>    uv3xyzHandle;
            public BufferTypeHandle<UniqueMeshIndex>     indexHandle;
            public BufferTypeHandle<UniqueMeshSubmesh>   submeshHandle;

            [NativeDisableParallelForRestriction] public UnityEngine.Mesh.MeshDataArray                 meshDataArray;
            [NativeDisableParallelForRestriction] public NativeArray<UnityObjectRef<UnityEngine.Mesh> > meshesToUpload;

            [NativeDisableContainerSafetyRestriction] NativeList<float3>                    tempNormals;
            [NativeDisableContainerSafetyRestriction] NativeList<float4>                    tempTangents;
            [NativeDisableContainerSafetyRestriction] NativeList<VertexAttributeDescriptor> tempDescriptors;

            public HasChecker<LiveBakedTag> liveBakedChecker;

            public unsafe void Execute(int chunkIndex)
            {
                if (!tempDescriptors.IsCreated)
                    tempDescriptors = new NativeList<VertexAttributeDescriptor>(8, Allocator.Temp);

                var  chunk     = collectedChunks[chunkIndex];
                bool liveBaked = liveBakedChecker[chunk.chunk];

                // Assign all the meshes now for a little better cache coherency.
                var mask    = chunk.chunk.GetEnabledMask(ref configHandle);
                var tracked = chunk.chunk.GetComponentDataPtrRO(ref trackedHandle);
                if (tracked != null)
                {
                    int dstIndex = chunk.prefixSum;
                    var e        = new ChunkEntityEnumerator(true, new v128(chunk.lower.Value, chunk.upper.Value), chunk.chunk.Count);
                    while (e.NextEntityIndex(out var entityIndex))
                    {
                        mask[entityIndex]        = false;
                        meshesToUpload[dstIndex] = tracked[entityIndex].mesh;
                        dstIndex++;
                    }
                }
                else
                {
                    var mmis     = chunk.chunk.GetComponentDataPtrRO(ref mmiHandle);
                    int dstIndex = chunk.prefixSum;
                    var e        = new ChunkEntityEnumerator(true, new v128(chunk.lower.Value, chunk.upper.Value), chunk.chunk.Count);
                    while (e.NextEntityIndex(out var entityIndex))
                    {
                        mask[entityIndex]        = false;
                        meshesToUpload[dstIndex] = meshPool.idToMeshMap[mmis[entityIndex].Mesh];
                        dstIndex++;
                    }
                }

                var configurations  = chunk.chunk.GetComponentDataPtrRO(ref configHandle);
                var positionBuffers = chunk.chunk.GetBufferAccessor(ref positionHandle);
                var normalBuffers   = chunk.chunk.GetBufferAccessor(ref normalHandle);
                var tangentBuffers  = chunk.chunk.GetBufferAccessor(ref tangentHandle);
                var colorBuffers    = chunk.chunk.GetBufferAccessor(ref colorHandle);
                var uv0xyBuffers    = chunk.chunk.GetBufferAccessor(ref uv0xyHandle);
                var uv3xyzBuffers   = chunk.chunk.GetBufferAccessor(ref uv3xyzHandle);
                var indexBuffers    = chunk.chunk.GetBufferAccessor(ref indexHandle);
                var submeshBuffers  = chunk.chunk.GetBufferAccessor(ref submeshHandle);

                var enumerator = new ChunkEntityEnumerator(true, new v128(chunk.lower.Value, chunk.upper.Value), chunk.chunk.Count);
                for (int meshIndex = chunk.prefixSum; enumerator.NextEntityIndex(out var entityIndex); meshIndex++)
                {
                    var config = configurations[entityIndex];

                    // Capture the arrays, possibly performing normal and tangent recalculation.
                    var                 indices   = indexBuffers.Length > 0 ? indexBuffers[entityIndex].AsNativeArray().Reinterpret<int>() : default;
                    var                 submeshes = submeshBuffers.Length > 0 ? submeshBuffers[entityIndex].AsNativeArray() : default;
                    var                 positions = positionBuffers.Length > 0 ? positionBuffers[entityIndex].AsNativeArray().Reinterpret<float3>() : default;
                    NativeArray<float3> normals   = default;
                    if (config.calculateNormals)
                    {
                        if (normalBuffers.Length > 0)
                        {
                            var nb = normalBuffers[entityIndex];
                            nb.ResizeUninitialized(positions.Length);
                            normals = nb.AsNativeArray().Reinterpret<float3>();
                        }
                        else
                        {
                            if (!tempNormals.IsCreated)
                                tempNormals = new NativeList<float3>(positions.Length, Allocator.Temp);
                            tempNormals.ResizeUninitialized(positions.Length);
                            normals = tempNormals.AsArray();
                        }
                        RecalculateNormals(positions, normals, indices, submeshes);
                    }
                    else if (normalBuffers.Length > 0)
                    {
                        normals = normalBuffers[entityIndex].AsNativeArray().Reinterpret<float3>();
                    }
                    var                 uv0xys   = uv0xyBuffers.Length > 0 ? uv0xyBuffers[entityIndex].AsNativeArray().Reinterpret<float2>() : default;
                    NativeArray<float4> tangents = default;
                    if (config.calculateTangents)
                    {
                        if (tangentBuffers.Length > 0)
                        {
                            var nb = tangentBuffers[entityIndex];
                            nb.ResizeUninitialized(positions.Length);
                            tangents = nb.AsNativeArray().Reinterpret<float4>();
                        }
                        else
                        {
                            if (!tempTangents.IsCreated)
                                tempTangents = new NativeList<float4>(positions.Length, Allocator.Temp);
                            tempTangents.ResizeUninitialized(positions.Length);
                            tangents = tempTangents.AsArray();
                        }
                        RecalculateTangents(positions, normals, tangents, uv0xys, indices, submeshes);
                    }
                    else if (tangentBuffers.Length > 0)
                    {
                        tangents = tangentBuffers[entityIndex].AsNativeArray().Reinterpret<float4>();
                    }
                    var colors  = colorBuffers.Length > 0 ? colorBuffers[entityIndex].AsNativeArray().Reinterpret<float4>() : default;
                    var uv3xyzs = uv3xyzBuffers.Length > 0 ? uv3xyzBuffers[entityIndex].AsNativeArray().Reinterpret<float3>() : default;

                    // Set up vertex attributes
                    tempDescriptors.Clear();
                    int vertexCount = 0;
                    if (positions.Length > 0)
                    {
                        vertexCount = positions.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0));
                    }
                    if (normals.Length > 0)
                    {
                        vertexCount = normals.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0));
                    }
                    if (tangents.Length > 0)
                    {
                        vertexCount = tangents.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 0));
                    }
                    if (colors.Length > 0)
                    {
                        vertexCount = colors.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0));
                    }
                    if (uv0xys.Length > 0)
                    {
                        vertexCount = uv0xys.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0));
                    }
                    if (uv3xyzBuffers.Length > 0)
                    {
                        vertexCount = uv3xyzs.Length;
                        tempDescriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 3, 0));
                    }

                    if (vertexCount == 0)
                    {
                        // We have an attribute-free mesh. We need to extract the vertex count from the indices.
                        if (indices.Length > 0)
                        {
                            foreach (var vindex in indices)
                            {
                                vertexCount = math.max(vertexCount, vindex + 1);
                            }
                        }
                        else
                        {
                            // Our mesh is fully defined by the submesh.
                            foreach (var submesh in submeshes)
                            {
                                vertexCount = math.max(vertexCount, submesh.indexStart + submesh.indexCount);
                            }
                        }
                    }

                    var meshData = meshDataArray[meshIndex];
                    meshData.SetVertexBufferParams(vertexCount, tempDescriptors.AsArray());
                    var stream0 = meshData.GetVertexData<float>(0);

                    int writeIndex = 0;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (positions.Length > 0)
                            Write(ref writeIndex, ref stream0, positions[i]);
                        if (normals.Length > 0)
                            Write(ref writeIndex, ref stream0, normals[i]);
                        if (tangents.Length > 0)
                            Write(ref writeIndex, ref stream0, tangents[i]);
                        if (colors.Length > 0)
                            Write(ref writeIndex, ref stream0, colors[i]);
                        if (uv0xys.Length > 0)
                            Write(ref writeIndex, ref stream0, uv0xys[i]);
                        if (uv3xyzs.Length > 0)
                            Write(ref writeIndex, ref stream0, uv3xyzs[i]);
                    }

                    // Process indices
                    int indexCount = indices.Length > 0 ? indices.Length : vertexCount;
                    if (vertexCount < ushort.MaxValue)
                    {
                        meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                        var indexStream = meshData.GetIndexData<ushort>();
                        if (indices.Length > 0)
                        {
                            for (int i = 0; i < indices.Length; i++)
                            {
                                indexStream[i] = (ushort)indices[i];
                            }
                        }
                        else
                        {
                            for (ushort i = 0; i < indexCount; i++)
                            {
                                indexStream[i] = i;
                            }
                        }
                    }
                    else
                    {
                        meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                        var indexStream = meshData.GetIndexData<int>();
                        if (indices.Length > 0)
                        {
                            indexStream.CopyFrom(indices);
                        }
                        else
                        {
                            for (int i = 0; i < indexCount; i++)
                            {
                                indexStream[i] = i;
                            }
                        }
                    }

                    // Process submeshes
                    meshData.subMeshCount = math.max(1, submeshes.Length);
                    var flags             = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontNotifyMeshUsers |
                                            MeshUpdateFlags.DontRecalculateBounds;
                    if (submeshes.Length == 0)
                    {
                        meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, UnityEngine.MeshTopology.Triangles), flags);
                    }
                    else
                    {
                        for (int submeshIndex = 0; submeshIndex < submeshes.Length; submeshIndex++)
                        {
                            var submesh  = submeshes[submeshIndex];
                            var topology = submesh.topology switch
                            {
                                UniqueMeshSubmesh.Topology.Triangles => UnityEngine.MeshTopology.Triangles,
                                UniqueMeshSubmesh.Topology.Lines => UnityEngine.MeshTopology.Lines,
                                UniqueMeshSubmesh.Topology.LineStrip => UnityEngine.MeshTopology.LineStrip,
                                UniqueMeshSubmesh.Topology.Points => UnityEngine.MeshTopology.Points,
                                _ => UnityEngine.MeshTopology.Triangles
                            };
                            meshData.SetSubMesh(submeshIndex, new SubMeshDescriptor(submesh.indexStart, submesh.indexCount, topology), flags);
                        }
                    }

                    // Process buffer clearing option
                    if (config.reclaimDynamicBufferMemoryAfterUpload && !liveBaked)
                    {
                        if (positionBuffers.Length > 0)
                        {
                            var b = positionBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (normalBuffers.Length > 0)
                        {
                            var b = normalBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (tangentBuffers.Length > 0)
                        {
                            var b = tangentBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (colorBuffers.Length > 0)
                        {
                            var b = colorBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (uv0xyBuffers.Length > 0)
                        {
                            var b = uv0xyBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (uv3xyzBuffers.Length > 0)
                        {
                            var b = uv3xyzBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (indexBuffers.Length > 0)
                        {
                            var b = indexBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                        if (submeshBuffers.Length > 0)
                        {
                            var b = submeshBuffers[entityIndex];
                            b.Clear();
                            b.TrimExcess();
                        }
                    }
                }
            }

            void RecalculateNormals(NativeArray<float3> positions, NativeArray<float3> normals, NativeArray<int> indices, NativeArray<UniqueMeshSubmesh> submeshes)
            {
                // Todo:
                throw new System.NotImplementedException("Recalculate normals for UniqueMesh is not supported at this time.");
            }

            void RecalculateTangents(NativeArray<float3>            positions,
                                     NativeArray<float3>            normals,
                                     NativeArray<float4>            tangents,
                                     NativeArray<float2>            uvs,
                                     NativeArray<int>               indices,
                                     NativeArray<UniqueMeshSubmesh> submeshes)
            {
                // Todo:
                throw new System.NotImplementedException("Recalculate tangents for UniqueMesh is not supported at this time.");
            }

            void Write(ref int writeIndex, ref NativeArray<float> stream, float2 value)
            {
                stream[writeIndex++] = value.x;
                stream[writeIndex++] = value.y;
            }

            void Write(ref int writeIndex, ref NativeArray<float> stream, float3 value)
            {
                stream[writeIndex++] = value.x;
                stream[writeIndex++] = value.y;
                stream[writeIndex++] = value.z;
            }

            void Write(ref int writeIndex, ref NativeArray<float> stream, float4 value)
            {
                stream[writeIndex++] = value.x;
                stream[writeIndex++] = value.y;
                stream[writeIndex++] = value.z;
                stream[writeIndex++] = value.w;
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
}

