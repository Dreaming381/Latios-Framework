﻿using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities;

namespace Latios
{
    public static class FluentQueryStarters
    {
        /// <summary>
        /// Starts the construction of an EntityQuery
        /// </summary>
        internal static unsafe FluentQuery Fluent(this ILatiosSystem system)
        {
            var                b        = system as SystemBase;
            fixed(SystemState* statePtr = &b.CheckedStateRef)
            return new FluentQuery
            {
                m_all                  = new NativeList<ComponentType>(Allocator.TempJob),
                m_any                  = new NativeList<ComponentType>(Allocator.TempJob),
                m_none                 = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyIfNotExcluded     = new NativeList<ComponentType>(Allocator.TempJob),
                m_allWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyIfNotExcludedWeak = new NativeList<ComponentType>(Allocator.TempJob),
                m_targetState          = statePtr,
                m_targetManager        = default,
                m_anyIsSatisfiedByAll  = false,
                m_options              = EntityQueryOptions.Default
            };
        }

        /// <summary>
        /// Starts the construction of an EntityQuery
        /// </summary>
        public static FluentQuery Fluent(this EntityManager em)
        {
            return new FluentQuery
            {
                m_all                  = new NativeList<ComponentType>(Allocator.TempJob),
                m_any                  = new NativeList<ComponentType>(Allocator.TempJob),
                m_none                 = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyIfNotExcluded     = new NativeList<ComponentType>(Allocator.TempJob),
                m_allWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyIfNotExcludedWeak = new NativeList<ComponentType>(Allocator.TempJob),
                m_targetState          = default,
                m_targetManager        = em,
                m_anyIsSatisfiedByAll  = false,
                m_options              = EntityQueryOptions.Default
            };
        }

        /// <summary>
        /// Starts the construction of an EntityQuery
        /// </summary>
        public static unsafe FluentQuery Fluent(this ref SystemState state)
        {
            fixed (SystemState* statePtr = &state)
            {
                return new FluentQuery
                {
                    m_all                  = new NativeList<ComponentType>(Allocator.TempJob),
                    m_any                  = new NativeList<ComponentType>(Allocator.TempJob),
                    m_none                 = new NativeList<ComponentType>(Allocator.TempJob),
                    m_anyIfNotExcluded     = new NativeList<ComponentType>(Allocator.TempJob),
                    m_allWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                    m_anyWeak              = new NativeList<ComponentType>(Allocator.TempJob),
                    m_anyIfNotExcludedWeak = new NativeList<ComponentType>(Allocator.TempJob),
                    m_targetState          = statePtr,
                    m_targetManager        = default,
                    m_anyIsSatisfiedByAll  = false,
                    m_options              = EntityQueryOptions.Default
                };
            }
        }
    }

    /// <summary>
    /// A Fluent builder object for creating EntityQuery instances.
    /// </summary>
    public unsafe struct FluentQuery
    {
        internal NativeList<ComponentType> m_all;
        internal NativeList<ComponentType> m_any;
        internal NativeList<ComponentType> m_none;
        internal NativeList<ComponentType> m_anyIfNotExcluded;
        internal NativeList<ComponentType> m_allWeak;
        internal NativeList<ComponentType> m_anyWeak;
        internal NativeList<ComponentType> m_anyIfNotExcludedWeak;

        internal SystemState*  m_targetState;
        internal EntityManager m_targetManager;

        internal bool m_anyIsSatisfiedByAll;

        internal EntityQueryOptions m_options;

        /// <summary>
        /// Adds a required component to the query with the specified access
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Is the component a chunk component for the query?</param>
        public FluentQuery WithAll<T>(bool readOnly = false, bool isChunkComponent = false)
        {
            if (isChunkComponent)
            {
                if (readOnly)
                    m_all.Add(ComponentType.ChunkComponentReadOnly<T>());
                else
                    m_all.Add(ComponentType.ChunkComponent<T>());
            }
            else
            {
                if (readOnly)
                    m_all.Add(ComponentType.ReadOnly<T>());
                else
                    m_all.Add(ComponentType.ReadWrite<T>());
            }
            return this;
        }

        /// <summary>
        /// Adds a required component to the query as ReadOnly unless the component has already been added (or added subsequently)
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="isChunkComponent">Is the component a chunk component for the query?</param>
        public FluentQuery WithAllWeak<T>(bool isChunkComponent = false)
        {
            if (isChunkComponent)
                m_allWeak.Add(ComponentType.ChunkComponentReadOnly<T>());
            else
                m_allWeak.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Adds a component to the WithAny category of the query using the specified access unless the component has already been added to the All category (or added subsequently) in which case the WithAny category is dropped
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Is the component a chunk component for the query?</param>
        public FluentQuery WithAny<T>(bool readOnly = false, bool isChunkComponent = false)
        {
            if (isChunkComponent)
            {
                if (readOnly)
                    m_any.Add(ComponentType.ChunkComponentReadOnly<T>());
                else
                    m_any.Add(ComponentType.ChunkComponent<T>());
            }
            else
            {
                if (readOnly)
                    m_any.Add(ComponentType.ReadOnly<T>());
                else
                    m_any.Add(ComponentType.ReadWrite<T>());
            }
            return this;
        }

        /// <summary>
        /// Adds a component to the WithAny category of the query marked as ReadOnly unless the component has already been added (or added subsequently) with WithAny write access or added to the All category (or added subsequently) in which case the WithAny category is dropped
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="isChunkComponent">Is the component a chunk component of the query?</param>
        public FluentQuery WithAnyWeak<T>(bool isChunkComponent = false)
        {
            if (isChunkComponent)
                m_anyWeak.Add(ComponentType.ChunkComponentReadOnly<T>());
            else
                m_anyWeak.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Same as WithAny except if the component was added using "Without" (or added subsequently) the component is not added
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Is the component a chunk component of the query?</param>
        public FluentQuery WithAnyNotExcluded<T>(bool readOnly = false, bool isChunkComponent = false)
        {
            if (isChunkComponent)
            {
                if (readOnly)
                    m_anyIfNotExcluded.Add(ComponentType.ChunkComponentReadOnly<T>());
                else
                    m_anyIfNotExcluded.Add(ComponentType.ChunkComponent<T>());
            }
            else
            {
                if (readOnly)
                    m_anyIfNotExcluded.Add(ComponentType.ReadOnly<T>());
                else
                    m_anyIfNotExcluded.Add(ComponentType.ReadWrite<T>());
            }
            return this;
        }

        /// <summary>
        /// Same as WithAnyWeak except if the component was added using "Without" (or added subsequently) the component is not added
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="isChunkComponent">Is the component a chunk component of the query?</param>
        public FluentQuery WithAnyNotExcludedWeak<T>(bool isChunkComponent = false)
        {
            if (isChunkComponent)
                m_anyIfNotExcludedWeak.Add(ComponentType.ChunkComponentReadOnly<T>());
            else
                m_anyIfNotExcludedWeak.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Adds a component to be explicitly excluded from the query
        /// </summary>
        /// <typeparam name="T">The type of component to exclude</typeparam>
        /// <param name="isChunkComponent">Is the component excluded a chunk component?</param>
        public FluentQuery Without<T>(bool isChunkComponent = false)
        {
            //m_none.Add(ComponentType.Exclude<T>());
            if (isChunkComponent)
                m_none.Add(ComponentType.ChunkComponentReadOnly<T>());
            else
                m_none.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Allows disabled entities to be included in the query
        /// </summary>
        /// <returns></returns>
        public FluentQuery IncludeDisabledEntities()
        {
            m_options |= EntityQueryOptions.IncludeDisabledEntities;
            return this;
        }

        /// <summary>
        /// Allows prefab entities to be included in the query
        /// </summary>
        /// <returns></returns>
        public FluentQuery IncludePrefabs()
        {
            m_options |= EntityQueryOptions.IncludePrefab;
            return this;
        }

        /// <summary>
        /// Turns on write group filtering for this query
        /// </summary>
        /// <returns></returns>
        public FluentQuery UseWriteGroups()
        {
            m_options |= EntityQueryOptions.FilterWriteGroup;
            return this;
        }

        /// <summary>
        /// Causes the EntityQuery to only check for the presence of components in the archetype
        /// and assumes that disabled components are included.
        /// </summary>
        /// <returns></returns>
        public FluentQuery IgnoreEnableableBits()
        {
            m_options |= EntityQueryOptions.IgnoreComponentEnabledState;
            return this;
        }

        public delegate void FluentDelegate(ref FluentQuery fluent);

        /// <summary>
        /// Apply a custom function to the FluentQuery
        /// </summary>
        /// <param name="fluentDelegate">The custom function to apply</param>
        /// <returns></returns>
        public FluentQuery WithDelegate(FluentDelegate fluentDelegate)
        {
            fluentDelegate.Invoke(ref this);
            return this;
        }

        /// <summary>
        /// Constructs the EntityQuery using the previous commands in the chain
        /// </summary>
        /// <returns></returns>
        public EntityQuery Build()
        {
            m_anyIsSatisfiedByAll = (m_any.Length + m_anyWeak.Length + m_anyIfNotExcluded.Length + m_anyIfNotExcludedWeak.Length) == 0;

            //Filter and merge "ifNotExcluded"
            RemoveDuplicates(m_none);
            RemoveDuplicates(m_anyIfNotExcluded);
            RemoveDuplicates(m_anyIfNotExcludedWeak);

            RemoveIfInList(m_anyIfNotExcluded,     m_none);
            RemoveIfInList(m_anyIfNotExcludedWeak, m_none);

            m_any.AddRange(m_anyIfNotExcluded.AsArray());
            m_anyWeak.AddRange(m_anyIfNotExcludedWeak.AsArray());

            //Cleanup
            RemoveDuplicates(m_all);
            RemoveDuplicates(m_any);
            RemoveDuplicates(m_allWeak);
            RemoveDuplicates(m_anyWeak);

            //throw cases:
            //any is in all and access mismatch

            //If a component in the any group is also in the all group with the same permissions, upgrade it to all and mark the flag.
            for (int i = 0; i < m_any.Length; i++)
            {
                for (int j = 0; j < m_all.Length; j++)
                {
                    var a = m_any[i];
                    var b = m_all[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        if (a.AccessModeType != b.AccessModeType)
                        {
                            throw new System.InvalidOperationException($"Cannot build an EntityQuery with type {a} given as both {a.AccessModeType} and {b.AccessModeType}");
                        }
                        else
                        {
                            m_anyIsSatisfiedByAll = true;
                        }
                    }
                }
            }

            //Merge allWeak
            for (int i = 0; i < m_allWeak.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < m_all.Length; j++)
                {
                    var a = m_allWeak[i];
                    var b = m_all[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        found = true;
                    }
                }
                if (!found)
                {
                    for (int j = 0; j < m_any.Length; j++)
                    {
                        var a = m_allWeak[i];
                        var b = m_any[j];
                        if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent && b.AccessModeType == ComponentType.AccessMode.ReadWrite)
                        {
                            a.AccessModeType      = ComponentType.AccessMode.ReadWrite;
                            m_allWeak[i]          = a;
                            m_anyIsSatisfiedByAll = true;
                        }
                    }
                    m_all.Add(m_allWeak[i]);
                }
            }

            //Merge anyWeak
            for (int i = 0; i < m_anyWeak.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < m_all.Length; j++)
                {
                    var a = m_anyWeak[i];
                    var b = m_all[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        found                 = true;
                        m_anyIsSatisfiedByAll = true;
                    }
                }
                if (!found)
                {
                    for (int j = 0; j < m_any.Length; j++)
                    {
                        var a = m_anyWeak[i];
                        var b = m_any[j];
                        if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent && b.AccessModeType == ComponentType.AccessMode.ReadWrite)
                        {
                            a.AccessModeType = ComponentType.AccessMode.ReadWrite;
                            m_anyWeak[i]     = a;
                        }
                    }
                    m_any.Add(m_anyWeak[i]);
                }
            }

            if (m_anyIsSatisfiedByAll)
                m_any.Clear();

            //EntityQueryDescBuilder desc = new EntityQueryDescBuilder(Allocator.Temp);
            //for (int i = 0; i < m_all.Length; i++)
            //    desc.AddAll(m_all[i]);
            //for (int i = 0; i < m_any.Length; i++)
            //    desc.AddAny(m_any[i]);
            //for (int i = 0; i < m_none.Length; i++)
            //    desc.AddNone(m_none[i]);
            //desc.Options(m_options);
            //desc.FinalizeQuery();

            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll(ref m_all).WithAny(ref m_any).WithNone(ref m_none).WithOptions(m_options);

            //var desc = new EntityQueryDesc()
            //{
            //    All     = m_all.ToArrayNBC(),
            //    Any     = m_any.ToArrayNBC(),
            //    None    = m_none.ToArrayNBC(),
            //    Options = m_options
            //};

            DisposeArrays();
            EntityQuery query;
            if (m_targetState != null)
            {
                //query = m_targetState->GetEntityQuery(desc);
                query = builder.Build(ref *m_targetState);
            }
            else if (m_targetManager != default)
            {
                //query = m_targetManager.CreateEntityQuery(desc);
                query = builder.Build(m_targetManager);
            }
            else
                throw new System.InvalidOperationException("Missing a system or entity manager reference to build an EntityQuery.");

            return query;
        }

        private void DisposeArrays()
        {
            m_all.Dispose();
            m_any.Dispose();
            m_none.Dispose();
            m_anyWeak.Dispose();
            m_allWeak.Dispose();
            m_anyIfNotExcluded.Dispose();
            m_anyIfNotExcludedWeak.Dispose();
        }

        private void RemoveDuplicates(NativeList<ComponentType> list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                for (int j = i + 1; j < list.Length; j++)
                {
                    var a = list[i];
                    var b = list[j];

                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        if (a.AccessModeType != b.AccessModeType)
                        {
                            throw new System.InvalidOperationException($"Cannot build an EntityQuery with type {a} given as both {a.AccessModeType} and {b.AccessModeType}");
                        }
                        else
                        {
                            list.RemoveAtSwapBack(j);
                            j--;
                        }
                    }
                }
            }
        }

        private void RemoveIfInList(NativeList<ComponentType> listToFilter, NativeList<ComponentType> typesToRemove)
        {
            for (int i = 0; i < listToFilter.Length; i++)
            {
                for (int j = 0; j < typesToRemove.Length; j++)
                {
                    if (listToFilter[i].TypeIndex == typesToRemove[j].TypeIndex && listToFilter[i].IsChunkComponent == typesToRemove[j].IsChunkComponent)
                    {
                        listToFilter.RemoveAtSwapBack(i);
                        i--;
                        j = typesToRemove.Length;
                    }
                }
            }
        }
    }
}

