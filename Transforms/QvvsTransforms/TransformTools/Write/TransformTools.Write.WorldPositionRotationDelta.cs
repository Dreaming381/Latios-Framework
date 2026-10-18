#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Apply World Position And Rotation Delta
        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TranslateRotateWorld(Entity entity, float3 translation, quaternion rotation, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                              = entityManager.GetComponentData<WorldTransform>(entity).worldTransform;
                currentTransform.position                                                  += translation;
                currentTransform.rotation                                                   = math.normalize(math.mul(rotation, currentTransform.rotation));
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform  = currentTransform });
                return;
            }
            TranslateRotateWorld(handle, translation, rotation, entityManager);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateWorld(Entity entity, float3 translation, quaternion rotation, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position              += translation;
                currentTransform.rotation               = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            TranslateRotateWorld(handle, translation, rotation, ref componentBroker);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateWorld(Entity entity, float3 translation, quaternion rotation, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW             = componentBroker.GetRW<WorldTransform>(entity, key);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position              += translation;
                currentTransform.rotation               = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            TranslateRotateWorld(handle, translation, rotation, key, ref componentBroker);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TranslateRotateWorld(EntityInHierarchyHandle handle, float3 translation, quaternion rotation, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateWorld(EntityInHierarchyHandle handle, float3 translation, quaternion rotation, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateWorld(EntityInHierarchyHandle handle, float3 translation, quaternion rotation, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TranslateRotateWorld(Entity entity,
                                                float3 translation,
                                                quaternion rotation,
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
                currentTransform.position              += translation;
                currentTransform.rotation               = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            TranslateRotateWorld(handle, translation, rotation, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TranslateRotateWorld(Entity entity,
                                                float3 translation,
                                                quaternion rotation,
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
                RefRW<WorldTransform> refRW             = transformLookupRW.GetCheckedLookup(entity, key).GetRefRW(entity);
                TransformQvvs         currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position              += translation;
                currentTransform.rotation               = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform            = currentTransform;
                return;
            }
            TranslateRotateWorld(handle, translation, rotation, ref transformLookupRW.GetCheckedLookup(handle.root.entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TranslateRotateWorld(EntityInHierarchyHandle handle,
                                                float3 translation,
                                                quaternion rotation,
                                                ref ComponentLookup<WorldTransform> transformLookupRW,
                                                ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands                                                                                       =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TranslateRotateWorld(EntityInHierarchyHandle handle,
                                                float3 translation,
                                                quaternion rotation,
                                                TransformsKey key,
                                                ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                                ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands                                                                                       =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Apply Ticked World Position And Rotation Delta
        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the ticked world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TranslateRotateTickedWorld(Entity entity, float3 translation, quaternion rotation, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                                    = entityManager.GetComponentData<TickedWorldTransform>(entity).worldTransform;
                currentTransform.position                                                        += translation;
                currentTransform.rotation                                                         = math.normalize(math.mul(rotation, currentTransform.rotation));
                entityManager.SetComponentData(entity, new TickedWorldTransform { worldTransform  = currentTransform });
                return;
            }
            TranslateRotateTickedWorld(handle, translation, rotation, entityManager);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the ticked world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateTickedWorld(Entity entity, float3 translation, quaternion rotation, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position                    += translation;
                currentTransform.rotation                     = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            TranslateRotateTickedWorld(handle, translation, rotation, ref componentBroker);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the ticked world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateTickedWorld(Entity entity, float3 translation, quaternion rotation, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW             = componentBroker.GetRW<TickedWorldTransform>(entity, key);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position                    += translation;
                currentTransform.rotation                     = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            TranslateRotateTickedWorld(handle, translation, rotation, key, ref componentBroker);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TranslateRotateTickedWorld(EntityInHierarchyHandle handle, float3 translation, quaternion rotation, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateTickedWorld(EntityInHierarchyHandle handle, float3 translation, quaternion rotation, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref TickedComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TranslateRotateTickedWorld(EntityInHierarchyHandle handle,
                                                      float3 translation,
                                                      quaternion rotation,
                                                      TransformsKey key,
                                                      ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref TickedComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the ticked world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TranslateRotateTickedWorld(Entity entity,
                                                      float3 translation,
                                                      quaternion rotation,
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
                currentTransform.position                    += translation;
                currentTransform.rotation                     = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            TranslateRotateTickedWorld(handle, translation, rotation, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="entity">The entity to apply the deltas to the ticked world position and rotation for</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TranslateRotateTickedWorld(Entity entity,
                                                      float3 translation,
                                                      quaternion rotation,
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
                RefRW<TickedWorldTransform> refRW             = transformLookupRW.GetCheckedLookup(entity, key).GetRefRW(entity);
                TransformQvvs               currentTransform  = refRW.ValueRO.worldTransform;
                currentTransform.position                    += translation;
                currentTransform.rotation                     = math.normalize(math.mul(rotation, currentTransform.rotation));
                refRW.ValueRW.worldTransform                  = currentTransform;
                return;
            }
            TranslateRotateTickedWorld(handle, translation, rotation, ref transformLookupRW.GetCheckedLookup(handle.root.entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TranslateRotateTickedWorld(EntityInHierarchyHandle handle,
                                                      float3 translation,
                                                      quaternion rotation,
                                                      ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                                      ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands                                                                                       =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Moves and rotates the entity by the specified translation and rotation deltas in ticked world-space
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked world position and rotation should receive the deltas</param>
        /// <param name="translation">Translates by this position delta</param>
        /// <param name="rotation">Rotates by this rotation delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TranslateRotateTickedWorld(EntityInHierarchyHandle handle,
                                                      float3 translation,
                                                      quaternion rotation,
                                                      TransformsKey key,
                                                      ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                                      ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { new TransformQvvs { position = translation, rotation = rotation } };
            Span<Propagate.WriteCommand> commands                                                                                       =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.WorldPositionRotationDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

