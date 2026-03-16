#if !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Entities;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Apply Local Transform Inverse Delta
        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TransformLocalInverse(Entity entity, in TransformQvvs appliedTransform, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                             = entityManager.GetComponentData<WorldTransform>(entity).worldTransform;
                TransformQvvs newTransform                                                 = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                entityManager.SetComponentData(entity, new WorldTransform { worldTransform = newTransform });
                return;
            }
            TransformLocalInverse(handle, appliedTransform, entityManager);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformLocalInverse(Entity entity, in TransformQvvs appliedTransform, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW            = componentBroker.GetRW<WorldTransform>(entity);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs         newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform           = newTransform;
                return;
            }
            TransformLocalInverse(handle, appliedTransform, ref componentBroker);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformLocalInverse(Entity entity, in TransformQvvs appliedTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<WorldTransform> refRW            = componentBroker.GetRW<WorldTransform>(entity, key);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs         newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform           = newTransform;
                return;
            }
            TransformLocalInverse(handle, appliedTransform, ref componentBroker);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TransformLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new EntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to WorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TransformLocalInverse(Entity entity,
                                                 in TransformQvvs appliedTransform,
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
                TransformQvvs         newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform           = newTransform;
                return;
            }
            TransformLocalInverse(handle, in appliedTransform, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform, and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TransformLocalInverse(Entity entity,
                                                 in TransformQvvs appliedTransform,
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
                RefRW<WorldTransform> refRW            = transformLookupRW.GetCheckedLookup(handle.root.entity, key).GetRefRW(entity);
                TransformQvvs         currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs         newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform           = newTransform;
                return;
            }
            TransformLocalInverse(handle, in appliedTransform, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TransformLocalInverse(EntityInHierarchyHandle handle,
                                                 in TransformQvvs appliedTransform,
                                                 ref ComponentLookup<WorldTransform> transformLookupRW,
                                                 ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Multiplies the entity's current local transform by the inverse of the specified transform and sets the entity's
        /// local transform to the result. The expression is as follows:
        /// localTransform = qvvs.inversemulqvvs(appliedTransform, localTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TransformLocalInverse(EntityInHierarchyHandle handle,
                                                 in TransformQvvs appliedTransform,
                                                 TransformsKey key,
                                                 ref TransformsComponentLookup<WorldTransform> transformLookupRW,
                                                 ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion

        #region Apply Ticked Local Transform Inverse Delta
        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the ticked local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TransformTickedLocalInverse(Entity entity, in TransformQvvs appliedTransform, EntityManager entityManager)
        {
            var handle = GetHierarchyHandle(entity, entityManager);
            if (handle.isNull)
            {
                TransformQvvs currentTransform                                                   = entityManager.GetComponentData<TickedWorldTransform>(entity).worldTransform;
                TransformQvvs newTransform                                                       = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                entityManager.SetComponentData(entity, new TickedWorldTransform { worldTransform = newTransform });
                return;
            }
            TransformTickedLocalInverse(handle, appliedTransform, entityManager);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the ticked local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformTickedLocalInverse(Entity entity, in TransformQvvs appliedTransform, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW            = componentBroker.GetRW<TickedWorldTransform>(entity);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs               newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform                 = newTransform;
                return;
            }
            TransformTickedLocalInverse(handle, appliedTransform, ref componentBroker);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the ticked local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformTickedLocalInverse(Entity entity, in TransformQvvs appliedTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            var handle = GetHierarchyHandle(entity, ref componentBroker);
            if (handle.isNull)
            {
                RefRW<TickedWorldTransform> refRW            = componentBroker.GetRW<TickedWorldTransform>(entity, key);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs               newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform                 = newTransform;
                return;
            }
            TransformTickedLocalInverse(handle, appliedTransform, ref componentBroker);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="entityManager">The EntityManager used to perform the write operations</param>
        public static void TransformTickedLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, EntityManager entityManager)
        {
            if (handle.isCopyParent)
                return;
            var                          lookup     = new TickedEntityManagerAccess(entityManager);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformTickedLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            ref var                      lookup     = ref ComponentBrokerAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="componentBroker">A ComponentBroker with write access to TickedWorldTransform and read access to RootReference, EntityInHierarchy, and EntityInHierarchyCleanup</param>
        public static void TransformTickedLocalInverse(EntityInHierarchyHandle handle, in TransformQvvs appliedTransform, TransformsKey key, ref ComponentBroker componentBroker)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            ref var                      lookup     = ref ComponentBrokerParallelAccess.From(ref componentBroker);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref lookup, ref lookup);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the ticked local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TransformTickedLocalInverse(Entity entity,
                                                       in TransformQvvs appliedTransform,
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
                TransformQvvs               newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform                 = newTransform;
                return;
            }
            TransformTickedLocalInverse(handle, in appliedTransform, ref transformLookupRW, ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="entity">The entity to apply the inverse delta to the ticked local transform for</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        public static void TransformTickedLocalInverse(Entity entity,
                                                       in TransformQvvs appliedTransform,
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
                RefRW<TickedWorldTransform> refRW            = transformLookupRW.GetCheckedLookup(handle.root.entity, key).GetRefRW(entity);
                TransformQvvs               currentTransform = refRW.ValueRO.worldTransform;
                TransformQvvs               newTransform     = qvvs.inversemulqvvsclean(appliedTransform, currentTransform);
                refRW.ValueRW.worldTransform                 = newTransform;
                return;
            }
            TransformTickedLocalInverse(handle, in appliedTransform, ref transformLookupRW.GetCheckedLookup(entity, key), ref entityStorageInfoLookup);
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform, and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="transformLookupRW">A write-accessible ComponentLookup. Writing to multiple entities within the same hierarchy from different threads is not safe!</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TransformTickedLocalInverse(EntityInHierarchyHandle handle,
                                                       in TransformQvvs appliedTransform,
                                                       ref ComponentLookup<TickedWorldTransform> transformLookupRW,
                                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands, ref LookupTickedWorldTransform.From(ref transformLookupRW),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }

        /// <summary>
        /// Multiplies the entity's current ticked local transform by the inverse of the specified transform and sets the entity's
        /// ticked local transform to the result. The expression is as follows:
        /// tickedLocalTransform = qvvs.inversemulqvvs(appliedTransform, tickedLocalTransform)
        /// </summary>
        /// <param name="handle">The hierarchy handle representing the entity whose ticked local transform should receive the inverse delta</param>
        /// <param name="appliedTransform">Translates by the inverse of this transform delta</param>
        /// <param name="key">A key to ensure the hierarchy is safe to access</param>
        /// <param name="transformLookupRW">A TransformsComponentLookup for parallel write access when the hierarchy is safe to access</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        public static void TransformTickedLocalInverse(EntityInHierarchyHandle handle,
                                                       in TransformQvvs appliedTransform,
                                                       TransformsKey key,
                                                       ref TransformsComponentLookup<TickedWorldTransform> transformLookupRW,
                                                       ref EntityStorageInfoLookup entityStorageInfoLookup)
        {
            if (handle.isCopyParent)
                return;
            key.Validate(handle.root.entity);
            Span<TransformQvvs>          transforms = stackalloc TransformQvvs[] { appliedTransform };
            Span<Propagate.WriteCommand> commands   =
                stackalloc Propagate.WriteCommand[] { new Propagate.WriteCommand
                                                      {
                                                          indexInHierarchy = handle.indexInHierarchy,
                                                          writeType        = Propagate.WriteCommand.WriteType.LocalInverseTransformDelta
                                                      } };
            Propagate.WriteAndPropagate(handle.m_hierarchy, handle.m_extraHierarchy, transforms, commands,
                                        ref LookupTickedWorldTransform.From(ref transformLookupRW.GetCheckedLookup(handle.root.entity, key)),
                                        ref EsilAlive.From(ref entityStorageInfoLookup));
        }
        #endregion
    }
}
#endif

