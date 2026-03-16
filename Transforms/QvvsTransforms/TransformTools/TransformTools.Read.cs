#if !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        /// <summary>
        /// Computes the local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the WorldTransform without stretch.
        /// </summary>
        /// <param name="handle">The hierarchy handle to compute the local transform for.</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="worldTransformLookupRO">A readonly ComponentLookup to the WorldTransform component</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs LocalTransformFrom(in EntityInHierarchyHandle handle,
                                                      EntityStorageInfoLookup entityStorageInfoLookup,
                                                      ref ComponentLookup<WorldTransform> worldTransformLookupRO,
                                                      out TransformQvvs parentTransform)
        {
            var worldTransform = worldTransformLookupRO[handle.entity];
            return Unsafe.LocalTransformFrom(in handle, in worldTransform, entityStorageInfoLookup, ref worldTransformLookupRO, out parentTransform);
        }

        /// <summary>
        /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the TickedWorldTransform without stretch.
        /// </summary>
        /// <param name="handle">The hierarchy handle to compute the local transform for.</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="tickedWorldTransformLookupRO">A readonly ComponentLookup to the TickedWorldTransform component</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs TickedLocalTransformFrom(in EntityInHierarchyHandle handle,
                                                            EntityStorageInfoLookup entityStorageInfoLookup,
                                                            ref ComponentLookup<TickedWorldTransform> tickedWorldTransformLookupRO,
                                                            out TransformQvvs parentTransform)
        {
            var worldTransform = tickedWorldTransformLookupRO[handle.entity];
            return Unsafe.TickedLocalTransformFrom(in handle, in worldTransform, entityStorageInfoLookup, ref tickedWorldTransformLookupRO, out parentTransform);
        }

        /// <summary>
        /// Computes the local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the WorldTransform without stretch.
        /// </summary>
        /// <param name="entity">The entity to compute the local transform for.</param>
        /// <param name="entityManager">The EntityManager which manages the entity</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs LocalTransformFrom(Entity entity, EntityManager entityManager, out TransformQvvs parentTransform)
        {
            ref var access = ref EntityManagerAccess.From(ref entityManager);
            return LocalTransformFrom(entity, ref access, ref access, ref access, out parentTransform);
        }

        /// <summary>
        /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the TickedWorldTransform without stretch.
        /// </summary>
        /// <param name="entity">The entity to compute the local transform for.</param>
        /// <param name="entityManager">The EntityManager which manages the entity</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs TickedLocalTransformFrom(Entity entity, EntityManager entityManager, out TransformQvvs parentTransform)
        {
            ref var access = ref TickedEntityManagerAccess.From(ref entityManager);
            return LocalTransformFrom(entity, ref access, ref access, ref access, out parentTransform);
        }

        /// <summary>
        /// Computes the local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the WorldTransform without stretch.
        /// </summary>
        /// <param name="entity">The entity to compute the local transform for.</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="rootReferenceLookupRO">A readonly ComponentLookup to the RootReference component</param>
        /// <param name="worldTransformLookupRO">A readonly ComponentLookup to the WorldTransform component</param>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs LocalTransformFrom(Entity entity,
                                                      EntityStorageInfoLookup entityStorageInfoLookup,
                                                      ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                                      ref ComponentLookup<WorldTransform>        worldTransformLookupRO,
                                                      ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                      ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO,
                                                      out TransformQvvs parentTransform)
        {
            var hierarchy = new LookupHierarchy(rootReferenceLookupRO, entityInHierarchyLookupRO, entityInHierarchyCleanupLookupRO);
            var result    = LocalTransformFrom(entity,
                                               ref EsilAlive.From(ref entityStorageInfoLookup),
                                               ref LookupWorldTransform.From(ref worldTransformLookupRO),
                                               ref hierarchy,
                                               out parentTransform);
            hierarchy.WriteBack(ref rootReferenceLookupRO, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            return result;
        }

        /// <summary>
        /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
        /// is identical to the TickedWorldTransform without stretch.
        /// </summary>
        /// <param name="entity">The entity to compute the local transform for.</param>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
        /// <param name="tickedRootReferenceLookupRO">A readonly ComponentLookup to the TickedRootReference component</param>
        /// <param name="tickedWorldTransformLookupRO">A readonly ComponentLookup to the TickedWorldTransform component</param>
        /// <param name="tickedEntityInHierarchyLookupRO">A readonly BufferLookup to the TickedEntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the TickedEntityInHierarchyCleanup dynamic buffer</param>
        /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
        /// <returns>The local position, rotation, and uniform scale of the entity</returns>
        public static TransformQvs TickedLocalTransformFrom(Entity entity,
                                                            EntityStorageInfoLookup entityStorageInfoLookup,
                                                            ref ComponentLookup<RootReference>         tickedRootReferenceLookupRO,
                                                            ref ComponentLookup<TickedWorldTransform>  tickedWorldTransformLookupRO,
                                                            ref BufferLookup<EntityInHierarchy>        tickedEntityInHierarchyLookupRO,
                                                            ref BufferLookup<EntityInHierarchyCleanup> tickedEntityInHierarchyCleanupLookupRO,
                                                            out TransformQvvs parentTransform)
        {
            var hierarchy = new LookupHierarchy(tickedRootReferenceLookupRO, tickedEntityInHierarchyLookupRO, tickedEntityInHierarchyCleanupLookupRO);
            var result    = LocalTransformFrom(entity,
                                               ref EsilAlive.From(ref entityStorageInfoLookup),
                                               ref LookupTickedWorldTransform.From(ref tickedWorldTransformLookupRO),
                                               ref hierarchy,
                                               out parentTransform);
            hierarchy.WriteBack(ref tickedRootReferenceLookupRO, ref tickedEntityInHierarchyLookupRO, ref tickedEntityInHierarchyCleanupLookupRO);
            return result;
        }

        internal static TransformQvs LocalTransformFrom<TAlive, TWorld>(in EntityInHierarchyHandle handle,
                                                                        ref TAlive alive,
                                                                        ref TWorld world,
                                                                        out TransformQvvs parentTransform)
            where TAlive : unmanaged, IAlive where TWorld : unmanaged, IWorldTransform
        {
            var worldTransform = world.GetWorldTransform(handle.entity);
            return Unsafe.LocalTransformFrom(handle, in worldTransform, ref alive, ref world, out parentTransform);
        }

        internal static TransformQvs LocalTransformFrom<TAlive, TWorld, THierarchy>(Entity entity,
                                                                                    ref TAlive alive,
                                                                                    ref TWorld world,
                                                                                    ref THierarchy hierarchy,
                                                                                    out TransformQvvs parentTransform)
            where TAlive : unmanaged, IAlive where TWorld : unmanaged, IWorldTransform where THierarchy : unmanaged, IHierarchy
        {
            var worldTransform = world.GetWorldTransform(entity);
            if (hierarchy.TryGetRootReference(entity, out var rootRef))
            {
                var handle       = rootRef.ToHandle(ref hierarchy);
                var parentHandle = handle.FindParent(ref alive);
                if (!parentHandle.isNull)
                {
                    parentTransform = world.GetWorldTransform(parentHandle.entity).worldTransform;
                    return WorldLocalOps.GetLocalTransformRO(in parentTransform, in worldTransform.worldTransform, in parentHandle, in handle, world.isTicked);
                }
            }
            parentTransform = TransformQvvs.identity;
            return new TransformQvs(worldTransform.position, worldTransform.rotation, worldTransform.scale);
        }

        internal static TransformQvvs ParentTransformFrom<TAlive, TWorld>(in EntityInHierarchyHandle handle,
                                                                          ref TAlive alive,
                                                                          ref TWorld world,
                                                                          out EntityInHierarchyHandle parentHandle)
            where TAlive : unmanaged, IAlive where TWorld : unmanaged, IWorldTransform
        {
            parentHandle = handle.FindParent(ref alive);
            if (!parentHandle.isNull)
                return world.GetWorldTransform(parentHandle.entity).worldTransform;
            return TransformQvvs.identity;
        }
    }
}
#endif

