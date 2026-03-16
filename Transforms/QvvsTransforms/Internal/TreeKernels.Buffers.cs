#if !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static unsafe partial class TreeKernels
    {
        #region Hierarchy Buffer Ops
        public static Span<EntityInHierarchy> CopyHierarchy(ref ThreadStackAllocator tsa, in ReadOnlySpan<EntityInHierarchy> hierarchy)
        {
            var span = tsa.AllocateAsSpan<EntityInHierarchy>(hierarchy.Length);
            hierarchy.CopyTo(span);
            return span;
        }

        public static void CopyHierarchyToCleanup(in DynamicBuffer<EntityInHierarchy> hierarchy, ref DynamicBuffer<EntityInHierarchyCleanup> cleanup)
        {
            cleanup.Clear();
            cleanup.CopyFrom(hierarchy.Reinterpret<EntityInHierarchyCleanup>());
        }

        public static Span<Entity> CopyHierarchyEntities(ref ThreadStackAllocator tsa, in ReadOnlySpan<EntityInHierarchy> hierarchy)
        {
            var span = tsa.AllocateAsSpan<Entity>(hierarchy.Length);
            for (int i = 0; i < hierarchy.Length; i++)
                span[i] = hierarchy[i].entity;
            return span;
        }

        public static Span<EntityInHierarchy> ExtractSubtree(ref ThreadStackAllocator tsa, in ReadOnlySpan<EntityInHierarchy> hierarchy, int subtreeRootIndex)
        {
            var maxDescendantsCount = hierarchy.Length - subtreeRootIndex;
            var extractionList      = new UnsafeList<EntityInHierarchy>(tsa.Allocate<EntityInHierarchy>(maxDescendantsCount), maxDescendantsCount);

            extractionList.Clear();  // The list initializer we are using sets both capacity and length.

            //descendantsToMove.Add((child, -1));
            extractionList.Add(new EntityInHierarchy
            {
                m_descendantEntity    = hierarchy[subtreeRootIndex].entity,
                m_parentIndex         = -1,
                m_childCount          = hierarchy[subtreeRootIndex].childCount,
                m_firstChildIndex     = 1,
                m_flags               = default,
                m_localPosition       = default,
                m_localScale          = 1f,
                m_tickedLocalPosition = default,
                m_tickedLocalScale    = 1f,
            });
            // The root is the first level. For each subsequent level, we iterate the entities added during the previous level.
            // And then we add their children.
            int firstParentInLevel               = 0;
            int parentCountInLevel               = 1;
            int firstParentHierarchyIndexInLevel = subtreeRootIndex;
            while (parentCountInLevel > 0)
            {
                var firstParentInNextLevel               = extractionList.Length;
                var parentCountInNextLevel               = 0;
                int firstParentHierarchyIndexInNextLevel = 0;
                for (int parentIndex = 0; parentIndex < parentCountInLevel; parentIndex++)
                {
                    var dstParentIndex    = parentIndex + firstParentInLevel;
                    var parentInHierarchy = hierarchy[firstParentHierarchyIndexInLevel + parentIndex];
                    if (parentIndex == 0)
                        firstParentHierarchyIndexInNextLevel  = parentInHierarchy.firstChildIndex;
                    parentCountInNextLevel                   += parentInHierarchy.childCount;
                    for (int i = 0; i < parentInHierarchy.childCount; i++)
                    {
                        var oldElement               = hierarchy[parentInHierarchy.firstChildIndex + i];
                        oldElement.m_firstChildIndex = int.MaxValue;
                        oldElement.m_parentIndex     = dstParentIndex;
                        extractionList.Add(oldElement);
                    }
                }
                firstParentInLevel               = firstParentInNextLevel;
                parentCountInLevel               = parentCountInNextLevel;
                firstParentHierarchyIndexInLevel = firstParentHierarchyIndexInNextLevel;
            }

            var result = new Span<EntityInHierarchy>(extractionList.Ptr, extractionList.Length);

            for (int i = 1; i < result.Length; i++)
            {
                ref var child           = ref result[i];
                ref var firstChildIndex = ref result[child.parentIndex].m_firstChildIndex;
                firstChildIndex         = math.min(firstChildIndex, i);
                var previous            = result[i - 1];
                child.m_firstChildIndex = previous.firstChildIndex + previous.childCount;
            }
            return result;
        }

        public static Span<Entity> GetAncestryEntitiesExcludingRoot(ref ThreadStackAllocator tsa, in ReadOnlySpan<EntityInHierarchy> hierarchy, int childIndex)
        {
            int count = 0;
            for (int i = hierarchy[childIndex].parentIndex; i > 0; i = hierarchy[i].parentIndex)
                count++;
            var result = tsa.AllocateAsSpan<Entity>(count);
            count      = 0;
            for (int i = hierarchy[childIndex].parentIndex; i > 0; i = hierarchy[i].parentIndex)
            {
                result[count] = hierarchy[i].entity;
                count++;
            }
            return result;
        }

        public static int FindEntityAfterChange(in ReadOnlySpan<EntityInHierarchy> hierarchy, Entity entity, int oldIndex)
        {
            if (oldIndex < hierarchy.Length && hierarchy[oldIndex].entity == entity)
                return oldIndex;
            for (int i = 1; i <= hierarchy.Length; i++)
                if (hierarchy[i].entity == entity)
                    return i;
            return -1;
        }

        public static void BuildOriginalParentChildHierarchy(ref DynamicBuffer<EntityInHierarchy> hierarchy, Entity parent, Entity child, InheritanceFlags flags)
        {
            hierarchy.Add(new EntityInHierarchy
            {
                m_childCount          = 1,
                m_descendantEntity    = parent,
                m_firstChildIndex     = 1,
                m_flags               = InheritanceFlags.Normal,
                m_parentIndex         = -1,
                m_localPosition       = default,
                m_localScale          = 1f,
                m_tickedLocalPosition = default,
                m_tickedLocalScale    = 1f,
            });
            hierarchy.Add(new EntityInHierarchy
            {
                m_childCount          = 0,
                m_descendantEntity    = child,
                m_firstChildIndex     = 2,
                m_flags               = flags,
                m_parentIndex         = 0,
                m_localPosition       = default,
                m_localScale          = 1f,
                m_tickedLocalPosition = default,
                m_tickedLocalScale    = 1f,
            });
        }

        public static int InsertSoloEntityIntoHierarchy(ref DynamicBuffer<EntityInHierarchy> hierarchy, int parentIndex, Entity soloChild, InheritanceFlags flags)
        {
            ref var newParentInHierarchy = ref hierarchy.ElementAt(parentIndex);
            var     insertionPoint       = newParentInHierarchy.firstChildIndex + newParentInHierarchy.childCount;
            newParentInHierarchy.m_childCount++;
            if (insertionPoint == hierarchy.Length)
            {
                var hierarchyArray = hierarchy.AsNativeArray();
                for (int i = parentIndex + 1; i < insertionPoint; i++)
                {
                    var parentElement = hierarchyArray[i];
                    parentElement.m_firstChildIndex++;
                    hierarchyArray[i] = parentElement;
                }
                hierarchy.Add(new EntityInHierarchy
                {
                    m_childCount          = 0,
                    m_descendantEntity    = soloChild,
                    m_firstChildIndex     = insertionPoint + 1,
                    m_flags               = flags,
                    m_parentIndex         = parentIndex,
                    m_localPosition       = default,
                    m_localScale          = 1f,
                    m_tickedLocalPosition = default,
                    m_tickedLocalScale    = 1f,
                });
            }
            else
            {
                var newFirstChildIndex = hierarchy[insertionPoint].firstChildIndex;
                hierarchy.Insert(insertionPoint, new EntityInHierarchy
                {
                    m_childCount          = 0,
                    m_descendantEntity    = soloChild,
                    m_firstChildIndex     = newFirstChildIndex,
                    m_flags               = flags,
                    m_parentIndex         = parentIndex,
                    m_localPosition       = default,
                    m_localScale          = 1f,
                    m_tickedLocalPosition = default,
                    m_tickedLocalScale    = 1f,
                });
                var hierarchyArray = hierarchy.AsNativeArray().AsSpan();
                for (int i = parentIndex + 1; i < hierarchyArray.Length; i++)
                {
                    ref var element = ref hierarchyArray[i];
                    element.m_firstChildIndex++;
                    if (element.parentIndex >= insertionPoint)
                        element.m_parentIndex++;
                }
            }
            return insertionPoint;
        }

        public static void BuildOriginalParentWithDescendantHierarchy(ref DynamicBuffer<EntityInHierarchy> hierarchy,
                                                                      Entity parent,
                                                                      ReadOnlySpan<EntityInHierarchy>      descendants,
                                                                      InheritanceFlags flags)
        {
            hierarchy.EnsureCapacity(descendants.Length + 1);
            hierarchy.Add(new EntityInHierarchy
            {
                m_childCount          = 1,
                m_descendantEntity    = parent,
                m_firstChildIndex     = 1,
                m_flags               = InheritanceFlags.Normal,
                m_parentIndex         = -1,
                m_localPosition       = default,
                m_localScale          = 1f,
                m_tickedLocalPosition = default,
                m_tickedLocalScale    = 1f,
            });
            for (int i = 0; i < descendants.Length; i++)
            {
                var newElement = descendants[i];
                newElement.m_firstChildIndex++;
                newElement.m_parentIndex++;
                hierarchy.Add(newElement);
            }
            hierarchy.ElementAt(1).m_flags = flags;
        }

        public static void InsertSubtreeIntoHierarchy(ref DynamicBuffer<EntityInHierarchy> hierarchy,
                                                      int parentIndex,
                                                      ReadOnlySpan<EntityInHierarchy>      subtreeToInsert,
                                                      InheritanceFlags flags)
        {
            var hierarchyOriginalLength  = hierarchy.Length;
            hierarchy.Length            += subtreeToInsert.Length;
            ref var parentInHierarchy    = ref hierarchy.ElementAt(parentIndex);
            var     insertionPoint       = parentInHierarchy.firstChildIndex + parentInHierarchy.childCount;
            parentInHierarchy.m_childCount++;
            var hierarchyArray = hierarchy.AsNativeArray().AsSpan();
            if (insertionPoint == hierarchyOriginalLength)
            {
                // We are appending the new child to the end, which means we can just copy the whole hierarchy.
                // But first, we need to push any previous firstChildIndex values one past.
                for (int i = parentIndex + 1; i < hierarchyOriginalLength; i++)
                {
                    var parentElement = hierarchyArray[i];
                    parentElement.m_firstChildIndex++;
                    hierarchyArray[i] = parentElement;
                }
                var childElement                = subtreeToInsert[0];
                childElement.m_parentIndex      = parentIndex;
                childElement.m_firstChildIndex += insertionPoint;
                childElement.m_flags            = flags;
                hierarchyArray[insertionPoint]  = childElement;
                for (int i = 1; i < subtreeToInsert.Length; i++)
                {
                    childElement                        = subtreeToInsert[i];
                    childElement.m_parentIndex         += insertionPoint;
                    childElement.m_firstChildIndex     += insertionPoint;
                    hierarchyArray[insertionPoint + i]  = childElement;
                }
            }
            else
            {
                // Move elements starting at the insertion point to the back
                for (int i = hierarchyOriginalLength - 1; i >= insertionPoint; i--)
                {
                    var src             = i;
                    var dst             = src + subtreeToInsert.Length;
                    hierarchyArray[dst] = hierarchyArray[src];
                }

                // Adjust first child index of parents preceeding the inserted child
                int existingChildrenToAdd = 0;
                for (int i = parentIndex + 1; i < insertionPoint; i++)
                {
                    var parentElement = hierarchyArray[i];
                    parentElement.m_firstChildIndex++;
                    existingChildrenToAdd += parentElement.childCount;
                    hierarchyArray[i]      = parentElement;
                }

                // Add the new child
                var newChildElement               = subtreeToInsert[0];
                newChildElement.m_parentIndex     = parentIndex;
                newChildElement.m_firstChildIndex = insertionPoint + 1 + existingChildrenToAdd;
                newChildElement.m_flags           = flags;
                int newChildrenToAdd              = newChildElement.childCount;
                hierarchyArray[insertionPoint]    = newChildElement;

                // Merge the hierarchies by alternating based on accumulated children batches
                int existingChildrenParentShift = 0;
                int existingChildrenChildShift  = 1 + newChildrenToAdd;
                int existingChildRunningIndex   = insertionPoint + subtreeToInsert.Length;
                int newChildrenLastAdded        = 1;
                int newChildrenParentShift      = insertionPoint;
                int newChildrenChildShift       = insertionPoint + existingChildrenToAdd;
                int newChildRunningIndex        = 1;
                int runningDst                  = insertionPoint + 1;

                while (newChildrenToAdd + existingChildrenToAdd > 0)
                {
                    int nextExistingChildrenToAdd = 0;
                    for (int i = 0; i < existingChildrenToAdd; i++)
                    {
                        var existingElement                = hierarchyArray[existingChildRunningIndex];
                        existingElement.m_parentIndex     += existingChildrenParentShift;
                        existingElement.m_firstChildIndex += existingChildrenChildShift;
                        nextExistingChildrenToAdd         += existingElement.childCount;
                        hierarchyArray[runningDst]         = existingElement;
                        existingChildRunningIndex++;
                        runningDst++;
                    }
                    newChildrenChildShift       += nextExistingChildrenToAdd;
                    existingChildrenParentShift += newChildrenLastAdded;

                    int nextNewChildrenToAdd = 0;
                    for (int i = 0; i < newChildrenToAdd; i++)
                    {
                        var newElement                = subtreeToInsert[newChildRunningIndex];
                        newElement.m_parentIndex     += newChildrenParentShift;
                        newElement.m_firstChildIndex += newChildrenChildShift;
                        nextNewChildrenToAdd         += newElement.childCount;
                        hierarchyArray[runningDst]    = newElement;
                        newChildRunningIndex++;
                        runningDst++;
                    }
                    existingChildrenChildShift += nextNewChildrenToAdd;
                    newChildrenParentShift     += existingChildrenToAdd;
                    newChildrenLastAdded        = newChildrenToAdd;
                    newChildrenToAdd            = nextNewChildrenToAdd;
                    existingChildrenToAdd       = nextExistingChildrenToAdd;
                }
            }
        }

        public static void RemoveSoloFromHierarchy(ref DynamicBuffer<EntityInHierarchy> hierarchy, int indexToRemove)
        {
            var parentIndex = hierarchy[indexToRemove].parentIndex;
            hierarchy.RemoveAt(indexToRemove);

            var hierarchyArray = hierarchy.AsNativeArray().AsSpan();
            hierarchyArray[parentIndex].m_childCount--;

            for (int i = parentIndex + 1; i < hierarchyArray.Length; i++)
            {
                ref var element = ref hierarchyArray[i];
                element.m_firstChildIndex--;
                if (element.parentIndex > indexToRemove)
                    element.m_parentIndex--;
            }
        }

        public static void RemoveSubtreeFromHierarchy(ref ThreadStackAllocator parentTsa, ref DynamicBuffer<EntityInHierarchy> hierarchy, int subtreeRootIndex,
                                                      ReadOnlySpan<EntityInHierarchy> extractedSubtree)
        {
            var tsa = parentTsa.CreateChildAllocator();

            // Start by decreasing the old parent's child count
            var oldHierarchyArray    = hierarchy.AsNativeArray().AsSpan();
            var subtreeParentIndex   = oldHierarchyArray[subtreeRootIndex].parentIndex;
            var oldParentInHierarchy = oldHierarchyArray[subtreeParentIndex];
            oldParentInHierarchy.m_childCount--;
            oldHierarchyArray[subtreeParentIndex] = oldParentInHierarchy;

            // Next, offset any entity first child indices for entities that are prior to the removed child
            for (int i = subtreeParentIndex + 1; i < subtreeRootIndex; i++)
            {
                var temp = oldHierarchyArray[i];
                temp.m_firstChildIndex--;
                oldHierarchyArray[i] = temp;
            }

            // Filter out the subtree in order.
            var dst                = subtreeRootIndex;
            var match              = 1;
            var modifiedSrcStart   = subtreeRootIndex + 1;
            var srcToDstIndicesMap = tsa.AllocateAsSpan<int>(oldHierarchyArray.Length - modifiedSrcStart + 1);
            for (int src = subtreeRootIndex + 1; src < oldHierarchyArray.Length; src++)
            {
                var srcData                                = oldHierarchyArray[src];
                srcToDstIndicesMap[src - modifiedSrcStart] = dst;
                if (match < extractedSubtree.Length && srcData.entity == extractedSubtree[match].entity)
                {
                    match++;
                    continue;
                }
                oldHierarchyArray[dst] = srcData;
                dst++;
            }
            srcToDstIndicesMap[oldHierarchyArray.Length - modifiedSrcStart] = dst;

            // Apply indexing conversions
            for (int i = subtreeRootIndex; i < dst; i++)
            {
                var element = oldHierarchyArray[i];
                if (element.parentIndex >= modifiedSrcStart)
                    element.m_parentIndex = srcToDstIndicesMap[element.parentIndex - modifiedSrcStart];
                element.m_firstChildIndex = srcToDstIndicesMap[element.firstChildIndex - modifiedSrcStart];
                oldHierarchyArray[i]      = element;
            }
            hierarchy.Length = dst;

            tsa.Dispose();
        }
        #endregion

        #region LinkedEntityGroup
        public static Span<Entity> GetHierarchyEntitiesInLeg(ref ThreadStackAllocator tsa,
                                                             ReadOnlySpan<EntityInHierarchy> hierarchy,
                                                             ReadOnlySpan<Entity>            oldLeg,
                                                             out bool matchedAll)
        {
            var required     = math.min(hierarchy.Length, oldLeg.Length);
            var resultBuffer = tsa.AllocateAsSpan<Entity>(required);
            int resultCount  = 0;
            matchedAll       = true;
            foreach (var h in hierarchy)
            {
                var  e       = h.entity;
                bool matched = false;
                for (int i = 0; i < oldLeg.Length; i++)
                {
                    if (oldLeg[i] == e)
                    {
                        resultBuffer[resultCount] = e;
                        resultCount++;
                        matched = true;
                        break;
                    }
                }
                matchedAll |= matched;
            }
            return resultBuffer.Slice(0, resultCount);
        }

        public static void AddEntityToLeg(ref DynamicBuffer<LinkedEntityGroup> leg, Entity entity)
        {
            leg.Add(new LinkedEntityGroup { Value = entity });
        }

        public static void AddEntitiesToLeg(ref DynamicBuffer<LinkedEntityGroup> leg, ReadOnlySpan<Entity> entities)
        {
            var buffer = leg.Reinterpret<Entity>();
            buffer.EnsureCapacity(buffer.Length + entities.Length);
            foreach (var e in entities)
                buffer.Add(e);
        }

        public static void AddHierarchyToLeg(ref DynamicBuffer<LinkedEntityGroup> leg, ReadOnlySpan<EntityInHierarchy> hierarchy)
        {
            var buffer = leg.Reinterpret<Entity>();
            buffer.EnsureCapacity(buffer.Length + hierarchy.Length);
            foreach (var e in hierarchy)
                buffer.Add(e.entity);
        }

        public static void RemoveEntityFromLeg(ref DynamicBuffer<LinkedEntityGroup> leg, Entity entity, out bool matched)
        {
            var index = leg.Reinterpret<Entity>().AsNativeArray().IndexOf(entity);
            if (index >= 0)
            {
                if (index > 0)
                    leg.RemoveAtSwapBack(index);
                matched = true;
            }
            else
                matched = false;
        }

        public static void RemoveHierarchyEntitiesFromLeg(ref DynamicBuffer<LinkedEntityGroup> leg, ReadOnlySpan<EntityInHierarchy> hierarchy)
        {
            foreach (var h in hierarchy)
                RemoveEntityFromLeg(ref leg, h.entity, out _);
        }

        public static Span<Entity> GetAndRemoveHierarchyEntitiesFromLeg(ref ThreadStackAllocator tsa,
                                                                        ref DynamicBuffer<LinkedEntityGroup> leg,
                                                                        ReadOnlySpan<EntityInHierarchy>      hierarchy,
                                                                        out bool matchedAll)
        {
            var required     = math.min(hierarchy.Length, leg.Length);
            var resultBuffer = tsa.AllocateAsSpan<Entity>(required);
            int resultCount  = 0;
            matchedAll       = true;
            foreach (var h in hierarchy)
            {
                RemoveEntityFromLeg(ref leg, h.entity, out var matched);
                matchedAll &= matched;
                if (matched)
                {
                    resultBuffer[resultCount] = h.entity;
                    resultCount++;
                }
            }
            return resultBuffer.Slice(0, resultCount);
        }

        public static void RemoveMarkedDescendantsFromLeg(ref DynamicBuffer<LinkedEntityGroup> legBuffer, ReadOnlySpan<EntityInHierarchy> hierarchy)
        {
            var leg = legBuffer.Reinterpret<Entity>();
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (hierarchy[i].m_firstChildIndex != int.MaxValue)
                    continue;

                var index = leg.AsNativeArray().IndexOf(hierarchy[i].entity);
                if (index >= 0)
                    leg.RemoveAtSwapBack(index);
            }
        }
        #endregion

        #region Root Reference and Local
        public static void UpdateRootReferencesFromDiff(ReadOnlySpan<EntityInHierarchy> hierarchy, ReadOnlySpan<Entity> oldEntities, EntityManager em)
        {
            var root = hierarchy[0].entity;
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (i >= oldEntities.Length || hierarchy[i].entity != oldEntities[i])
                    em.SetComponentData(hierarchy[i].entity, new RootReference { m_rootEntity = root, m_indexInHierarchy = i });
            }
        }

        public static void UpdateRootReferencesFromDiff(ReadOnlySpan<EntityInHierarchy>    hierarchy,
                                                        ReadOnlySpan<Entity>               oldEntities,
                                                        ref ComponentLookup<RootReference> rootReferenceLookupRW)
        {
            var root = hierarchy[0].entity;
            for (int i = 1; i < hierarchy.Length; i++)
            {
                if (i >= oldEntities.Length || hierarchy[i].entity != oldEntities[i])
                    rootReferenceLookupRW[hierarchy[i].entity] = new RootReference { m_rootEntity = root, m_indexInHierarchy = i };
            }
        }

        public static void UpdateLocalTransformsOfNewAncestorComponents(ReadOnlySpan<ComponentAddSet> addSets, Span<EntityInHierarchy> hierarchy)
        {
            foreach (var addSet in addSets)
            {
                if (addSet.parent == Entity.Null || addSet.noChange)
                    continue;

                if (!addSet.worldTransform && !addSet.tickedWorldTransform)
                    continue;

                ref var element = ref hierarchy[addSet.indexInHierarchy];
                if (addSet.copyNormalToTicked)
                {
                    element.m_tickedLocalPosition = element.m_localPosition;
                    element.m_tickedLocalScale    = element.m_localScale;
                }
                else if (addSet.copyTickedToNormal)
                {
                    element.m_localPosition = element.m_tickedLocalPosition;
                    element.m_localScale    = element.m_tickedLocalScale;
                }
                else
                {
                    if (addSet.setNormalToIdentity)
                    {
                        element.m_localPosition = default;
                        element.m_localScale    = 1f;
                    }
                    else if (addSet.setTickedToIdentity)
                    {
                        element.m_tickedLocalPosition = default;
                        element.m_tickedLocalScale    = 1f;
                    }
                }
            }
        }
        #endregion
    }
}
#endif

