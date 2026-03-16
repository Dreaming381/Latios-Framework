#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Set Local Transform
        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local transform for</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetLocalTransform(Entity entity, in TransformQvvs newLocalTransform, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform = newLocalTransform });
                return;
            }
            SetLocalTransform(handle, in newLocalTransform, entityManager);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local transform for</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalTransform(Entity entity, in TransformQvvs newLocalTransform, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                componentBroker.GetRW<WorldTransform>(entity).ValueRW.worldTransform = newLocalTransform;
                return;
            }
            SetLocalTransform(handle, in newLocalTransform, ref componentBroker);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local transform for</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalTransform(Entity entity, in TransformQvvs newLocalTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                componentBroker.GetRW<WorldTransform>(entity, key).ValueRW.worldTransform = newLocalTransform;
                return;
            }
            SetLocalTransform(handle, in newLocalTransform, ref componentBroker);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local transform for</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetLocalTransform(Entity entity,
                                             in TransformQvvs newLocalTransform,
                                             ref ComponentLookup<WorldTransform>        transformLookupRW,
                                             ref EntityStorageInfoLookup entityStorageInfoLookup,
                                             ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                             ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                             ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                transformLookupRW[entity] = new WorldTransform { worldTransform = newLocalTransform };
                return;
            }
            SetLocalTransform(handle, in newLocalTransform, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local transform for</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetLocalTransform(Entity entity,
                                             in TransformQvvs newLocalTransform,
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
                transformLookupRW.GetCheckedLookup(handle.root.entity, key)[entity] = new WorldTransform { worldTransform = newLocalTransform };
                return;
            }
            SetLocalTransform(handle, in newLocalTransform, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetLocalTransform(EntityInHierarchyHandle handle,
                                             in TransformQvvs newLocalTransform,
                                             ref ComponentLookup<WorldTransform> transformLookupRW,
                                             ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Sets the local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetLocalTransform(EntityInHierarchyHandle handle,
                                             in TransformQvvs newLocalTransform,
                                             TransformsKey key,
                                             ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                             ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Set Ticked Local Transform
        /// <summary>
        /// Sets the ticked local transform of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local transform for</param>
        /// <param name="newLocalTransform">The new transform value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetTickedLocalTransform(Entity entity, in TransformQvvs newLocalTransform, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                entityManager.SetComponentData(entity, new TickedWorldTransform() {
                    worldTransform = newLocalTransform
                });
                return;
            }
            SetTickedLocalTransform(handle, newLocalTransform, entityManager);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local transform for</param>
        /// <param name="newLocalTransform">The new transform value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalTransform(Entity entity, in TransformQvvs newLocalTransform, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                componentBroker.GetRW<TickedWorldTransform>(entity).ValueRW.worldTransform = newLocalTransform;
                return;
            }
            SetTickedLocalTransform(handle, in newLocalTransform, ref componentBroker);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local transform for</param>
        /// <param name="newLocalTransform">The new transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalTransform(Entity entity, in TransformQvvs newLocalTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                componentBroker.GetRW<TickedWorldTransform>(entity, key).ValueRW.worldTransform = newLocalTransform;
                return;
            }
            SetTickedLocalTransform(handle, in newLocalTransform, ref componentBroker);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetTickedLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalTransform(EntityInHierarchyHandle handle, in TransformQvvs newLocalTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local transform for</param>
        /// <param name="newLocalTransform">The new transform value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetTickedLocalTransform(Entity entity,
                                                   in TransformQvvs newLocalTransform,
                                                   ref ComponentLookup<TickedWorldTransform>  transformLookupRW,
                                                   ref EntityStorageInfoLookup entityStorageInfoLookup,
                                                   ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                                   ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                   ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                transformLookupRW[entity] = new TickedWorldTransform { worldTransform = newLocalTransform };
                return;
            }
            SetTickedLocalTransform(handle, in newLocalTransform, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the ticked local transform of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local transform for</param>
        /// <param name="newLocalTransform">The new transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetTickedLocalTransform(Entity entity,
                                                   in TransformQvvs newLocalTransform,
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
                transformLookupRW.GetCheckedLookup(handle.root.entity, key)[entity] = new TickedWorldTransform { worldTransform = newLocalTransform };
                return;
            }
            SetTickedLocalTransform(handle, in newLocalTransform, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the ticked local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetTickedLocalTransform(EntityInHierarchyHandle handle,
                                                   in TransformQvvs newLocalTransform,
                                                   ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                                   ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Sets the ticked local transform  of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should be set</param>
        /// <param name="newLocalTransform">The new local transform value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetTickedLocalTransform(EntityInHierarchyHandle handle,
                                                   in TransformQvvs newLocalTransform,
                                                   TransformsKey key,
                                                   ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                                   ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { newLocalTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalTransformQvvsSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

