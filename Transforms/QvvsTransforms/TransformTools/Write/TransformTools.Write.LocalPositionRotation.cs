#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Set Local Position And Rotation
        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local position and rotation for</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                             = entityManager.GetComponentData<WorldTransform>(entity).worldTransform;
                currentTransform.position                                                  = newLocalPosition;
                currentTransform.rotation                                                  = newLocalRotation;
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform = currentTransform });
                return;
            }
            SetLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, entityManager);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local position and rotation for</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW            = componentBroker.GetRW<WorldTransform>(entity);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position              = newLocalPosition;
                currentTransform.rotation              = newLocalRotation;
                refRW.ValueRW.worldTransform           = currentTransform;
                return;
            }
            SetLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref componentBroker);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the local position and rotation for</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW            = componentBroker.GetRW<WorldTransform>(entity, key);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position              = newLocalPosition;
                currentTransform.rotation              = newLocalRotation;
                refRW.ValueRW.worldTransform           = currentTransform;
                return;
            }
            SetLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, key, ref componentBroker);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetLocalPositionAndRotation(EntityInHierarchyHandle handle, float3 newLocalPosition, quaternion newLocalRotation, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalPositionAndRotation(EntityInHierarchyHandle handle, float3 newLocalPosition, quaternion newLocalRotation, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetLocalPositionAndRotation(EntityInHierarchyHandle handle, float3 newLocalPosition, quaternion newLocalRotation, TransformsKey key,
                                                       ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity
        /// </summary>
        /// <param name="entity">The entity to set the local position and rotation for</param>
        /// <param name="newLocalPosition">The new position value</param>
        /// <param name="newLocalRotation">The new rotation value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetLocalPositionAndRotation(Entity entity,
                                                       float3 newLocalPosition,
                                                       quaternion newLocalRotation,
                                                       ref ComponentLookup<WorldTransform>        transformLookupRW,
                                                       ref EntityStorageInfoLookup entityStorageInfoLookup,
                                                       ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                                       ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                       ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW            = transformLookupRW.GetRefRW(entity);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position              = newLocalPosition;
                currentTransform.rotation              = newLocalRotation;
                refRW.ValueRW.worldTransform           = currentTransform;
                return;
            }
            SetLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity
        /// </summary>
        /// <param name="entity">The entity to set the local position and rotation for</param>
        /// <param name="newLocalPosition">The new position value</param>
        /// <param name="newLocalRotation">The new rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetLocalPositionAndRotation(Entity entity,
                                                       float3 newLocalPosition,
                                                       quaternion newLocalRotation,
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
                RefRW<WorldTransform> refRW            = transformLookupRW.GetCheckedLookup(entity, key).GetRefRW(entity);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position              = newLocalPosition;
                currentTransform.rotation              = newLocalRotation;
                refRW.ValueRW.worldTransform           = currentTransform;
                return;
            }
            SetLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref transformLookupRW.GetCheckedLookup(handle.root.entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetLocalPositionAndRotation(EntityInHierarchyHandle handle,
                                                       float3 newLocalPosition,
                                                       quaternion newLocalRotation,
                                                       ref ComponentLookup<WorldTransform> transformLookupRW,
                                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands                                                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Sets the local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetLocalPositionAndRotation(EntityInHierarchyHandle handle,
                                                       float3 newLocalPosition,
                                                       quaternion newLocalRotation,
                                                       TransformsKey key,
                                                       ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands                                                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Set Ticked Local Position And Rotation
        /// <summary>
        /// Sets the ticked local position and rotation of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local position and rotation for</param>
        /// <param name="newLocalPosition">The new position value</param>
        /// <param name="newLocalRotation">The new rotation value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetTickedLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                                   = entityManager.GetComponentData<TickedWorldTransform>(entity).worldTransform;
                currentTransform.position                                                        = newLocalPosition;
                currentTransform.rotation                                                        = newLocalRotation;
                entityManager.SetComponentData(entity, new TickedWorldTransform { worldTransform = currentTransform });
                return;
            }
            SetTickedLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, entityManager);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the ticked local position and rotation for</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW            = componentBroker.GetRW<TickedWorldTransform>(entity);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position                    = newLocalPosition;
                currentTransform.rotation                    = newLocalRotation;
                refRW.ValueRW.worldTransform                 = currentTransform;
                return;
            }
            SetTickedLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref componentBroker);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="entity">The entity to set the ticked local position and rotation for</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalPositionAndRotation(Entity entity, float3 newLocalPosition, quaternion newLocalRotation, TransformsKey key,
                                                             ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW            = componentBroker.GetRW<TickedWorldTransform>(entity, key);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position                    = newLocalPosition;
                currentTransform.rotation                    = newLocalRotation;
                refRW.ValueRW.worldTransform                 = currentTransform;
                return;
            }
            SetTickedLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, key, ref componentBroker);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void SetTickedLocalPositionAndRotation(EntityInHierarchyHandle handle, float3 newLocalPosition, quaternion newLocalRotation, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalPositionAndRotation(EntityInHierarchyHandle handle,
                                                             float3 newLocalPosition,
                                                             quaternion newLocalRotation,
                                                             ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref TickedComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void SetTickedLocalPositionAndRotation(EntityInHierarchyHandle handle, float3 newLocalPosition, quaternion newLocalRotation, TransformsKey key,
                                                             ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref TickedComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local position and rotation for</param>
        /// <param name="newLocalPosition">The new position value</param>
        /// <param name="newLocalRotation">The new rotation value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetTickedLocalPositionAndRotation(Entity entity,
                                                             float3 newLocalPosition,
                                                             quaternion newLocalRotation,
                                                             ref ComponentLookup<TickedWorldTransform>  transformLookupRW,
                                                             ref EntityStorageInfoLookup entityStorageInfoLookup,
                                                             ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                                             ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                             ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            var handle = GetHierarchyHandle(entity, ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW            = transformLookupRW.GetRefRW(entity);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position                    = newLocalPosition;
                currentTransform.rotation                    = newLocalRotation;
                refRW.ValueRW.worldTransform                 = currentTransform;
                return;
            }
            SetTickedLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity
        /// </summary>
        /// <param name="entity">The entity to set the ticked local position and rotation for</param>
        /// <param name="newLocalPosition">The new position value</param>
        /// <param name="newLocalRotation">The new rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void SetTickedLocalPositionAndRotation(Entity entity,
                                                             float3 newLocalPosition,
                                                             quaternion newLocalRotation,
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
                RefRW<TickedWorldTransform> refRW            = transformLookupRW.GetCheckedLookup(entity, key).GetRefRW(entity);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                currentTransform.position                    = newLocalPosition;
                currentTransform.rotation                    = newLocalRotation;
                refRW.ValueRW.worldTransform                 = currentTransform;
                return;
            }
            SetTickedLocalPositionAndRotation(handle, newLocalPosition, newLocalRotation, ref transformLookupRW.GetCheckedLookup(handle.root.entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetTickedLocalPositionAndRotation(EntityInHierarchyHandle handle,
                                                             float3 newLocalPosition,
                                                             quaternion newLocalRotation,
                                                             ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                                             ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands                                                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Sets the ticked local position and rotation of an entity.
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local position and rotation should be set</param>
        /// <param name="newLocalPosition">The new local position value</param>
        /// <param name="newLocalRotation">The new local rotation value</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void SetTickedLocalPositionAndRotation(EntityInHierarchyHandle handle,
                                                             float3 newLocalPosition,
                                                             quaternion newLocalRotation,
                                                             TransformsKey key,
                                                             ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                                             ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = newLocalPosition, rotation = newLocalRotation } };
            Span<Propagate.WriteCommand> commands                                                                                            =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalPositionRotationSet
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

