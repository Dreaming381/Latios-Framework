#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Apply Local Stretch Delta
        /// <summary>
        /// Stretches the entity by the specified factors along each axis
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void Stretch(Entity entity, float3 stretchFactors, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                              = entityManager.GetComponentData<WorldTransform>(entity).worldTransform;
                currentTransform.stretch                                                   *= stretchFactors;
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform  = currentTransform });
                return;
            }
            Stretch(handle, stretchFactors, entityManager);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Stretch(Entity entity, float3 stretchFactors, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch               *= stretchFactors;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Stretch(handle, stretchFactors, ref componentBroker);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Stretch(Entity entity, float3 stretchFactors, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity, key);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch               *= stretchFactors;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Stretch(handle, stretchFactors, ref componentBroker);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void Stretch(EntityInHierarchyHandle handle, float3 stretchFactors, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Stretch(EntityInHierarchyHandle handle, float3 stretchFactors, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Stretch(EntityInHierarchyHandle handle, float3 stretchFactors, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void Stretch(Entity entity,
                                   float3 stretchFactors,
                                   ref ComponentLookup<WorldTransform>        transformLookupRW,
                                   ref EntityStorageInfoLookup entityStorageInfoLookup,
                                   ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                   ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                   ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = transformLookupRW.GetRefRW(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch               *= stretchFactors;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Stretch(handle, stretchFactors, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void Stretch(Entity entity,
                                   float3 stretchFactors,
                                   TransformsKey key,
                                   ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                   ref EntityStorageInfoLookup entityStorageInfoLookup,
                                   ref ComponentLookup<RootReference>            rootReferenceLookupRO,
                                   ref BufferLookup<EntityInHierarchy>           entityInHierarchyLookupRO,
                                   ref BufferLookup<EntityInHierarchyCleanup>    entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = transformLookupRW.GetCheckedLookup(handle.root.entity, key).GetRefRW(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch               *= stretchFactors;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Stretch(handle, stretchFactors, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void Stretch(EntityInHierarchyHandle handle,
                                   float3 stretchFactors,
                                   ref ComponentLookup<WorldTransform> transformLookupRW,
                                   ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands                                                              =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Stretches the entity by the specified factors along each axis.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void Stretch(EntityInHierarchyHandle handle,
                                   float3 stretchFactors,
                                   TransformsKey key,
                                   ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                   ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands                                                              =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Apply Ticked Local Stretch Delta
        /// <summary>
        /// Stretches the entity by the specified factors for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void StretchTicked(Entity entity, float3 stretchFactors, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                                    = entityManager.GetComponentData<TickedWorldTransform>(entity).worldTransform;
                currentTransform.stretch                                                         *= stretchFactors;
                entityManager.SetComponentData(entity, new TickedWorldTransform { worldTransform  = currentTransform });
                return;
            }
            StretchTicked(handle, stretchFactors, entityManager);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void StretchTicked(Entity entity, float3 stretchFactors, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch                     *= stretchFactors;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            StretchTicked(handle, stretchFactors, ref componentBroker);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void StretchTicked(Entity entity, float3 stretchFactors, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity, key);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch                     *= stretchFactors;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            StretchTicked(handle, stretchFactors, ref componentBroker);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void StretchTicked(EntityInHierarchyHandle handle, float3 stretchFactors, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void StretchTicked(EntityInHierarchyHandle handle, float3 stretchFactors, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void StretchTicked(EntityInHierarchyHandle handle, float3 stretchFactors, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void StretchTicked(Entity entity,
                                         float3 stretchFactors,
                                         ref ComponentLookup<TickedWorldTransform>  transformLookupRW,
                                         ref EntityStorageInfoLookup entityStorageInfoLookup,
                                         ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                         ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                         ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = transformLookupRW.GetRefRW(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch                     *= stretchFactors;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            StretchTicked(handle, stretchFactors, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked stretch for</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void StretchTicked(Entity entity,
                                         float3 stretchFactors,
                                         TransformsKey key,
                                         ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                         ref EntityStorageInfoLookup entityStorageInfoLookup,
                                         ref ComponentLookup<RootReference>                  rootReferenceLookupRO,
                                         ref BufferLookup<EntityInHierarchy>                 entityInHierarchyLookupRO,
                                         ref BufferLookup<EntityInHierarchyCleanup>          entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = transformLookupRW.GetCheckedLookup(handle.root.entity, key).GetRefRW(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.stretch                     *= stretchFactors;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            StretchTicked(handle, stretchFactors, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void StretchTicked(EntityInHierarchyHandle handle,
                                         float3 stretchFactors,
                                         ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                         ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands                                                              =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Stretches the entity by the specified factors for the tick.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked stretch should receive the delta</param>
        /// <param name="stretchFactors">Stretches by these stretch factors</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void StretchTicked(EntityInHierarchyHandle handle,
                                         float3 stretchFactors,
                                         TransformsKey key,
                                         ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                         ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { stretch = stretchFactors } };
            Span<Propagate.WriteCommand> commands                                                              =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.StretchDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

