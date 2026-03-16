#if !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        public static partial class Unsafe
        {
            /// <summary>
            /// Computes the local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the WorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The WorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="entityManager">The EntityManager which manages the entity</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs LocalTransformFrom(in EntityInHierarchyHandle handle,
                                                          in WorldTransform worldTransform,
                                                          EntityManager entityManager,
                                                          out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  in worldTransform,
                                  ref EntityManagerAccess.From(ref entityManager),
                                  ref EntityManagerAccess.From(ref entityManager),
                                  out parentTransform);

            /// <summary>
            /// Computes the local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the WorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The WorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="componentBroker">A ComponentBroker with read access to EntityInHierarchy, EntityInHierarchyCleanup, and WorldTransform</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs LocalTransformFrom(in EntityInHierarchyHandle handle,
                                                          in WorldTransform worldTransform,
                                                          ref ComponentBroker componentBroker,
                                                          out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  in worldTransform,
                                  ref ComponentBrokerAccess.From(ref componentBroker),
                                  ref ComponentBrokerAccess.From(ref componentBroker),
                                  out parentTransform);

            /// <summary>
            /// Computes the local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the WorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The WorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="key">A key to ensure the hierarchy is safe to access</param>
            /// <param name="componentBroker">A ComponentBroker with read access to EntityInHierarchy, EntityInHierarchyCleanup, and WorldTransform</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs LocalTransformFrom(in EntityInHierarchyHandle handle,
                                                          in WorldTransform worldTransform,
                                                          TransformsKey key,
                                                          ref ComponentBroker componentBroker,
                                                          out TransformQvvs parentTransform)
            {
                key.Validate(handle.root.entity);
                return LocalTransformFrom(in handle,
                                          in worldTransform,
                                          ref ComponentBrokerParallelAccess.From(ref componentBroker),
                                          ref ComponentBrokerParallelAccess.From(ref componentBroker),
                                          out parentTransform);
            }

            /// <summary>
            /// Computes the local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the WorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The WorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
            /// <param name="worldTransformLookupRO">A readonly ComponentLookup to the WorldTransform component</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs LocalTransformFrom(in EntityInHierarchyHandle handle,
                                                          in WorldTransform worldTransform,
                                                          EntityStorageInfoLookup entityStorageInfoLookup,
                                                          ref ComponentLookup<WorldTransform> worldTransformLookupRO,
                                                          out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  in worldTransform,
                                  ref EsilAlive.From(ref entityStorageInfoLookup),
                                  ref LookupWorldTransform.From(ref worldTransformLookupRO),
                                  out parentTransform);

            /// <summary>
            /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the TickedWorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="tickedWorldTransform">The TickedWorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="entityManager">The EntityManager which manages the entity</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs TickedLocalTransformFrom(in EntityInHierarchyHandle handle,
                                                                in TickedWorldTransform tickedWorldTransform,
                                                                EntityManager entityManager,
                                                                out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  tickedWorldTransform.ToUnticked(),
                                  ref TickedEntityManagerAccess.From(ref entityManager),
                                  ref TickedEntityManagerAccess.From(ref entityManager),
                                  out parentTransform);

            /// <summary>
            /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the TickedWorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The TickedWorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="componentBroker">A ComponentBroker with read access to EntityInHierarchy, EntityInHierarchyCleanup, and WorldTransform</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs TickedLocalTransformFrom(in EntityInHierarchyHandle handle,
                                                                in WorldTransform worldTransform,
                                                                ref ComponentBroker componentBroker,
                                                                out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  in worldTransform,
                                  ref TickedComponentBrokerAccess.From(ref componentBroker),
                                  ref TickedComponentBrokerAccess.From(ref componentBroker),
                                  out parentTransform);

            /// <summary>
            /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the TickedWorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="worldTransform">The TickedWorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="key">A key to ensure the hierarchy is safe to access</param>
            /// <param name="componentBroker">A ComponentBroker with read access to EntityInHierarchy, EntityInHierarchyCleanup, and WorldTransform</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs TickedLocalTransformFrom(in EntityInHierarchyHandle handle,
                                                                in WorldTransform worldTransform,
                                                                TransformsKey key,
                                                                ref ComponentBroker componentBroker,
                                                                out TransformQvvs parentTransform)
            {
                key.Validate(handle.root.entity);
                return LocalTransformFrom(in handle,
                                          in worldTransform,
                                          ref TickedComponentBrokerParallelAccess.From(ref componentBroker),
                                          ref TickedComponentBrokerParallelAccess.From(ref componentBroker),
                                          out parentTransform);
            }

            /// <summary>
            /// Computes the ticked local transform of the entity. If the entity does not have a parent, then this
            /// is identical to the TickedWorldTransform without stretch.
            /// </summary>
            /// <param name="handle">The handle of the entity within the hierarchy, often obtained from the RootReference component</param>
            /// <param name="tickedWorldTransform">The TickedWorldTransform of the entity possessing the RootReference, obtained from chunk iteration</param>
            /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup from the same world the hierarchy belongs to</param>
            /// <param name="tickedWorldTransformLookupRO">A readonly ComponentLookup to the TickedWorldTransform component</param>
            /// <param name="parentTransform">The parent's world-space transform for convenience, identity if there was no parent</param>
            /// <returns>The local position, rotation, and uniform scale of the entity</returns>
            public static TransformQvs TickedLocalTransformFrom(in EntityInHierarchyHandle handle,
                                                                in TickedWorldTransform tickedWorldTransform,
                                                                EntityStorageInfoLookup entityStorageInfoLookup,
                                                                ref ComponentLookup<TickedWorldTransform> tickedWorldTransformLookupRO,
                                                                out TransformQvvs parentTransform)
            => LocalTransformFrom(in handle,
                                  tickedWorldTransform.ToUnticked(),
                                  ref EsilAlive.From(ref entityStorageInfoLookup),
                                  ref LookupTickedWorldTransform.From(ref tickedWorldTransformLookupRO),
                                  out parentTransform);

            internal static TransformQvs LocalTransformFrom<TAlive, TWorld>(in EntityInHierarchyHandle handle,
                                                                            in WorldTransform worldTransform,
                                                                            ref TAlive alive,
                                                                            ref TWorld worldTransformLookupRO,
                                                                            out TransformQvvs parentTransform) where TAlive : unmanaged, IAlive where TWorld : unmanaged,
            IWorldTransform
            {
                var parentHandle = handle.FindParent(ref alive);
                if (!parentHandle.isNull)
                {
                    parentTransform = worldTransformLookupRO.GetWorldTransform(parentHandle.entity).worldTransform;
                    return WorldLocalOps.GetLocalTransformRO(in parentTransform, in worldTransform.worldTransform, in parentHandle, in handle, worldTransformLookupRO.isTicked);
                }
                parentTransform = TransformQvvs.identity;
                return new TransformQvs(worldTransform.position, worldTransform.rotation, worldTransform.scale);
            }
        }
    }
}
#endif

