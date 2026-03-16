#if !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static unsafe partial class TreeKernels
    {
        #region API
        public static void RemoveDeadDescendantsFromHierarchy(ref ThreadStackAllocator parentTsa, ref DynamicBuffer<EntityInHierarchy> hierarchy, EntityManager em)
        {
            var srcScan   = hierarchy.AsNativeArray().AsSpan();
            int deadCount = CountAndMarkDeadDescendantsInHierarchy(srcScan, em);
            if (deadCount == 0)
                return;

            PatchLocalTransformsFromChildrenOfMarkedDead(srcScan, em);
            hierarchy.Length = RemoveMarkedDescendantsInHierarchy(ref parentTsa, srcScan, deadCount);
        }

        public static void RemoveDeadDescendantsFromHierarchy(ref ThreadStackAllocator parentTsa, ref DynamicBuffer<EntityInHierarchy> hierarchy, EntityStorageInfoLookup esil,
                                                              ref ComponentLookup<WorldTransform> worldTransformRO, ref ComponentLookup<TickedWorldTransform> tickedTransformRO)
        {
            var srcScan   = hierarchy.AsNativeArray().AsSpan();
            int deadCount = CountAndMarkDeadDescendantsInHierarchy(srcScan, esil);
            if (deadCount == 0)
                return;

            PatchLocalTransformsFromChildrenOfMarkedDead(srcScan, esil, ref worldTransformRO, ref tickedTransformRO);
            hierarchy.Length = RemoveMarkedDescendantsInHierarchy(ref parentTsa,
                                                                  srcScan,
                                                                  deadCount);
        }

        public static void RemoveDeadAndUnreferencedDescendantsFromHierarchy(ref ThreadStackAllocator parentTsa,
                                                                             ref DynamicBuffer<EntityInHierarchy>      hierarchy,
                                                                             EntityStorageInfoLookup esil,
                                                                             ref ComponentLookup<WorldTransform>       worldTransformRO,
                                                                             ref ComponentLookup<TickedWorldTransform> tickedTransformRO,
                                                                             ref ComponentLookup<RootReference>        rootReferenceRO)
        {
            var srcScan   = hierarchy.AsNativeArray().AsSpan();
            int deadCount = CountAndMarkDeadAndUnreferencedDescendantsInHierarchy(srcScan, ref rootReferenceRO);
            if (deadCount == 0)
                return;

            PatchLocalTransformsFromChildrenOfMarkedDead(srcScan, esil, ref worldTransformRO, ref tickedTransformRO);
            hierarchy.Length = RemoveMarkedDescendantsInHierarchy(ref parentTsa, srcScan, deadCount);
        }

        public static void RemoveDeadDescendantsFromHierarchyAndLeg(ref ThreadStackAllocator parentTsa, ref DynamicBuffer<EntityInHierarchy> hierarchy,
                                                                    ref DynamicBuffer<LinkedEntityGroup> leg, EntityManager em)
        {
            var srcScan   = hierarchy.AsNativeArray().AsSpan();
            int deadCount = CountAndMarkDeadDescendantsInHierarchy(srcScan, em);
            if (deadCount == 0)
                return;

            PatchLocalTransformsFromChildrenOfMarkedDead(srcScan, em);
            RemoveMarkedDescendantsFromLeg(ref leg, srcScan);
            hierarchy.Length = RemoveMarkedDescendantsInHierarchy(ref parentTsa, srcScan, deadCount);
        }

        public static void RemoveDeadDescendantsFromHierarchyAndLeg(ref ThreadStackAllocator parentTsa,
                                                                    ref DynamicBuffer<EntityInHierarchy>      hierarchy,
                                                                    ref DynamicBuffer<LinkedEntityGroup>      leg,
                                                                    EntityStorageInfoLookup esil,
                                                                    ref ComponentLookup<WorldTransform>       worldTransformRO,
                                                                    ref ComponentLookup<TickedWorldTransform> tickedTransformRO)
        {
            var srcScan   = hierarchy.AsNativeArray().AsSpan();
            int deadCount = CountAndMarkDeadDescendantsInHierarchy(srcScan, esil);
            if (deadCount == 0)
                return;

            PatchLocalTransformsFromChildrenOfMarkedDead(srcScan, esil, ref worldTransformRO, ref tickedTransformRO);
            RemoveMarkedDescendantsFromLeg(ref leg, srcScan);
            hierarchy.Length = RemoveMarkedDescendantsInHierarchy(ref parentTsa, srcScan, deadCount);
        }

        public static void RemoveDeadEntitiesFromLeg(ref DynamicBuffer<LinkedEntityGroup> leg, EntityStorageInfoLookup esil)
        {
            int dst  = 0;
            var span = leg.AsNativeArray().AsSpan();
            for (int i = 0; i < leg.Length; i++)
            {
                if (esil.IsAlive(span[i].Value))
                {
                    span[dst] = span[i];
                    dst++;
                }
            }
            leg.Length = dst;
        }
        #endregion

        #region Impl
        static int CountAndMarkDeadDescendantsInHierarchy(Span<EntityInHierarchy> hierarchy, EntityManager em)
        {
            int deadCount = 0;
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (!em.IsAlive(hierarchy[i].entity))
                {
                    hierarchy[i].m_firstChildIndex = int.MaxValue;
                    deadCount++;
                }
            }
            return deadCount;
        }

        static int CountAndMarkDeadDescendantsInHierarchy(Span<EntityInHierarchy> hierarchy, EntityStorageInfoLookup esil)
        {
            int deadCount = 0;
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (!esil.IsAlive(hierarchy[i].entity))
                {
                    hierarchy[i].m_firstChildIndex = int.MaxValue;
                    deadCount++;
                }
            }
            return deadCount;
        }

        static int CountAndMarkDeadAndUnreferencedDescendantsInHierarchy(Span<EntityInHierarchy> hierarchy, ref ComponentLookup<RootReference> rootRefRO)
        {
            int deadCount = 0;
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (!rootRefRO.TryGetComponent(hierarchy[i].entity, out var rootRef) || rootRef.rootEntity != hierarchy[0].entity)
                {
                    hierarchy[i].m_firstChildIndex = int.MaxValue;
                    deadCount++;
                }
            }
            return deadCount;
        }

        static void PatchLocalTransformsFromChildrenOfMarkedDead(Span<EntityInHierarchy> hierarchy, EntityManager em)
        {
            for (int i = 1; i < hierarchy.Length; i++)
            {
                ref var element = ref hierarchy[i];
                if (element.m_firstChildIndex == int.MaxValue)
                    continue;
                if (hierarchy[element.parentIndex].m_firstChildIndex == int.MaxValue)
                {
                    if (em.HasComponent<WorldTransform>(element.entity))
                    {
                        var local               = TransformTools.LocalTransformFrom(element.entity, em, out _);
                        element.m_localPosition = local.position;
                        element.m_localScale    = local.scale;
                    }
                    if (em.HasComponent<TickedWorldTransform>(element.entity))
                    {
                        var local                     = TransformTools.TickedLocalTransformFrom(element.entity, em, out _);
                        element.m_tickedLocalPosition = local.position;
                        element.m_tickedLocalScale    = local.scale;
                    }
                }
            }
        }

        static void PatchLocalTransformsFromChildrenOfMarkedDead(Span<EntityInHierarchy> hierarchy, EntityStorageInfoLookup esil,
                                                                 ref ComponentLookup<WorldTransform> worldLookup, ref ComponentLookup<TickedWorldTransform> tickedLookup)
        {
            for (int i = 1; i < hierarchy.Length; i++)
            {
                ref var element = ref hierarchy[i];
                if (element.m_firstChildIndex == int.MaxValue)
                    continue;
                if (hierarchy[element.parentIndex].m_firstChildIndex == int.MaxValue)
                {
                    var parent = FindRealParentAmongMarked(hierarchy, element.parentIndex);
                    if (worldLookup.TryGetComponent(element.entity, out var wt))
                    {
                        var parentTransform = worldLookup[parent];
                        WorldLocalOps.UpdateLocalTransformForCleanedParent(in parentTransform.worldTransform, in wt.worldTransform, ref element, false);
                    }
                    if (tickedLookup.TryGetComponent(element.entity, out var tt))
                    {
                        var parentTransform = worldLookup[parent];
                        WorldLocalOps.UpdateLocalTransformForCleanedParent(in parentTransform.worldTransform, in tt.worldTransform, ref element, true);
                    }
                }
            }
        }

        static Entity FindRealParentAmongMarked(Span<EntityInHierarchy> hierarchy, int deadParentIndex)
        {
            for (int i = deadParentIndex; i > 0; i = hierarchy[i].parentIndex)
            {
                if (hierarchy[i].m_firstChildIndex != int.MaxValue)
                    return hierarchy[i].entity;
            }
            return hierarchy[0].entity;
        }

        static int RemoveMarkedDescendantsInHierarchy(ref ThreadStackAllocator parentTsa, Span<EntityInHierarchy> hierarchy, int deadCount)
        {
            for (int i = 2; i < hierarchy.Length; i++)
            {
                ref var element = ref hierarchy[i];
                while (hierarchy[element.parentIndex].m_firstChildIndex == int.MaxValue)
                    element.m_parentIndex = hierarchy[element.parentIndex].parentIndex;
            }
            for (int i = 1; i < hierarchy.Length; i++)
            {
                ref var element = ref hierarchy[i];
                if (element.m_firstChildIndex == int.MaxValue)
                    element.m_parentIndex = int.MaxValue;
            }

            var tsa        = parentTsa.CreateChildAllocator();
            var selectFrom = CopyHierarchy(ref tsa, hierarchy);
            var srcToDst   = tsa.AllocateAsSpan<int>(selectFrom.Length);
            var dstBuffer  = hierarchy.Slice(0, selectFrom.Length - deadCount);

            dstBuffer[0].m_childCount = 0;
            srcToDst.Fill(int.MaxValue);
            srcToDst[0] = 0;

            for (int dst = 1; dst < dstBuffer.Length; dst++)
            {
                int bestValue = int.MaxValue;
                int bestIndex = int.MaxValue;
                for (int i = 1; i < selectFrom.Length; i++)
                {
                    var parentIndex = selectFrom[i].parentIndex;
                    var val         = parentIndex != int.MaxValue ? srcToDst[selectFrom[i].parentIndex] : int.MaxValue;
                    if (val < bestValue)
                    {
                        bestValue = val;
                        bestIndex = i;
                    }
                }
                ref var best                  = ref selectFrom[bestIndex];
                ref var dstElement            = ref dstBuffer[dst];
                dstElement.m_descendantEntity = best.m_descendantEntity;
                dstElement.m_flags            = best.m_flags;
                dstElement.m_parentIndex      = bestValue;
                dstElement.m_childCount       = 0;
                srcToDst[bestIndex]           = dst;
                dstBuffer[bestValue].m_childCount++;
                best.m_parentIndex = int.MaxValue;
            }

            int running = 1 + dstBuffer[0].childCount;
            for (int i = 1; i < dstBuffer.Length; i++)
            {
                dstBuffer[i].m_firstChildIndex  = running;
                running                        += dstBuffer[i].childCount;
            }
            tsa.Dispose();
            return dstBuffer.Length;
        }
        #endregion
    }
}
#endif

