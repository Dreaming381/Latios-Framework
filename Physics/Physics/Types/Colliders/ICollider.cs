using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    internal interface ICollider
    {
        AABB CalculateAABB(RigidTransform transform);
        GjkSupportPoint GetSupportPoint(float3 direction);
        GjkSupportPoint GetSupportPoint(float3 direction, RigidTransform bInASpace);
        float3 GetPointBySupportIndex(int index);
        AABB GetSupportAabb();
        //AABB GetSupportAabb(RigidTransform bInASpace);
    }
}

