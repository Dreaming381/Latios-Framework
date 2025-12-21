#region Header
#if UNITY_EDITOR && !DISABLE_HYBRID_RENDERER_PICKING
#define ENABLE_PICKING
#endif

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Unity.Rendering;
#endregion

namespace Latios.Kinemation.Systems
{
    public unsafe partial class LatiosEntitiesGraphicsSystem : SubSystem
    {
        static readonly ProfilerMarker m_latiosPerformCullingMarker = new ProfilerMarker("LatiosOnPerformCulling");

        private JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext batchCullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
            if (m_unmanaged.m_needsFirstUpdate)
                return default;

            using var         marker                    = m_latiosPerformCullingMarker.Auto();
            var               wrappedIncludeExcludeList = new WrappedPickingIncludeExcludeList(batchCullingContext.viewType);
            fixed (Unmanaged* unmanaged                 = &m_unmanaged)
            {
                DoOnPerformCullingBegin(unmanaged, ref CheckedStateRef, ref batchCullingContext, ref cullingOutput, ref wrappedIncludeExcludeList);
                SuperSystem.UpdateSystem(latiosWorldUnmanaged, m_cullingSuperSystem.SystemHandle);
                DoOnPerformCullingEnd(unmanaged, out var result);
                return result;
            }
        }

        [BurstCompile]
        static bool DoOnPerformCullingBegin(Unmanaged*                           unmanaged,
                                            ref SystemState state,
                                            ref BatchCullingContext batchCullingContext,
                                            ref BatchCullingOutput cullingOutput,
                                            ref WrappedPickingIncludeExcludeList wrappedIncludeExcludeList)
        {
            return unmanaged->OnPerformCullingBegin(ref state, ref batchCullingContext, ref cullingOutput, ref wrappedIncludeExcludeList);
        }

        [BurstCompile]
        static void DoOnPerformCullingEnd(Unmanaged* unmanaged, out JobHandle finalHandle)
        {
            finalHandle = unmanaged->OnPerformCullingEnd();
        }

        private unsafe void OnFinishedCulling(IntPtr customCullingResult)
        {
            m_unmanaged.OnFinishedCulling(ref CheckedStateRef, customCullingResult);
        }

        partial struct Unmanaged
        {
            public bool OnPerformCullingBegin(ref SystemState state,
                                              ref BatchCullingContext batchCullingContext,
                                              ref BatchCullingOutput cullingOutput,
                                              ref WrappedPickingIncludeExcludeList wrappedIncludeExcludeList)
            {
                cullingOutput.customCullingResult[0] = (IntPtr)m_cullPassIndexThisFrame;

                IncludeExcludeListFilter includeExcludeListFilter = GetPickingIncludeExcludeListFilterForCurrentCullingCallback(state.EntityManager,
                                                                                                                                batchCullingContext,
                                                                                                                                wrappedIncludeExcludeList,
                                                                                                                                m_ThreadLocalAllocators.GeneralAllocator->ToAllocator);

                // If inclusive filtering is enabled and we know there are no included entities,
                // we can skip all the work because we know that the result will be nothing.
                if (includeExcludeListFilter.IsIncludeEnabled && includeExcludeListFilter.IsIncludeEmpty)
                {
                    includeExcludeListFilter.Dispose();
                    return false;
                }

                latiosWorld.worldBlackboardEntity.SetComponentData(new CullingContext
                {
                    cullIndexThisFrame  = m_cullPassIndexThisFrame,
                    cullingFlags        = batchCullingContext.cullingFlags,
                    cullingLayerMask    = batchCullingContext.cullingLayerMask,
                    localToWorldMatrix  = batchCullingContext.localToWorldMatrix,
                    lodParameters       = batchCullingContext.lodParameters,
                    projectionType      = batchCullingContext.projectionType,
                    receiverPlaneCount  = batchCullingContext.receiverPlaneCount,
                    receiverPlaneOffset = batchCullingContext.receiverPlaneOffset,
                    sceneCullingMask    = batchCullingContext.sceneCullingMask,
                    viewID              = batchCullingContext.viewID,
                    viewType            = batchCullingContext.viewType,
                });
                latiosWorld.worldBlackboardEntity.SetComponentData(new DispatchContext
                {
                    globalSystemVersionOfLatiosEntitiesGraphics = m_globalSystemVersionAtLastUpdate,
                    lastSystemVersionOfLatiosEntitiesGraphics   = m_LastSystemVersionAtLastUpdate,
                    dispatchIndexThisFrame                      = m_dispatchPassIndexThisFrame
                });

                var cullingPlanesBuffer = latiosWorld.worldBlackboardEntity.GetBuffer<CullingPlane>(false);
                cullingPlanesBuffer.Clear();
                cullingPlanesBuffer.Reinterpret<Plane>().AddRange(batchCullingContext.cullingPlanes);
                var splitsBuffer = latiosWorld.worldBlackboardEntity.GetBuffer<CullingSplitElement>(false);
                splitsBuffer.Clear();
                splitsBuffer.Reinterpret<CullingSplit>().AddRange(batchCullingContext.cullingSplits);

                latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(new BrgCullingContext
                {
                    cullingThreadLocalAllocator                          = m_ThreadLocalAllocators,
                    batchCullingOutput                                   = cullingOutput,
                    batchFilterSettingsByRenderFilterSettingsSharedIndex = m_FilterSettings,
                    // To be able to access the material/mesh IDs, we need access to the registered material/mesh
                    // arrays. If we can't get them, then we simply skip in those cases.
                    brgRenderMeshArrays = m_brgRenderMeshArrays,

#if UNITY_EDITOR
                    includeExcludeListFilter = includeExcludeListFilter,
#endif
                });
                latiosWorld.worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(default, false);
                return true;
            }

            public JobHandle OnPerformCullingEnd()
            {
                var worldBlackboardEntity = latiosWorld.worldBlackboardEntity;
                latiosWorld.GetCollectionComponent<BrgCullingContext>(worldBlackboardEntity, out var finalHandle);
                worldBlackboardEntity.UpdateJobDependency<BrgCullingContext>(finalHandle, false);
                m_cullingCallbackFinalJobHandles.Add(finalHandle);
                m_cullPassIndexThisFrame++;
                return finalHandle;
            }

            public unsafe void OnFinishedCulling(ref SystemState state, IntPtr customCullingResult)
            {
                //UnityEngine.Debug.Log($"OnFinishedCulling pass {(int)customCullingResult}");

                if (m_needsFirstUpdate || m_cullPassIndexThisFrame == m_cullPassIndexForLastDispatch)
                    return;

                m_cullingDispatchSuperSystem.Update(state.WorldUnmanaged);
                m_cullPassIndexForLastDispatch = m_cullPassIndexThisFrame;
                m_dispatchPassIndexThisFrame++;
                if (m_dispatchPassIndexThisFrame > 1024)
                {
                    JobHandle.CompleteAll(m_cullingCallbackFinalJobHandles.AsArray());
                    m_ThreadLocalAllocators.Rewind();
                }
            }
        }

        struct WrappedPickingIncludeExcludeList
        {
#if ENABLE_PICKING && !DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
#if UNITY_6000_3_OR_NEWER
            public PickingIncludeExcludeEntityIdList includeExcludeList;

            public WrappedPickingIncludeExcludeList(BatchCullingViewType viewType)
            {
                includeExcludeList = default;
                if (viewType == BatchCullingViewType.Picking)
                    includeExcludeList = HandleUtility.GetPickingIncludeExcludeEntityIdList(Allocator.Temp);
                else if (viewType == BatchCullingViewType.SelectionOutline)
                    includeExcludeList = HandleUtility.GetSelectionOutlineIncludeExcludeEntityIdList(Allocator.Temp);
            }
#else
            public PickingIncludeExcludeList includeExcludeList;

            public WrappedPickingIncludeExcludeList(BatchCullingViewType viewType)
            {
                includeExcludeList = default;
                if (viewType == BatchCullingViewType.Picking)
                    includeExcludeList = HandleUtility.GetPickingIncludeExcludeList(Allocator.Temp);
                else if (viewType == BatchCullingViewType.SelectionOutline)
                    includeExcludeList = HandleUtility.GetSelectionOutlineIncludeExcludeList(Allocator.Temp);
            }
#endif
#else
            public WrappedPickingIncludeExcludeList(BatchCullingViewType viewType)
            {
            }
#endif
        }

        // This function does only return a meaningful IncludeExcludeListFilter object when called from a BRG culling callback.
        static IncludeExcludeListFilter GetPickingIncludeExcludeListFilterForCurrentCullingCallback(EntityManager entityManager,
                                                                                                    in BatchCullingContext cullingContext,
                                                                                                    WrappedPickingIncludeExcludeList wrappedIncludeExcludeList,
                                                                                                    Allocator allocator)
        {
#if ENABLE_PICKING && !DISABLE_INCLUDE_EXCLUDE_LIST_FILTERING
#if UNITY_6000_3_OR_NEWER
            PickingIncludeExcludeEntityIdList includeExcludeList = wrappedIncludeExcludeList.includeExcludeList;
            NativeArray<EntityId>             emptyArray         = new NativeArray<EntityId>(0, Allocator.Temp);
#else
            PickingIncludeExcludeList includeExcludeList = wrappedIncludeExcludeList.includeExcludeList;
            NativeArray<int>          emptyArray         = new NativeArray<int>(0, Allocator.Temp);
#endif

            var includeEntityIndices = includeExcludeList.IncludeEntities;
            if (cullingContext.viewType == BatchCullingViewType.SelectionOutline)
            {
                // Make sure the include list for the selection outline is never null even if there is nothing in it.
                // Null NativeArray and empty NativeArray are treated as different things when used to construct an IncludeExcludeListFilter object:
                // - Null include list means that nothing is discarded because the filtering is skipped.
                // - Empty include list means that everything is discarded because the filtering is enabled but never passes.
                // With selection outline culling, we want the filtering to happen in any case even if the array contains nothing so that we don't highlight everything in the latter case.
                if (!includeEntityIndices.IsCreated)
                    includeEntityIndices = emptyArray;
            }
            else if (includeEntityIndices.Length == 0)
            {
                includeEntityIndices = default;
            }

            var excludeEntityIndices = includeExcludeList.ExcludeEntities;
            if (excludeEntityIndices.Length == 0)
                excludeEntityIndices = default;

            IncludeExcludeListFilter includeExcludeListFilter = new IncludeExcludeListFilter(
                entityManager,
#if UNITY_6000_3_OR_NEWER
                includeEntityIndices.Reinterpret<int>(),
                excludeEntityIndices.Reinterpret<int>(),
#else
                includeEntityIndices,
                excludeEntityIndices,
#endif
                allocator);

            return includeExcludeListFilter;
#else
            return default;
#endif
        }
    }
}

