using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A convex hull collider shape that can be scaled and stretched in local space efficiently.
    /// It is often derived from a Mesh.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct ConvexCollider
    {
        /// <summary>
        /// The blob asset containing the raw convex hull data
        /// </summary>
        [FieldOffset(24)] public BlobAssetReference<ConvexColliderBlob> convexColliderBlob;
        /// <summary>
        /// The premultiplied scale and stretch in local space
        /// </summary>
        [FieldOffset(0)] public float3 scale;

        /// <summary>
        /// Creates a new ConvexCollider
        /// </summary>
        /// <param name="convexColliderBlob">The blob asset containing the raw convex hull data</param>
        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob) : this(convexColliderBlob, new float3(1f, 1f, 1f))
        {
        }

        /// <summary>
        /// Creates a new ConvexCollider
        /// </summary>
        /// <param name="convexColliderBlob">The blob asset containing the raw convex hull data</param>
        /// <param name="scale">The premultiplied scale and stretch in local space</param>
        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob, float3 scale)
        {
            this.convexColliderBlob = convexColliderBlob;
            this.scale              = scale;
        }
    }

    /// <summary>
    /// A definition of a raw baked convex hull. This definition is designed for SIMD operations
    /// and direct traversal is only recommended for advanced users.
    /// </summary>
    public struct ConvexColliderBlob
    {
        // Note: Max vertices is 252, max faces is 252, and max vertices per face is 32
        public BlobArray<float>  verticesX;
        public BlobArray<float>  verticesY;
        public BlobArray<float>  verticesZ;
        public BlobArray<float3> vertexNormals;

        public BlobArray<IndexPair> vertexIndicesInEdges;
        public BlobArray<float3>    edgeNormals;

        // outward normals and distance to origin
        public BlobArray<float> facePlaneX;
        public BlobArray<float> facePlaneY;
        public BlobArray<float> facePlaneZ;
        public BlobArray<float> facePlaneDist;

        // xyz normal w, signed distance
        public BlobArray<float4> faceEdgeOutwardPlanes;

        public BlobArray<EdgeIndexInFace> edgeIndicesInFaces;
        public BlobArray<StartAndCount>   edgeIndicesInFacesStartsAndCounts;

        public BlobArray<IndexPair>     faceIndicesByEdge;
        public BlobArray<byte>          faceIndicesByVertex;
        public BlobArray<StartAndCount> faceIndicesByVertexStartsAndCounts;

        public BlobArray<byte> yz2DVertexIndices;
        public BlobArray<byte> xz2DVertexIndices;
        public BlobArray<byte> xy2DVertexIndices;

        public Aabb localAabb;

        public float3     centerOfMass;
        public float3x3   inertiaTensor;
        public quaternion unscaledInertiaTensorOrientation;
        public float3     unscaledInertiaTensorDiagonal;

        public FixedString128Bytes meshName;

        public struct IndexPair
        {
            public byte x;
            public byte y;
        }

        public struct EdgeIndexInFace
        {
            // 32 edges per face * 252 faces only requires 13 bits
            public ushort raw;
            public int index => raw & 0x7fff;
            public bool flipped => (raw & 0x8000) != 0;
        }

        public struct StartAndCount
        {
            // 32 vertices / edges per face * 252 faces only requires 13 bits, and the count only requires 6
            public ushort start;
            public byte   count;
        }

        /// <summary>
        /// Constructs a Blob Asset for the specified mesh vertices. The user is responsible for the lifecycle
        /// of the resulting blob asset. Calling in a Baker may not result in correct incremental behavior.
        /// </summary>
        /// <param name="builder">The initialized BlobBuilder to create the blob asset with</param>
        /// <param name="vertices">The vertices of the mesh</param>
        /// <param name="name">The name of the mesh which will be stored in the blob</param>
        /// <param name="allocator">The allocator used for the finally BlobAsset, typically Persistent</param>
        /// <returns>A reference to the created Blob Asset which is user-owned</returns>
        public static unsafe BlobAssetReference<ConvexColliderBlob> BuildBlob(ref BlobBuilder builder,
                                                                              ReadOnlySpan<float3>             vertices,
                                                                              in FixedString128Bytes name,
                                                                              AllocatorManager.AllocatorHandle allocator)
        {
            ref var blobRoot  = ref builder.ConstructRoot<ConvexColliderBlob>();
            blobRoot.meshName = name;

            // Todo: This method makes very heavy use of Allocator.Temp

            // ConvexHullBuilder doesn't seem to properly check duplicated vertices when they are nice numbers.
            // So we deduplicate things ourselves.
            var hashedMeshVertices = new NativeParallelHashSet<float3>(vertices.Length, Allocator.Temp);
            for (int i = 0; i < vertices.Length; i++)
                hashedMeshVertices.Add(vertices[i]);
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
            var convexVertices = new NativeList<float3>(convexHullBuilder.vertices.peakCount, Allocator.Temp);
            var indexVertexMap = new NativeArray<byte>(convexHullBuilder.vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            foreach (int vIndex in convexHullBuilder.vertices.indices)
            {
                indexVertexMap[vIndex] = (byte)convexVertices.Length;
                convexVertices.Add(convexHullBuilder.vertices[vIndex].position);
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

            var viie = builder.Allocate(ref blobRoot.vertexIndicesInEdges, vertexIndicesInEdges.Length);
            for (int i = 0; i < vertexIndicesInEdges.Length; i++)
                viie[i] = new IndexPair { x = (byte)vertexIndicesInEdges[i].x, y = (byte)vertexIndicesInEdges[i].y };
            var eiif                                                             = builder.Allocate(ref blobRoot.edgeIndicesInFaces,
                                                                                                    edgeIndicesInFaces.Length);
            for (int i = 0; i < edgeIndicesInFaces.Length; i++)
                eiif[i] = new EdgeIndexInFace { raw = (ushort)(((ushort)edgeIndicesInFaces[i]) | math.select(0, 0x8000, edgeFlippedInFaces[i])) };
            var eiifsac                             = builder.Allocate(ref blobRoot.edgeIndicesInFacesStartsAndCounts,
                                                                       edgeIndicesInFacesStartsAndCounts.Length);
            for (int i = 0; i < edgeIndicesInFacesStartsAndCounts.Length; i++)
                eiifsac[i] = new StartAndCount
                {
                    start = (ushort)edgeIndicesInFacesStartsAndCounts[i].x,
                    count = (byte)edgeIndicesInFacesStartsAndCounts[i].y
                };

            var fibvsac = builder.Allocate(ref blobRoot.faceIndicesByVertexStartsAndCounts, convexVertices.Length);  // This clears memory
            var fibe    = builder.Allocate(ref blobRoot.faceIndicesByEdge, vertexIndicesInEdges.Length);

            var edgeNormals = builder.Allocate(ref blobRoot.edgeNormals, vertexIndicesInEdges.Length);  // Clears memory

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
                facePlaneDist[faceIndex] = plane.distanceToOrigin;

                var edgeIndicesStartAndCount = edgeIndicesInFacesStartsAndCounts[faceIndex];

                bool ccw = true;
                {
                    int2 abIndices = vertexIndicesInEdges[edgeIndicesInFaces[edgeIndicesStartAndCount.x]];
                    int2 cIndices  = vertexIndicesInEdges[edgeIndicesInFaces[edgeIndicesStartAndCount.x + 1]];
                    if (edgeFlippedInFaces[edgeIndicesStartAndCount.x])
                        abIndices = abIndices.yx;
                    if (edgeFlippedInFaces[edgeIndicesStartAndCount.x + 1])
                        cIndices = cIndices.yx;
                    float3 a     = convexVertices[abIndices.x];
                    float3 b     = convexVertices[abIndices.y];
                    float3 c     = convexVertices[cIndices.y];
                    ccw          = math.dot(math.cross(plane.normal, b - a), c - a) < 0f;
                }

                for (int faceEdgeIndex = edgeIndicesStartAndCount.x; faceEdgeIndex < edgeIndicesStartAndCount.x + edgeIndicesStartAndCount.y; faceEdgeIndex++)
                {
                    int edgeIndex           = edgeIndicesInFaces[faceEdgeIndex];
                    edgeNormals[edgeIndex] += plane.normal;

                    int2 abIndices = vertexIndicesInEdges[edgeIndex];
                    if (edgeFlippedInFaces[faceEdgeIndex])
                        abIndices                        = abIndices.yx;
                    float3 a                             = convexVertices[abIndices.x];
                    float3 b                             = convexVertices[abIndices.y];
                    var    outwardNormal                 = math.cross(plane.normal, b - a);
                    outwardNormal                        = math.select(-outwardNormal, outwardNormal, ccw);
                    faceEdgeOutwardPlanes[faceEdgeIndex] = new Plane(outwardNormal, -math.dot(outwardNormal, a));
                    fibvsac[abIndices.x].count++;
                }
            }

            var vertexNormals = builder.Allocate(ref blobRoot.vertexNormals, convexVertices.Length);  // Clears memory

            for (int edgeIndex = 0; edgeIndex < vertexIndicesInEdges.Length; edgeIndex++)
            {
                edgeNormals[edgeIndex] = math.normalize(edgeNormals[edgeIndex]);

                var abIndices               = vertexIndicesInEdges[edgeIndex];
                vertexNormals[abIndices.x] += edgeNormals[edgeIndex];
                vertexNormals[abIndices.y] += edgeNormals[edgeIndex];

                float3 a = convexVertices[abIndices.x];
                float3 b = convexVertices[abIndices.y];

                fibe[edgeIndex] = new IndexPair { x = 255, y = 255 };
            }

            var verticesX = builder.Allocate(ref blobRoot.verticesX, convexVertices.Length);
            var verticesY = builder.Allocate(ref blobRoot.verticesY, convexVertices.Length);
            var verticesZ = builder.Allocate(ref blobRoot.verticesZ, convexVertices.Length);

            Aabb aabb = new Aabb(convexVertices[0], convexVertices[0]);

            int runningCount = 0;
            for (int vertexIndex = 0; vertexIndex < convexVertices.Length; vertexIndex++)
            {
                var vertex = convexVertices[vertexIndex];

                verticesX[vertexIndex] = vertex.x;
                verticesY[vertexIndex] = vertex.y;
                verticesZ[vertexIndex] = vertex.z;

                aabb = Physics.CombineAabb(vertex, aabb);

                vertexNormals[vertexIndex]  = math.normalize(vertexNormals[vertexIndex]);
                fibvsac[vertexIndex].start  = (ushort)runningCount;
                runningCount               += fibvsac[vertexIndex].count;
            }

            if (convexHullBuilder.hullMassProperties.volume == 0f)
                convexHullBuilder.UpdateHullMassProperties();

            blobRoot.localAabb     = aabb;
            blobRoot.centerOfMass  = convexHullBuilder.hullMassProperties.centerOfMass;
            blobRoot.inertiaTensor = convexHullBuilder.hullMassProperties.inertiaTensor;
            mathex.DiagonalizeSymmetricApproximation(blobRoot.inertiaTensor, out var inertiaTensorOrientation, out blobRoot.unscaledInertiaTensorDiagonal);
            blobRoot.unscaledInertiaTensorOrientation = new quaternion(inertiaTensorOrientation);

            var fibv       = builder.Allocate(ref blobRoot.faceIndicesByVertex, runningCount);
            var fibvCounts = new NativeArray<byte>(convexVertices.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);

            for (int faceIndex = 0; faceIndex < edgeIndicesInFacesStartsAndCounts.Length; faceIndex++)
            {
                var edgeIndicesStartAndCount = edgeIndicesInFacesStartsAndCounts[faceIndex];
                for (int faceEdgeIndex = edgeIndicesStartAndCount.x; faceEdgeIndex < edgeIndicesStartAndCount.x + edgeIndicesStartAndCount.y; faceEdgeIndex++)
                {
                    int edgeIndex = edgeIndicesInFaces[faceEdgeIndex];
                    if (fibe[edgeIndex].x == 255)
                        fibe[edgeIndex].x = (byte)faceIndex;
                    else
                        fibe[edgeIndex].y = (byte)faceIndex;
                    int2 abIndices        = vertexIndicesInEdges[edgeIndex];
                    if (edgeFlippedInFaces[faceEdgeIndex])
                        abIndices                                               = abIndices.yx;
                    fibv[fibvsac[abIndices.x].start + fibvCounts[abIndices.x]]  = (byte)faceIndex;
                    fibvCounts[abIndices.x]                                    += 1;
                }
            }

            Build2DHull(ref builder,
                        ref blobRoot.yz2DVertexIndices,
                        null,
                        (float*)verticesY.GetUnsafePtr(),
                        (float*)verticesZ.GetUnsafePtr(),
                        (byte)verticesX.Length,
                        new float3(1f, 0f, 0f));
            Build2DHull(ref builder,
                        ref blobRoot.xz2DVertexIndices,
                        (float*)verticesX.GetUnsafePtr(),
                        null,
                        (float*)verticesZ.GetUnsafePtr(),
                        (byte)verticesX.Length,
                        new float3(0f, 1f, 0f));
            Build2DHull(ref builder,
                        ref blobRoot.xy2DVertexIndices,
                        (float*)verticesX.GetUnsafePtr(),
                        (float*)verticesY.GetUnsafePtr(),
                        null,
                        (byte)verticesX.Length,
                        new float3(0f, 0f, 1f));

            return builder.CreateBlobAssetReference<ConvexColliderBlob>(allocator);
        }

        unsafe static void Build2DHull(ref BlobBuilder builder, ref BlobArray<byte> hullTarget, float* xPtr, float* yPtr, float* zPtr, byte srcVerticesCount, float3 normal)
        {
            Span<float3> srcVertices = stackalloc float3[srcVerticesCount];
            for (int i = 0; i < srcVerticesCount; i++)
            {
                float x        = xPtr != null ? xPtr[i] : 0f;
                float y        = yPtr != null ? yPtr[i] : 0f;
                float z        = zPtr != null ? zPtr[i] : 0f;
                srcVertices[i] = new float3(x, y, z);
            }

            Span<byte> indices  = stackalloc byte[srcVerticesCount];
            var        hullSize = ExpandingPolygonBuilder2D.Build(ref indices, srcVertices, normal);
            var        result   = builder.Allocate(ref hullTarget, hullSize);
            for (int i = 0; i < hullSize; i++)
                result[i] = indices[i];
        }
    }
}

