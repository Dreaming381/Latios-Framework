using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

// The contents of this file mostly come from Unity.Physics. It is primarily used in EPA.
// Only variable names and math utility methods were changed. The math utilities are functionally
// equivalent but Psyshock presents a slightly different API.
// Simplifying the memory model and using hardware 128 bit multiplication are two possible future improvements.

namespace Latios.Psyshock
{
    /// <summary>
    /// Convex hull builder.
    /// </summary>
    [NoAlias]
    unsafe internal struct ConvexHullBuilder
    {
        // Mesh representation of the hull
        [NoAlias]
        private ElementPoolBase m_vertices;
        [NoAlias]
        private ElementPoolBase m_triangles;

        public unsafe ElementPool<Vertex> vertices
        {
            get
            {
                fixed (ElementPoolBase* vertices = &m_vertices)
                {
                    return new ElementPool<Vertex> { elementPoolBase = vertices };
                }
            }
        }

        public unsafe ElementPool<Triangle> triangles
        {
            get
            {
                fixed (ElementPoolBase* triangles = &m_triangles)
                {
                    return new ElementPool<Triangle> { elementPoolBase = triangles };
                }
            }
        }

        // Number of bits with which vertices are quantized
        public enum IntResolution
        {
            Low,  // 16 bit, sufficient for ConvexConvexDistanceQueries
            High  // 30 bit, required to build hulls larger than ~1m without nearly parallel faces
        }

        // Array of faces' planes, length = NumFaces, updated when BuildFaceIndices() is called
        public Plane* planes { get; private set; }

        // -1 for empty, 0 for single point, 1 for line segment, 2 for flat convex polygon, 3 for convex polytope
        public int dimension { get; private set; }

        // Number of faces, coplanar triangles make a single face, updated when BuildFaceIndices() is called
        public int numFaces { get; private set; }

        // Sum of vertex counts of all faces.  This is greater than the number of elements in Vertices because
        // each vertex is appears on multiple faces.
        public int numFaceVertices { get; private set; }

        // Valid only when Dimension == 2, the plane in which the hull lies
        public Plane projectionPlane { get; private set; }

        // Valid only after calling UpdateHullMassProperties()
        public MassProperties hullMassProperties { get; private set; }

        private long          m_intNormalDirectionX;
        private long          m_intNormalDirectionY;
        private long          m_intNormalDirectionZ;
        private IntResolution m_intResolution;
        private IntegerSpace  m_integerSpace;
        private Aabb          m_integerSpaceAabb;
        private uint          m_nextUid;

        private const float k_planeEps = 1e-4f;  // Maximum distance any vertex in a face can be from the plane

        /// <summary>
        /// Convex hull vertex.
        /// </summary>
        public struct Vertex : IPoolElement
        {
            public float3 position;
            public int3   intPosition;
            public int    cardinality;
            public uint   userData;
            public int nextFree { get { return (int)userData; } set { userData = (uint)value; } }

            public bool isAllocated => cardinality != -1;

            public Vertex(float3 position, uint userData)
            {
                this.position = position;
                this.userData = userData;
                cardinality   = 0;
                intPosition   = new int3(0);
            }

            void IPoolElement.MarkFree(int nextFree)
            {
                cardinality   = -1;
                this.nextFree = nextFree;
            }
        }

        /// <summary>
        /// Convex hull triangle.
        /// </summary>
        public struct Triangle : IPoolElement
        {
            public int  vertex0, vertex1, vertex2;
            public Edge link0, link1, link2;
            public int  faceIndex;
            public uint uid { get; private set; }
            public int nextFree { get { return (int)uid; } set { uid = (uint)value; } }

            public bool isAllocated => faceIndex != -2;

            public Triangle(int vertex0, int vertex1, int vertex2, uint uid)
            {
                faceIndex    = 0;
                this.vertex0 = vertex0;
                this.vertex1 = vertex1;
                this.vertex2 = vertex2;
                link0        = Edge.k_invalid;
                link1        = Edge.k_invalid;
                link2        = Edge.k_invalid;
                this.uid     = uid;
            }

            public unsafe int GetVertex(int index)
            {
                fixed (int* p = &vertex0)
                {
                    return p[index];
                }
            }
            public unsafe void SetVertex(int index, int value)
            {
                fixed (int* p = &vertex0)
                {
                    p[index] = value;
                }
            }

            public unsafe Edge GetLink(int index)
            {
                fixed (Edge* p = &link0)
                {
                    return p[index];
                }
            }
            public unsafe void SetLink(int index, Edge handle)
            {
                fixed (Edge* p = &link0)
                {
                    p[index] = handle;
                }
            }

            void IPoolElement.MarkFree(int nextFree)
            {
                faceIndex     = -2;
                this.nextFree = nextFree;
            }
        }

        /// <summary>
        /// An edge of a triangle, used internally to traverse hull topology.
        /// </summary>
        public struct Edge : IEquatable<Edge>
        {
            public readonly int data;

            public bool isValid => data != k_invalid.data;
            public int triangleIndex => data >> 2;
            public int edgeIndex => data & 3;

            public static readonly Edge k_invalid = new Edge(0x7fffffff);

            public Edge(int value)
            {
                data = value;
            }
            public Edge(int triangleIndex, int edgeIndex)
            {
                data = triangleIndex << 2 | edgeIndex;
            }

            public Edge Next => isValid ? new Edge(triangleIndex, (edgeIndex + 1) % 3) : k_invalid;
            public Edge Prev => isValid ? new Edge(triangleIndex, (edgeIndex + 2) % 3) : k_invalid;

            public bool Equals(Edge other) => data == other.data;
        }

        /// <summary>
        /// An edge of a face (possibly made from multiple triangles).
        /// </summary>
        public struct FaceEdge
        {
            public Edge start;  // the first edge of the face
            public Edge current;  // the current edge of the face

            public bool isValid => current.isValid;

            public static readonly FaceEdge k_invalid = new FaceEdge { start = Edge.k_invalid, current = Edge.k_invalid };

            public static implicit operator Edge(FaceEdge fe) => fe.current;
        }

        /// <summary>
        /// Convex hull mass properties.
        /// </summary>
        public struct MassProperties
        {
            public float3   centerOfMass;
            public float3x3 inertiaTensor;
            public float    surfaceArea;
            public float    volume;
        }

        /// <summary>
        /// A quantized integer space.
        /// </summary>
        private struct IntegerSpace
        {
            // int * Scale + Offset = float
            public readonly float3 offset;
            public readonly float  scale;
            public readonly float  invScale;

            public IntegerSpace(Aabb aabb, int resolution)
            {
                Physics.GetCenterExtents(aabb, out var center, out var extents);
                float extent = math.cmax(extents);
                scale        = extent / resolution;
                invScale     = math.select(resolution / extent, 0, extent <= 0);
                offset       = center - (extent / 2);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int3 ToIntegerSpace(float3 x) => new int3((x - offset) * invScale + 0.5f);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public float3 ToFloatSpace(int3 x) => x * new float3(scale) + offset;
        }

        // Create a hull builder with external storage
        // vertices must be at least large enough to hold verticesCapacity elements, triangles and planes must be large enough to hold 2 * verticesCapacity elements
        // domain is the AABB of all points that will be added to the hull
        // simplificationTolerance is the sum of tolerances that will be passed to SimplifyVertices() and SimplifyFacesAndShrink()
        public unsafe ConvexHullBuilder(int verticesCapacity, Vertex* vertices, Triangle* triangles, Plane* planes,
                                        Aabb domain, float simplificationTolerance, IntResolution intResolution)
        {
            m_vertices            = new ElementPoolBase(vertices, verticesCapacity);
            m_triangles           = new ElementPoolBase(triangles, 2 * verticesCapacity);
            this.planes           = planes;
            dimension             = -1;
            numFaces              = 0;
            numFaceVertices       = 0;
            projectionPlane       = new Plane(new float3(0), 0);
            hullMassProperties    = new MassProperties();
            m_intNormalDirectionX = 0;
            m_intNormalDirectionY = 0;
            m_intNormalDirectionZ = 0;
            m_intResolution       = intResolution;
            m_nextUid             = 1;

            // Add some margin for error to the domain.  This loses some quantization resolution and therefore some accuracy, but it's possible that
            // SimplifyVertices and SimplifyFacesAndMerge will not stay perfectly within the requested error limits, and expanding the limits avoids
            // clipping against the domain AABB in AddPoint
            const float constantMargin = 0.01f;
            const float linearMargin   = 0.1f;
            Physics.GetCenterExtents(domain, out var center, out var extents);
            extents            += math.max(simplificationTolerance * 2f, constantMargin);
            extents            += math.cmax(extents) * linearMargin;
            m_integerSpaceAabb  = new Aabb(center - extents, center + extents);

            int quantizationBits = (intResolution == IntResolution.Low ? 16 : 30);
            m_integerSpace       = new IntegerSpace(domain, (1 << quantizationBits) - 1);
        }

        /// <summary>
        /// Copy the content of another convex hull into this one.
        /// </summary>
        public unsafe ConvexHullBuilder(int verticesCapacity, Vertex* vertices, Triangle* triangles, Plane* planes,
                                        ConvexHullBuilder other)
        {
            m_vertices            = new ElementPoolBase(vertices, verticesCapacity);
            m_triangles           = new ElementPoolBase(triangles, 2 * verticesCapacity);
            this.planes           = planes;
            dimension             = other.dimension;
            numFaces              = other.numFaces;
            numFaceVertices       = other.numFaceVertices;
            projectionPlane       = other.projectionPlane;
            hullMassProperties    = other.hullMassProperties;
            m_intNormalDirectionX = other.m_intNormalDirectionX;
            m_intNormalDirectionY = other.m_intNormalDirectionY;
            m_intNormalDirectionZ = other.m_intNormalDirectionZ;
            m_intResolution       = other.m_intResolution;
            m_nextUid             = other.m_nextUid;
            m_integerSpaceAabb    = other.m_integerSpaceAabb;
            m_integerSpace        = other.m_integerSpace;

            this.vertices.CopyFrom(other.vertices);
            this.triangles.CopyFrom(other.triangles);
            if (other.numFaces > 0)
            {
                UnsafeUtility.MemCpy(this.planes, other.planes, other.numFaces * sizeof(Plane));
            }
        }

        #region Construction

        /// <summary>
        /// Reset the convex hull.
        /// </summary>
        public void Reset()
        {
            vertices.Clear();
            triangles.Clear();
            dimension       = -1;
            numFaces        = 0;
            numFaceVertices = 0;
            projectionPlane = new Plane(new float3(0), 0);
        }

        //
        public unsafe void Compact()
        {
            // Compact the vertices array
            NativeArray<int> vertexRemap = new NativeArray<int>(vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            if (vertices.Compact((int*)vertexRemap.GetUnsafePtr()))
            {
                // Remap all of the vertices in triangles, then compact the triangles array
                foreach (int t in triangles.Indices)
                {
                    Triangle tri = triangles[t];
                    tri.vertex0  = vertexRemap[tri.vertex0];
                    tri.vertex1  = vertexRemap[tri.vertex1];
                    tri.vertex2  = vertexRemap[tri.vertex2];
                    triangles.Set(t, tri);
                }
            }

            triangles.Compact(null);
        }

        /// <summary>
        /// Add a point the the convex hull.
        /// </summary>
        /// <param name="point">Point to insert.</param>
        /// <param name="userData">User data attached to the new vertex if insertion succeeds.</param>
        /// <param name="force2D">If true, the hull will not grow beyond two dimensions.</param>
        /// <returns>true if the insertion succeeded, false otherwise.</returns>
        public unsafe bool AddPoint(float3 point, uint userData = 0, bool force2D = false)
        {
            // Reset faces.
            numFaces        = 0;
            numFaceVertices = 0;

            // Return false if there is not enough room to allocate a vertex.
            if (!vertices.canAllocate)
            {
                return false;
            }

            // Point should be inside the quantization AABB, if not then clip it
            if (!math.all(point >= m_integerSpaceAabb.min & point <= m_integerSpaceAabb.max))
            {
                point = math.max(math.min(point, m_integerSpaceAabb.max), m_integerSpaceAabb.min);
            }
            int3 intPoint = m_integerSpace.ToIntegerSpace(point);

            // Insert vertex.
            switch (dimension)
            {
                // Empty hull, just add a vertex.
                case -1:
                {
                    AllocateVertex(point, userData);
                    dimension = 0;
                }
                break;

                // 0 dimensional hull, make a line.
                case 0:
                {
                    const float minDistanceFromPoint = 1e-5f;
                    if (math.lengthsq(vertices[0].position - point) <= minDistanceFromPoint * minDistanceFromPoint)
                        return false;
                    AllocateVertex(point, userData);
                    dimension = 1;
                }
                break;

                // 1 dimensional hull, make a triangle.
                case 1:
                {
                    IntCross(vertices[0].intPosition - intPoint, vertices[1].intPosition - intPoint, out long normalDirectionX, out long normalDirectionY,
                             out long normalDirectionZ);
                    if (normalDirectionX == 0 && normalDirectionY == 0 && normalDirectionZ == 0)
                    {
                        // Still 1D, keep whichever two vertices are farthest apart
                        float3x3 edgesTransposed =
                            math.transpose(new float3x3(vertices[1].position - vertices[0].position, point - vertices[1].position, vertices[0].position - point));
                        float3 edgesLengthSq = edgesTransposed.c0 * edgesTransposed.c0 + edgesTransposed.c1 * edgesTransposed.c1 + edgesTransposed.c2 * edgesTransposed.c2;
                        bool3  isLongestEdge = edgesLengthSq == math.cmax(edgesLengthSq);
                        if (isLongestEdge.y)
                        {
                            Vertex newVertex      = vertices[0];
                            newVertex.position    = point;
                            newVertex.intPosition = m_integerSpace.ToIntegerSpace(point);
                            newVertex.userData    = userData;
                            vertices.Set(0, newVertex);
                        }
                        else if (isLongestEdge.z)
                        {
                            Vertex newVertex      = vertices[1];
                            newVertex.position    = point;
                            newVertex.intPosition = m_integerSpace.ToIntegerSpace(point);
                            newVertex.userData    = userData;
                            vertices.Set(1, newVertex);
                        }  // else, point is on the edge between Vertices[0] and Vertices[1]
                    }
                    else
                    {
                        // Extend dimension.
                        AllocateVertex(point, userData);
                        dimension             = 2;
                        projectionPlane       = ComputePlane(0, 1, 2, true);
                        m_intNormalDirectionX = normalDirectionX;
                        m_intNormalDirectionY = normalDirectionY;
                        m_intNormalDirectionZ = normalDirectionZ;
                    }
                }
                break;

                // 2 dimensional hull, make a volume or expand face.
                case 2:
                {
                    long det = 0;
                    if (!force2D)
                    {
                        // Try to expand to a 3D hull
                        for (int i = vertices.peakCount - 2, j = vertices.peakCount - 1, k = 0; k < vertices.peakCount - 2; i = j, j = k, k++)
                        {
                            det = IntDet(i, j, k, intPoint);
                            if (det != 0)
                            {
                                // Extend dimension.
                                projectionPlane = new Plane(new float3(0), 0);

                                // Orient tetrahedron.
                                if (det > 0)
                                {
                                    Vertex t = vertices[k];
                                    vertices.Set(k, vertices[j]);
                                    vertices.Set(j, t);
                                }

                                // Allocate vertex.
                                int nv          = vertices.peakCount;
                                int vertexIndex = AllocateVertex(point, userData);

                                // Build tetrahedron.
                                dimension = 3;
                                Edge nt0  = AllocateTriangle(i, j, k);
                                Edge nt1  = AllocateTriangle(j, i, vertexIndex);
                                Edge nt2  = AllocateTriangle(k, j, vertexIndex);
                                Edge nt3  = AllocateTriangle(i, k, vertexIndex);
                                BindEdges(nt0,      nt1); BindEdges(nt0.Next, nt2); BindEdges(nt0.Prev, nt3);
                                BindEdges(nt1.Prev, nt2.Next); BindEdges(nt2.Prev, nt3.Next); BindEdges(nt3.Prev, nt1.Next);

                                // Re-insert other vertices.
                                bool success = true;
                                for (int v = 0; v < nv; v++)
                                {
                                    if (v == i || v == j || v == k)
                                    {
                                        continue;
                                    }
                                    Vertex vertex = vertices[v];
                                    vertices.Release(v);
                                    success = success & AddPoint(vertex.position, vertex.userData);
                                }
                                return success;
                            }
                        }
                    }
                    if (det == 0)
                    {
                        // Hull is still 2D
                        bool* isOutside    = stackalloc bool[vertices.peakCount];
                        bool  isOutsideAny = false;
                        for (int i = vertices.peakCount - 1, j = 0; j < vertices.peakCount; i = j++)
                        {
                            // Test if the point is inside the edge
                            // Note, even with 16 bit quantized coordinates, we cannot fit this calculation in 64 bit integers
                            int3 edge  = vertices[j].intPosition - vertices[i].intPosition;
                            int3 delta = intPoint - vertices[i].intPosition;
                            IntCross(edge, delta, out long cx, out long cy, out long cz);
                            Int128 dot    = Int128.Mul(m_intNormalDirectionX, cx) + Int128.Mul(m_intNormalDirectionY, cy) + Int128.Mul(m_intNormalDirectionZ, cz);
                            isOutside[i]  = dot.IsNegative;
                            isOutsideAny |= isOutside[i];
                        }

                        // If the point is outside the hull, insert it and remove any points that it was outside of
                        if (isOutsideAny)
                        {
                            Vertex* newVertices        = stackalloc Vertex[vertices.peakCount + 1];
                            int     numNewVertices     = 1;
                            newVertices[0]             = new Vertex(point, userData);
                            newVertices[0].intPosition = intPoint;
                            for (int i = vertices.peakCount - 1, j = 0; j < vertices.peakCount; i = j++)
                            {
                                if (isOutside[i] && isOutside[i] != isOutside[j])
                                {
                                    newVertices[numNewVertices++] = vertices[j];
                                    for (; ; )
                                    {
                                        if (isOutside[j])
                                            break;
                                        j                             = (j + 1) % vertices.peakCount;
                                        newVertices[numNewVertices++] = vertices[j];
                                    }
                                    break;
                                }
                            }

                            vertices.CopyFrom(newVertices, numNewVertices);
                        }
                    }
                }
                break;

                // 3 dimensional hull, add vertex.
                case 3:
                {
                    int* nextTriangles = stackalloc int[triangles.peakCount];
                    for (int i = 0; i < triangles.peakCount; i++)
                    {
                        nextTriangles[i] = -1;
                    }

                    Edge* newEdges = stackalloc Edge[vertices.peakCount];
                    for (int i = 0; i < vertices.peakCount; i++)
                    {
                        newEdges[i] = Edge.k_invalid;
                    }

                    // Classify all triangles as either front(faceIndex = 1) or back(faceIndex = -1).
                    int    firstFrontTriangleIndex = -1, numFrontTriangles = 0, numBackTriangles = 0;
                    int    lastFrontTriangleIndex  = -1;
                    float3 floatPoint              = m_integerSpace.ToFloatSpace(intPoint);
                    float  maxDistance             = 0.0f;
                    foreach (int triangleIndex in triangles.Indices)
                    {
                        Triangle triangle = triangles[triangleIndex];
                        long     det      = IntDet(triangle.vertex0, triangle.vertex1, triangle.vertex2, intPoint);
                        if (det == 0)
                        {
                            // Check for duplicated vertex.
                            if (math.all(vertices[triangle.vertex0].intPosition == intPoint))
                                return false;
                            if (math.all(vertices[triangle.vertex1].intPosition == intPoint))
                                return false;
                            if (math.all(vertices[triangle.vertex2].intPosition == intPoint))
                                return false;
                        }
                        if (det > 0)
                        {
                            nextTriangles[triangleIndex] = firstFrontTriangleIndex;
                            firstFrontTriangleIndex      = triangleIndex;
                            if (lastFrontTriangleIndex == -1)
                            {
                                lastFrontTriangleIndex = triangleIndex;
                            }

                            triangle.faceIndex = 1;
                            numFrontTriangles++;

                            Plane plane    = ComputePlane(triangleIndex, true);
                            float distance = math.dot(plane.normal, floatPoint) + plane.distanceFromOrigin;
                            maxDistance    = math.max(distance, maxDistance);
                        }
                        else
                        {
                            triangle.faceIndex = -1;
                            numBackTriangles++;
                        }
                        triangles.Set(triangleIndex, triangle);
                    }

                    // Return false if the vertex is inside the hull
                    if (numFrontTriangles == 0 || numBackTriangles == 0)
                    {
                        return false;
                    }

                    // Link boundary loop.
                    Edge loopEdge  = Edge.k_invalid;
                    int  loopCount = 0;
                    for (int frontTriangle = firstFrontTriangleIndex; frontTriangle != -1; frontTriangle = nextTriangles[frontTriangle])
                    {
                        for (int j = 0; j < 3; ++j)
                        {
                            var  edge     = new Edge(frontTriangle, j);
                            Edge linkEdge = GetLinkedEdge(edge);
                            if (triangles[linkEdge.triangleIndex].faceIndex == -1)
                            {
                                int vertexIndex = StartVertex(linkEdge);

                                // Vertex already bound.
                                Assert.IsTrue(newEdges[vertexIndex].Equals(Edge.k_invalid));

                                // Link.
                                newEdges[vertexIndex] = linkEdge;
                                loopEdge              = linkEdge;
                                loopCount++;
                            }
                        }
                    }

                    // Return false if there is not enough room to allocate new triangles.
                    if ((triangles.peakCount + loopCount - numFrontTriangles) > triangles.capacity)
                    {
                        return false;
                    }

                    // Release front triangles.
                    do
                    {
                        int next = nextTriangles[firstFrontTriangleIndex];
                        ReleaseTriangle(firstFrontTriangleIndex);
                        firstFrontTriangleIndex = next;
                    }
                    while (firstFrontTriangleIndex != -1);

                    // Add vertex.
                    int newVertex = AllocateVertex(point, userData);

                    // Add fan of triangles.
                    {
                        Edge firstFanEdge = Edge.k_invalid, lastFanEdge = Edge.k_invalid;
                        for (int i = 0; i < loopCount; ++i)
                        {
                            int  v0 = StartVertex(loopEdge);
                            int  v1 = EndVertex(loopEdge);
                            Edge t  = AllocateTriangle(v1, v0, newVertex);
                            BindEdges(loopEdge, t);
                            if (lastFanEdge.isValid)
                                BindEdges(t.Next, lastFanEdge.Prev);
                            else
                                firstFanEdge = t;

                            lastFanEdge = t;
                            loopEdge    = newEdges[v1];
                        }
                        BindEdges(lastFanEdge.Prev, firstFanEdge.Next);
                    }
                }
                break;
            }
            return true;
        }

        // Flatten the hull to 2D
        // This is used to handle edge cases where very thin 3D hulls become 2D or invalid during simplification.
        // Extremely thin 3D hulls inevitably have nearly parallel faces, which cause problems in collision detection,
        // so the best solution is to flatten them completely.
        public unsafe void Rebuild2D()
        {
            Assert.AreEqual(dimension, 3);

            // Copy the vertices and compute the OLS plane
            Plane   plane;
            float3* tempVertices = stackalloc float3[vertices.peakCount];
            Aabb    aabb         = new Aabb(float.MaxValue, float.MinValue);
            int     numVertices  = 0;
            {
                OLSData data = new OLSData();
                data.Init();
                foreach (int v in vertices.Indices)
                {
                    float3 position             = vertices[v].position;
                    tempVertices[numVertices++] = position;
                    data.Include(position, 1.0f);
                    //aabb.Include(position);
                    aabb = Physics.CombineAabb(position, aabb);
                }
                Physics.GetCenterExtents(aabb, out _, out var extents);
                float3 direction = 1.0f / math.max(1e-10f, extents);  // Use the min aabb extent as regressand
                data.Solve(direction, direction);
                plane = data.plane;
            }

            // Rebuild the hull from the projection of the vertices to the plane
            Reset();
            for (int i = 0; i < numVertices; i++)
            {
                const bool force2D = true;
                AddPoint(mathex.projectPoint(plane, tempVertices[i]), 0, force2D);
            }

            BuildFaceIndices();
        }

        // Helper to sort triangles in BuildFaceIndices
        unsafe struct CompareAreaDescending : IComparer<int>
        {
            public NativeArray<float> areas;
            public CompareAreaDescending(NativeArray<float> areas)
            {
                this.areas = areas;
            }
            public int Compare(int x, int y)
            {
                return areas[y].CompareTo(areas[x]);
            }
        }

        // Set the face index for each triangle. Triangles lying in the same plane will have the same face index.
        public void BuildFaceIndices(NativeArray<Plane> planes = default)
        {
            const float convexEps = 1e-5f;  // Maximum error allowed in face convexity

            numFaces        = 0;
            numFaceVertices = 0;

            NativeArray<bool> planesUsed = new NativeArray<bool>();
            if (planes.IsCreated)
            {
                planesUsed = new NativeArray<bool>(planes.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            }

            switch (dimension)
            {
                case 2:
                    numFaces        = 2;
                    numFaceVertices = 2 * vertices.peakCount;
                    break;

                case 3:
                {
                    // Make a compact list of triangles and their areas
                    NativeArray<int>   triangleIndices = new NativeArray<int>(triangles.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    NativeArray<float> triangleAreas   = new NativeArray<float>(triangles.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    int                numTriangles    = 0;
                    foreach (int triangleIndex in triangles.Indices)
                    {
                        Triangle t                      = triangles[triangleIndex];
                        float3   o                      = vertices[t.vertex0].position;
                        float3   a                      = vertices[t.vertex1].position - o;
                        float3   b                      = vertices[t.vertex2].position - o;
                        triangleAreas[triangleIndex]    = math.lengthsq(math.cross(a, b));
                        triangleIndices[numTriangles++] = triangleIndex;
                    }

                    // Sort the triangles by descending area. It is best to choose the face plane from the largest triangle
                    // because 1) it minimizes the distance to other triangles and therefore the plane error, and 2) it avoids numerical
                    // problems computing degenerate triangle normals
                    triangleIndices.GetSubArray(0, numTriangles).Sort(new CompareAreaDescending(triangleAreas));

                    // Clear faces
                    NativeArray<Edge> boundaryEdges = new NativeArray<Edge>(triangles.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int iTriangle = 0; iTriangle < numTriangles; iTriangle++)
                    {
                        int      triangleIndex = triangleIndices[iTriangle];
                        Triangle t             = triangles[triangleIndex]; t.faceIndex = -1; triangles.Set(triangleIndex, t);
                    }

                    // Merge triangles into faces
                    for (int iTriangle = 0; iTriangle < numTriangles; iTriangle++)
                    {
                        // Check if the triangle is already part of a face
                        int triangleIndex = triangleIndices[iTriangle];
                        if (triangles[triangleIndex].faceIndex != -1)
                        {
                            continue;
                        }

                        // Create a new face
                        int      newFaceIndex = numFaces++;
                        Triangle t            = triangles[triangleIndex]; t.faceIndex = newFaceIndex; triangles.Set(triangleIndex, t);

                        // Search for the plane that best fits the triangle
                        int bestPlane = -1;
                        if (planes != null)
                        {
                            float  bestError = k_planeEps;
                            float3 a         = vertices[t.vertex0].position;
                            float3 b         = vertices[t.vertex1].position;
                            float3 c         = vertices[t.vertex2].position;
                            for (int i = 0; i < planes.Length; i++)
                            {
                                if (planesUsed[i])
                                    continue;
                                Plane  currentPlane = planes[i];
                                float3 errors       =
                                    new float3(mathex.signedDistance(currentPlane, a), mathex.signedDistance(currentPlane, b), mathex.signedDistance(currentPlane, c));
                                float error = math.cmax(math.abs(errors));
                                if (error < bestError)
                                {
                                    bestError = error;
                                    bestPlane = i;
                                }
                            }
                        }

                        // If a plane that fits the triangle was found, use it.  Otherwise compute one from the triangle vertices
                        Plane plane;
                        if (bestPlane < 0)
                        {
                            plane = ComputePlane(triangleIndex);
                        }
                        else
                        {
                            planesUsed[bestPlane] = true;
                            plane                 = planes[bestPlane];
                        }
                        this.planes[newFaceIndex] = plane;

                        // Search for neighboring triangles that can be added to the face
                        boundaryEdges[0]     = new Edge(triangleIndex, 0);
                        boundaryEdges[1]     = new Edge(triangleIndex, 1);
                        boundaryEdges[2]     = new Edge(triangleIndex, 2);
                        int numBoundaryEdges = 3;
                        while (true)
                        {
                            int   openBoundaryEdgeIndex = -1;
                            float maxArea               = -1;

                            for (int i = 0; i < numBoundaryEdges; ++i)
                            {
                                Edge edge       = boundaryEdges[i];
                                Edge linkedEdge = GetLinkedEdge(edge);

                                int linkedTriangleIndex = linkedEdge.triangleIndex;

                                if (triangles[linkedTriangleIndex].faceIndex != -1)
                                    continue;
                                if (triangleAreas[linkedTriangleIndex] <= maxArea)
                                    continue;

                                int    apex      = ApexVertex(linkedEdge);
                                float3 newVertex = vertices[apex].position;
                                if (math.abs(mathex.signedDistance(plane, newVertex)) > k_planeEps)
                                {
                                    continue;
                                }

                                float3 linkedNormal = ComputePlane(linkedTriangleIndex).normal;
                                if (math.dot(plane.normal, linkedNormal) < 0.0f)
                                {
                                    continue;
                                }

                                float4 p0 = mathex.planeFrom(newVertex, newVertex - vertices[StartVertex(edge)].position, plane.normal);
                                float4 p1 = mathex.planeFrom(newVertex, vertices[EndVertex(edge)].position - newVertex, plane.normal);

                                var accept = true;
                                for (int j = 1; accept && j < (numBoundaryEdges - 1); ++j)
                                {
                                    float3 x  = vertices[EndVertex(boundaryEdges[(i + j) % numBoundaryEdges])].position;
                                    float  d  = math.max(math.dot(p0, x.xyz1()), math.dot(p1, x.xyz1()));
                                    accept   &= d < convexEps;
                                }

                                if (accept)
                                {
                                    openBoundaryEdgeIndex = i;
                                    maxArea               = triangleAreas[linkedTriangleIndex];
                                }
                            }

                            if (openBoundaryEdgeIndex != -1)
                            {
                                Edge linkedEdge = GetLinkedEdge(boundaryEdges[openBoundaryEdgeIndex]);

                                // Check if merge has made the shape 2D, if so then quit
                                if (numBoundaryEdges >= boundaryEdges.Length)
                                {
                                    numFaces = 3;  // force 2D rebuild
                                    break;
                                }

                                // Insert two edges in place of the open boundary edge
                                for (int i = numBoundaryEdges; i > openBoundaryEdgeIndex; i--)
                                {
                                    boundaryEdges[i] = boundaryEdges[i - 1];
                                }
                                numBoundaryEdges++;
                                boundaryEdges[openBoundaryEdgeIndex]     = linkedEdge.Next;
                                boundaryEdges[openBoundaryEdgeIndex + 1] = linkedEdge.Prev;

                                Triangle tri  = triangles[linkedEdge.triangleIndex];
                                tri.faceIndex = newFaceIndex;
                                triangles.Set(linkedEdge.triangleIndex, tri);
                            }
                            else
                            {
                                break;
                            }
                        }
                        numFaceVertices += numBoundaryEdges;
                    }

                    // Triangle merging may turn 3D shapes into 2D, check for that case and reduce the dimension
                    if (numFaces < 4)
                    {
                        Rebuild2D();
                    }
                }
                break;
            }
        }

        private int AllocateVertex(float3 point, uint userData)
        {
            Assert.IsTrue(math.all(point >= m_integerSpaceAabb.min & point <= m_integerSpaceAabb.max));
            var vertex      = new Vertex(point, userData) {
                intPosition = m_integerSpace.ToIntegerSpace(point)
            };
            return vertices.Allocate(vertex);
        }

        private Edge AllocateTriangle(int vertex0, int vertex1, int vertex2)
        {
            Triangle triangle      = new Triangle(vertex0, vertex1, vertex2, m_nextUid++);
            int      triangleIndex = triangles.Allocate(triangle);

            Vertex v;
            v = vertices[vertex0]; v.cardinality++; vertices.Set(vertex0, v);
            v = vertices[vertex1]; v.cardinality++; vertices.Set(vertex1, v);
            v = vertices[vertex2]; v.cardinality++; vertices.Set(vertex2, v);

            return new Edge(triangleIndex, 0);
        }

        private void ReleaseTriangle(int triangle, bool releaseOrphanVertices = true)
        {
            for (int i = 0; i < 3; ++i)
            {
                int    j = triangles[triangle].GetVertex(i);
                Vertex v = vertices[j];
                v.cardinality--;
                vertices.Set(j, v);
                if (v.cardinality == 0 && releaseOrphanVertices)
                {
                    vertices.Release(j);
                }
            }

            triangles.Release(triangle);
        }

        private void BindEdges(Edge lhs, Edge rhs)
        {
            // Incompatible edges.
            Assert.IsTrue(EndVertex(lhs) == StartVertex(rhs) && StartVertex(lhs) == EndVertex(rhs));

            Triangle lf = triangles[lhs.triangleIndex];
            Triangle rf = triangles[rhs.triangleIndex];
            lf.SetLink(lhs.edgeIndex, rhs);
            rf.SetLink(rhs.edgeIndex, lhs);
            triangles.Set(lhs.triangleIndex, lf);
            triangles.Set(rhs.triangleIndex, rf);
        }

        #endregion

        #region Simplification

        // Removes vertices that are colinear with two neighbors or coplanar with all neighbors.
        public unsafe void RemoveRedundantVertices()
        {
            const float toleranceSq = 1e-10f;

            NativeArray<Vertex> newVertices = new NativeArray<Vertex>(vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<bool>   removed     = new NativeArray<bool>(vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            while (true)
            {
                bool remove = false;
                if (dimension != 3)
                    break;

                for (int i = 0; i < vertices.peakCount; i++)
                {
                    removed[i] = false;
                }

                int numNewVertices = 0;
                foreach (int v in vertices.Indices)
                {
                    float3 x          = vertices[v].position;
                    bool   keep       = true;
                    bool   coplanar   = true;
                    bool   anyRemoved = false;

                    // For each pair of edges incident to v
                    Edge firstEdge = GetVertexEdge(v);
                    Edge edge0     = firstEdge;
                    do
                    {
                        Triangle triangle0  = triangles[edge0.triangleIndex];
                        int      index0     = triangle0.GetVertex((edge0.edgeIndex + 1) % 3);
                        anyRemoved         |= removed[index0];
                        if (!removed[index0])  // Ignore already removed vertices
                        {
                            // Double precision is necessary because the calculation involves comparing a difference of squares, which loses a lot of accuracy for long
                            // edges, against a very small fixed tolerance
                            double3 v0            = vertices[index0].position;
                            double3 edge0Vec      = x - v0;
                            double  edge0LengthSq = math.lengthsq(edge0Vec);
                            Edge    edge1         = GetLinkedEdge(edge0.Prev);

                            // Test if the triangle normals face the same direction.  This is necessary for nearly flat hulls, where a vertex can be on triangles that
                            // have nearly opposite face normals, so all of the points may be nearly coplanar but the vertex is not safe to remove
                            {
                                Triangle triangle1  = triangles[edge1.triangleIndex];
                                float3   v00        = vertices[triangle0.vertex0].position, v01 = vertices[triangle0.vertex1].position, v02 = vertices[triangle0.vertex2].position;
                                float3   v10        = vertices[triangle1.vertex0].position, v11 = vertices[triangle1.vertex1].position, v12 = vertices[triangle1.vertex2].position;
                                coplanar           &= (math.dot(math.cross(v01 - v00, v02 - v00), math.cross(v11 - v10, v12 - v10)) >= 0);
                            }

                            while (edge1.data != firstEdge.data && keep)
                            {
                                // Test if the distance from x to the line through v1 and v0 is less than tolerance.  If not, then the three vertices
                                // are colinear and x is unnecessary.
                                // The math below is derived from the fact that the distance is the length of the rejection of (x - v0) from (v1 - v0), and
                                // lengthSq(rejection) + lengthSq(x - v0) = lengthSq(projection)
                                int     index1 = triangles[edge1.triangleIndex].GetVertex((edge1.edgeIndex + 1) % 3);
                                double3 v1     = vertices[index1].position;
                                if (!removed[index1])  // Ignore already removed vertices
                                {
                                    double3 lineVec          = v1 - v0;
                                    double  lineVecLengthSq  = math.lengthsq(lineVec);
                                    double  dot              = math.dot(edge0Vec, lineVec);
                                    double  diffSq           = edge0LengthSq * lineVecLengthSq - dot * dot;
                                    double  scaledTolSq      = toleranceSq * lineVecLengthSq;
                                    keep                    &= (dot < 0 || dot > lineVecLengthSq || diffSq > scaledTolSq);

                                    Edge edge2 = GetLinkedEdge(edge1.Prev);
                                    if (edge2.data != firstEdge.data)
                                    {
                                        int index2 = triangles[edge2.triangleIndex].GetVertex((edge2.edgeIndex + 1) % 3);
                                        if (!removed[index2])
                                        {
                                            double3 v2   = vertices[index2].position;
                                            double3 n    = math.cross(v2 - v0, v1 - v0);
                                            double  det  = math.dot(n, edge0Vec);
                                            coplanar    &= (det * det < math.lengthsq(n) * toleranceSq);
                                        }
                                    }
                                }
                                edge1 = GetLinkedEdge(edge1.Prev);
                            }
                        }
                        edge0 = GetLinkedEdge(edge0.Prev);
                    }
                    while (edge0.data != firstEdge.data && keep);
                    keep &= (!coplanar || anyRemoved);

                    removed[v] = !keep;
                    if (keep)
                    {
                        newVertices[numNewVertices++] = vertices[v];
                    }
                    else
                    {
                        remove = true;
                    }
                }

                if (!remove)
                {
                    break;  // nothing to remove
                }

                if (numNewVertices < 4)
                {
                    // This can happen for nearly-flat hulls
                    Rebuild2D();
                    break;
                }

                Reset();
                for (int i = 0; i < numNewVertices; i++)
                {
                    Vertex vertex = newVertices[i];
                    AddPoint(vertex.position, vertex.userData);
                }
            }
        }

        // Simplification of two vertices into one new vertex
        struct Collapse
        {
            public int    vertexA;  // Source vertex, index into Vertices
            public int    vertexB;  // Source vertex
            public float3 target;  // Position to replace the original vertices with
            public float  cost;  // Sum of squared distances from Target to the planes incident to the original vertices
        }

        // Orders Collapses by Cost
        struct CollapseComparer : IComparer<Collapse>
        {
            public int Compare(Collapse x, Collapse y)
            {
                return x.cost.CompareTo(y.cost);
            }
        }

        void SetUserData(int v, uint data)
        {
            Vertex vertex = vertices[v]; vertex.userData = data; vertices.Set(v, vertex);
        }

        // Returns a plane containing the edge through vertexIndex0 and vertexIndex1, with normal at equal angles
        // to normal0 and normal1 (those of the triangles that share the edge)
        Plane GetEdgePlane(int vertexIndex0, int vertexIndex1, float3 normal0, float3 normal1)
        {
            float3 vertex0     = vertices[vertexIndex0].position;
            float3 vertex1     = vertices[vertexIndex1].position;
            float3 edgeVec     = vertex1 - vertex0;
            float3 edgeNormal0 = math.normalize(math.cross(edgeVec, normal0));
            float3 edgeNormal1 = math.normalize(math.cross(normal1, edgeVec));
            float3 edgeNormal  = math.normalize(edgeNormal0 + edgeNormal1);
            return new Plane(edgeNormal, -math.dot(edgeNormal, vertex0));
        }

        // Returns a matrix M so that (x, y, z, 1) * M * (x, y, z, 1)^T = square of the distance from (x, y, z) to plane
        double4x4 GetPlaneDistSqMatrix(double4 plane)
        {
            return new double4x4(plane * plane.x, plane * plane.y, plane * plane.z, plane * plane.w);
        }

        // Finds the minimum cost Collapse for a, b using previously computed error matrices.  Returns false if preservFaces = true and there is no collapse that would not
        // violate a face, true otherwise.
        // faceIndex: if >=0, index of the one multi-triangle face on the collapsing edge. -1 if there are two multi-tri faces, -2 if there are none.
        Collapse GetCollapse(int a, int b, ref NativeArray<double4x4> matrices)
        {
            double4x4 matrix = matrices[(int)vertices[a].userData] + matrices[(int)vertices[b].userData];

            // error = x^T * matrix * x, its only extreme point is a minimum and its gradient is linear
            // the value of x that minimizes error is the root of the gradient
            float4 x    = float4.zero;
            float  cost = float.MaxValue;
            switch (dimension)
            {
                case 2:
                {
                    // In 2D force vertices to collapse on their original edge (could potentially get lower error by restricting
                    // to the plane, but this is simpler and good enough)
                    float3    u           = vertices[a].position;
                    float3    v           = vertices[b].position;
                    float3    edge        = v - u;
                    double3x3 solveMatrix = new double3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz);
                    double    denom       = math.dot(math.mul(solveMatrix, edge), edge);
                    if (denom == 0)  // unexpected, just take the midpoint to avoid divide by zero
                    {
                        x    = new float4(u + edge * 0.5f, 1);
                        cost = (float)math.dot(math.mul(matrix, x), x);
                    }
                    else
                    {
                        // Find the extreme point on the line through u and v
                        double3 solveOffset = matrix.c3.xyz;
                        float   extremum    = (float)(-math.dot(math.mul(solveMatrix, u) + solveOffset, edge) / denom);
                        x                   = new float4(u + edge * math.clamp(extremum, 0.0f, 1.0f), 1);

                        // Minimum error is at the extremum or one of the two boundaries, test all three and choose the least
                        float uError = (float)math.dot(math.mul(matrix, new double4(u, 1)), u.xyz1());
                        float vError = (float)math.dot(math.mul(matrix, new double4(v, 1)), v.xyz1());
                        float xError = (float)math.dot(math.mul(matrix, x), x);
                        cost         = math.min(math.min(uError, vError), xError);
                        float3 point = math.select(u.xyz, v.xyz, cost == vError);
                        point        = math.select(point, x.xyz, cost == xError);
                    }
                    break;
                }

                case 3:
                {
                    // 3D, collapse point does not have to be on the edge between u and v
                    double4x4 solveMatrix = new double4x4(
                        new double4(matrix.c0.xyz, 0),
                        new double4(matrix.c1.xyz, 0),
                        new double4(matrix.c2.xyz, 0),
                        new double4(matrix.c3.xyz, 1));
                    double det = math.determinant(solveMatrix);
                    if (det < 1e-6f)  // determinant should be positive, small values indicate fewer than three planes that are significantly distinct from each other
                    {
                        goto case 2;
                    }

                    x    = (float4)math.mul(math.inverse(solveMatrix), new double4(0, 0, 0, 1));
                    cost = (float)math.dot(math.mul(matrix, x), x);

                    break;
                }
            }

            return new Collapse
            {
                vertexA = a,
                vertexB = b,
                target  = x.xyz,
                cost    = cost
            };
        }

        // Returns the index of pair i,j in an array of all unique unordered pairs of nonnegative ints less than n, sorted (0,0),(0,1),...(0,n-1),(1,1),(1,2),...,(1,n-1),...,(n-1,n-1)
        int Index2d(uint i, uint j, uint n)
        {
            return (int)(i * (n + n - i - 1) / 2 + j);
        }

        // Simplifies the hull by collapsing pairs of vertices until the number of vertices is no more than maxVertices and no further pairs can be collapsed without
        // introducing error in excess of maxError.
        // Based on QEM, but with contractions only allowed for vertices connected by a triangle edge, and only to be replaced by vertices on the same edge
        // Note, calling this function destroys vertex user data
        public unsafe void SimplifyVertices(float maxError, int maxVertices)
        {
            // Simplification is only possible in 2D / 3D
            if (dimension < 2)
            {
                return;
            }

            // Must build faces before calling
            if (numFaces == 0)
            {
                Assert.IsTrue(false);
                return;
            }

            // Calculate initial error matrices
            NativeArray<Collapse>  collapses   = new NativeArray<Collapse>();
            NativeArray<double4x4> matrices    = new NativeArray<double4x4>(vertices.peakCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
            int                    numVertices = 0;
            if (dimension == 3)
            {
                // Get an edge from each face
                NativeArray<Edge> firstEdges = new NativeArray<Edge>(numFaces, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (FaceEdge faceEdge = GetFirstFace(); faceEdge.isValid; faceEdge = GetNextFace(faceEdge))
                {
                    int triangleIndex     = faceEdge.current.triangleIndex;
                    int faceIndex         = triangles[triangleIndex].faceIndex;
                    firstEdges[faceIndex] = faceEdge.start;
                }

                // Build error matrices
                for (int i = 0; i < numFaces; i++)
                {
                    // Calculate the error matrix for this face
                    float4    plane      = planes[i];
                    double4x4 faceMatrix = GetPlaneDistSqMatrix(plane);

                    // Add it to the error matrix of each vertex on the face
                    for (FaceEdge edge = new FaceEdge { start = firstEdges[i], current = firstEdges[i] }; edge.isValid; edge = GetNextFaceEdge(edge))  // For each edge of the current face
                    {
                        // Add the error matrix
                        int vertex0        = triangles[edge.current.triangleIndex].GetVertex(edge.current.edgeIndex);
                        matrices[vertex0] += faceMatrix;

                        // Check if the edge is acute
                        Edge     opposite         = GetLinkedEdge(edge);
                        Triangle oppositeTriangle = triangles[opposite.triangleIndex];
                        int      vertex1          = oppositeTriangle.GetVertex(opposite.edgeIndex);
                        if (vertex0 < vertex1)  // Count each edge only once
                        {
                            float3 oppositeNormal = planes[oppositeTriangle.faceIndex].normal;
                            if (math.dot(plane.xyz, oppositeNormal) < -0.017452f)  // 91 degrees -- right angles are common in input data, avoid creating edge planes that distort the original faces
                            {
                                // Add an edge plane to the cost metric for each vertex on the edge to preserve sharp features
                                float4    edgePlane   = GetEdgePlane(vertex0, vertex1, plane.xyz, oppositeNormal);
                                double4x4 edgeMatrix  = GetPlaneDistSqMatrix(edgePlane);
                                matrices[vertex0]    += edgeMatrix;
                                matrices[vertex1]    += edgeMatrix;
                            }
                        }
                    }
                }

                // Allocate space for QEM
                collapses = new NativeArray<Collapse>(triangles.peakCount * 3 / 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            }
            else
            {
                // In 2D, the vertices are sorted and each one has two edge planes
                for (int i = vertices.peakCount - 1, j = 0; j < vertices.peakCount; i = j, j++)
                {
                    //float4 edgePlane = PlaneFromTwoEdges(Vertices[i].Position, Vertices[j].Position - Vertices[i].Position, ProjectionPlane.Normal);
                    float4    edgePlane   = mathex.planeFrom(vertices[i].position, vertices[j].position - vertices[i].position, projectionPlane.normal);
                    double4x4 edgeMatrix  = GetPlaneDistSqMatrix(edgePlane);
                    matrices[i]          += edgeMatrix;
                    matrices[j]          += edgeMatrix;
                }

                numVertices = vertices.peakCount;
                collapses   = new NativeArray<Collapse>(vertices.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            }

            // Write the original index of each vertex to its user data so that we can maintain its error matrix across rebuilds
            foreach (int v in vertices.Indices)
            {
                SetUserData(v, (uint)v);
                numVertices++;
            }

            // Repeatedly simplify the hull until the count is less than maxVertices and there are no further changes that satisfy maxError
            NativeArray<Vertex> newVertices        = new NativeArray<Vertex>(numVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);  // Note, only Position and UserData are used
            bool                enforceMaxVertices = false;
            float               maxCost            = maxError * maxError;
            while (true)
            {
                // Build a list of potential edge collapses
                int  numCollapses = 0;
                bool force2D      = false;
                if (dimension == 3)
                {
                    // Build collapses for each edge
                    foreach (int t in triangles.Indices)
                    {
                        Triangle triangle = triangles[t];
                        for (int i = 0; i < 3; i++)
                        {
                            int opposite = triangle.GetLink(i).triangleIndex;
                            if (t < opposite)  // Count each edge only once
                            {
                                // Calculate the error matrix for the two vertex edges combined
                                int a = triangle.GetVertex(i);
                                int b = triangle.GetVertex((i + 1) % 3);

                                Collapse collapse = GetCollapse(a, b, ref matrices);
                                if (collapse.cost <= maxCost || enforceMaxVertices)
                                {
                                    collapses[numCollapses++] = collapse;
                                }
                            }
                        }
                    }
                }
                else
                {
                    force2D = true;  // if hull is ever 2D, it must remain 2D through simplification
                    for (int i = vertices.peakCount - 1, j = 0; j < vertices.peakCount; i = j, j++)
                    {
                        Collapse collapse = GetCollapse(i, j, ref matrices);
                        if (collapse.cost <= maxCost || enforceMaxVertices)
                        {
                            collapses[numCollapses++] = collapse;
                        }
                    }
                }

                // Collapse vertices in order of increasing cost
                collapses.GetSubArray(0, numCollapses).Sort(new CollapseComparer());
                int numNewVertices = 0;
                for (int i = 0; i < numCollapses; i++)
                {
                    // If this pass is just to enforce the vertex count, then stop collapsing as soon as it's satisfied
                    Collapse collapse = collapses[i];
                    if (enforceMaxVertices && numVertices <= maxVertices)
                    {
                        break;
                    }

                    // If one of the vertices has been collapsed already, it can't collapse again until the next pass
                    uint matrixA = vertices[collapse.vertexA].userData;
                    uint matrixB = vertices[collapse.vertexB].userData;
                    if (matrixA == uint.MaxValue || matrixB == uint.MaxValue)
                    {
                        continue;
                    }

                    // Mark the vertices removed
                    SetUserData(collapse.vertexA, uint.MaxValue);
                    SetUserData(collapse.vertexB, uint.MaxValue);
                    numVertices--;

                    // Combine error matrices for use in the next pass
                    double4x4 combined     = matrices[(int)matrixA] + matrices[(int)matrixB];
                    matrices[(int)matrixA] = combined;

                    newVertices[numNewVertices++] = new Vertex
                    {
                        position = collapse.target,
                        userData = matrixA
                    };
                }

                // If nothing was collapsed, we're done
                if (numNewVertices == 0)
                {
                    if (numVertices > maxVertices)
                    {
                        // Now it's necessary to exceed maxError in order to get under the vertex limit
                        Assert.IsFalse(enforceMaxVertices);
                        enforceMaxVertices = true;
                        continue;
                    }
                    break;
                }

                // Add all of the original vertices that weren't removed to the list
                foreach (int v in vertices.Indices)
                {
                    if (vertices[v].userData != uint.MaxValue)
                    {
                        newVertices[numNewVertices++] = vertices[v];
                    }
                }

                // Rebuild
                Reset();
                for (int i = 0; i < numNewVertices; ++i)
                {
                    AddPoint(newVertices[i].position, newVertices[i].userData, force2D);
                }
                RemoveRedundantVertices();

                // If this was a max vertices pass and we are now under the limit, we're done
                if (enforceMaxVertices && numVertices <= maxVertices)
                {
                    break;
                }

                // Further simplification is only possible in 2D / 3D
                if (dimension < 2)
                {
                    break;
                }

                // Count the vertices
                numVertices = 0;
                foreach (int v in vertices.Indices)
                {
                    numVertices++;
                }
            }
        }

        // Simplification of two planes into a single new plane
        struct FaceMerge
        {
            public int   face0;  // Face index
            public int   face1;  // Face index
            public Plane plane;  // Plane to replace the two faces
            public float cost;  // Sum of squared distances from original vertices to Plane
            public bool  smallAngle;  // True if the angle between the source faces is below the minimum angle threshold
        }

        // Data required to calculate the OLS of a set of points, without needing to store the points themselves.
        // OLSData for the union of point sets can be computed from those sets' OLSDatas without needing the original points.
        struct OLSData
        {
            // Inputs
            private float  m_weight;  // Cost multiplier
            private int    m_count;  // Number of points in the set
            private float3 m_sums;  // Sum of x, y, z
            private float3 m_squareSums;  // Sum of x^2, y^2, z^2
            private float3 m_productSums;  // Sum of xy, yz, zx

            // Outputs, assigned when Solve() is called
            public Plane plane;  // OLS plane of the points in the set
            public float cost;  // m_Weight * sum of squared distances from points in the set to Plane

            // Empty the set
            public void Init()
            {
                m_weight      = 0;
                m_count       = 0;
                m_sums        = float3.zero;
                m_squareSums  = float3.zero;
                m_productSums = float3.zero;
            }

            // Add a single point to the set
            public void Include(float3 v, float weight)
            {
                m_weight = math.max(m_weight, weight);
                m_count++;
                m_sums        += v;
                m_squareSums  += v * v;
                m_productSums += v * v.yzx;
            }

            // Add all points from the
            public void Include(OLSData source, float weight)
            {
                m_weight       = math.max(math.max(m_weight, source.m_weight), weight);
                m_count       += source.m_count;
                m_sums        += source.m_sums;
                m_squareSums  += source.m_squareSums;
                m_productSums += source.m_productSums;
            }

            // Calculate OLS of all included points and store the results in Plane and Cost.
            // Returned plane has normal dot direction >= 0.
            public void Solve(float3 normal0, float3 normal1)
            {
                float3 averageDirection    = normal0 + normal1;
                float3 absAverageDirection = math.abs(averageDirection);
                bool3  maxAxis             = math.cmax(absAverageDirection) == absAverageDirection;

                // Solve using the axis closest to the average normal for the regressand
                bool planeOk;
                if (maxAxis.x)
                {
                    planeOk    = Solve(m_count, m_sums.yzx, m_squareSums.yzx, m_productSums.yzx, out Plane plane);
                    this.plane = new Plane(plane.normal.zxy, plane.distanceFromOrigin);
                }
                else if (maxAxis.y)
                {
                    planeOk    = Solve(m_count, m_sums.zxy, m_squareSums.zxy, m_productSums.zxy, out Plane plane);
                    this.plane = new Plane(plane.normal.yzx, plane.distanceFromOrigin);
                }
                else
                {
                    planeOk = Solve(m_count, m_sums, m_squareSums, m_productSums, out plane);
                }

                // Calculate the error
                if (!planeOk)
                {
                    cost = float.MaxValue;
                }
                else
                {
                    float4x4 errorMatrix = new float4x4(
                        m_squareSums.x, m_productSums.x, m_productSums.z, m_sums.x,
                        m_productSums.x, m_squareSums.y, m_productSums.y, m_sums.y,
                        m_productSums.z, m_productSums.y, m_squareSums.z, m_sums.z,
                        m_sums.x, m_sums.y, m_sums.z, m_count);
                    cost = math.dot(math.mul(errorMatrix, plane), plane) * m_weight;
                }

                // Flip the plane if it's pointing the wrong way
                if (math.dot(plane.normal, averageDirection) < 0)
                {
                    plane = mathex.flip(plane);
                }
            }

            // Solve implementation, uses regressor xy regressand z
            // Returns false if the problem is singular
            private static bool Solve(int count, float3 sums, float3 squareSums, float3 productSums, out Plane plane)
            {
                // Calculate the plane with minimum sum of squares of distances to points in the set
                double3x3 gram = new double3x3(
                    count, sums.x, sums.y,
                    sums.x, squareSums.x, productSums.x,
                    sums.y, productSums.x, squareSums.y);
                if (math.determinant(gram) == 0)  // check for singular gram matrix (unexpected, points should be from nondegenerate tris and so span at least 2 dimensions)
                {
                    plane = new Plane(new float3(1, 0, 0), 0);
                    return false;
                }
                double3x3 gramInv   = math.inverse(gram);
                double3   momentSum = new double3(sums.z, productSums.zy);
                float3    coeff     = (float3)math.mul(gramInv, momentSum);
                float3    normal    = new float3(coeff.yz, -1);
                float     invLength = math.rsqrt(math.lengthsq(normal));
                plane               = new Plane(normal * invLength, coeff.x * invLength);
                return true;
            }
        }

        // Helper for calculating edge weights, returns the squared sin of the angle between normal0 and normal1 if the angle is > 90 degrees, otherwise returns 1.
        float SinAngleSq(float3 normal0, float3 normal1)
        {
            float cosAngle = math.dot(normal0, normal1);
            return math.select(1.0f, 1.0f - cosAngle * cosAngle, cosAngle < 0);
        }

        // 1) Simplifies the hull by combining pairs of neighboring faces until the estimated face count is below maxFaces, there are no faces left
        // with an angle below minAngleBetweenFaces, and no combinations that can be made without increasing the error above simplificationTolerance.
        // 2) Shrinks the hull by pushing its planes in as much as possible without moving a vertex further than shrinkDistance or zeroing the volume.
        // 3) Reduces the vertex count below a fixed maximum, this is necessary in case face simplification increased the count above the limit
        // Returns - the distance that the faces were moved in by shrinking
        // Merging and shrinking are combined into a single operation because both work on the planes of the hull and require vertices to be rebuilt
        // afterwards.  Rebuilding vertices is the slowest part of hull generation, so best to do it only once.
        public unsafe float SimplifyFacesAndShrink(float simplificationTolerance, float minAngleBetweenFaces, float shrinkDistance, int maxFaces, int maxVertices)
        {
            // Return if merging is not allowed and shrinking is off
            if (simplificationTolerance <= 0.0f && minAngleBetweenFaces <= 0.0f && shrinkDistance <= 0.0f)
            {
                return 0.0f;
            }

            // Only 3D shapes can shrink
            if (dimension < 3)
            {
                return 0.0f;
            }

            float       cosMinAngleBetweenFaces   = math.cos(minAngleBetweenFaces);
            float       simplificationToleranceSq = simplificationTolerance * simplificationTolerance;
            const float k_cosMaxMergeAngle        = 0.707107f;  // Don't merge planes at >45 degrees

            // Make a copy of the planes that we can edit
            int                numPlanes    = numFaces;
            int                maxNumPlanes = numPlanes + triangles.peakCount;
            NativeArray<Plane> planes       = new NativeArray<Plane>(maxNumPlanes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);  // +Triangles.PeakCount to make room for edge planes
            for (int i = 0; i < numPlanes; i++)
            {
                planes[i] = this.planes[i];
            }

            // Find the boundary edges of each face
            NativeArray<int>  firstFaceEdgeIndex = new NativeArray<int>(numPlanes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int>  numFaceEdges       = new NativeArray<int>(numPlanes, Allocator.Temp, NativeArrayOptions.ClearMemory);
            NativeArray<Edge> faceEdges          = new NativeArray<Edge>(triangles.peakCount * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int               totalNumEdges      = 0;
            foreach (int t in triangles.Indices)
            {
                // Search each triangle to find one on each face
                Triangle triangle  = triangles[t];
                int      faceIndex = triangle.faceIndex;
                if (numFaceEdges[faceIndex] > 0)
                {
                    continue;
                }

                // Find a boundary edge on the triangle
                firstFaceEdgeIndex[faceIndex] = totalNumEdges;
                for (int i = 0; i < 3; i++)
                {
                    int linkedTriangle = triangle.GetLink(i).triangleIndex;
                    if (triangles[linkedTriangle].faceIndex != faceIndex)
                    {
                        // Save all edges of the face
                        Edge edge = new Edge(t, i);
                        for (FaceEdge faceEdge = new FaceEdge { start = edge, current = edge }; faceEdge.isValid; faceEdge = GetNextFaceEdge(faceEdge))
                        {
                            faceEdges[totalNumEdges++] = faceEdge.current;
                        }
                        numFaceEdges[faceIndex] = totalNumEdges - firstFaceEdgeIndex[faceIndex];
                        break;
                    }
                }
            }

            // Build OLS data for each face, and calculate the minimum span of the hull among its plane normal directions
            NativeArray<OLSData> olsData = new NativeArray<OLSData>(maxNumPlanes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            float                minSpan = float.MaxValue;
            for (int i = 0; i < numPlanes; i++)
            {
                Plane   plane = planes[i];
                OLSData ols   = new OLSData(); ols.Init();

                float lastSinAngleSq;
                {
                    Edge  lastEdge  = faceEdges[firstFaceEdgeIndex[i] + numFaceEdges[i] - 1];
                    Plane lastPlane = planes[triangles[GetLinkedEdge(lastEdge).triangleIndex].faceIndex];
                    lastSinAngleSq  = SinAngleSq(plane.normal, lastPlane.normal);
                }

                for (int j = 0; j < numFaceEdges[i]; j++)
                {
                    // Use the minimum angle of the two edges incident to the vertex to weight it
                    Edge  nextEdge       = faceEdges[firstFaceEdgeIndex[i] + j];
                    Plane nextPlane      = planes[triangles[GetLinkedEdge(nextEdge).triangleIndex].faceIndex];
                    float nextSinAngleSq = SinAngleSq(plane.normal, nextPlane.normal);
                    float weight         = 1.0f / math.max(float.Epsilon, math.min(lastSinAngleSq, nextSinAngleSq));
                    lastSinAngleSq       = nextSinAngleSq;

                    // Include the weighted vertex in OLS data
                    float3 vertex = vertices[triangles[nextEdge.triangleIndex].GetVertex(nextEdge.edgeIndex)].position;
                    ols.Include(vertex, weight);
                }

                olsData[i] = ols;

                // Calculate the span in the plane normal direction
                float span = 0.0f;
                foreach (Vertex vertex in vertices.Elements)
                {
                    span = math.max(span, -mathex.signedDistance(plane, vertex.position));
                }
                minSpan = math.min(span, minSpan);
            }

            // If the minimum span is below the simplification tolerance then we can build a 2D hull without exceeding the tolerance.
            // This often gives a more accurate result, since nearly-flat hulls will get rebuilt from edge plane collisions.
            // Reserve it for extreme cases where the error from flattening is far less than the edge plane error.
            if (minSpan < simplificationTolerance * 0.1f)
            {
                Rebuild2D();
                return 0.0f;
            }

            // Build a list of potential merges and calculate their costs
            // Also add edge planes at any sharp edges, because small changes in angle at those edges can introduce significant error. (Consider for example
            // a thin wedge, if one of the planes at the sharp end rotates so that the edge angle decreases further then the intersection of those planes
            // could move a long distance).
            // Note -- no merges are built for edge planes, which means that they could introduce faces with an angle below minAngleBetweenFaces.
            // This should be rare and edge faces should be extremely thin, which makes it very unlikely for a body to come to rest on one and jitter.
            NativeArray<FaceMerge> merges        = new NativeArray<FaceMerge>(numPlanes * (numPlanes - 1), Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<Edge>      edgePlanes    = new NativeArray<Edge>(triangles.peakCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int                    numMerges     = 0;
            int                    numEdgePlanes = 0;
            for (int i = 0; i < numPlanes; i++)
            {
                for (int j = 0; j < numFaceEdges[i]; j++)
                {
                    Edge edge = faceEdges[firstFaceEdgeIndex[i] + j];

                    // Get the neighboring face
                    Edge     neighborEdge      = GetLinkedEdge(edge);
                    Triangle neighborTriangle  = triangles[neighborEdge.triangleIndex];
                    int      neighborFaceIndex = neighborTriangle.faceIndex;
                    if (neighborFaceIndex < i)
                    {
                        continue;  // One merge entry per pair
                    }

                    // Check for sharp angles
                    const float k_cosSharpAngle = -0.866025f;  // 150deg
                    float       dot             = math.dot(planes[i].normal, planes[neighborFaceIndex].normal);
                    if (dot < k_cosMaxMergeAngle)
                    {
                        if (dot < k_cosSharpAngle)
                        {
                            int edgeIndex         = numEdgePlanes++;
                            edgePlanes[edgeIndex] = edge;

                            // Create an edge plane
                            float3 normal0                = planes[i].normal;
                            float3 normal1                = planes[neighborFaceIndex].normal;
                            int    vertexIndex0           = triangles[edge.triangleIndex].GetVertex(edge.edgeIndex);
                            int    vertexIndex1           = neighborTriangle.GetVertex(neighborEdge.edgeIndex);
                            int    edgePlaneIndex         = numPlanes + edgeIndex;
                            Plane  edgePlane              = GetEdgePlane(vertexIndex0, vertexIndex1, normal0, normal1);
                            edgePlane.distanceFromOrigin -= simplificationTolerance / 2.0f;  // push the edge plane out slightly so it only becomes active if the face planes change significiantly
                            planes[edgePlaneIndex]        = edgePlane;

                            // Build its OLS data
                            OLSData ols = new OLSData(); ols.Init();
                            ols.Include(vertices[vertexIndex0].position, 1.0f);
                            ols.Include(vertices[vertexIndex1].position, 1.0f);
                            olsData[edgePlaneIndex] = ols;
                        }

                        // Don't merge faces with >90 degree angle
                        continue;
                    }

                    // Calculate the cost to merge the faces
                    OLSData combined = olsData[i];
                    combined.Include(olsData[neighborFaceIndex], 0.0f);
                    combined.Solve(planes[i].normal, planes[neighborFaceIndex].normal);
                    bool smallAngle = (dot > cosMinAngleBetweenFaces);
                    if (combined.cost <= simplificationToleranceSq || smallAngle)
                    {
                        merges[numMerges++] = new FaceMerge
                        {
                            face0      = i,
                            face1      = neighborFaceIndex,
                            cost       = combined.cost,
                            plane      = combined.plane,
                            smallAngle = smallAngle
                        };
                    }
                }
            }

            // Calculate the plane offset for shrinking
            // shrinkDistance is the maximum distance that we may move a vertex.  Find the largest plane offset that respects that limit.
            float offset = shrinkDistance;
            {
                // Find an edge incident to each vertex (doesn't matter which one)
                Edge* vertexEdges = stackalloc Edge[vertices.peakCount];
                foreach (int triangleIndex in triangles.Indices)
                {
                    Triangle triangle             = triangles[triangleIndex];
                    vertexEdges[triangle.vertex0] = new Edge(triangleIndex, 0);
                    vertexEdges[triangle.vertex1] = new Edge(triangleIndex, 1);
                    vertexEdges[triangle.vertex2] = new Edge(triangleIndex, 2);
                }

                // Calculates the square of the distance that each vertex moves if all of its incident planes' are moved unit distance along their normals
                float            maxShiftSq   = 1.0f;
                NativeArray<int> planeIndices = new NativeArray<int>(numPlanes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                foreach (int iVertex in vertices.Indices)
                {
                    Edge vertexEdge = vertexEdges[iVertex];

                    // Build a list of planes of faces incident to the vertex
                    int  numPlaneIndices = 0;
                    Edge edge            = vertexEdge;
                    int  lastFaceIndex   = -1;
                    do
                    {
                        int faceIndex = triangles[edge.triangleIndex].faceIndex;
                        if (faceIndex != lastFaceIndex)  // there could be multiple incident triangles on the same face, only need to add it once
                        {
                            planeIndices[numPlaneIndices++] = faceIndex;
                            lastFaceIndex                   = faceIndex;
                        }
                        edge = GetLinkedEdge(edge).Next;
                    }
                    while (edge.data != vertexEdge.data);
                    while (planeIndices[numPlaneIndices - 1] == planeIndices[0])
                    {
                        numPlaneIndices--;  // first and last edge could be on different triangles on the same face
                    }

                    // Iterate over all triplets of planes
                    const float k_cosWideAngle = 0.866025f;  // Only limit movement of vertices at corners sharper than 30 degrees
                    for (int i = 0; i < numPlaneIndices - 2; i++)
                    {
                        float3 iNormal = planes[planeIndices[i]].normal;
                        for (int j = i + 1; j < numPlaneIndices - 1; j++)
                        {
                            float3 jNormal = planes[planeIndices[j]].normal;
                            float3 ijCross = math.cross(iNormal, jNormal);

                            for (int k = j + 1; k < numPlaneIndices; k++)
                            {
                                float3 kNormal = planes[planeIndices[k]].normal;

                                // Skip corners with wide angles
                                float3 dots = new float3(
                                    math.dot(iNormal, jNormal),
                                    math.dot(jNormal, kNormal),
                                    math.dot(kNormal, iNormal));
                                if (math.any(dots < k_cosWideAngle))
                                {
                                    // Calculate the movement of the planes' intersection with respect to the planes' shift
                                    float  det     = math.dot(ijCross, kNormal);
                                    float  invDet  = math.rcp(det);
                                    float3 jkCross = math.cross(jNormal, kNormal);
                                    float3 kiCross = math.cross(kNormal, iNormal);
                                    float  shiftSq = math.lengthsq(ijCross + jkCross + kiCross) * invDet * invDet;
                                    shiftSq        = math.select(shiftSq, 1e10f, invDet == 0.0f);  // avoid nan/inf in unexpected case of zero or extremely small det
                                    Assert.IsTrue(shiftSq >= 1.0f);
                                    maxShiftSq = math.max(maxShiftSq, shiftSq);
                                }
                            }
                        }
                    }
                }

                // Calculate how far we can move the planes without moving vertices more than the limit
                offset *= math.rsqrt(maxShiftSq);

                // Can't shrink more than the inner sphere radius, minSpan / 4 is a lower bound on that radius so use it to clamp the offset
                offset = math.min(offset, minSpan / 4.0f);
            }

            // Merge faces
            int numMerged              = 0;
            int numOriginalPlanes      = numPlanes;
            numPlanes                 += numEdgePlanes;
            NativeArray<bool> removed  = new NativeArray<bool>(numPlanes, Allocator.Temp, NativeArrayOptions.ClearMemory);
            while (true)
            {
                while (numMerges > 0 && numPlanes > 4)
                {
                    // Find the cheapest merge
                    int   mergeIndex           = 0;
                    int   smallAngleMergeIndex = -1;
                    float smallAngleMergeCost  = float.MaxValue;
                    for (int i = 0; i < numMerges; i++)
                    {
                        if (merges[i].cost < merges[mergeIndex].cost)
                        {
                            mergeIndex = i;
                        }

                        if (merges[i].smallAngle && merges[i].cost < smallAngleMergeCost)
                        {
                            smallAngleMergeIndex = i;
                            smallAngleMergeCost  = merges[i].cost;
                        }
                    }

                    // If the cheapest merge is above the cost threshold, take the cheapest merge between a pair of planes that are below the angle
                    // threshold and therefore must be merged regardless of cost.  If there are none, then quit if the estimated face count is below
                    // the limit, otherwise stick with the cheapest merge
                    if (merges[mergeIndex].cost > simplificationToleranceSq)
                    {
                        if (smallAngleMergeIndex < 0)
                        {
                            // We can't know the exact face count, because until we build the shape we don't know which planes will have intersections
                            // on the hull.  Eg. edge planes may or may not be used, or planes may be removed due to shrinking.  Make a rough guess.
                            int estimatedFaceCount = numPlanes - numEdgePlanes - numMerged;
                            if (estimatedFaceCount <= maxFaces)
                            {
                                break;
                            }
                        }
                        else
                        {
                            mergeIndex = smallAngleMergeIndex;
                        }
                    }

                    // Remove the selected merge from the list
                    FaceMerge merge    = merges[mergeIndex];
                    merges[mergeIndex] = merges[--numMerges];
                    numMerged++;

                    // Replace plane 0 with the merged plane, and remove plane 1
                    planes[merge.face0]  = merge.plane;
                    removed[merge.face1] = true;

                    // Combine plane 1's OLS data into plane 0's
                    {
                        OLSData combined = olsData[merge.face0];
                        combined.Include(olsData[merge.face1], 0.0f);
                        olsData[merge.face0] = combined;
                    }

                    // Update any other potential merges involving either of the original planes to point to the new merged planes
                    for (int i = numMerges - 1; i >= 0; i--)
                    {
                        // Test if the merge includes one of the planes that was just updated
                        // If it references the plane that was removed, update it to point to the new combined plane
                        FaceMerge updateMerge = merges[i];
                        if (updateMerge.face0 == merge.face1)
                        {
                            updateMerge.face0 = merge.face0;
                        }
                        else if (updateMerge.face1 == merge.face1)
                        {
                            updateMerge.face1 = merge.face0;
                        }
                        else if (updateMerge.face0 != merge.face0 && updateMerge.face1 != merge.face0)
                        {
                            continue;
                        }

                        // Can't merge a plane with itself, this happens if there is eg. a trifan that gets merged together
                        if (updateMerge.face0 == updateMerge.face1)
                        {
                            merges[i] = merges[--numMerges];
                            continue;
                        }

                        // Limit the maximum merge angle
                        float dot = math.dot(planes[updateMerge.face0].normal, planes[updateMerge.face1].normal);
                        if (dot < k_cosMaxMergeAngle)
                        {
                            merges[i] = merges[--numMerges];
                            continue;
                        }

                        // Calculate the new plane and cost
                        float   weight   = 1.0f / math.max(float.Epsilon, SinAngleSq(planes[updateMerge.face0].normal, planes[updateMerge.face1].normal));
                        OLSData combined = olsData[updateMerge.face0];
                        combined.Include(olsData[updateMerge.face1], weight);
                        combined.Solve(planes[updateMerge.face0].normal, planes[updateMerge.face1].normal);
                        bool smallAngle = (dot > cosMinAngleBetweenFaces);
                        if (updateMerge.cost <= simplificationToleranceSq || smallAngle)
                        {
                            // Write back
                            updateMerge.cost       = combined.cost;
                            updateMerge.plane      = combined.plane;
                            updateMerge.smallAngle = smallAngle;
                            merges[i]              = updateMerge;
                        }
                        else
                        {
                            // Remove the merge
                            merges[i] = merges[--numMerges];
                        }
                    }
                }

                if (numMerged == 0)
                {
                    break;  // Nothing merged, quit
                }

                // Check for any planes with small angles.  It is somewhat uncommon, but sometimes planes that either were not neighbors, or whose merge was dropped, later become nearly
                // parallel to each other as a result of another merge, and therefore need to be merged to each other
                numMerges = 0;
                for (int i = 0; i < numOriginalPlanes - 1; i++)
                {
                    if (removed[i])
                        continue;
                    for (int j = i + 1; j < numOriginalPlanes; j++)
                    {
                        if (removed[j])
                            continue;
                        if (math.dot(planes[i].normal, planes[j].normal) > cosMinAngleBetweenFaces)
                        {
                            OLSData combined = olsData[i];
                            combined.Include(olsData[j], 0.0f);
                            combined.Solve(planes[i].normal, planes[j].normal);
                            merges[numMerges++] = new FaceMerge
                            {
                                face0      = i,
                                face1      = j,
                                cost       = combined.cost,
                                plane      = combined.plane,
                                smallAngle = true
                            };
                        }
                    }
                }
                if (numMerges == 0)
                {
                    break;  // No new merges found, quit
                }
            }

            // Compact the planes and push them in
            for (int i = numPlanes - 1; i >= 0; i--)
            {
                if (removed[i])
                {
                    planes[i] = planes[--numPlanes];
                }
                else
                {
                    planes[i] = new Plane(planes[i].normal, planes[i].distanceFromOrigin + offset);
                }
            }

            // Calculate cross products of all face pairs
            NativeArray<float3> crosses    = new NativeArray<float3>(numPlanes * (numPlanes - 1) / 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int                 crossIndex = 0;
            for (int i = 0; i < numPlanes - 1; i++)
            {
                Plane  plane0 = planes[i];
                float3 point0 = -plane0.normal * plane0.distanceFromOrigin;  // A point on plane0
                for (int j = i + 1; j < numPlanes; j++)
                {
                    Plane  plane1 = planes[j];
                    float3 cross  = math.cross(plane0.normal, plane1.normal);

                    // Get the line through the two planes and check if it intersects the domain.
                    // If not, then it has no intersections that will be kept and we can skip it in the n^4 loop.
                    float3 tangent0 = math.cross(plane0.normal, cross);
                    float3 point01  = point0 - tangent0 * mathex.signedDistance(plane1, point0) / math.dot(plane1.normal, tangent0);  //point on both plane0 and plane1
                    float3 invCross = math.select(math.rcp(cross), math.sqrt(float.MaxValue), cross == float3.zero);
                    float3 tMin     = (m_integerSpaceAabb.min - point01) * invCross;
                    float3 tMax     = (m_integerSpaceAabb.max - point01) * invCross;
                    float3 tEnter   = math.min(tMin, tMax);
                    float3 tExit    = math.max(tMin, tMax);
                    bool   hit      = (math.cmax(tEnter) <= math.cmin(tExit));
                    if (hit)
                    {
                        crosses[crossIndex] = cross;
                    }
                    else
                    {
                        crosses[crossIndex] = float3.zero;
                    }
                    crossIndex++;
                }
            }

            // Find all intersections of three planes.  Note, this is a very slow O(numPlanes^4) operation.
            // Intersections are calculated with double precision, otherwise points more than a couple meters from the origin can have error
            // above the tolerance for the inner loop.
            NativeArray<float3> newVertices     = new NativeArray<float3>(vertices.peakCount * 100, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int                 numNewVertices  = 0;
            int                 indexMultiplier = 2 * numPlanes - 3;
            for (int i = 0; i < numPlanes - 2; i++)
            {
                int iBase = i * (indexMultiplier - i) / 2 - 1;
                for (int j = i + 1; j < numPlanes - 1; j++)
                {
                    // Test if discs i and j intersect
                    double3 ijCross = crosses[iBase + j];
                    if (math.all(ijCross == 0.0f))  // broadphase test
                    {
                        continue;
                    }

                    int jBase = j * (indexMultiplier - j) / 2 - 1;
                    for (int k = j + 1; k < numPlanes; k++)
                    {
                        // Test if all discs intersect pairwise
                        double3 ikCross = crosses[iBase + k];
                        double3 jkCross = crosses[jBase + k];
                        if (math.all(ikCross == 0.0f) || math.all(jkCross == 0.0f))  // broadphase test
                        {
                            continue;
                        }

                        // Find the planes' point of intersection
                        float3 x;
                        {
                            double det = math.dot(planes[i].normal, jkCross);
                            if (math.abs(det) < 1e-8f)
                            {
                                continue;
                            }
                            double invDet = 1.0f / det;
                            x             =
                                (float3)((planes[i].distanceFromOrigin * jkCross - planes[j].distanceFromOrigin * ikCross + planes[k].distanceFromOrigin * ijCross) * -invDet);
                        }

                        // Test if the point is inside of all of the other planes
                        {
                            bool inside = true;
                            for (int l = 0; l < numPlanes; l++)
                            {
                                const float tolerance = 1e-5f;
                                if (math.dot(planes[l].normal, x) > tolerance - planes[l].distanceFromOrigin)
                                {
                                    inside = false;
                                    break;
                                }
                            }

                            if (!inside)
                            {
                                continue;
                            }
                        }

                        // Check if we already found an intersection that is almost exactly the same as x
                        float minDistanceSq = 1e-10f;
                        bool  keep          = true;
                        for (int l = 0; l < numNewVertices; l++)
                        {
                            if (math.distancesq(newVertices[l], x) < minDistanceSq)
                            {
                                keep = false;
                                break;
                            }
                        }

                        if (keep)
                        {
                            newVertices[numNewVertices++] = x;
                        }
                    }
                    crossIndex++;
                }
            }

            // Check if there are enough vertices to form a 3D shape
            if (numNewVertices < 4)
            {
                // This can happen if the hull was nearly flat
                Rebuild2D();
                return 0.0f;
            }

            // Rebuild faces using the plane intersection vertices
            if (numNewVertices >= 4)
            {
                Reset();
                for (int i = 0; i < numNewVertices; i++)
                {
                    AddPoint(newVertices[i]);
                }
            }

            // When more than three planes meet at one point, the intersections computed from each subset of three planes can be slightly different
            // due to float rounding.  This creates unnecessary extra points in the hull and sometimes also numerical problems for BuildFaceIndices
            // from degenerate triangles.  This is fixed by another round of simplification with the error tolerance set low enough that the vertices
            // cannot move far enough to introduce new unintended faces.
            RemoveRedundantVertices();
            BuildFaceIndices(planes.GetSubArray(0, numPlanes));
            SimplifyVertices(k_planeEps, maxVertices);

            // Snap coords to their quantized values for the last build
            foreach (int v in vertices.Indices)
            {
                Vertex vertex   = vertices[v];
                vertex.position = m_integerSpace.ToFloatSpace(vertex.intPosition);
                vertices.Set(v, vertex);
            }

            BuildFaceIndices(planes.GetSubArray(0, numPlanes));

            return offset;
        }

        #endregion

        #region Edge methods

        /// <summary>
        /// Returns one of the triangle edges starting from a given vertex.
        /// Note: May be one of the inner edges of a face.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Edge GetVertexEdge(int vertexIndex)
        {
            Assert.IsTrue(dimension == 3);
            foreach (int triangleIndex in triangles.Indices)
            {
                Triangle triangle = triangles[triangleIndex];
                if (triangle.vertex0 == vertexIndex)
                    return new Edge(triangleIndex, 0);
                if (triangle.vertex1 == vertexIndex)
                    return new Edge(triangleIndex, 1);
                if (triangle.vertex2 == vertexIndex)
                    return new Edge(triangleIndex, 2);
            }
            return Edge.k_invalid;
        }

        /// <summary>
        /// Returns an edge's linked edge on the neighboring triangle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Edge GetLinkedEdge(Edge edge) => edge.isValid ? triangles[edge.triangleIndex].GetLink(edge.edgeIndex) : edge;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int StartVertex(Edge edge) => triangles[edge.triangleIndex].GetVertex(edge.edgeIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int EndVertex(Edge edge) => StartVertex(edge.Next);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ApexVertex(Edge edge) => StartVertex(edge.Prev);

        /// <summary>
        /// Returns (the first edge of) the first face.
        /// </summary>
        public FaceEdge GetFirstFace()
        {
            return numFaces > 0 ? GetFirstFace(0) : FaceEdge.k_invalid;
        }

        /// <summary>
        /// Returns the first face edge from a given face index.
        /// </summary>
        public FaceEdge GetFirstFace(int faceIndex)
        {
            foreach (int triangleIndex in triangles.Indices)
            {
                if (triangles[triangleIndex].faceIndex != faceIndex)
                {
                    continue;
                }
                for (int i = 0; i < 3; ++i)
                {
                    var edge = new Edge(triangleIndex, i);
                    if (triangles[GetLinkedEdge(edge).triangleIndex].faceIndex != faceIndex)
                    {
                        return new FaceEdge { start = edge, current = edge };
                    }
                }
            }
            return FaceEdge.k_invalid;
        }

        /// <summary>
        /// Returns (the first edge of) the next face.
        /// </summary>
        public FaceEdge GetNextFace(FaceEdge fe)
        {
            int faceIndex = fe.isValid ? triangles[fe.start.triangleIndex].faceIndex + 1 : 0;
            if (faceIndex < numFaces)
                return GetFirstFace(faceIndex);
            return FaceEdge.k_invalid;
        }

        /// <summary>
        /// Returns the next edge within a face.
        /// </summary>
        public FaceEdge GetNextFaceEdge(FaceEdge fe)
        {
            int  faceIndex = triangles[fe.start.triangleIndex].faceIndex;
            bool found     = false;
            fe.current     = fe.current.Next;
            for (int n = vertices[StartVertex(fe.current)].cardinality; n > 0; --n)
            {
                if (triangles[GetLinkedEdge(fe.current).triangleIndex].faceIndex == faceIndex)
                {
                    fe.current = GetLinkedEdge(fe.current).Next;
                }
                else
                {
                    found = true;
                    break;
                }
            }

            if (!found || fe.current.Equals(fe.start))
                return FaceEdge.k_invalid;
            return fe;
        }

        #endregion

        #region Queries

        /// <summary>
        /// Returns the centroid of the convex hull.
        /// </summary>
        public float3 ComputeCentroid()
        {
            float4 sum = new float4(0);
            foreach (Vertex vertex in vertices.Elements)
            {
                sum += new float4(vertex.position, 1);
            }

            if (sum.w > 0)
                return sum.xyz / sum.w;
            return new float3(0);
        }

        /// <summary>
        /// Compute the mass properties of the convex hull.
        /// Note: Inertia computation adapted from S. Melax, http://www.melax.com/volint.
        /// </summary>
        public unsafe void UpdateHullMassProperties()
        {
            var mp = new MassProperties();
            switch (dimension)
            {
                case 0:
                    mp.centerOfMass = vertices[0].position;
                    break;
                case 1:
                    mp.centerOfMass = (vertices[0].position + vertices[1].position) * 0.5f;
                    break;
                case 2:
                {
                    float3 offset = ComputeCentroid();
                    for (int n = vertices.peakCount, i = n - 1, j = 0; j < n; i = j++)
                    {
                        float w          = math.length(math.cross(vertices[i].position - offset, vertices[j].position - offset));
                        mp.centerOfMass += (vertices[i].position + vertices[j].position + offset) * w;
                        mp.surfaceArea  += w;
                    }
                    mp.centerOfMass  /= mp.surfaceArea * 3;
                    mp.inertiaTensor  = float3x3.identity;  // <todo>
                    mp.surfaceArea   *= 0.5f;
                }
                break;
                case 3:
                {
                    float3 offset       = ComputeCentroid();
                    int    numTriangles = 0;
                    float* dets         = stackalloc float[triangles.capacity];
                    foreach (int i in triangles.Indices)
                    {
                        float3 v0        = vertices[triangles[i].vertex0].position - offset;
                        float3 v1        = vertices[triangles[i].vertex1].position - offset;
                        float3 v2        = vertices[triangles[i].vertex2].position - offset;
                        float  w         = math.determinant(new float3x3(v0, v1, v2));
                        mp.centerOfMass += (v0 + v1 + v2) * w;
                        mp.volume       += w;
                        mp.surfaceArea  += math.length(math.cross(v1 - v0, v2 - v0));
                        dets[i]          = w;
                        numTriangles++;
                    }

                    mp.centerOfMass = mp.centerOfMass / (mp.volume * 4) + offset;

                    var diag = new float3(0);
                    var offd = new float3(0);

                    foreach (int i in triangles.Indices)
                    {
                        float3 v0  = vertices[triangles[i].vertex0].position - mp.centerOfMass;
                        float3 v1  = vertices[triangles[i].vertex1].position - mp.centerOfMass;
                        float3 v2  = vertices[triangles[i].vertex2].position - mp.centerOfMass;
                        diag      += (v0 * v1 + v1 * v2 + v2 * v0 + v0 * v0 + v1 * v1 + v2 * v2) * dets[i];
                        offd      += (v0.yzx * v1.zxy + v1.yzx * v2.zxy + v2.yzx * v0.zxy +
                                      v0.yzx * v2.zxy + v1.yzx * v0.zxy + v2.yzx * v1.zxy +
                                      (v0.yzx * v0.zxy + v1.yzx * v1.zxy + v2.yzx * v2.zxy) * 2) * dets[i];
                        numTriangles++;
                    }

                    diag /= mp.volume * (60 / 6);
                    offd /= mp.volume * (120 / 6);

                    mp.inertiaTensor.c0 = new float3(diag.y + diag.z, -offd.z, -offd.y);
                    mp.inertiaTensor.c1 = new float3(-offd.z, diag.x + diag.z, -offd.x);
                    mp.inertiaTensor.c2 = new float3(-offd.y, -offd.x, diag.x + diag.y);

                    mp.surfaceArea /= 2;
                    mp.volume      /= 6;
                }
                break;
            }

            hullMassProperties = mp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Plane ComputePlane(int vertex0, int vertex1, int vertex2, bool fromIntCoordinates)
        {
            float3 cross;  // non-normalized plane direction
            float3 point;  // point on the plane
            if (fromIntCoordinates)
            {
                int3 o = vertices[vertex0].intPosition;
                int3 a = vertices[vertex1].intPosition - o;
                int3 b = vertices[vertex2].intPosition - o;
                IntCross(a, b, out long cx, out long cy, out long cz);
                float scaleSq = m_integerSpace.scale * m_integerSpace.scale;  // scale down to avoid overflow normalizing
                cross         = new float3(cx * scaleSq, cy * scaleSq, cz * scaleSq);
                point         = m_integerSpace.ToFloatSpace(o);
            }
            else
            {
                point    = vertices[vertex0].position;
                float3 a = vertices[vertex1].position - point;
                float3 b = vertices[vertex2].position - point;
                cross    = math.cross(a, b);
            }
            float3 n = math.normalize(cross);
            return new Plane(n, -math.dot(n, point));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Plane ComputePlane(int triangleIndex, bool fromIntCoordinates = true)
        {
            return ComputePlane(triangles[triangleIndex].vertex0, triangles[triangleIndex].vertex1, triangles[triangleIndex].vertex2, fromIntCoordinates);
        }

        #endregion

        #region int math

        // Sets cx, cy, cz = a x b, note that all components of a and b must be 31 bits or fewer
        private static void IntCross(int3 a, int3 b, out long cx, out long cy, out long cz)
        {
            cx = (long)a.y * b.z - (long)a.z * b.y;
            cy = (long)a.z * b.x - (long)a.x * b.z;
            cz = (long)a.x * b.y - (long)a.y * b.x;
        }

        // Computes det (b-a, c-a, d-a) and returns an integer that is positive when det is positive, negative when det is negative, and zero when det is zero.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long IntDet(int3 a, int3 b, int3 c, int3 d)
        {
            int3 ab = b - a, ac = c - a, ad = d - a;
            IntCross(ab, ac, out long kx, out long ky, out long kz);
            if (m_intResolution == IntResolution.Low)
            {
                // abcd coords are 16 bit, k are 35 bit, dot product is 54 bit and fits in long
                return kx * ad.x + ky * ad.y + kz * ad.z;
            }
            else
            {
                // abcd coords are 30 bit, k are 63 bit, dot product is 96 bit and won't fit in long so use int128
                Int128 det = Int128.Mul(kx, ad.x) + Int128.Mul(ky, ad.y) + Int128.Mul(kz, ad.z);
                return (long)(det.high | (det.low & 1) | (det.low >> 1));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long IntDet(int a, int b, int c, int d)
        {
            return IntDet(vertices[a].intPosition, vertices[b].intPosition, vertices[c].intPosition, vertices[d].intPosition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long IntDet(int a, int b, int c, int3 d)
        {
            return IntDet(vertices[a].intPosition, vertices[b].intPosition, vertices[c].intPosition, d);
        }

        #endregion

        public ConvexHullBuilder(
            NativeArray<float3> points,
            ConvexHullGenerationParameters generationParameters,
            int maxVertices, int maxFaces, int maxFaceVertices,
            out float convexRadius
            )
        {
            int verticesCapacity = math.max(maxVertices, points.Length);
            var vertices         = new NativeArray<Vertex>(verticesCapacity, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var triangles        = new NativeArray<Triangle>(verticesCapacity * 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var planes           = new NativeArray<Plane>(verticesCapacity * 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            var simplificationTolerance = generationParameters.SimplificationTolerance;
            var shrinkDistance          = generationParameters.BevelRadius;
            var minAngle                = generationParameters.MinimumAngle;

            // Build the points' AABB
            Aabb domain = new Aabb();
            for (int iPoint = 0; iPoint < points.Length; iPoint++)
            {
                domain = Physics.CombineAabb(points[iPoint], domain);
            }

            // Build the initial hull
            ConvexHullBuilder builder = new ConvexHullBuilder(vertices.Length, (Vertex*)vertices.GetUnsafePtr(),
                                                              (Triangle*)triangles.GetUnsafePtr(), (Plane*)planes.GetUnsafePtr(),
                                                              domain, simplificationTolerance, IntResolution.High);
            for (int iPoint = 0; iPoint < points.Length; iPoint++)
            {
                builder.AddPoint(points[iPoint]);
            }

            builder.RemoveRedundantVertices();

            // Simplify the vertices using half of the tolerance
            builder.BuildFaceIndices();
            builder.SimplifyVertices(simplificationTolerance / 2, maxVertices);
            builder.BuildFaceIndices();

            // Build mass properties before shrinking
            builder.UpdateHullMassProperties();

            // SimplifyFacesAndShrink() can increase the vertex count, potentially above the size of the input vertices.  Check if there is enough space in the
            // buffers, and if not then allocate temporary storage
            NativeArray<Vertex>   tempVertices        = new NativeArray<Vertex>();
            NativeArray<Triangle> tempTriangles       = new NativeArray<Triangle>();
            NativeArray<Plane>    tempPlanes          = new NativeArray<Plane>();
            bool                  allocateTempBuilder = false;
            if (builder.dimension == 3)
            {
                int maxNumVertices = 0;
                foreach (int v in builder.vertices.Indices)
                {
                    maxNumVertices += builder.vertices[v].cardinality - 1;
                }

                allocateTempBuilder = true;  // TEMP TESTING maxNumVertices > Vertices.Length;
                if (allocateTempBuilder)
                {
                    tempVertices                  = new NativeArray<Vertex>(maxNumVertices, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    tempTriangles                 = new NativeArray<Triangle>(maxNumVertices * 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    tempPlanes                    = new NativeArray<Plane>(maxNumVertices * 2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    ConvexHullBuilder tempBuilder = new ConvexHullBuilder(maxNumVertices,
                                                                          (Vertex*)tempVertices.GetUnsafePtr(),
                                                                          (Triangle*)tempTriangles.GetUnsafePtr(), (Plane*)tempPlanes.GetUnsafePtr(), builder);
                    builder = tempBuilder;
                }

                // Merge faces
                convexRadius = builder.SimplifyFacesAndShrink(simplificationTolerance / 2, minAngle, shrinkDistance, maxFaces, maxVertices);
            }
            else
            {
                convexRadius = 0f;
            }

            // Simplifier cannot directly enforce k_MaxFaceVertices or k_MaxFaces.  It can also fail to satisfy k_MaxFaces due to numerical error.
            // In these cases the hull is simplified by collapsing vertices until the counts are low enough, at the cost of possibly violating
            // simplificationTolerance or minAngle.
            maxVertices = builder.vertices.peakCount;
            while (true)
            {
                // Check if the face count is low enough, and in the 2D case, if the vertices per face is low enough
                if (builder.numFaces <= maxFaces && (builder.dimension == 3 || builder.vertices.peakCount < maxFaceVertices))
                {
                    // Iterate over all faces to check if any have too many vertices
                    bool simplify = false;
                    for (FaceEdge hullFace = builder.GetFirstFace(); hullFace.isValid; hullFace = builder.GetNextFace(hullFace))
                    {
                        int numFaceVertices = 0;
                        for (FaceEdge edge = hullFace; edge.isValid; edge = builder.GetNextFaceEdge(edge))
                        {
                            numFaceVertices++;
                        }

                        if (numFaceVertices > maxFaceVertices)
                        {
                            simplify = true;
                            break;
                        }
                    }

                    if (!simplify)
                    {
                        break;
                    }
                }

                // Reduce the vertex count 20%, but no need to go below the highest vertex count that always satisfies k_MaxFaces and k_MaxFaceVertices
                int limit   = math.min(maxFaces / 2, maxFaceVertices);
                maxVertices = math.max((int)(maxVertices * 0.8f), limit);
                builder.SimplifyVertices(simplificationTolerance, maxVertices);
                builder.BuildFaceIndices();
                if (maxVertices == limit)
                {
                    break;  // We should now be within the limits, and if not then something has gone wrong and it's better not to loop forever
                }
            }

            if (allocateTempBuilder)
            {
                // The vertex, triangle and face counts should now be within limits, so we can copy back to the original storage
                builder.Compact();
                ConvexHullBuilder tempBuilder = new ConvexHullBuilder(vertices.Length, (Vertex*)vertices.GetUnsafePtr(),
                                                                      (Triangle*)triangles.GetUnsafePtr(), (Plane*)planes.GetUnsafePtr(), builder);
                builder = tempBuilder;
            }

            // Write back
            this = builder;
        }
    }

    // ConvexHullBuilder combined with NativeArrays to store its data
    // Keeping NativeArray out of the ConvexHullBuilder itself allows ConvexHullBuilder to be passed to Burst jobs
    internal struct ConvexHullBuilderStorage : IDisposable
    {
        private NativeArray<ConvexHullBuilder.Vertex>   m_vertices;
        private NativeArray<ConvexHullBuilder.Triangle> m_triangles;
        private NativeArray<Plane>                      m_planes;
        public ConvexHullBuilder                        builder;

        public unsafe ConvexHullBuilderStorage(int verticesCapacity, Allocator allocator, Aabb domain, float simplificationTolerance, ConvexHullBuilder.IntResolution resolution)
        {
            int trianglesCapacity = 2 * verticesCapacity;
            m_vertices            = new NativeArray<ConvexHullBuilder.Vertex>(verticesCapacity, allocator);
            m_triangles           = new NativeArray<ConvexHullBuilder.Triangle>(trianglesCapacity, allocator);
            m_planes              = new NativeArray<Plane>(trianglesCapacity, allocator);
            builder               = new ConvexHullBuilder(verticesCapacity, (ConvexHullBuilder.Vertex*)NativeArrayUnsafeUtility.GetUnsafePtr(m_vertices),
                                                          (ConvexHullBuilder.Triangle*)NativeArrayUnsafeUtility.GetUnsafePtr(m_triangles),
                                                          (Plane*)NativeArrayUnsafeUtility.GetUnsafePtr(m_planes),
                                                          domain, simplificationTolerance, resolution);
        }

        public unsafe ConvexHullBuilderStorage(int verticesCapacity, Allocator allocator, ref ConvexHullBuilder builder)
        {
            m_vertices   = new NativeArray<ConvexHullBuilder.Vertex>(verticesCapacity, allocator);
            m_triangles  = new NativeArray<ConvexHullBuilder.Triangle>(verticesCapacity * 2, allocator);
            m_planes     = new NativeArray<Plane>(verticesCapacity * 2, allocator);
            this.builder = new ConvexHullBuilder(verticesCapacity, (ConvexHullBuilder.Vertex*)NativeArrayUnsafeUtility.GetUnsafePtr(m_vertices),
                                                 (ConvexHullBuilder.Triangle*)NativeArrayUnsafeUtility.GetUnsafePtr(m_triangles),
                                                 (Plane*)NativeArrayUnsafeUtility.GetUnsafePtr(m_planes), builder);
        }

        public void Dispose()
        {
            if (m_vertices.IsCreated)
            {
                m_vertices.Dispose();
            }
            if (m_triangles.IsCreated)
            {
                m_triangles.Dispose();
            }
            if (m_planes.IsCreated)
            {
                m_planes.Dispose();
            }
        }
    }

    // Basic 128 bit signed integer arithmetic
    internal struct Int128
    {
        public ulong low;
        public ulong high;

        const ulong k_Low32 = 0xffffffffUL;

        public bool IsNegative => (high & 0x8000000000000000UL) != 0;
        public bool IsNonNegative => (high & 0x8000000000000000UL) == 0;
        public bool IsZero => (high | low) == 0;
        public bool IsPositive => IsNonNegative && !IsZero;

        public static Int128 Zero => new Int128 { high = 0, low = 0 };

        public static Int128 operator +(Int128 a, Int128 b)
        {
            ulong low  = a.low + b.low;
            ulong high = a.high + b.high;
            if (low < a.low)
                high++;
            return new Int128
            {
                low  = low,
                high = high
            };
        }

        public static Int128 operator -(Int128 a, Int128 b)
        {
            return a + (-b);
        }

        public static Int128 operator -(Int128 a)
        {
            ulong low  = ~a.low + 1;
            ulong high = ~a.high;
            if (a.low == 0)
                high++;
            return new Int128
            {
                low  = low,
                high = high
            };
        }

        public static Int128 Mul(long x, int y)
        {
            ulong  absX        = (ulong)math.abs(x);
            ulong  absY        = (ulong)math.abs(y);
            ulong  lowProduct  = (absX & k_Low32) * absY;
            ulong  highProduct = (absX >> 32) * absY;
            ulong  low         = (highProduct << 32) + lowProduct;
            ulong  carry       = ((highProduct & k_Low32) + (lowProduct >> 32)) >> 32;
            ulong  high        = ((highProduct >> 32) & k_Low32) + carry;
            Int128 product     = new Int128
            {
                low  = low,
                high = high
            };
            if (x < 0 ^ y < 0)
            {
                product = -product;
            }
            return product;
        }

        public static Int128 Mul(long x, long y)
        {
            ulong absX = (ulong)math.abs(x);
            ulong absY = (ulong)math.abs(y);

            ulong loX = absX & k_Low32;
            ulong hiX = absX >> 32;
            ulong loY = absY & k_Low32;
            ulong hiY = absY >> 32;

            ulong lolo = loX * loY;
            ulong lohi = loX * hiY;
            ulong hilo = hiX * loY;
            ulong hihi = hiX * hiY;

            ulong low   = lolo + (lohi << 32) + (hilo << 32);
            ulong carry = ((lolo >> 32) + (lohi & k_Low32) + (hilo & k_Low32)) >> 32;
            ulong high  = hihi + (lohi >> 32) + (hilo >> 32) + carry;

            Int128 product = new Int128
            {
                low  = low,
                high = high
            };
            if (x < 0 ^ y < 0)
            {
                product = -product;
            }
            return product;
        }
    }

    [Serializable]
    internal struct ConvexHullGenerationParameters : IEquatable<ConvexHullGenerationParameters>
    {
        internal const string k_BevelRadiusTooltip =
            "Determines how rounded the edges of the convex shape will be. A value greater than 0 results in more optimized collision, at the expense of some shape detail.";

        const float k_DefaultSimplificationTolerance = 0.015f;
        const float k_DefaultBevelRadius             = 0.05f;
        const float k_DefaultMinAngle                = 2.5f * math.PI / 180f;  // 2.5 degrees

        public static readonly ConvexHullGenerationParameters Default = new ConvexHullGenerationParameters
        {
            SimplificationTolerance = k_DefaultSimplificationTolerance,
            BevelRadius             = k_DefaultBevelRadius,
            MinimumAngle            = k_DefaultMinAngle
        };

        public float SimplificationTolerance { get => m_SimplificationTolerance; set => m_SimplificationTolerance = value; }
        [UnityEngine.Tooltip("Specifies maximum distance that any input point may be moved when simplifying convex hull.")]
        [UnityEngine.SerializeField]
        float m_SimplificationTolerance;

        public float BevelRadius { get => m_BevelRadius; set => m_BevelRadius = value; }
        [UnityEngine.Tooltip(k_BevelRadiusTooltip)]
        [UnityEngine.SerializeField]
        float m_BevelRadius;

        public float MinimumAngle { get => m_MinimumAngle; set => m_MinimumAngle = value; }
        [UnityEngine.Tooltip("Specifies the angle between adjacent faces below which they should be made coplanar.")]
        [UnityEngine.SerializeField]
        float m_MinimumAngle;

        public bool Equals(ConvexHullGenerationParameters other) =>
        m_SimplificationTolerance == other.m_SimplificationTolerance &&
        m_BevelRadius == other.m_BevelRadius &&
        m_MinimumAngle == other.m_MinimumAngle;

        public override int GetHashCode() =>
        unchecked ((int)math.hash(new float3(m_SimplificationTolerance, m_BevelRadius, m_MinimumAngle)));
    }

    interface IPoolElement
    {
        bool isAllocated { get; }
        void MarkFree(int nextFree);
        int nextFree { get; }
    }

    // Underlying implementation of ElementPool
    // This is split into a different structure so that it can be unmanaged (since templated structures are always managed)
    [NoAlias]
    unsafe internal struct ElementPoolBase
    {
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        private void*        m_elements;  // storage for all elements (allocated and free)
        private readonly int m_capacity;  // number of elements
        private int          m_firstFreeIndex;  // the index of the first free element (or -1 if none free)

        public int capacity => m_capacity;  // the maximum number of elements that can be allocated
        public int peakCount { get; private set; }  // the maximum number of elements allocated so far
        public bool canAllocate => m_firstFreeIndex >= 0 || peakCount < capacity;

        public unsafe ElementPoolBase(void* userBuffer, int capacity)
        {
            m_elements       = userBuffer;
            m_capacity       = capacity;
            m_firstFreeIndex = -1;
            peakCount        = 0;
        }

        // Add an element to the pool
        public int Allocate<T>(T element) where T : unmanaged, IPoolElement
        {
            T* elements = ((T*)m_elements);

            Assert.IsTrue(element.isAllocated);
            if (m_firstFreeIndex != -1)
            {
                int index        = m_firstFreeIndex;
                T*  freeElement  = (T*)m_elements + index;
                m_firstFreeIndex = freeElement->nextFree;
                *freeElement     = element;
                return index;
            }

            Assert.IsTrue(peakCount < capacity);
            elements[peakCount++] = element;
            return peakCount - 1;
        }

        // Remove an element from the pool
        public void Release<T>(int index) where T : unmanaged, IPoolElement
        {
            T* elementsTyped = (T*)m_elements;
            elementsTyped[index].MarkFree(m_firstFreeIndex);
            m_firstFreeIndex = index;
        }

        // Empty the pool
        public void Clear()
        {
            peakCount        = 0;
            m_firstFreeIndex = -1;
        }

        public bool IsAllocated<T>(int index) where T : unmanaged, IPoolElement
        {
            T element = ((T*)m_elements)[index];
            return element.isAllocated;
        }

        public T Get<T>(int index) where T : unmanaged, IPoolElement
        {
            Assert.IsTrue(index < capacity);
            T element = ((T*)m_elements)[index];
            Assert.IsTrue(element.isAllocated);
            return element;
        }

        public void Set<T>(int index, T value) where T : unmanaged, IPoolElement
        {
            Assert.IsTrue(index < capacity);
            ((T*)m_elements)[index] = value;
        }

        public unsafe void CopyFrom<T>(ElementPoolBase other) where T : unmanaged, IPoolElement
        {
            Assert.IsTrue(other.peakCount <= capacity);
            peakCount        = other.peakCount;
            m_firstFreeIndex = other.m_firstFreeIndex;
            UnsafeUtility.MemCpy(m_elements, other.m_elements, peakCount * UnsafeUtility.SizeOf<T>());
        }

        public unsafe void CopyFrom<T>(void* buffer, int length) where T : unmanaged, IPoolElement
        {
            Assert.IsTrue(length <= capacity);
            peakCount        = length;
            m_firstFreeIndex = -1;
            UnsafeUtility.MemCpy(m_elements, buffer, peakCount * UnsafeUtility.SizeOf<T>());
        }

        // Compacts the pool so that all of the allocated elements are contiguous, and resets PeakCount to the current allocated count.
        // remap may be null or an array of size at least PeakCount, if not null and the return value is true then Compact() sets remap[oldIndex] = newIndex for all allocated elements.
        // Returns true if compact occurred, false if the pool was already compact.
        public unsafe bool Compact<T>(int* remap) where T : unmanaged, IPoolElement
        {
            if (m_firstFreeIndex == -1)
            {
                return false;
            }
            int numElements = 0;
            for (int i = 0; i < peakCount; i++)
            {
                T element = ((T*)m_elements)[i];
                if (element.isAllocated)
                {
                    if (remap != null)
                        remap[i]                    = numElements;
                    ((T*)m_elements)[numElements++] = element;
                }
            }
            peakCount        = numElements;
            m_firstFreeIndex = -1;
            return true;
        }

        #region Enumerables

        public IndexEnumerable<T> GetIndices<T>() where T : unmanaged, IPoolElement
        {
            NativeArray<T> slice = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(m_elements, peakCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref slice, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif
            return new IndexEnumerable<T> { slice = slice };
        }

        public ElementEnumerable<T> GetElements<T>() where T : unmanaged, IPoolElement
        {
            NativeArray<T> slice = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(m_elements, peakCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref slice, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif
            return new ElementEnumerable<T> { slice = slice };
        }

        public struct IndexEnumerable<T> where T : unmanaged, IPoolElement
        {
            internal NativeArray<T> slice;

            public IndexEnumerator<T> GetEnumerator() => new IndexEnumerator<T>(ref slice);
        }

        public struct ElementEnumerable<T> where T : unmanaged, IPoolElement
        {
            internal NativeArray<T> slice;

            public ElementEnumerator<T> GetEnumerator() => new ElementEnumerator<T>(ref slice);
        }

        // An enumerator for iterating over the indices
        public struct IndexEnumerator<T> where T : unmanaged, IPoolElement
        {
            internal NativeArray<T> slice;
            internal int            index;

            public int Current => index;

            internal IndexEnumerator(ref NativeArray<T> slice)
            {
                this.slice = slice;
                index      = -1;
            }

            public bool MoveNext()
            {
                while (true)
                {
                    if (++index >= slice.Length)
                    {
                        return false;
                    }
                    if (slice[index].isAllocated)
                    {
                        return true;
                    }
                }
            }
        }

        // An enumerator for iterating over the allocated elements
        public struct ElementEnumerator<T> where T : unmanaged, IPoolElement
        {
            internal NativeArray<T>     slice;
            internal IndexEnumerator<T> indexEnumerator;

            public T Current => slice[indexEnumerator.Current];

            internal ElementEnumerator(ref NativeArray<T> slice)
            {
                this.slice      = slice;
                indexEnumerator = new IndexEnumerator<T>(ref slice);
            }

            public bool MoveNext() => indexEnumerator.MoveNext();
        }

        #endregion
    }

    // A fixed capacity array acting as a pool of allocated/free structs referenced by indices
    unsafe internal struct ElementPool<T> where T : unmanaged, IPoolElement
    {
        public ElementPoolBase* elementPoolBase;

        public int capacity => elementPoolBase->capacity;  // the maximum number of elements that can be allocated
        public int peakCount => elementPoolBase->peakCount;  // the maximum number of elements allocated so far
        public bool canAllocate => elementPoolBase->canAllocate;

        // Add an element to the pool
        public int Allocate(T element) {
            return elementPoolBase->Allocate<T>(element);
        }

        // Remove an element from the pool
        public void Release(int index) {
            elementPoolBase->Release<T>(index);
        }

        // Empty the pool
        public void Clear() {
            elementPoolBase->Clear();
        }

        public bool IsAllocated(int index)
        {
            return elementPoolBase->IsAllocated<T>(index);
        }

        // Get/set an element
        public T this[int index]
        {
            get { return elementPoolBase->Get<T>(index); }
            set { elementPoolBase->Set<T>(index, value); }
        }

        public void Set(int index, T value) {
            elementPoolBase->Set<T>(index, value);
        }

        public unsafe void CopyFrom(ElementPool<T> other) {
            elementPoolBase->CopyFrom<T>(*other.elementPoolBase);
        }

        public unsafe void CopyFrom(void* buffer, int length) {
            elementPoolBase->CopyFrom<T>(buffer, length);
        }

        public unsafe bool Compact(int* remap) {
            return elementPoolBase->Compact<T>(remap);
        }

        public ElementPoolBase.IndexEnumerable<T> Indices => elementPoolBase->GetIndices<T>();
        public ElementPoolBase.ElementEnumerable<T> Elements => elementPoolBase->GetElements<T>();
    }
}

