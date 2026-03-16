#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Apply Local Scale Delta
        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void Scale(Entity entity, float scaleFactor, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                              = entityManager.GetComponentData<WorldTransform>(entity).worldTransform;
                currentTransform.scale                                                     *= scaleFactor;
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform  = currentTransform });
                return;
            }
            Scale(handle, scaleFactor, entityManager);
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Scale(Entity entity, float scaleFactor, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.scale                 *= scaleFactor;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Scale(handle, scaleFactor, ref componentBroker);
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Scale(Entity entity, float scaleFactor, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity, key);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.scale                 *= scaleFactor;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Scale(handle, scaleFactor, ref componentBroker);
        }

        /// <summary>
        /// Scales the entity by the specified factor.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void Scale(EntityInHierarchyHandle handle, float scaleFactor, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Scale(EntityInHierarchyHandle handle, float scaleFactor, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void Scale(EntityInHierarchyHandle handle, float scaleFactor, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void Scale(Entity entity,
                                 float scaleFactor,
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
                currentTransform.scale                 *= scaleFactor;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Scale(handle, scaleFactor, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void Scale(Entity entity,
                                 float scaleFactor,
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
                currentTransform.scale                 *= scaleFactor;
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            Scale(handle, scaleFactor, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void Scale(EntityInHierarchyHandle handle,
                                 float scaleFactor,
                                 ref ComponentLookup<WorldTransform> transformLookupRW,
                                 ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Scales the entity by the specified factor
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void Scale(EntityInHierarchyHandle handle,
                                 float scaleFactor,
                                 TransformsKey key,
                                 ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                 ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Apply Ticked Local Scale Delta
        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void ScaleTicked(Entity entity, float scaleFactor, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                                    = entityManager.GetComponentData<TickedWorldTransform>(entity).worldTransform;
                currentTransform.scale                                                           *= scaleFactor;
                entityManager.SetComponentData(entity, new TickedWorldTransform { worldTransform  = currentTransform });
                return;
            }
            ScaleTicked(handle, scaleFactor, entityManager);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void ScaleTicked(Entity entity, float scaleFactor, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.scale                       *= scaleFactor;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            ScaleTicked(handle, scaleFactor, ref componentBroker);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void ScaleTicked(Entity entity, float scaleFactor, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity, key);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.scale                       *= scaleFactor;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            ScaleTicked(handle, scaleFactor, ref componentBroker);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void ScaleTicked(EntityInHierarchyHandle handle, float scaleFactor, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void ScaleTicked(EntityInHierarchyHandle handle, float scaleFactor, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void ScaleTicked(EntityInHierarchyHandle handle, float scaleFactor, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void ScaleTicked(Entity entity,
                                       float scaleFactor,
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
                currentTransform.scale                       *= scaleFactor;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            ScaleTicked(handle, scaleFactor, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="entity">The entity to apply the delta to the ticked world scale for</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void ScaleTicked(Entity entity,
                                       float scaleFactor,
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
                currentTransform.scale                       *= scaleFactor;
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            ScaleTicked(handle, scaleFactor, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void ScaleTicked(EntityInHierarchyHandle handle,
                                       float scaleFactor,
                                       ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Scales the entity by the specified factor for the tick
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world scale should receive the delta</param>
        /// <param name="scaleFactor">Scales by this scale factor</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void ScaleTicked(EntityInHierarchyHandle handle,
                                       float scaleFactor,
                                       TransformsKey key,
                                       ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { scale = scaleFactor } };
            Span<Propagate.WriteCommand> commands                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.ScaleDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

