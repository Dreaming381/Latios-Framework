using Color = UnityEngine.Color;
using Debug = UnityEngine.Debug;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        public static void DrawCollider(SphereCollider sphere, RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            math.sincos(math.PI / segmentsPerPi, out float sin, out float cos);
            float2 turnVector = new float2(cos, sin);
            float2 previous   = new float2(1f, 0f);

            transform.pos += sphere.center;

            for (int segment = 0; segment < segmentsPerPi; segment++)
            {
                float2 current = LatiosMath.ComplexMul(previous, turnVector);

                float2 currentScaled  = current * sphere.radius;
                float2 previousScaled = previous * sphere.radius;

                for (int i = 0; i < segmentsPerPi; i++)
                {
                    var xTransform = transform;
                    var zTransform = transform;
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

        public static void DrawCollider(CapsuleCollider capsule, RigidTransform transform, Color color, int segmentsPerPi = 6)
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

        public static void DrawCollider(BoxCollider box, RigidTransform transform, Color color)
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

        public static void DrawCollider(TriangleCollider triangle, RigidTransform transform, Color color)
        {
            float3 a = math.transform(transform, triangle.pointA);
            float3 b = math.transform(transform, triangle.pointB);
            float3 c = math.transform(transform, triangle.pointC);

            Debug.DrawLine(a, b, color);
            Debug.DrawLine(b, c, color);
            Debug.DrawLine(c, a, color);
        }

        public static void DrawCollider(ConvexCollider convex, RigidTransform transform, Color color)
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

        public static void DrawCollider(CompoundCollider compound, RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            ref var blob  = ref compound.compoundColliderBlob.Value;
            var     scale = new PhysicsScale(compound.scale);

            for (int i = 0; i < blob.blobColliders.Length; i++)
            {
                var c = Physics.ScaleCollider(blob.colliders[i], scale);
                var t = math.mul(transform, blob.transforms[i]);
                DrawCollider(c, t, color, segmentsPerPi);
            }
        }

        public static void DrawCollider(Collider collider, RigidTransform transform, Color color, int segmentsPerPi = 6)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    SphereCollider sphere = collider;
                    DrawCollider(sphere, transform, color, segmentsPerPi);
                    break;
                case ColliderType.Capsule:
                    CapsuleCollider capsule = collider;
                    DrawCollider(capsule, transform, color, segmentsPerPi);
                    break;
                case ColliderType.Box:
                    BoxCollider box = collider;
                    DrawCollider(box, transform, color);
                    break;
                case ColliderType.Triangle:
                    TriangleCollider triangle = collider;
                    DrawCollider(triangle, transform, color);
                    break;
                case ColliderType.Convex:
                    ConvexCollider convex = collider;
                    DrawCollider(convex, transform, color);
                    break;
                case ColliderType.Compound:
                    CompoundCollider compound = collider;
                    DrawCollider(compound, transform, color, segmentsPerPi);
                    break;
            }
        }
    }
}

