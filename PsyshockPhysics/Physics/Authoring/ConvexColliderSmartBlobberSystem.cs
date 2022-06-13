using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Psyshock.Authoring
{
    public struct ConvexColliderBakeData
    {
        public Mesh sharedMesh;
    }

    public static class ConvexColliderSmartBlobberAPIExtensions
    {
        public static SmartBlobberHandle<ConvexColliderBlob> CreateBlob(this GameObjectConversionSystem conversionSystem,
                                                                        GameObject gameObject,
                                                                        ConvexColliderBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ConvexColliderSmartBlobberSystem>().AddToConvert(gameObject, bakeData);
        }

        public static SmartBlobberHandleUntyped CreateBlobUntyped(this GameObjectConversionSystem conversionSystem,
                                                                  GameObject gameObject,
                                                                  ConvexColliderBakeData bakeData)
        {
            return conversionSystem.World.GetExistingSystem<Systems.ConvexColliderSmartBlobberSystem>().AddToConvertUntyped(gameObject, bakeData);
        }
    }
}

namespace Latios.Psyshock.Authoring.Systems
{
    [ConverterVersion("latios", 4)]
    public class ConvexColliderSmartBlobberSystem : SmartBlobberConversionSystem<ConvexColliderBlob, ConvexColliderBakeData, ConvexConverter, ConvexContext>
    {
        protected override void Filter(FilterBlobberData blobberData, ref ConvexContext context, NativeArray<int> inputToFilteredMapping)
        {
            var hashes = new NativeArray<int>(blobberData.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < blobberData.Count; i++)
            {
                var input = blobberData.input[i];
                if (input.sharedMesh == null || !input.sharedMesh.isReadable)
                {
                    if (input.sharedMesh != null && !input.sharedMesh.isReadable)
                        Debug.LogError(
                            $"Psyshock failed to convert convex mesh {input.sharedMesh.name}. The mesh was not marked as readable. Please correct this in the mesh asset's import settings.");

                    hashes[i]                 = default;
                    inputToFilteredMapping[i] = -1;
                }
                else
                {
                    DeclareAssetDependency(blobberData.associatedObject[i], input.sharedMesh);
                    hashes[i] = input.sharedMesh.GetInstanceID();
                }
            }

            new DeduplicateJob { hashes = hashes, inputToFilteredMapping = inputToFilteredMapping }.Run();
            hashes.Dispose();
        }

        protected override void PostFilter(PostFilterBlobberData blobberData, ref ConvexContext context)
        {
            var meshList = new List<Mesh>();

            var converters = blobberData.converters;

            for (int i = 0; i < blobberData.Count; i++)
            {
                var mesh = blobberData.input[i].sharedMesh;
                meshList.Add(mesh);

                converters[i] = new ConvexConverter
                {
                    meshName = mesh.name
                };
            }

            context.meshes = Mesh.AcquireReadOnlyMeshData(meshList);
        }

        [BurstCompile]
        struct DeduplicateJob : IJob
        {
            [ReadOnly] public NativeArray<int> hashes;
            public NativeArray<int>            inputToFilteredMapping;

            public void Execute()
            {
                var map = new NativeHashMap<int, int>(hashes.Length, Allocator.Temp);
                for (int i = 0; i < hashes.Length; i++)
                {
                    if (inputToFilteredMapping[i] < 0)
                        continue;

                    if (map.TryGetValue(hashes[i], out int index))
                        inputToFilteredMapping[i] = index;
                    else
                        map.Add(hashes[i], i);
                }
            }
        }
    }

    public struct ConvexConverter : ISmartBlobberContextBuilder<ConvexColliderBlob, ConvexContext>
    {
        public FixedString128Bytes meshName;

        public unsafe BlobAssetReference<ConvexColliderBlob> BuildBlob(int prefilterIndex, int postfilterIndex, ref ConvexContext context)
        {
            if (!context.vector3Cache.IsCreated)
            {
                context.vector3Cache = new NativeList<Vector3>(Allocator.Temp);
            }

            var builder = new BlobBuilder(Allocator.Temp);

            ref var blobRoot = ref builder.ConstructRoot<ConvexColliderBlob>();
            var     mesh     = context.meshes[postfilterIndex];

            blobRoot.meshName = meshName;

            var vector3Cache = context.vector3Cache;
            vector3Cache.ResizeUninitialized(mesh.vertexCount);
            mesh.GetVertices(vector3Cache);

            // ConvexHullBuilder doesn't seem to properly check duplicated vertices when they are nice numbers.
            // So we deduplicate things ourselves.
            var hashedMeshVertices = new NativeHashSet<float3>(vector3Cache.Length, Allocator.Temp);
            for (int i = 0; i < vector3Cache.Length; i++)
                hashedMeshVertices.Add(vector3Cache[i]);
            var meshVertices = hashedMeshVertices.ToNativeArray(Allocator.Temp);

            // These are the default Unity uses except with 0 bevel radius.
            // They don't matter too much since Unity is allowed to violate them anyways to meet storage constraints.
            var parameters = new ConvexHullGenerationParameters
            {
                BevelRadius             = 0f,
                MinimumAngle            = math.radians(2.5f),
                SimplificationTolerance = 0.015f
            };
            // These are the default storage constraints Unity uses.
            // Changing them will likely break EPA.
            int maxVertices     = 252;
            int maxFaces        = 252;
            int maxFaceVertices = 32;

            var convexHullBuilder = new ConvexHullBuilder(meshVertices, parameters, maxVertices, maxFaces, maxFaceVertices, out float convexRadius);
            // Todo: We should handle 2D convex colliders more elegantly.
            // Right now our queries don't consider it.
            if (convexHullBuilder.dimension != 3)
            {
                parameters.MinimumAngle            = 0f;
                parameters.SimplificationTolerance = 0f;
                convexHullBuilder                  = new ConvexHullBuilder(meshVertices, parameters, maxVertices, maxFaces, maxFaceVertices, out convexRadius);
            }
            UnityEngine.Assertions.Assert.IsTrue(convexRadius < math.EPSILON);

            // Based on Unity.Physics - ConvexCollider.cs
            var vertices       = new NativeList<float3>(convexHullBuilder.vertices.peakCount, Allocator.Temp);
            var indexVertexMap = new NativeArray<byte>(convexHullBuilder.vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            foreach (int vIndex in convexHullBuilder.vertices.indices)
            {
                indexVertexMap[vIndex] = (byte)vertices.Length;
                vertices.Add(convexHullBuilder.vertices[vIndex].position);
            }

            var facePlanes                        = new NativeList<Plane>(convexHullBuilder.numFaces, Allocator.Temp);
            var edgeIndicesInFaces                = new NativeList<int>(convexHullBuilder.numFaceVertices, Allocator.Temp);
            var edgeIndicesInFacesStartsAndCounts = new NativeList<int2>(convexHullBuilder.numFaces, Allocator.Temp);
            var vertexIndicesInEdges              = new NativeList<int2>(convexHullBuilder.vertices.peakCount, Allocator.Temp);
            var edgeHashMap                       = new NativeHashMap<int2, int>(convexHullBuilder.vertices.peakCount, Allocator.Temp);
            var edgeFlippedInFaces                = new NativeList<bool>(convexHullBuilder.numFaceVertices, Allocator.Temp);

            var tempVerticesInFace = new NativeList<int>(Allocator.Temp);

            for (ConvexHullBuilder.FaceEdge hullFace = convexHullBuilder.GetFirstFace(); hullFace.isValid; hullFace = convexHullBuilder.GetNextFace(hullFace))
            {
                ConvexHullBuilder.Edge firstEdge = hullFace;
                Plane                  facePlane = convexHullBuilder.planes[convexHullBuilder.triangles[firstEdge.triangleIndex].faceIndex];
                facePlanes.Add(facePlane);

                // Walk the face's outer vertices & edges
                tempVerticesInFace.Clear();
                for (ConvexHullBuilder.FaceEdge edge = hullFace; edge.isValid; edge = convexHullBuilder.GetNextFaceEdge(edge))
                {
                    tempVerticesInFace.Add(indexVertexMap[convexHullBuilder.StartVertex(edge)]);
                }
                UnityEngine.Assertions.Assert.IsTrue(tempVerticesInFace.Length >= 3);

                // The rest of this is custom.
                int edgeIndicesInFaceStart = edgeIndicesInFaces.Length;
                int previousVertexIndex    = tempVerticesInFace[tempVerticesInFace.Length - 1];
                for (int i = 0; i < tempVerticesInFace.Length; i++)
                {
                    int2 edge           = new int2(previousVertexIndex, tempVerticesInFace[i]);
                    previousVertexIndex = tempVerticesInFace[i];
                    if (edgeHashMap.TryGetValue(edge, out var edgeIndex))
                    {
                        edgeIndicesInFaces.Add(edgeIndex);
                        edgeFlippedInFaces.Add(false);
                    }
                    else if (edgeHashMap.TryGetValue(edge.yx, out edgeIndex))
                    {
                        edgeIndicesInFaces.Add(edgeIndex);
                        edgeFlippedInFaces.Add(true);
                    }
                    else
                    {
                        edgeIndex = vertexIndicesInEdges.Length;
                        vertexIndicesInEdges.Add(edge);
                        edgeHashMap.Add(edge, edgeIndex);
                        edgeIndicesInFaces.Add(edgeIndex);
                        edgeFlippedInFaces.Add(false);
                    }
                }
                edgeIndicesInFacesStartsAndCounts.Add(new int2(edgeIndicesInFaceStart, tempVerticesInFace.Length));
            }

            var viie = builder.ConstructFromNativeArray(ref blobRoot.vertexIndicesInEdges,
                                                        (int2*)vertexIndicesInEdges.GetUnsafeReadOnlyPtr(),
                                                        vertexIndicesInEdges.Length);
            var eiif    = builder.ConstructFromNativeArray(ref blobRoot.edgeIndicesInFaces, (int*)edgeIndicesInFaces.GetUnsafeReadOnlyPtr(), edgeIndicesInFaces.Length);
            var eiifsac = builder.ConstructFromNativeArray(ref blobRoot.edgeIndicesInFacesStartsAndCounts,
                                                           (int2*)edgeIndicesInFacesStartsAndCounts.GetUnsafeReadOnlyPtr(),
                                                           edgeIndicesInFacesStartsAndCounts.Length);

            var edgeNormals = builder.Allocate(ref blobRoot.edgeNormals, vertexIndicesInEdges.Length);
            for (int i = 0; i < edgeNormals.Length; i++)
                edgeNormals[i] = float3.zero;

            var facePlaneX    = builder.Allocate(ref blobRoot.facePlaneX, edgeIndicesInFacesStartsAndCounts.Length);
            var facePlaneY    = builder.Allocate(ref blobRoot.facePlaneY, edgeIndicesInFacesStartsAndCounts.Length);
            var facePlaneZ    = builder.Allocate(ref blobRoot.facePlaneZ, edgeIndicesInFacesStartsAndCounts.Length);
            var facePlaneDist = builder.Allocate(ref blobRoot.facePlaneDist, edgeIndicesInFacesStartsAndCounts.Length);

            var faceEdgeOutwardPlanes = builder.Allocate(ref blobRoot.faceEdgeOutwardPlanes, edgeIndicesInFaces.Length);

            for (int faceIndex = 0; faceIndex < edgeIndicesInFacesStartsAndCounts.Length; faceIndex++)
            {
                var plane                = facePlanes[faceIndex];
                facePlaneX[faceIndex]    = plane.normal.x;
                facePlaneY[faceIndex]    = plane.normal.y;
                facePlaneZ[faceIndex]    = plane.normal.z;
                facePlaneDist[faceIndex] = plane.distanceFromOrigin;

                var edgeIndicesStartAndCount = edgeIndicesInFacesStartsAndCounts[faceIndex];

                bool ccw = true;
                {
                    int2 abIndices = vertexIndicesInEdges[edgeIndicesInFaces[edgeIndicesStartAndCount.x]];
                    int2 cIndices  = vertexIndicesInEdges[edgeIndicesInFaces[edgeIndicesStartAndCount.x + 1]];
                    if (edgeFlippedInFaces[edgeIndicesStartAndCount.x])
                        abIndices = abIndices.yx;
                    if (edgeFlippedInFaces[edgeIndicesStartAndCount.x + 1])
                        cIndices = cIndices.yx;
                    float3 a     = vertices[abIndices.x];
                    float3 b     = vertices[abIndices.y];
                    float3 c     = vertices[cIndices.y];
                    ccw          = math.dot(math.cross(plane.normal, b - a), c - a) < 0f;
                }

                for (int faceEdgeIndex = edgeIndicesStartAndCount.x; faceEdgeIndex < edgeIndicesStartAndCount.x + edgeIndicesStartAndCount.y; faceEdgeIndex++)
                {
                    int edgeIndex           = edgeIndicesInFaces[faceEdgeIndex];
                    edgeNormals[edgeIndex] += plane.normal;

                    int2 abIndices = vertexIndicesInEdges[edgeIndex];
                    if (edgeFlippedInFaces[faceEdgeIndex])
                        abIndices                        = abIndices.yx;
                    float3 a                             = vertices[abIndices.x];
                    float3 b                             = vertices[abIndices.y];
                    var    outwardNormal                 = math.cross(plane.normal, b - a);
                    outwardNormal                        = math.select(-outwardNormal, outwardNormal, ccw);
                    faceEdgeOutwardPlanes[faceEdgeIndex] = new Plane(outwardNormal, -math.dot(outwardNormal, a));
                }
            }

            var vertexNormals = builder.Allocate(ref blobRoot.vertexNormals, vertices.Length);
            for (int i = 0; i < vertexNormals.Length; i++)
                vertexNormals[i] = float3.zero;

            for (int edgeIndex = 0; edgeIndex < vertexIndicesInEdges.Length; edgeIndex++)
            {
                edgeNormals[edgeIndex] = math.normalize(edgeNormals[edgeIndex]);

                var abIndices               = vertexIndicesInEdges[edgeIndex];
                vertexNormals[abIndices.x] += edgeNormals[edgeIndex];
                vertexNormals[abIndices.y] += edgeNormals[edgeIndex];

                float3 a = vertices[abIndices.x];
                float3 b = vertices[abIndices.y];
            }

            var verticesX = builder.Allocate(ref blobRoot.verticesX, vertices.Length);
            var verticesY = builder.Allocate(ref blobRoot.verticesY, vertices.Length);
            var verticesZ = builder.Allocate(ref blobRoot.verticesZ, vertices.Length);

            Aabb aabb = new Aabb(vertices[0], vertices[0]);

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                var vertex = vertices[vertexIndex];

                verticesX[vertexIndex] = vertex.x;
                verticesY[vertexIndex] = vertex.y;
                verticesZ[vertexIndex] = vertex.z;

                aabb = Physics.CombineAabb(vertex, aabb);

                vertexNormals[vertexIndex] = math.normalize(vertexNormals[vertexIndex]);
            }

            blobRoot.localAabb = aabb;

            return builder.CreateBlobAssetReference<ConvexColliderBlob>(Allocator.Persistent);
        }
    }

    public struct ConvexContext : System.IDisposable
    {
        [ReadOnly] public Mesh.MeshDataArray meshes;

        [NativeDisableContainerSafetyRestriction] public NativeList<Vector3> vector3Cache;

        public void Dispose() => meshes.Dispose();
    }
}

