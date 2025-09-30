using System.Collections.Generic;
using Color = UnityEngine.Color;
using Latios.Calci;
using Latios.Transforms;
using Latios.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        /// <summary>
        /// An interface for drawing lines, which can then be used to draw collider wireframes
        /// </summary>
        public interface ILineDrawer
        {
            public void DrawLine(float3 start, float3 end, Color color);
        }

        struct EngineDrawer : ILineDrawer
        {
            public void DrawLine(float3 start, float3 end, Color color) => UnityEngine.Debug.DrawLine(start, end, color);
        }

        /// <summary>
        /// Draw an AABB wireframe using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="aabb">The AABB to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawAabb(Aabb aabb, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawAabb(aabb, color);
        }

        /// <summary>
        /// Draw an AABB wireframe using this line drawing interface
        /// </summary>
        /// <param name="aabb">The AABB to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawAabb<T>(ref this T drawer, Aabb aabb, Color color) where T : unmanaged, ILineDrawer
        {
            float3 leftTopFront     = new float3(aabb.min.x, aabb.max.y, aabb.min.z);
            float3 rightTopFront    = new float3(aabb.max.x, aabb.max.y, aabb.min.z);
            float3 leftBottomFront  = new float3(aabb.min.x, aabb.min.y, aabb.min.z);
            float3 rightBottomFront = new float3(aabb.max.x, aabb.min.y, aabb.min.z);
            float3 leftTopBack      = new float3(aabb.min.x, aabb.max.y, aabb.max.z);
            float3 rightTopBack     = new float3(aabb.max.x, aabb.max.y, aabb.max.z);
            float3 leftBottomBack   = new float3(aabb.min.x, aabb.min.y, aabb.max.z);
            float3 rightBottomBack  = new float3(aabb.max.x, aabb.min.y, aabb.max.z);

            drawer.DrawLine(leftTopFront,     rightTopFront,    color);
            drawer.DrawLine(rightTopFront,    rightBottomFront, color);
            drawer.DrawLine(rightBottomFront, leftBottomFront,  color);
            drawer.DrawLine(leftBottomFront,  leftTopFront,     color);

            drawer.DrawLine(leftTopBack,      rightTopBack,     color);
            drawer.DrawLine(rightTopBack,     rightBottomBack,  color);
            drawer.DrawLine(rightBottomBack,  leftBottomBack,   color);
            drawer.DrawLine(leftBottomBack,   leftTopBack,      color);

            drawer.DrawLine(leftTopFront,     leftTopBack,      color);
            drawer.DrawLine(rightTopFront,    rightTopBack,     color);
            drawer.DrawLine(leftBottomFront,  leftBottomBack,   color);
            drawer.DrawLine(rightBottomFront, rightBottomBack,  color);
        }

        /// <summary>
        /// Draws a wireframe of a sphere using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The sphere to draw</param>
        /// <param name="transform">The transform of the sphere in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc</param>
        public static void DrawCollider(in SphereCollider sphere, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in sphere, in transform, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a sphere using this line drawing interface
        /// </summary>
        /// <param name="sphere">The sphere to draw</param>
        /// <param name="transform">The transform of the sphere in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc</param>
        public static void DrawCollider<T>(ref this T drawer, in SphereCollider sphere, in RigidTransform transform, Color color, int segmentsPerPi = 6) where T : unmanaged,
        ILineDrawer
        {
            math.sincos(math.PI / segmentsPerPi, out float sin, out float cos);
            float2 turnVector = new float2(cos, sin);
            float2 previous   = new float2(1f, 0f);

            var tf  = transform;
            tf.pos += math.rotate(transform, sphere.center);

            for (int segment = 0; segment < segmentsPerPi; segment++)
            {
                float2 current = LatiosMath.ComplexMul(previous, turnVector);

                float2 currentScaled  = current * sphere.radius;
                float2 previousScaled = previous * sphere.radius;

                for (int i = 0; i < segmentsPerPi; i++)
                {
                    var xTransform = tf;
                    var zTransform = tf;
                    xTransform.rot = math.mul(xTransform.rot, quaternion.RotateX(math.PI * i / segmentsPerPi));
                    zTransform.rot = math.mul(zTransform.rot, quaternion.RotateZ(math.PI * i / segmentsPerPi));

                    float3 currentX  = math.transform(xTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                    float3 previousX = math.transform(xTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousX, currentX, color);
                    currentX  = math.transform(xTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                    previousX = math.transform(xTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousX, currentX, color);
                    if (i != 0)
                    {
                        float3 currentZ  = math.transform(zTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        float3 previousZ = math.transform(zTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                        drawer.DrawLine(previousZ, currentZ, color);
                        currentZ  = math.transform(zTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        previousZ = math.transform(zTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                        drawer.DrawLine(previousZ, currentZ, color);
                    }
                }

                previous = current;
            }
        }

        /// <summary>
        /// Draws a wireframe of a capsule using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="capsule">The capsule to draw</param>
        /// <param name="transform">The transform of the capsule in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc</param>
        public static void DrawCollider(in CapsuleCollider capsule, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in capsule, in transform, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a capsule using this line drawing interface
        /// </summary>
        /// <param name="capsule">The capsule to draw</param>
        /// <param name="transform">The transform of the capsule in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc</param>
        public static void DrawCollider<T>(ref this T drawer, in CapsuleCollider capsule, in RigidTransform transform, Color color, int segmentsPerPi = 6) where T : unmanaged,
        ILineDrawer
        {
            if (math.distance(capsule.pointA, capsule.pointB) < math.EPSILON)
            {
                SphereCollider sphere = new SphereCollider(capsule.pointA, capsule.radius);
                drawer.DrawCollider(sphere, transform, color, segmentsPerPi);
                return;
            }

            math.sincos(math.PI / segmentsPerPi, out float sin, out float cos);
            float2 turnVector = new float2(cos, sin);
            float2 previous   = new float2(1f, 0f);

            float3 capA = float3.zero;
            float3 capB = new float3(0f, math.distance(capsule.pointA, capsule.pointB), 0f);

            quaternion rotation;
            if (math.all((math.abs(capsule.pointB - capsule.pointA)).xz <= math.EPSILON))
            {
                rotation = quaternion.identity;
            }
            else if (math.all((math.abs(capsule.pointB - capsule.pointA)).xy <= math.EPSILON))
            {
                rotation = quaternion.RotateX(math.PI / 2f);
            }
            else
            {
                rotation = quaternion.LookRotationSafe(math.forward(), capsule.pointB - capsule.pointA);
            }
            var localTransform = new RigidTransform(rotation, capsule.pointA);

            for (int segment = 0; segment < segmentsPerPi; segment++)
            {
                float2 current = LatiosMath.ComplexMul(previous, turnVector);

                float2 currentScaled  = current * capsule.radius;
                float2 previousScaled = previous * capsule.radius;

                for (int i = 0; i < segmentsPerPi; i++)
                {
                    var axTransform = math.mul(transform, localTransform);
                    var azTransform = math.mul(transform, localTransform);
                    axTransform     = math.mul(axTransform, new RigidTransform(quaternion.RotateX(math.PI * i / segmentsPerPi), capA));
                    azTransform     = math.mul(azTransform, new RigidTransform(quaternion.EulerZXY(-math.PI / 2f, 0f, math.PI * i / segmentsPerPi), capA));
                    var bxTransform = math.mul(transform, localTransform);
                    var bzTransform = math.mul(transform, localTransform);
                    bxTransform     = math.mul(bxTransform, new RigidTransform(quaternion.RotateX(math.PI * i / segmentsPerPi), capB));
                    bzTransform     = math.mul(bzTransform, new RigidTransform(quaternion.EulerZXY(-math.PI / 2f, 0f, math.PI * i / segmentsPerPi), capB));

                    float3 currentX  = math.transform(bxTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                    float3 previousX = math.transform(bxTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousX, currentX, color);
                    currentX  = math.transform(axTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                    previousX = math.transform(axTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousX, currentX, color);

                    float3 currentZ  = math.transform(bzTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                    float3 previousZ = math.transform(bzTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousZ, currentZ, color);
                    currentZ  = math.transform(azTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                    previousZ = math.transform(azTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                    drawer.DrawLine(previousZ, currentZ, color);

                    if (i == 0)
                    {
                        float3 ya = math.transform(axTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        float3 yb = math.transform(bxTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        drawer.DrawLine(ya, yb, color);
                        ya = math.transform(axTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        yb = math.transform(bxTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        drawer.DrawLine(ya, yb, color);
                    }
                }

                previous = current;
            }
        }

        /// <summary>
        /// Draws a wireframe of a box using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="box">The box to draw</param>
        /// <param name="transform">The transform of the box in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in BoxCollider box, in RigidTransform transform, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in box, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a box using this line drawing interface
        /// </summary>
        /// <param name="box">The box to draw</param>
        /// <param name="transform">The transform of the box in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider<T>(ref this T drawer, in BoxCollider box, in RigidTransform transform, Color color) where T : unmanaged, ILineDrawer
        {
            var aabb = new Aabb(box.center - box.halfSize, box.center + box.halfSize);

            float3 leftTopFront     = math.transform(transform, new float3(aabb.min.x, aabb.max.y, aabb.min.z));
            float3 rightTopFront    = math.transform(transform, new float3(aabb.max.x, aabb.max.y, aabb.min.z));
            float3 leftBottomFront  = math.transform(transform, new float3(aabb.min.x, aabb.min.y, aabb.min.z));
            float3 rightBottomFront = math.transform(transform, new float3(aabb.max.x, aabb.min.y, aabb.min.z));
            float3 leftTopBack      = math.transform(transform, new float3(aabb.min.x, aabb.max.y, aabb.max.z));
            float3 rightTopBack     = math.transform(transform, new float3(aabb.max.x, aabb.max.y, aabb.max.z));
            float3 leftBottomBack   = math.transform(transform, new float3(aabb.min.x, aabb.min.y, aabb.max.z));
            float3 rightBottomBack  = math.transform(transform, new float3(aabb.max.x, aabb.min.y, aabb.max.z));

            drawer.DrawLine(leftTopFront,     rightTopFront,    color);
            drawer.DrawLine(rightTopFront,    rightBottomFront, color);
            drawer.DrawLine(rightBottomFront, leftBottomFront,  color);
            drawer.DrawLine(leftBottomFront,  leftTopFront,     color);

            drawer.DrawLine(leftTopBack,      rightTopBack,     color);
            drawer.DrawLine(rightTopBack,     rightBottomBack,  color);
            drawer.DrawLine(rightBottomBack,  leftBottomBack,   color);
            drawer.DrawLine(leftBottomBack,   leftTopBack,      color);

            drawer.DrawLine(leftTopFront,     leftTopBack,      color);
            drawer.DrawLine(rightTopFront,    rightTopBack,     color);
            drawer.DrawLine(leftBottomFront,  leftBottomBack,   color);
            drawer.DrawLine(rightBottomFront, rightBottomBack,  color);
        }

        /// <summary>
        /// Draws a wireframe of a triangle using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="triangle">The triangle to draw</param>
        /// <param name="transform">The transform of the triangle in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in TriangleCollider triangle, in RigidTransform transform, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in triangle, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a triangle using this line drawing interface
        /// </summary>
        /// <param name="triangle">The triangle to draw</param>
        /// <param name="transform">The transform of the triangle in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider<T>(ref this T drawer, in TriangleCollider triangle, in RigidTransform transform, Color color) where T : unmanaged, ILineDrawer
        {
            float3 a = math.transform(transform, triangle.pointA);
            float3 b = math.transform(transform, triangle.pointB);
            float3 c = math.transform(transform, triangle.pointC);

            drawer.DrawLine(a, b, color);
            drawer.DrawLine(b, c, color);
            drawer.DrawLine(c, a, color);
        }

        /// <summary>
        /// Draws a wireframe of a convex mesh using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="convex">The convex mesh to draw</param>
        /// <param name="transform">The transform of the convex mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in ConvexCollider convex, in RigidTransform transform, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in convex, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a convex mesh using this line drawing interface
        /// </summary>
        /// <param name="convex">The convex mesh to draw</param>
        /// <param name="transform">The transform of the convex mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider<T>(ref this T drawer, in ConvexCollider convex, in RigidTransform transform, Color color) where T : unmanaged, ILineDrawer
        {
            ref var blob = ref convex.convexColliderBlob.Value;

            for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
            {
                var    abIndices = blob.vertexIndicesInEdges[i];
                float3 a         = new float3(blob.verticesX[abIndices.x], blob.verticesY[abIndices.x], blob.verticesZ[abIndices.x]) * convex.scale;
                float3 b         = new float3(blob.verticesX[abIndices.y], blob.verticesY[abIndices.y], blob.verticesZ[abIndices.y]) * convex.scale;
                drawer.DrawLine(math.transform(transform, a), math.transform(transform, b), color);
            }
        }

        /// <summary>
        /// Draws a wireframe of a TriMesh using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="triMesh">The mesh to draw</param>
        /// <param name="transform">The transform of the mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in TriMeshCollider triMesh, in RigidTransform transform, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in triMesh, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a TriMesh using this line drawing interface
        /// </summary>
        /// <param name="triMesh">The mesh to draw</param>
        /// <param name="transform">The transform of the mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider<T>(ref this T drawer, in TriMeshCollider triMesh, in RigidTransform transform, Color color) where T : unmanaged, ILineDrawer
        {
            ref var blob = ref triMesh.triMeshColliderBlob.Value;

            if (blob.triangles.Length == 0)
                return;

            using var allocator = ThreadStackAllocator.GetAllocator();
            var       edges     = allocator.AllocateAsSpan<float3x2>(3 * blob.triangles.Length);
            for (int i = 0; i < blob.triangles.Length; i++)
            {
                var      triangle = blob.triangles[i];
                float3x2 a        = new float3x2(triangle.pointA, triangle.pointB);
                if (EdgeComparer.CompareFloat3(a.c0, a.c1) < 0)
                    (a.c0, a.c1) = (a.c1, a.c0);
                float3x2 b       = new float3x2(triangle.pointB, triangle.pointC);
                if (EdgeComparer.CompareFloat3(b.c0, b.c1) < 0)
                    (b.c0, b.c1) = (b.c1, b.c0);
                float3x2 c       = new float3x2(triangle.pointC, triangle.pointA);
                if (EdgeComparer.CompareFloat3(c.c0, c.c1) < 0)
                    (c.c0, c.c1) = (c.c1, c.c0);
                edges[i * 3]     = a;
                edges[i * 3 + 1] = b;
                edges[i * 3 + 2] = c;
            }
            edges.Sort(new EdgeComparer());

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                edge.c0  = math.transform(transform, edge.c0 * triMesh.scale);
                edge.c1  = math.transform(transform, edge.c1 * triMesh.scale);
            }

            var previousEdge = edges[0];
            var drawEdge     = previousEdge;
            drawEdge.c0      = math.transform(transform, drawEdge.c0 * triMesh.scale);
            drawEdge.c1      = math.transform(transform, drawEdge.c1 * triMesh.scale);
            drawer.DrawLine(drawEdge.c0, drawEdge.c1, color);
            for (int i = 1; i < edges.Length; i++)
            {
                var edge = edges[i];
                if (edge.Equals(previousEdge))
                    continue;

                previousEdge = edge;
                edge.c0      = math.transform(transform, edge.c0 * triMesh.scale);
                edge.c1      = math.transform(transform, edge.c1 * triMesh.scale);
                drawer.DrawLine(edge.c0, edge.c1, color);
            }
        }

        struct EdgeComparer : IComparer<float3x2>
        {
            public static int CompareFloat3(float3 a, float3 b)
            {
                var result = a.x.CompareTo(b.x);
                if (result == 0)
                {
                    result = a.y.CompareTo(b.y);
                    if (result == 0)
                        result = a.z.CompareTo(b.z);
                }
                return result;
            }

            public int Compare(float3x2 a, float3x2 b)
            {
                var result = CompareFloat3(a.c0, b.c0);
                if (result == 0)
                    result = CompareFloat3(a.c1, b.c1);
                return result;
            }
        }

        /// <summary>
        /// Draws a wireframe of a single triangle within the TriMesh using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="triMesh">The mesh to draw</param>
        /// <param name="transform">The transform of the mesh in world space</param>
        /// <param name="subCollider">The triangle index in the TriMesh to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawSubCollider(in TriMeshCollider triMesh, in RigidTransform transform, int subCollider, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawSubCollider(in triMesh, in transform, subCollider, color);
        }

        /// <summary>
        /// Draws a wireframe of a single triangle within the TriMesh using this line drawing interface
        /// </summary>
        /// <param name="triMesh">The mesh to draw</param>
        /// <param name="transform">The transform of the mesh in world space</param>
        /// <param name="subCollider">The triangle index in the TriMesh to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawSubCollider<T>(ref this T drawer, in TriMeshCollider triMesh, in RigidTransform transform, int subCollider, Color color) where T : unmanaged,
        ILineDrawer
        {
            ref var blob = ref triMesh.triMeshColliderBlob.Value;

            var triangle = Physics.ScaleStretchCollider(blob.triangles[subCollider], 1f, triMesh.scale);
            drawer.DrawCollider(in triangle, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of all subcolliders in a compound using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="compound">The compound to draw</param>
        /// <param name="transform">The transform of the compound in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc for any subcolliders which have round features</param>
        public static void DrawCollider(in CompoundCollider compound, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in compound, in transform, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of all subcolliders in a compound using this line drawing interface
        /// </summary>
        /// <param name="compound">The compound to draw</param>
        /// <param name="transform">The transform of the compound in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc for any subcolliders which have round features</param>
        public static void DrawCollider<T>(ref this T drawer, in CompoundCollider compound, in RigidTransform transform, Color color, int segmentsPerPi = 6) where T : unmanaged,
        ILineDrawer
        {
            ref var blob = ref compound.compoundColliderBlob.Value;

            for (int i = 0; i < blob.blobColliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var c, out var localTransform);
                var t = math.mul(transform, localTransform);
                drawer.DrawCollider(c, t, color, segmentsPerPi);
            }
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider in a compound using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="compound">The compound to draw</param>
        /// <param name="transform">The transform of the compound in world space</param>
        /// <param name="subCollider">The index of the subcollider within the compound to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc for any subcolliders which have round features</param>
        public static void DrawSubCollider(in CompoundCollider compound, in RigidTransform transform, int subCollider, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawSubCollider(in compound, in transform, subCollider, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider in a compound using this line drawing interface
        /// </summary>
        /// <param name="compound">The compound to draw</param>
        /// <param name="transform">The transform of the compound in world space</param>
        /// <param name="subCollider">The index of the subcollider within the compound to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc for any subcolliders which have round features</param>
        public static void DrawSubCollider<T>(ref this T drawer, in CompoundCollider compound, in RigidTransform transform, int subCollider, Color color,
                                              int segmentsPerPi = 6) where T : unmanaged, ILineDrawer
        {
            ref var blob = ref compound.compoundColliderBlob.Value;

            compound.GetScaledStretchedSubCollider(subCollider, out var c, out var localTransform);
            var t = math.mul(transform, localTransform);
            drawer.DrawCollider(c, t, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a Terrain using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="terrain">The terrain to draw</param>
        /// <param name="transform">The transform of the terrain in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in TerrainCollider terrain, in RigidTransform transform, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in terrain, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a Terrain using this line drawing interface
        /// </summary>
        /// <param name="terrain">The terrain to draw</param>
        /// <param name="transform">The transform of the terrain in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider<T>(ref this T drawer, in TerrainCollider terrain, in RigidTransform transform, Color color) where T : unmanaged, ILineDrawer
        {
            ref var blob = ref terrain.terrainColliderBlob.Value;

            for (int row = 0; row <= blob.quadRows; row++)
            {
                var previous = math.transform(transform, PointRayTerrain.CreateLocalVertex(ref blob, new int2(0, row), terrain.baseHeightOffset, terrain.scale));
                for (int indexInRow = 1; indexInRow <= blob.quadsPerRow; indexInRow++)
                {
                    var current = math.transform(transform, PointRayTerrain.CreateLocalVertex(ref blob, new int2(indexInRow, row), terrain.baseHeightOffset, terrain.scale));
                    drawer.DrawLine(previous, current, color);
                    previous = current;
                }
            }

            for (int row = 1; row <= blob.quadRows; row++)
            {
                float3 previousBottom = default;
                float3 previousTop    = default;
                for (int indexInRow = 0; indexInRow <= blob.quadsPerRow; indexInRow++)
                {
                    var currentTop    = math.transform(transform, PointRayTerrain.CreateLocalVertex(ref blob, new int2(indexInRow, row), terrain.baseHeightOffset, terrain.scale));
                    var currentBottom = math.transform(transform,
                                                       PointRayTerrain.CreateLocalVertex(ref blob, new int2(indexInRow, row - 1), terrain.baseHeightOffset, terrain.scale));
                    drawer.DrawLine(currentBottom, currentTop, color);

                    if (indexInRow > 0)
                    {
                        if (blob.GetSplitParity(indexInRow - 1))
                            drawer.DrawLine(currentBottom, previousTop, color);
                        else
                            drawer.DrawLine(previousBottom, currentTop, color);
                    }
                    previousBottom = currentBottom;
                    previousTop    = currentTop;
                }
            }
        }

        /// <summary>
        /// Draws a wireframe of a single triangle within the Terrain using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="terrain">The terrain to draw</param>
        /// <param name="transform">The transform of the terrain in world space</param>
        /// <param name="subCollider">The index of the triangle within the terrain to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawSubCollider(in TerrainCollider terrain, in RigidTransform transform, int subCollider, Color color)
        {
            var drawer = new EngineDrawer();
            drawer.DrawSubCollider(in terrain, in transform, subCollider, color);
        }

        /// <summary>
        /// Draws a wireframe of a single triangle within the Terrain using this line drawing interface
        /// </summary>
        /// <param name="terrain">The terrain to draw</param>
        /// <param name="transform">The transform of the terrain in world space</param>
        /// <param name="subCollider">The index of the triangle within the terrain to draw</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawSubCollider<T>(ref this T drawer, in TerrainCollider terrain, in RigidTransform transform, int subCollider, Color color) where T : unmanaged,
        ILineDrawer
        {
            ref var blob = ref terrain.terrainColliderBlob.Value;

            var triangle = PointRayTerrain.CreateLocalTriangle(ref blob, blob.GetTriangle(subCollider), terrain.baseHeightOffset, terrain.scale);
            drawer.DrawCollider(in triangle, in transform, color);
        }

        /// <summary>
        /// Draws a wireframe of a collider using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="collider">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawCollider(in Collider collider, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in collider, in transform, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a collider using this line drawing interface
        /// </summary>
        /// <param name="collider">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawCollider<T>(ref this T drawer, in Collider collider, in RigidTransform transform, Color color, int segmentsPerPi = 6) where T : unmanaged,
        ILineDrawer
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    drawer.DrawCollider(in collider.m_sphere,     transform, color, segmentsPerPi);
                    break;
                case ColliderType.Capsule:
                    drawer.DrawCollider(in collider.m_capsule,    transform, color, segmentsPerPi);
                    break;
                case ColliderType.Box:
                    drawer.DrawCollider(in collider.m_box,        transform, color);
                    break;
                case ColliderType.Triangle:
                    drawer.DrawCollider(in collider.m_triangle,   transform, color);
                    break;
                case ColliderType.Convex:
                    drawer.DrawCollider(in collider.m_convex,     transform, color);
                    break;
                case ColliderType.TriMesh:
                    drawer.DrawCollider(in collider.m_triMesh(),  transform, color);
                    break;
                case ColliderType.Compound:
                    drawer.DrawCollider(in collider.m_compound(), transform, color, segmentsPerPi);
                    break;
                case ColliderType.Terrain:
                    drawer.DrawCollider(in collider.m_terrain(),  transform, color);
                    break;
            }
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider within the collider using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="collider">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="subCollider">The index of the subcollider within the collider to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawSubCollider(in Collider collider, in RigidTransform transform, int subCollider, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawSubCollider(in collider, in transform, subCollider, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider within the collider using this line drawing interface
        /// </summary>
        /// <param name="collider">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="subCollider">The index of the subcollider within the collider to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawSubCollider<T>(ref this T drawer, in Collider collider, in RigidTransform transform, int subCollider, Color color,
                                              int segmentsPerPi = 6) where T : unmanaged, ILineDrawer
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    drawer.DrawCollider(in collider.m_sphere,   transform, color, segmentsPerPi);
                    break;
                case ColliderType.Capsule:
                    drawer.DrawCollider(in collider.m_capsule,  transform, color, segmentsPerPi);
                    break;
                case ColliderType.Box:
                    drawer.DrawCollider(in collider.m_box,      transform, color);
                    break;
                case ColliderType.Triangle:
                    drawer.DrawCollider(in collider.m_triangle, transform, color);
                    break;
                case ColliderType.Convex:
                    drawer.DrawCollider(in collider.m_convex,   transform, color);
                    break;
                case ColliderType.TriMesh:
                    drawer.DrawSubCollider(in collider.m_triMesh(),  transform, subCollider, color);
                    break;
                case ColliderType.Compound:
                    drawer.DrawSubCollider(in collider.m_compound(), transform, subCollider, color, segmentsPerPi);
                    break;
                case ColliderType.Terrain:
                    drawer.DrawSubCollider(in collider.m_terrain(),  transform, subCollider, color);
                    break;
            }
        }

        /// <summary>
        /// Draws a wireframe of a collider using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawCollider(in Collider collider, in TransformQvvs transform, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawCollider(in collider, in transform, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a collider using this line drawing interface
        /// </summary>
        /// <param name="sphere">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawCollider<T>(ref this T drawer, in Collider collider, in TransformQvvs transform, Color color, int segmentsPerPi = 6) where T : unmanaged, ILineDrawer
        {
            var c = collider;
            Physics.ScaleStretchCollider(ref c, transform.scale, transform.stretch);
            drawer.DrawCollider(in c, new RigidTransform(transform.rotation, transform.position), color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider within the collider using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="subCollider">The index of the subcollider within the collider to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawSubCollider(in Collider collider, in TransformQvvs transform, int subCollider, Color color, int segmentsPerPi = 6)
        {
            var drawer = new EngineDrawer();
            drawer.DrawSubCollider(in collider, in transform, subCollider, color, segmentsPerPi);
        }

        /// <summary>
        /// Draws a wireframe of a single subcollider within the collider using this line drawing interface
        /// </summary>
        /// <param name="sphere">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="subCollider">The index of the subcollider within the collider to draw</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawSubCollider<T>(ref this T drawer, in Collider collider, in TransformQvvs transform, int subCollider, Color color,
                                              int segmentsPerPi = 6) where T : unmanaged, ILineDrawer
        {
            var c = collider;
            Physics.ScaleStretchCollider(ref c, transform.scale, transform.stretch);
            drawer.DrawSubCollider(in c, new RigidTransform(transform.rotation, transform.position), subCollider, color, segmentsPerPi);
        }
    }
}

