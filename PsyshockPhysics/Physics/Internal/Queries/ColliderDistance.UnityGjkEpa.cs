using Unity.Mathematics;

// This file contains several heavy general-purpose collision and spatial algorithms for convex shapes borrowed
// from Unity. The GJK + EPA implementation comes from Unity.Physics (ConvexConvexDistance.cs). It has been
// adapted to the Psyshock ecosystem. The support functions were replaced entirely. Other mechanisms like
// FourTransformedPoints were replaced with Psyshock equivalents. EPA relies heavily on ConvexHullBuilder
// and has been transferred mostly untouched.
// Overall, these two algorithms behave correctly in Psyshock, but are a little noisy, generating errors up to
// 5.5e-4 or worse. The SAT-based algorithms should be preferred where available.

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        // Todo: It would be nice if we could get the real normals out of Gjk,
        // but due to precision issues, we could slide off a key feature during barycentric sampling.
        // Fortunately, with the hit points, we can use point queries to extract the normals.
        // But this means we can't use Gjk for point vs convex mesh distance queries
        internal struct GjkResult
        {
            public float  distance;
            public float3 hitpointOnAInASpace;
            public float3 hitpointOnBInASpace;
            public float3 normalizedOriginToClosestCsoPoint;
        }

        // From Unity.Physics ConvexConvexDistance.cs
        // Mostly unmodified other than naming and interface
        unsafe internal static GjkResult DoGjkEpa(Collider colliderA, Collider colliderB, RigidTransform bInASpace)
        {
            const float epsTerminationSq = 1e-8f;  // Main loop quits when it cannot find a point that improves the simplex by at least this much
            const float epsPenetrationSq = 1e-9f;  // Epsilon used to check for penetration.

            // Initialize simplex with an arbitrary CSO support point
            Simplex simplex                               = default;
            simplex.numVertices                           = 1;
            simplex.a                                     = GetSupport(colliderA, colliderB, new float3(1f, 0f, 0f), bInASpace);
            simplex.originToClosestPointUnscaledDirection = simplex.a.pos;
            simplex.scaledDistance                        = math.lengthsq(simplex.a.pos);
            float scaleSq                                 = simplex.scaledDistance;

            // Iterate.
            int       iteration     = 0;
            bool      penetration   = false;
            const int maxIterations = 64;  // Todo: This seems arbitrary
            for (; iteration < maxIterations; ++iteration)
            {
                // Find a new support vertex
                SupportPoint newSupportPoint = GetSupport(colliderA, colliderB, -simplex.originToClosestPointUnscaledDirection, bInASpace);

                // If the new vertex is not significantly closer to the origin, quit
                float scaledImprovement = math.dot(simplex.a.pos - newSupportPoint.pos, simplex.originToClosestPointUnscaledDirection);
                if (scaledImprovement * math.abs(scaledImprovement) < epsTerminationSq * scaleSq)
                {
                    break;
                }

                // Add the new vertex and reduce the simplex
                switch (simplex.numVertices++)
                {
                    case 1: simplex.b  = newSupportPoint; break;
                    case 2: simplex.c  = newSupportPoint; break;
                    default: simplex.d = newSupportPoint; break;
                }
                simplex.SolveClosestToOriginAndReduce();

                // Check for penetration
                scaleSq                = math.lengthsq(simplex.originToClosestPointUnscaledDirection);
                float scaledDistanceSq = simplex.scaledDistance * simplex.scaledDistance;
                // Todo: the numVertices == 4 makes sense as that only happens if the simplex contains the origin.
                // However, I still don't understand the second half of this expression. These scale factors don't make much sense.
                if (simplex.numVertices == 4 || scaledDistanceSq <= epsPenetrationSq * scaleSq)
                {
                    penetration = true;
                    break;
                }
            }

            UnityEngine.Assertions.Assert.IsTrue(iteration < maxIterations);

            // Finalize result.
            GjkResult result                            = default;
            float3    normalizedOriginToClosestCsoPoint = default;

            // Handle penetration.
            if (penetration)
            {
                // Allocate a hull for EPA
                int                         verticesCapacity        = 64;
                int                         triangleCapacity        = 2 * verticesCapacity;
                ConvexHullBuilder.Vertex*   vertices                = stackalloc ConvexHullBuilder.Vertex[verticesCapacity];
                ConvexHullBuilder.Triangle* triangles               = stackalloc ConvexHullBuilder.Triangle[triangleCapacity];
                Aabb                        domain                  = GetCsoAabb(colliderA, colliderB, bInASpace);
                const float                 simplificationTolerance = 0.0f;
                var                         hull                    = new ConvexHullBuilder(verticesCapacity, vertices, triangles, null,
                                                                    domain, simplificationTolerance, ConvexHullBuilder.IntResolution.Low);
                const float k_epaEpsilon = 1e-4f;

                // Add simplex vertices to the hull, remove any vertices from the simplex that do not increase the hull dimension
                hull.AddPoint(simplex.a.pos, simplex.a.id);
                if (simplex.numVertices > 1)
                {
                    hull.AddPoint(simplex.b.pos, simplex.b.id);
                    if (simplex.numVertices > 2)
                    {
                        int dimension = hull.dimension;
                        hull.AddPoint(simplex.c.pos, simplex.c.id);
                        if (dimension == 0 && hull.dimension == 1)
                        {
                            simplex.b = simplex.c;
                        }
                        if (simplex.numVertices > 3)
                        {
                            dimension = hull.dimension;
                            hull.AddPoint(simplex.d.pos, simplex.d.id);
                            if (dimension > hull.dimension)
                            {
                                if (dimension == 0)
                                {
                                    simplex.b = simplex.d;
                                }
                                else if (dimension == 1)
                                {
                                    simplex.c = simplex.d;
                                }
                            }
                        }
                    }
                }
                simplex.numVertices = (hull.dimension + 1);

                // If the simplex is not 3D, try expanding the hull in all directions
                while (hull.dimension < 3)
                {
                    // Choose expansion directions
                    float3 support0, support1, support2;
                    switch (simplex.numVertices)
                    {
                        case 1:
                            support0 = new float3(1, 0, 0);
                            support1 = new float3(0, 1, 0);
                            support2 = new float3(0, 0, 1);
                            break;
                        case 2:
                            mathex.getDualPerpendicularNormalized(math.normalize(simplex.b.pos - simplex.a.pos), out support0, out support1);
                            support2 = float3.zero;
                            break;
                        default:
                            UnityEngine.Assertions.Assert.IsTrue(simplex.numVertices == 3);
                            support0 = math.cross(simplex.b.pos - simplex.a.pos, simplex.c.pos - simplex.a.pos);
                            support1 = float3.zero;
                            support2 = float3.zero;
                            break;
                    }

                    // Try each one
                    int  numSupports = 4 - simplex.numVertices;
                    bool success     = false;
                    for (int i = 0; i < numSupports; i++)
                    {
                        for (int j = 0; j < 2; j++)  // +/- each direction
                        {
                            SupportPoint vertex = GetSupport(colliderA, colliderB, support0, bInASpace);
                            hull.AddPoint(vertex.pos, vertex.id);
                            if (hull.dimension == simplex.numVertices)
                            {
                                switch (simplex.numVertices)
                                {
                                    case 1: simplex.b  = vertex; break;
                                    case 2: simplex.c  = vertex; break;
                                    default: simplex.d = vertex; break;
                                }

                                // Next dimension
                                success = true;
                                simplex.numVertices++;
                                i = numSupports;
                                break;
                            }
                            support0 = -support0;
                        }
                        support0 = support1;
                        support1 = support2;
                    }

                    if (!success)
                    {
                        break;
                    }
                }

                // We can still fail to build a tetrahedron if the minkowski difference is really flat.
                // In those cases just find the closest point to the origin on the infinite extension of the simplex (point / line / plane)
                if (hull.dimension != 3)
                {
                    switch (simplex.numVertices)
                    {
                        case 1:
                        {
                            result.distance                   = math.length(simplex.a.pos);
                            normalizedOriginToClosestCsoPoint = -math.normalizesafe(simplex.a.pos, new float3(1f, 0f, 0f));
                            break;
                        }
                        case 2:
                        {
                            float3 edge      = math.normalize(simplex.b.pos - simplex.a.pos);
                            float3 direction = math.cross(math.cross(edge, simplex.a.pos), edge);
                            mathex.getDualPerpendicularNormalized(edge, out float3 safeNormal, out _);  // backup, take any direction perpendicular to the edge
                            float3 normal                     = math.normalizesafe(direction, safeNormal);
                            result.distance                   = math.dot(normal, simplex.a.pos);
                            normalizedOriginToClosestCsoPoint = -normal;
                            break;
                        }
                        default:
                        {
                            UnityEngine.Assertions.Assert.IsTrue(simplex.numVertices == 3);
                            float3 cross         = math.cross(simplex.b.pos - simplex.a.pos, simplex.c.pos - simplex.a.pos);
                            float  crossLengthSq = math.lengthsq(cross);
                            if (crossLengthSq < 1e-8f)  // hull builder can accept extremely thin triangles for which we cannot compute an accurate normal
                            {
                                simplex.numVertices = 2;
                                goto case 2;
                            }
                            float3 normal                     = cross * math.rsqrt(crossLengthSq);
                            float  dot                        = math.dot(normal, simplex.a.pos);
                            result.distance                   = math.abs(dot);
                            normalizedOriginToClosestCsoPoint = math.select(-normal, normal, dot < 0f);
                            break;
                        }
                    }
                }
                else
                {
                    int   closestTriangleIndex;
                    Plane closestPlane  = new Plane();
                    float stopThreshold = k_epaEpsilon;
                    uint* uidsCache     = stackalloc uint[triangleCapacity];
                    for (int i = 0; i < triangleCapacity; i++)
                    {
                        uidsCache[i] = 0;
                    }
                    float* distancesCache = stackalloc float[triangleCapacity];
                    do
                    {
                        // Select closest triangle.
                        closestTriangleIndex = -1;
                        foreach (int triangleIndex in hull.triangles.indices)
                        {
                            if (hull.triangles[triangleIndex].uid != uidsCache[triangleIndex])
                            {
                                uidsCache[triangleIndex]      = hull.triangles[triangleIndex].uid;
                                distancesCache[triangleIndex] = hull.ComputePlane(triangleIndex).distanceFromOrigin;
                            }
                            if (closestTriangleIndex == -1 || distancesCache[closestTriangleIndex] < distancesCache[triangleIndex])
                            {
                                closestTriangleIndex = triangleIndex;
                            }
                        }
                        closestPlane = hull.ComputePlane(closestTriangleIndex);

                        // Add supporting vertex or exit.
                        SupportPoint sv  = GetSupport(colliderA, colliderB, closestPlane.normal, bInASpace);
                        float        d2P = math.dot(closestPlane.normal, sv.pos) + closestPlane.distanceFromOrigin;
                        if (math.abs(d2P) > stopThreshold && hull.AddPoint(sv.pos, sv.id))
                            stopThreshold *= 1.3f;
                        else
                            break;
                    }
                    while (++iteration < maxIterations);

                    // There could be multiple triangles in the closest plane, pick the one that has the closest point to the origin on its face
                    foreach (int triangleIndex in hull.triangles.indices)
                    {
                        if (distancesCache[triangleIndex] >= closestPlane.distanceFromOrigin - k_epaEpsilon)
                        {
                            ConvexHullBuilder.Triangle triangle = hull.triangles[triangleIndex];
                            float3                     a        = hull.vertices[triangle.vertex0].position;
                            float3                     b        = hull.vertices[triangle.vertex1].position;
                            float3                     c        = hull.vertices[triangle.vertex2].position;
                            float3                     cross    = math.cross(b - a, c - a);
                            float3                     dets     = new float3(
                                math.dot(math.cross(a - c, cross), a),
                                math.dot(math.cross(b - a, cross), b),
                                math.dot(math.cross(c - b, cross), c));
                            if (math.all(dets >= 0))
                            {
                                Plane plane = hull.ComputePlane(triangleIndex);
                                if (math.dot(plane.normal, closestPlane.normal) > (1f - k_epaEpsilon))
                                {
                                    closestTriangleIndex = triangleIndex;
                                    closestPlane         = hull.ComputePlane(triangleIndex);
                                }
                                break;
                            }
                        }
                    }

                    // Generate simplex.
                    {
                        ConvexHullBuilder.Triangle triangle           = hull.triangles[closestTriangleIndex];
                        simplex.numVertices                           = 3;
                        simplex.a.pos                                 = hull.vertices[triangle.vertex0].position; simplex.a.id = hull.vertices[triangle.vertex0].userData;
                        simplex.b.pos                                 = hull.vertices[triangle.vertex1].position; simplex.b.id = hull.vertices[triangle.vertex1].userData;
                        simplex.c.pos                                 = hull.vertices[triangle.vertex2].position; simplex.c.id = hull.vertices[triangle.vertex2].userData;
                        simplex.originToClosestPointUnscaledDirection = -closestPlane.normal;
                        simplex.scaledDistance                        = closestPlane.distanceFromOrigin;

                        // Set normal and distance.
                        normalizedOriginToClosestCsoPoint = -closestPlane.normal;
                        result.distance                   = closestPlane.distanceFromOrigin;
                    }
                }
            }
            else
            {
                // Compute distance and normal.
                float lengthSq    = math.lengthsq(simplex.originToClosestPointUnscaledDirection);
                float invLength   = math.rsqrt(lengthSq);
                bool  smallLength = lengthSq == 0;
                //ret.ClosestPoints.Distance  = math.select(simplex.scaledDistance * invLength, 0.0f, smallLength);
                result.distance = math.select(simplex.scaledDistance * invLength, 0f, smallLength);
                // If the distance is 0, then the direction shouldn't matter, so pick something safe.
                normalizedOriginToClosestCsoPoint = math.select(simplex.originToClosestPointUnscaledDirection * invLength, new float3(1f, 0f, 0f), smallLength);

                // Todo: I don't understand how the commented out check would fail without result.distance being broken as well. So I added this Assert instead.
                UnityEngine.Assertions.Assert.IsTrue(math.all(math.isfinite(normalizedOriginToClosestCsoPoint)));

                // Make sure the normal is always valid.
                //if (!math.all(math.isfinite(normalizedHitpointBToHitpointA)))
                //{
                //    ret.ClosestPoints.NormalInA = new float3(1, 0, 0);
                //}
            }

            // Compute position.
            float3 closestPoint        = normalizedOriginToClosestCsoPoint * result.distance;
            float4 coordinates         = simplex.ComputeBarycentricCoordinates(closestPoint);
            result.hitpointOnAInASpace = GetSupport(colliderA, simplex.a.idA) * coordinates.x +
                                         GetSupport(colliderA, simplex.b.idA) * coordinates.y +
                                         GetSupport(colliderA, simplex.c.idA) * coordinates.z +
                                         GetSupport(colliderA, simplex.d.idA) * coordinates.w;

            // Done.
            UnityEngine.Assertions.Assert.IsTrue(math.isfinite(result.distance));
            UnityEngine.Assertions.Assert.IsTrue(math.abs(math.lengthsq(normalizedOriginToClosestCsoPoint) - 1.0f) < 1e-5f);

            // Patch distance with radius
            float radialA                             = GetRadialPadding(colliderA);
            float radialB                             = GetRadialPadding(colliderB);
            result.hitpointOnAInASpace               += normalizedOriginToClosestCsoPoint * radialA;
            result.distance                          -= radialA;
            result.distance                          -= radialB;
            result.hitpointOnBInASpace                = result.hitpointOnAInASpace - normalizedOriginToClosestCsoPoint * result.distance;
            result.normalizedOriginToClosestCsoPoint  = normalizedOriginToClosestCsoPoint;
            return result;
        }

        private struct Simplex
        {
            public SupportPoint a, b, c, d;
            public float3       originToClosestPointUnscaledDirection;  // Points from the origin towards the closest point on the simplex
            public float        scaledDistance;  // closestPoint = originToClosestPointUnscaledDirection * scaledDistance / lengthSq(originToClosestPointUnscaledDirection)
            public int          numVertices;

            /// <summary>
            /// Compute the closest point on the simplex, returns true if the simplex contains a duplicate vertex
            /// </summary>
            public void SolveClosestToOriginAndReduce()
            {
                switch (numVertices)
                {
                    // Point.
                    case 1:
                        // closestPoint = a.pos * scaledDistance / scaled Distance = a.pos * 1f = a.pos
                        originToClosestPointUnscaledDirection = a.pos;
                        scaledDistance                        = math.lengthsq(originToClosestPointUnscaledDirection);
                        break;

                    // Line.
                    case 2:
                    {
                        float3 ab  = b.pos - a.pos;
                        float  den = math.dot(ab, ab);
                        float  num = math.dot(-a.pos, ab);  //math.dot(aToOrigin, ab)

                        // Reduce if closest point does not project on the line segment.
                        if (num >= den)
                        {
                            numVertices = 1;
                            a           = b;
                            goto case 1;
                        }

                        // Get the unscaled direction from the origin to the closest point on the line by finding the perpendicular vector from oa to ab,
                        // then finding the perpendicular vector from that and ab.
                        originToClosestPointUnscaledDirection = math.cross(math.cross(ab, a.pos), ab);
                        // Todo: Understand chained cross product magnitude so that this scaling makes sense.
                        scaledDistance = math.dot(originToClosestPointUnscaledDirection, a.pos);
                    }
                    break;

                    // Triangle.
                    case 3:
                    {
                        //Todo: Need to break this down further. Still don't fully understand this.
                        float3 ca = a.pos - c.pos;
                        float3 cb = b.pos - c.pos;
                        float3 n  = math.cross(cb, ca);

                        // Reduce if the closest point does not project in the triangle.
                        float3 unscaledCbNormal = math.cross(cb, n);
                        float  detCbnOB         = math.dot(unscaledCbNormal, b.pos);
                        float  detNcaOC         = math.determinant(new float3x3(n, ca, c.pos));
                        if (detCbnOB < 0)  // if origin to b points opposite to unscaledCbNormal
                        {
                            // if origin to c points aligned to unscaledCaNormal or origin to c points opposite to unscaledCbNormal
                            if (detNcaOC >= 0 || math.determinant(new float3x3(n, unscaledCbNormal, c.pos)) < 0)
                            {
                                a = b;
                            }
                        }
                        else if (detNcaOC >= 0)
                        {
                            float dot = math.dot(c.pos, n);
                            if (dot < 0)
                            {
                                // Reorder vertices so that n points away from the origin
                                SupportPoint temp = a;
                                a                 = b;
                                b                 = temp;
                                n                 = -n;
                                dot               = -dot;
                            }
                            originToClosestPointUnscaledDirection = n;
                            scaledDistance                        = dot;
                            break;
                        }

                        b           = c;
                        numVertices = 2;
                        goto case 2;
                    }

                    // Tetrahedra.
                    case 4:
                    {
                        // Todo: I don't fully understand this case either. Need to draw it out.
                        simdFloat3 tetra = new simdFloat3(a.pos, b.pos, c.pos, d.pos);

                        // This routine finds the closest feature to the origin on the tetra by testing the origin against the planes of the
                        // voronoi diagram. If the origin is near the border of two regions in the diagram, then the plane tests might exclude
                        // it from both because of float rounding.  To avoid this problem we use some tolerance testing the face planes and let
                        // EPA handle those border cases.  1e-5 is a somewhat arbitrary value and the actual distance scales with the tetra, so
                        // this might need to be tuned later!
                        float3 faceTest = simd.dot(simd.cross(tetra, tetra.bcad), d.pos).xyz;
                        if (math.all(faceTest >= -1e-5f))
                        {
                            // Origin is inside the tetra
                            originToClosestPointUnscaledDirection = float3.zero;
                            break;
                        }

                        // Check if the closest point is on a face
                        bool3      insideFace  = (faceTest >= 0).xyz;
                        simdFloat3 edges       = d.pos - tetra;
                        simdFloat3 normals     = simd.cross(edges, edges.bcad);
                        bool3      insideEdge0 = (simd.dot(simd.cross(normals, edges), d.pos) >= 0f).xyz;
                        bool3      insideEdge1 = (simd.dot(simd.cross(edges.bcad, normals), d.pos) >= 0f).xyz;
                        bool3      onFace      = insideEdge0 & insideEdge1 & !insideFace;
                        if (math.any(onFace))
                        {
                            if (onFace.y)
                            {
                                a = b; b = c;
                            }
                            else if (onFace.z)
                            {
                                b = c;
                            }
                        }
                        else
                        {
                            // Check if the closest point is on an edge
                            // TODO maybe we can safely drop two vertices in this case
                            bool3 insideVertex = (simd.dot(edges, d.pos) >= 0f).xyz;
                            bool3 onEdge       = (!insideEdge0 & !insideEdge1.zxy & insideVertex);
                            if (math.any(onEdge.yz))
                            {
                                a = b; b = c;
                            }
                        }

                        c           = d;
                        numVertices = 3;
                        goto case 3;
                    }
                }
            }

            // Compute the barycentric coordinates of the closest point.
            public float4 ComputeBarycentricCoordinates(float3 closestPoint)
            {
                // Todo: Understand the inner workings of this function. Doesn't stop me from sneaking in some simdFloat3 though ;)
                float4 coordinates = float4.zero;
                switch (numVertices)
                {
                    case 1:
                        coordinates.x = 1;
                        break;
                    case 2:
                        float distance = math.distance(a.pos, b.pos);
                        UnityEngine.Assertions.Assert.AreNotEqual(distance, 0.0f);  // TODO just checking if this happens in my tests
                        if (distance == 0.0f)  // Very rare case, simplex is really 1D.
                        {
                            goto case 1;
                        }
                        coordinates.x = math.distance(b.pos, closestPoint) / distance;
                        coordinates.y = 1 - coordinates.x;
                        break;
                    case 3:
                    {
                        simdFloat3 abc = new simdFloat3(a.pos, b.pos, c.pos, d.pos);

                        //coordinates.x = math.length(math.cross(b.pos - closestPoint, c.pos - closestPoint));
                        //coordinates.y = math.length(math.cross(c.pos - closestPoint, a.pos - closestPoint));
                        //coordinates.z = math.length(math.cross(a.pos - closestPoint, b.pos - closestPoint));
                        coordinates.xyz = simd.length(simd.cross(abc.bcad - closestPoint, abc.cabd - closestPoint)).xyz;
                        float sum       = math.csum(coordinates.xyz);
                        if (sum == 0.0f)  // Very rare case, simplex is really 2D.  Happens because of int->float conversion from the hull builder.
                        {
                            // Choose the two farthest apart vertices to keep
                            float3 lengthsSq = simd.distancesq(abc, abc.bcad).xyz;
                            bool3  longest   = math.cmin(lengthsSq) == lengthsSq;
                            if (longest.y)
                            {
                                a.pos = c.pos;
                            }
                            else if (longest.z)
                            {
                                a.pos = b.pos;
                                b.pos = c.pos;
                            }
                            coordinates.z = 0.0f;
                            numVertices   = 2;
                            goto case 2;
                        }
                        coordinates /= sum;
                        break;
                    }
                    case 4:
                    {
                        simdFloat3 abcd = new simdFloat3(a.pos, b.pos, c.pos, d.pos);
                        coordinates     = simd.dot(simd.cross(abcd.ddda, abcd.cabb), abcd.bcac);  // four determinants
                        float sum       = math.csum(coordinates);
                        UnityEngine.Assertions.Assert.AreNotEqual(sum, 0.0f);  // TODO just checking that this doesn't happen in my tests
                        if (sum == 0.0f)  // Unexpected case, may introduce significant error by dropping a vertex but it's better than nan
                        {
                            coordinates.zw = 0.0f;
                            numVertices    = 3;
                            goto case 3;
                        }
                        coordinates /= sum;
                        break;
                    }
                }

                return coordinates;
            }
        }
    }
}

