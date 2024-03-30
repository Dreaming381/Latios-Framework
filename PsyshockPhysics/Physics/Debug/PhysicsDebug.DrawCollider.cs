using Color = UnityEngine.Color;
using Debug = UnityEngine.Debug;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        /// <summary>
        /// Draws a wireframe of a sphere using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The sphere to draw</param>
        /// <param name="transform">The transform of the sphere in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc</param>
        public static void DrawCollider(in SphereCollider sphere, in RigidTransform transform, Color color, int segmentsPerPi = 6)
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
                    Debug.DrawLine(previousX, currentX, color);
                    currentX  = math.transform(xTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                    previousX = math.transform(xTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                    Debug.DrawLine(previousX, currentX, color);
                    if (i != 0)
                    {
                        float3 currentZ  = math.transform(zTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        float3 previousZ = math.transform(zTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                        Debug.DrawLine(previousZ, currentZ, color);
                        currentZ  = math.transform(zTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        previousZ = math.transform(zTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                        Debug.DrawLine(previousZ, currentZ, color);
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
            if (math.distance(capsule.pointA, capsule.pointB) < math.EPSILON)
            {
                SphereCollider sphere = new SphereCollider(capsule.pointA, capsule.radius);
                DrawCollider(sphere, transform, color, segmentsPerPi);
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
                    Debug.DrawLine(previousX, currentX, color);
                    currentX  = math.transform(axTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                    previousX = math.transform(axTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                    Debug.DrawLine(previousX, currentX, color);

                    float3 currentZ  = math.transform(bzTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                    float3 previousZ = math.transform(bzTransform, new float3(previousScaled.x, 0f, previousScaled.y));
                    Debug.DrawLine(previousZ, currentZ, color);
                    currentZ  = math.transform(azTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                    previousZ = math.transform(azTransform, -new float3(previousScaled.x, 0f, previousScaled.y));
                    Debug.DrawLine(previousZ, currentZ, color);

                    if (i == 0)
                    {
                        float3 ya = math.transform(axTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        float3 yb = math.transform(bxTransform, -new float3(currentScaled.x, 0f, currentScaled.y));
                        Debug.DrawLine(ya, yb, color);
                        ya = math.transform(axTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        yb = math.transform(bxTransform, new float3(currentScaled.x, 0f, currentScaled.y));
                        Debug.DrawLine(ya, yb, color);
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
            var aabb = new Aabb(box.center - box.halfSize, box.center + box.halfSize);

            float3 leftTopFront     = math.transform(transform, new float3(aabb.min.x, aabb.max.y, aabb.min.z));
            float3 rightTopFront    = math.transform(transform, new float3(aabb.max.x, aabb.max.y, aabb.min.z));
            float3 leftBottomFront  = math.transform(transform, new float3(aabb.min.x, aabb.min.y, aabb.min.z));
            float3 rightBottomFront = math.transform(transform, new float3(aabb.max.x, aabb.min.y, aabb.min.z));
            float3 leftTopBack      = math.transform(transform, new float3(aabb.min.x, aabb.max.y, aabb.max.z));
            float3 rightTopBack     = math.transform(transform, new float3(aabb.max.x, aabb.max.y, aabb.max.z));
            float3 leftBottomBack   = math.transform(transform, new float3(aabb.min.x, aabb.min.y, aabb.max.z));
            float3 rightBottomBack  = math.transform(transform, new float3(aabb.max.x, aabb.min.y, aabb.max.z));

            Debug.DrawLine(leftTopFront,     rightTopFront,    color);
            Debug.DrawLine(rightTopFront,    rightBottomFront, color);
            Debug.DrawLine(rightBottomFront, leftBottomFront,  color);
            Debug.DrawLine(leftBottomFront,  leftTopFront,     color);

            Debug.DrawLine(leftTopBack,      rightTopBack,     color);
            Debug.DrawLine(rightTopBack,     rightBottomBack,  color);
            Debug.DrawLine(rightBottomBack,  leftBottomBack,   color);
            Debug.DrawLine(leftBottomBack,   leftTopBack,      color);

            Debug.DrawLine(leftTopFront,     leftTopBack,      color);
            Debug.DrawLine(rightTopFront,    rightTopBack,     color);
            Debug.DrawLine(leftBottomFront,  leftBottomBack,   color);
            Debug.DrawLine(rightBottomFront, rightBottomBack,  color);
        }

        /// <summary>
        /// Draws a wireframe of a triangle using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="triangle">The triangle to draw</param>
        /// <param name="transform">The transform of the triangle in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in TriangleCollider triangle, in RigidTransform transform, Color color)
        {
            float3 a = math.transform(transform, triangle.pointA);
            float3 b = math.transform(transform, triangle.pointB);
            float3 c = math.transform(transform, triangle.pointC);

            Debug.DrawLine(a, b, color);
            Debug.DrawLine(b, c, color);
            Debug.DrawLine(c, a, color);
        }

        /// <summary>
        /// Draws a wireframe of a convex mesh using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="convex">The convex mesh to draw</param>
        /// <param name="transform">The transform of the convex mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in ConvexCollider convex, in RigidTransform transform, Color color)
        {
            ref var blob = ref convex.convexColliderBlob.Value;

            for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
            {
                var    abIndices = blob.vertexIndicesInEdges[i];
                float3 a         = new float3(blob.verticesX[abIndices.x], blob.verticesY[abIndices.x], blob.verticesZ[abIndices.x]) * convex.scale;
                float3 b         = new float3(blob.verticesX[abIndices.y], blob.verticesY[abIndices.y], blob.verticesZ[abIndices.y]) * convex.scale;
                Debug.DrawLine(math.transform(transform, a), math.transform(transform, b), color);
            }
        }

        /// <summary>
        /// Draws a wireframe of a TriMesh using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="triMesh">The convex mesh to draw</param>
        /// <param name="transform">The transform of the convex mesh in world space</param>
        /// <param name="color">The color of the wireframe</param>
        public static void DrawCollider(in TriMeshCollider triMesh, in RigidTransform transform, Color color)
        {
            ref var blob = ref triMesh.triMeshColliderBlob.Value;

            for (int i = 0; i < blob.triangles.Length; i++)
            {
                var triangle = Physics.ScaleStretchCollider(blob.triangles[i], 1f, triMesh.scale);
                DrawCollider(in triangle, in transform, color);
            }
        }

        /// <summary>
        /// Draws a wireframe of all subcolliders in a compound using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The compound to draw</param>
        /// <param name="transform">The transform of the compound in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc for any subcolliders which have round features</param>
        public static void DrawCollider(in CompoundCollider compound, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            ref var blob = ref compound.compoundColliderBlob.Value;

            for (int i = 0; i < blob.blobColliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var c, out var localTransform);
                var t = math.mul(transform, localTransform);
                DrawCollider(c, t, color, segmentsPerPi);
            }
        }

        /// <summary>
        /// Draws a wireframe of a collider using UnityEngine.Debug.DrawLine calls
        /// </summary>
        /// <param name="sphere">The collider to draw</param>
        /// <param name="transform">The transform of the collider in world space</param>
        /// <param name="color">The color of the wireframe</param>
        /// <param name="segmentsPerPi">The number of segments to draw per 180 degree arc if the collider has round features</param>
        public static void DrawCollider(in Collider collider, in RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    DrawCollider(in collider.m_sphere,     transform, color, segmentsPerPi);
                    break;
                case ColliderType.Capsule:
                    DrawCollider(in collider.m_capsule,    transform, color, segmentsPerPi);
                    break;
                case ColliderType.Box:
                    DrawCollider(in collider.m_box,        transform, color);
                    break;
                case ColliderType.Triangle:
                    DrawCollider(in collider.m_triangle,   transform, color);
                    break;
                case ColliderType.Convex:
                    DrawCollider(in collider.m_convex,     transform, color);
                    break;
                case ColliderType.TriMesh:
                    DrawCollider(in collider.m_triMesh(),  transform, color);
                    break;
                case ColliderType.Compound:
                    DrawCollider(in collider.m_compound(), transform, color, segmentsPerPi);
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
            var c = collider;
            Physics.ScaleStretchCollider(ref c, transform.scale, transform.stretch);
            DrawCollider(in c, new RigidTransform(transform.rotation, transform.position), color, segmentsPerPi);
        }
    }
}

