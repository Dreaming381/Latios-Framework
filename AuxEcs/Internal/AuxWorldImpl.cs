using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal unsafe struct AuxWorldImpl : IDisposable
    {
        AllComponentsStore allComponentsStore;
        AllArchetypesStore allArchetypesStore;
        EntityStore        entityStore;

        public AuxWorldImpl(AllocatorManager.AllocatorHandle allocator)
        {
            allComponentsStore = new AllComponentsStore(allocator);
            allArchetypesStore = new AllArchetypesStore(allocator);
            entityStore        = new EntityStore(allocator);
        }

        public void Dispose()
        {
            allComponentsStore.Dispose();
            allArchetypesStore.Dispose();
            entityStore.Dispose();
        }

        public AllocatorManager.AllocatorHandle allocator => allArchetypesStore.allocator;

        public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
        {
            ref var componentStore = ref allComponentsStore.GetOrAddStore<T>(out int typeId);
            if (entityStore.TryGetLocation(entity, out var location))
            {
                // Entity already exists
                ref var archetype            = ref allArchetypesStore[location.archetypeIndex];
                var     typeIndexInArchetype = archetype.GetTypeIndexInArchetype(typeId);
                if (typeIndexInArchetype >= 0)
                {
                    // Type already exists. Replace it.
                    var indexInStore = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype)[typeIndexInArchetype];
                    var auxRef       = componentStore.GetRef<T>(indexInStore);
                    componentStore.Replace(indexInStore);
                    *auxRef.componentPtr = component;
                    return;
                }

                var       oldTypes   = archetype.typeIds;
                var       oldIndices = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype);
                Span<int> newTypes   = stackalloc int[oldTypes.Length + 1];
                Span<int> newIndices = stackalloc int[oldIndices.Length + 1];
                int       dst        = 0;
                for (int i = 0; i < newTypes.Length; i++)
                {
                    if (i == dst && (i == oldTypes.Length || oldTypes[i] > typeId))
                    {
                        newTypes[dst]        = typeId;
                        var newIndex         = componentStore.Add();
                        var auxRef           = componentStore.GetRef<T>(newIndex);
                        *auxRef.componentPtr = component;
                        newIndices[dst]      = newIndex;
                        dst++;
                    }
                    if (i < oldTypes.Length)
                    {
                        newTypes[dst]   = oldTypes[i];
                        newIndices[dst] = oldIndices[i];
                        dst++;
                    }
                }

                ChangeArchetype(ref archetype, entity, in location, newTypes, newIndices);
            }
            else
            {
                // New Entity
                Span<int> newTypes         = stackalloc int[1];
                newTypes[0]                = typeId;
                Span<int> newIndices       = stackalloc int[1];
                var       newIndex         = componentStore.Add();
                var       auxRef           = componentStore.GetRef<T>(newIndex);
                *auxRef.componentPtr       = component;
                newIndices[0]              = newIndex;
                var entityIndexInArchetype = allArchetypesStore.GetOrAddArchetype(newTypes, out var archetypeIndex).Add(
                    entity,
                    newIndices);
                entityStore.SetLocation(entity, new EntityStore.Location { archetypeIndex = archetypeIndex, indexInArchetype = entityIndexInArchetype });
            }
        }

        public void RemoveComponent<T>(Entity entity) where T : unmanaged
        {
            if (!entityStore.TryGetLocation(entity, out var location))
                return;

            var componentStore = allComponentsStore.TryGetStore<T>(out var typeId);
            if (componentStore == null)
                return;

            ref var archetype = ref allArchetypesStore[location.archetypeIndex];
            if (archetype.typeIds.Length == 1)
            {
                // If there's only one component left, and this is it, then remove the entity entirely
                if (archetype.typeIds[0] != typeId)
                    return;

                var indexInStore = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype)[0];
                componentStore->Remove(indexInStore);
                archetype.Remove(location.indexInArchetype);
                entityStore.Remove(entity);
                return;
            }

            var typeIndexInArchetype = archetype.GetTypeIndexInArchetype(typeId);
            if (typeIndexInArchetype < 0)
                return;

            var       oldTypes   = archetype.typeIds;
            var       oldIndices = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype);
            Span<int> newTypes   = stackalloc int[oldTypes.Length - 1];
            Span<int> newIndices = stackalloc int[oldIndices.Length - 1];
            oldTypes.Slice(0, typeIndexInArchetype).CopyTo(newTypes.Slice(0, typeIndexInArchetype));
            oldIndices.Slice(0, typeIndexInArchetype).CopyTo(newIndices.Slice(0, typeIndexInArchetype));
            if (typeIndexInArchetype + 1 < newTypes.Length)
            {
                var start     = typeIndexInArchetype + 1;
                var remainder = oldTypes.Length - start;
                oldTypes.Slice(start, remainder).CopyTo(newTypes.Slice(start - 1, remainder));
                oldIndices.Slice(start, remainder).CopyTo(newIndices.Slice(start - 1, remainder));
            }

            ChangeArchetype(ref archetype, entity, in location, newTypes, newIndices);
        }

        public void RemoveAllComponents(Entity entity)
        {
            if (!entityStore.TryGetLocation(entity, out var location))
                return;

            ref var archetype      = ref allArchetypesStore[location.archetypeIndex];
            var     typeIds        = archetype.typeIds;
            var     indicesInStore = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype);
            for (int i = 0; i < typeIds.Length; i++)
            {
                ref var componentStore = ref allComponentsStore[typeIds[i]];
                componentStore.Remove(indicesInStore[i]);
            }
            archetype.Remove(location.indexInArchetype);
            entityStore.Remove(entity);
            return;
        }

        public bool TryGetComponent<T>(Entity entity, out AuxRef<T> componentRef) where T : unmanaged
        {
            componentRef = default;
            if (!entityStore.TryGetLocation(entity, out var location))
                return false;
            var componentStore = allComponentsStore.TryGetStore<T>(out var typeId);
            if (componentStore == null)
                return false;

            ref var archetype            = ref allArchetypesStore[location.archetypeIndex];
            var     typeIndexInArchetype = archetype.GetTypeIndexInArchetype(typeId);
            if (typeIndexInArchetype >= 0)
            {
                // Type already exists. Replace it.
                var indexInStore = archetype.GetComponentIndicesForEntityIndex(location.indexInArchetype)[typeIndexInArchetype];
                componentRef     = componentStore->GetRef<T>(indexInStore);
                return true;
            }
            return false;
        }

        public AuxComponentEnumerator<T> AllOf<T>() where T : unmanaged
        {
            var componentStore = allComponentsStore.TryGetStore<T>(out _);
            return new AuxComponentEnumerator<T>(componentStore);
        }

        public AuxQueryEnumerator<T0> AllWith<T0>()
            where T0 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1> AllWith<T0, T1>()
            where T0 : unmanaged
            where T1 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2> AllWith<T0, T1, T2>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2, T3> AllWith<T0, T1, T2, T3>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2, T3>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4> AllWith<T0, T1, T2, T3, T4>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2, T3, T4>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5> AllWith<T0, T1, T2, T3, T4, T5>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2, T3, T4, T5>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6> AllWith<T0, T1, T2, T3, T4, T5, T6>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6>(archs, comps);
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6, T7> AllWith<T0, T1, T2, T3, T4, T5, T6, T7>()
            where T0 : unmanaged
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
            where T7 : unmanaged
        {
            fixed (AllArchetypesStore*  archs = &allArchetypesStore)
            fixed (AllComponentsStore * comps = &allComponentsStore)
            return new AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6, T7>(archs, comps);
        }

        void ChangeArchetype(ref ArchetypeStore archetype, Entity entity, in EntityStore.Location location, in ReadOnlySpan<int> newTypes, in ReadOnlySpan<int> newIndices)
        {
            var removeOp = archetype.Remove(location.indexInArchetype);
            if (removeOp.swappedBackEntity != Entity.Null)
                entityStore.SetLocation(removeOp.swappedBackEntity, new EntityStore.Location
                {
                    archetypeIndex   = location.archetypeIndex,
                    indexInArchetype = removeOp.newIndex
                });

            var entityIndexInArchetype = allArchetypesStore.GetOrAddArchetype(newTypes, out var archetypeIndex).Add(
                entity,
                newIndices);
            entityStore.SetLocation(entity, new EntityStore.Location { archetypeIndex = archetypeIndex, indexInArchetype = entityIndexInArchetype });
        }
    }
}

