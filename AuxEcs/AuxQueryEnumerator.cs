using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0>
        where T0 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0)
            {
                entity = this.entity;
                c0     = this.c0;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        int                 typeId0;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;

            valid = componentStore0 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1>
        where T0 : unmanaged
        where T1 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        int                 typeId0;
        int                 typeId1;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2, T3>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;
            public AuxRef<T3> c3;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2,
                                    out AuxRef<T3> c3)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
                c3     = this.c3;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        ComponentStore*     componentStore3;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;
        int                 typeId3;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        int  typeIndex3;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            componentStore3         = allComponentsStore->TryGetStore<T3>(out typeId3);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;
            typeIndex3              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null &&
                    componentStore3 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
                typeId3,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2, T3> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                    typeId3,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        typeIndex3       = typeIndicesInArchetype[3];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                tuple.c3             = componentStore3->GetRef<T3>(componentIndices[typeIndex3]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2, T3, T4>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;
            public AuxRef<T3> c3;
            public AuxRef<T4> c4;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2,
                                    out AuxRef<T3> c3,
                                    out AuxRef<T4> c4)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
                c3     = this.c3;
                c4     = this.c4;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        ComponentStore*     componentStore3;
        ComponentStore*     componentStore4;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;
        int                 typeId3;
        int                 typeId4;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        int  typeIndex3;
        int  typeIndex4;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            componentStore3         = allComponentsStore->TryGetStore<T3>(out typeId3);
            componentStore4         = allComponentsStore->TryGetStore<T4>(out typeId4);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;
            typeIndex3              = -1;
            typeIndex4              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null &&
                    componentStore3 != null &&
                    componentStore4 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
                typeId3,
                typeId4,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                    typeId3,
                    typeId4,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        typeIndex3       = typeIndicesInArchetype[3];
                        typeIndex4       = typeIndicesInArchetype[4];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                tuple.c3             = componentStore3->GetRef<T3>(componentIndices[typeIndex3]);
                tuple.c4             = componentStore4->GetRef<T4>(componentIndices[typeIndex4]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2, T3, T4, T5>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
        where T5 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;
            public AuxRef<T3> c3;
            public AuxRef<T4> c4;
            public AuxRef<T5> c5;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2,
                                    out AuxRef<T3> c3,
                                    out AuxRef<T4> c4,
                                    out AuxRef<T5> c5)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
                c3     = this.c3;
                c4     = this.c4;
                c5     = this.c5;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        ComponentStore*     componentStore3;
        ComponentStore*     componentStore4;
        ComponentStore*     componentStore5;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;
        int                 typeId3;
        int                 typeId4;
        int                 typeId5;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        int  typeIndex3;
        int  typeIndex4;
        int  typeIndex5;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            componentStore3         = allComponentsStore->TryGetStore<T3>(out typeId3);
            componentStore4         = allComponentsStore->TryGetStore<T4>(out typeId4);
            componentStore5         = allComponentsStore->TryGetStore<T5>(out typeId5);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;
            typeIndex3              = -1;
            typeIndex4              = -1;
            typeIndex5              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null &&
                    componentStore3 != null &&
                    componentStore4 != null &&
                    componentStore5 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
                typeId3,
                typeId4,
                typeId5,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                    typeId3,
                    typeId4,
                    typeId5,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        typeIndex3       = typeIndicesInArchetype[3];
                        typeIndex4       = typeIndicesInArchetype[4];
                        typeIndex5       = typeIndicesInArchetype[5];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                tuple.c3             = componentStore3->GetRef<T3>(componentIndices[typeIndex3]);
                tuple.c4             = componentStore4->GetRef<T4>(componentIndices[typeIndex4]);
                tuple.c5             = componentStore5->GetRef<T5>(componentIndices[typeIndex5]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
        where T5 : unmanaged
        where T6 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;
            public AuxRef<T3> c3;
            public AuxRef<T4> c4;
            public AuxRef<T5> c5;
            public AuxRef<T6> c6;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2,
                                    out AuxRef<T3> c3,
                                    out AuxRef<T4> c4,
                                    out AuxRef<T5> c5,
                                    out AuxRef<T6> c6)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
                c3     = this.c3;
                c4     = this.c4;
                c5     = this.c5;
                c6     = this.c6;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        ComponentStore*     componentStore3;
        ComponentStore*     componentStore4;
        ComponentStore*     componentStore5;
        ComponentStore*     componentStore6;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;
        int                 typeId3;
        int                 typeId4;
        int                 typeId5;
        int                 typeId6;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        int  typeIndex3;
        int  typeIndex4;
        int  typeIndex5;
        int  typeIndex6;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            componentStore3         = allComponentsStore->TryGetStore<T3>(out typeId3);
            componentStore4         = allComponentsStore->TryGetStore<T4>(out typeId4);
            componentStore5         = allComponentsStore->TryGetStore<T5>(out typeId5);
            componentStore6         = allComponentsStore->TryGetStore<T6>(out typeId6);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;
            typeIndex3              = -1;
            typeIndex4              = -1;
            typeIndex5              = -1;
            typeIndex6              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null &&
                    componentStore3 != null &&
                    componentStore4 != null &&
                    componentStore5 != null &&
                    componentStore6 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
                typeId3,
                typeId4,
                typeId5,
                typeId6,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                    typeId3,
                    typeId4,
                    typeId5,
                    typeId6,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        typeIndex3       = typeIndicesInArchetype[3];
                        typeIndex4       = typeIndicesInArchetype[4];
                        typeIndex5       = typeIndicesInArchetype[5];
                        typeIndex6       = typeIndicesInArchetype[6];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                tuple.c3             = componentStore3->GetRef<T3>(componentIndices[typeIndex3]);
                tuple.c4             = componentStore4->GetRef<T4>(componentIndices[typeIndex4]);
                tuple.c5             = componentStore5->GetRef<T5>(componentIndices[typeIndex5]);
                tuple.c6             = componentStore6->GetRef<T6>(componentIndices[typeIndex6]);
                return true;
            }
        }
    }

    /// <summary>
    /// An enumerator that iterates entities containing multiple component types. The enumerator remains valid
    /// even with archetype changes. However archetype changes applied to entities within the query may cause
    /// some matching entities to be skipped.
    /// </summary>
    public unsafe struct AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6, T7>
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
        where T4 : unmanaged
        where T5 : unmanaged
        where T6 : unmanaged
        where T7 : unmanaged
    {
        /// <summary>
        /// A collection of AuxRef instances for entities matching a query at the time of creation.
        /// </summary>
        public struct Tuple
        {
            public Entity     entity;
            public AuxRef<T0> c0;
            public AuxRef<T1> c1;
            public AuxRef<T2> c2;
            public AuxRef<T3> c3;
            public AuxRef<T4> c4;
            public AuxRef<T5> c5;
            public AuxRef<T6> c6;
            public AuxRef<T7> c7;

            public void Deconstruct(out Entity entity,
                                    out AuxRef<T0> c0,
                                    out AuxRef<T1> c1,
                                    out AuxRef<T2> c2,
                                    out AuxRef<T3> c3,
                                    out AuxRef<T4> c4,
                                    out AuxRef<T5> c5,
                                    out AuxRef<T6> c6,
                                    out AuxRef<T7> c7)
            {
                entity = this.entity;
                c0     = this.c0;
                c1     = this.c1;
                c2     = this.c2;
                c3     = this.c3;
                c4     = this.c4;
                c5     = this.c5;
                c6     = this.c6;
                c7     = this.c7;
            }
        }

        Tuple               tuple;
        AllArchetypesStore* allArchetypesStore;
        ComponentStore*     componentStore0;
        ComponentStore*     componentStore1;
        ComponentStore*     componentStore2;
        ComponentStore*     componentStore3;
        ComponentStore*     componentStore4;
        ComponentStore*     componentStore5;
        ComponentStore*     componentStore6;
        ComponentStore*     componentStore7;
        int                 typeId0;
        int                 typeId1;
        int                 typeId2;
        int                 typeId3;
        int                 typeId4;
        int                 typeId5;
        int                 typeId6;
        int                 typeId7;

        int  archetypeIndex;
        int  indexInArchetype;
        int  typeIndex0;
        int  typeIndex1;
        int  typeIndex2;
        int  typeIndex3;
        int  typeIndex4;
        int  typeIndex5;
        int  typeIndex6;
        int  typeIndex7;
        bool valid;

        internal AuxQueryEnumerator(AllArchetypesStore* allArchetypesStore, AllComponentsStore* allComponentsStore)
        {
            tuple                   = default;
            this.allArchetypesStore = allArchetypesStore;
            componentStore0         = allComponentsStore->TryGetStore<T0>(out typeId0);
            componentStore1         = allComponentsStore->TryGetStore<T1>(out typeId1);
            componentStore2         = allComponentsStore->TryGetStore<T2>(out typeId2);
            componentStore3         = allComponentsStore->TryGetStore<T3>(out typeId3);
            componentStore4         = allComponentsStore->TryGetStore<T4>(out typeId4);
            componentStore5         = allComponentsStore->TryGetStore<T5>(out typeId5);
            componentStore6         = allComponentsStore->TryGetStore<T6>(out typeId6);
            componentStore7         = allComponentsStore->TryGetStore<T7>(out typeId7);
            archetypeIndex          = -1;
            indexInArchetype        = -1;
            typeIndex0              = -1;
            typeIndex1              = -1;
            typeIndex2              = -1;
            typeIndex3              = -1;
            typeIndex4              = -1;
            typeIndex5              = -1;
            typeIndex6              = -1;
            typeIndex7              = -1;

            valid = componentStore0 != null &&
                    componentStore1 != null &&
                    componentStore2 != null &&
                    componentStore3 != null &&
                    componentStore4 != null &&
                    componentStore5 != null &&
                    componentStore6 != null &&
                    componentStore7 != null;
        }

        /// <summary>
        /// Counts the number of entities matching the query currently.
        /// </summary>
        /// <returns></returns>
        public int Count()
        {
            if (!valid)
                return 0;
            int       result         = 0;
            Span<int> typeIdsToMatch = stackalloc int[]
            {
                typeId0,
                typeId1,
                typeId2,
                typeId3,
                typeId4,
                typeId5,
                typeId6,
                typeId7,
            };
            for (int i = 0; i < allArchetypesStore->archetypesCount; i++)
            {
                ref var archetype = ref (*allArchetypesStore)[i];
                if (archetype.instanceCount == 0)
                    continue;
                if (archetype.Matches(typeIdsToMatch))
                    result += archetype.instanceCount;
            }
            return result;
        }

        public AuxQueryEnumerator<T0, T1, T2, T3, T4, T5, T6, T7> GetEnumerator() => this;

        public Tuple Current => tuple;

        public bool MoveNext()
        {
            if (!valid)
                return false;

            if (archetypeIndex < 0 || indexInArchetype + 1 >= (*allArchetypesStore)[archetypeIndex].instanceCount)
            {
                // Find next matching archetype
                Span<int> typeIdsToMatch = stackalloc int[]
                {
                    typeId0,
                    typeId1,
                    typeId2,
                    typeId3,
                    typeId4,
                    typeId5,
                    typeId6,
                    typeId7,
                };
                Span<int> typeIndicesInArchetype = stackalloc int[typeIdsToMatch.Length];
                bool      foundArchetype         = false;
                while (archetypeIndex + 1 < allArchetypesStore->archetypesCount)
                {
                    archetypeIndex++;
                    ref var archetype = ref (*allArchetypesStore)[archetypeIndex];
                    if (archetype.instanceCount == 0)
                        continue;
                    if (archetype.TryMatch(typeIdsToMatch, typeIndicesInArchetype))
                    {
                        typeIndex0       = typeIndicesInArchetype[0];
                        typeIndex1       = typeIndicesInArchetype[1];
                        typeIndex2       = typeIndicesInArchetype[2];
                        typeIndex3       = typeIndicesInArchetype[3];
                        typeIndex4       = typeIndicesInArchetype[4];
                        typeIndex5       = typeIndicesInArchetype[5];
                        typeIndex6       = typeIndicesInArchetype[6];
                        typeIndex7       = typeIndicesInArchetype[7];
                        indexInArchetype = -1;
                        foundArchetype   = true;
                        break;
                    }
                }
                if (!foundArchetype)
                    return false;
            }

            {
                indexInArchetype++;
                ref var archetype = ref (*allArchetypesStore)[archetypeIndex];

                tuple.entity         = archetype.GetEntity(indexInArchetype);
                var componentIndices = archetype.GetComponentIndicesForEntityIndex(indexInArchetype);
                tuple.c0             = componentStore0->GetRef<T0>(componentIndices[typeIndex0]);
                tuple.c1             = componentStore1->GetRef<T1>(componentIndices[typeIndex1]);
                tuple.c2             = componentStore2->GetRef<T2>(componentIndices[typeIndex2]);
                tuple.c3             = componentStore3->GetRef<T3>(componentIndices[typeIndex3]);
                tuple.c4             = componentStore4->GetRef<T4>(componentIndices[typeIndex4]);
                tuple.c5             = componentStore5->GetRef<T5>(componentIndices[typeIndex5]);
                tuple.c6             = componentStore6->GetRef<T6>(componentIndices[typeIndex6]);
                tuple.c7             = componentStore7->GetRef<T7>(componentIndices[typeIndex7]);
                return true;
            }
        }
    }
}

