using Latios.Transforms;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.Psyshock
{
    internal static class LayerQueryProcessors
    {
        public unsafe struct RaycastClosestImmediateProcessor : IFindObjectsProcessor
        {
            private Ray            m_ray;
            private RaycastResult* m_resultPtr;
            private LayerBodyInfo* m_infoPtr;

            public RaycastClosestImmediateProcessor(Ray ray, ref RaycastResult result, ref LayerBodyInfo info)
            {
                m_ray                         = ray;
                m_resultPtr                   = (RaycastResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                     = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndex = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                var hit = PointRayDispatch.Raycast(in m_ray, result.collider,result.transform, out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                    m_ray.end = m_resultPtr->position;
                }
            }
        }

        public unsafe struct RaycastAnyImmediateProcessor : IFindObjectsProcessor
        {
            private Ray            m_ray;
            private RaycastResult* m_resultPtr;
            private LayerBodyInfo* m_infoPtr;

            public RaycastAnyImmediateProcessor(Ray ray, ref RaycastResult result, ref LayerBodyInfo info)
            {
                m_ray                         = ray;
                m_resultPtr                   = (RaycastResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                     = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndex = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                if (m_resultPtr->subColliderIndex >= 0)
                    return;

                var hit = PointRayDispatch.Raycast(in m_ray, result.collider, result.transform, out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                }
            }
        }

        public unsafe struct PointDistanceClosestImmediateProcessor : IFindObjectsProcessor
        {
            private float3               m_point;
            private float                m_maxDistance;
            private PointDistanceResult* m_resultPtr;
            private LayerBodyInfo*       m_infoPtr;

            public PointDistanceClosestImmediateProcessor(float3 point, float maxDistance, ref PointDistanceResult result, ref LayerBodyInfo info)
            {
                m_point                       = point;
                m_maxDistance                 = maxDistance;
                m_resultPtr                   = (PointDistanceResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                     = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndex = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                var hit = PointRayDispatch.DistanceBetween(m_point, result.collider, result.transform, m_maxDistance, out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                    m_maxDistance = m_resultPtr->distance;
                }
            }
        }

        public unsafe struct PointDistanceAnyImmediateProcessor : IFindObjectsProcessor
        {
            private float3               m_point;
            private float                m_maxDistance;
            private PointDistanceResult* m_resultPtr;
            private LayerBodyInfo*       m_infoPtr;

            public PointDistanceAnyImmediateProcessor(float3 point, float maxDistance, ref PointDistanceResult result, ref LayerBodyInfo info)
            {
                m_point                       = point;
                m_maxDistance                 = maxDistance;
                m_resultPtr                   = (PointDistanceResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                     = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndex = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                if (m_resultPtr->subColliderIndex >= 0)
                    return;

                var hit = PointRayDispatch.DistanceBetween(m_point, result.collider, result.transform, m_maxDistance, out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                }
            }
        }

        public unsafe struct ColliderDistanceClosestImmediateProcessor : IFindObjectsProcessor
        {
            private Collider                m_collider;
            private RigidTransform          m_transform;
            private float                   m_maxDistance;
            private ColliderDistanceResult* m_resultPtr;
            private LayerBodyInfo*          m_infoPtr;

            public ColliderDistanceClosestImmediateProcessor(in Collider collider,
                                                             in RigidTransform transform,
                                                             float maxDistance,
                                                             ref ColliderDistanceResult result,
                                                             ref LayerBodyInfo info)
            {
                m_collider                     = collider;
                m_transform                    = transform;
                m_maxDistance                  = maxDistance;
                m_resultPtr                    = (ColliderDistanceResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                      = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndexB = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                var target          = result.collider;
                var targetTransform = result.transform;
                Physics.ScaleStretchCollider(ref target, targetTransform.scale, targetTransform.stretch);
                var hit = ColliderColliderDispatch.DistanceBetween(in m_collider,
                                                                   in m_transform,
                                                                   target,
                                                                   new RigidTransform(targetTransform.rotation, targetTransform.position),
                                                                   m_maxDistance,
                                                                   out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                    m_maxDistance = m_resultPtr->distance;
                }
            }
        }

        public unsafe struct ColliderDistanceAnyImmediateProcessor : IFindObjectsProcessor
        {
            private Collider                m_collider;
            private RigidTransform          m_transform;
            private float                   m_maxDistance;
            private ColliderDistanceResult* m_resultPtr;
            private LayerBodyInfo*          m_infoPtr;

            public ColliderDistanceAnyImmediateProcessor(in Collider collider,
                                                         in RigidTransform transform,
                                                         float maxDistance,
                                                         ref ColliderDistanceResult result,
                                                         ref LayerBodyInfo info)
            {
                m_collider                     = collider;
                m_transform                    = transform;
                m_maxDistance                  = maxDistance;
                m_resultPtr                    = (ColliderDistanceResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                      = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndexB = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                if (m_resultPtr->subColliderIndexB >= 0)
                    return;

                var target          = result.collider;
                var targetTransform = result.transform;
                Physics.ScaleStretchCollider(ref target, targetTransform.scale, targetTransform.stretch);
                var hit = ColliderColliderDispatch.DistanceBetween(in m_collider,
                                                                   in m_transform,
                                                                   target,
                                                                   new RigidTransform(targetTransform.rotation, targetTransform.position),
                                                                   m_maxDistance,
                                                                   out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                }
            }
        }

        public unsafe struct ColliderCastClosestImmediateProcessor : IFindObjectsProcessor
        {
            private Collider            m_collider;
            private RigidTransform      m_start;
            private float3              m_end;
            private ColliderCastResult* m_resultPtr;
            private LayerBodyInfo*      m_infoPtr;

            public ColliderCastClosestImmediateProcessor(in Collider collider,
                                                         in RigidTransform start,
                                                         float3 end,
                                                         ref ColliderCastResult result,
                                                         ref LayerBodyInfo info)
            {
                m_collider                            = collider;
                m_start                               = start;
                m_end                                 = end;
                m_resultPtr                           = (ColliderCastResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                             = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndexOnTarget = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                var target          = result.collider;
                var targetTransform = result.transform;
                Physics.ScaleStretchCollider(ref target, targetTransform.scale, targetTransform.stretch);
                var hit = ColliderColliderDispatch.ColliderCast(in m_collider,
                                                                in m_start,
                                                                m_end,
                                                                target,
                                                                new RigidTransform(targetTransform.rotation, targetTransform.position),
                                                                out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                    m_end = m_resultPtr->distance * math.normalize(m_end - m_start.pos) + m_start.pos;
                }
            }
        }

        public unsafe struct ColliderCastAnyImmediateProcessor : IFindObjectsProcessor
        {
            private Collider            m_collider;
            private RigidTransform      m_start;
            private float3              m_end;
            private ColliderCastResult* m_resultPtr;
            private LayerBodyInfo*      m_infoPtr;

            public ColliderCastAnyImmediateProcessor(in Collider collider,
                                                     in RigidTransform start,
                                                     float3 end,
                                                     ref ColliderCastResult result,
                                                     ref LayerBodyInfo info)
            {
                m_collider                            = collider;
                m_start                               = start;
                m_end                                 = end;
                m_resultPtr                           = (ColliderCastResult*)UnsafeUtility.AddressOf(ref result);
                m_infoPtr                             = (LayerBodyInfo*)UnsafeUtility.AddressOf(ref info);
                m_resultPtr->subColliderIndexOnTarget = -1;
            }

            public void Execute(in FindObjectsResult result)
            {
                if (m_resultPtr->subColliderIndexOnTarget >= 0)
                    return;

                var target          = result.collider;
                var targetTransform = result.transform;
                Physics.ScaleStretchCollider(ref target, targetTransform.scale, targetTransform.stretch);
                var hit = ColliderColliderDispatch.ColliderCast(in m_collider,
                                                                in m_start,
                                                                m_end,
                                                                target,
                                                                new RigidTransform(targetTransform.rotation, targetTransform.position),
                                                                out var newResult);
                if (hit)
                {
                    *m_resultPtr = newResult;
                    *m_infoPtr   = new LayerBodyInfo
                    {
                        body        = result.body,
                        bodyIndex   = result.bodyIndex,
                        sourceIndex = result.sourceIndex,
                        aabb        = result.aabb
                    };
                }
            }
        }
    }
}

