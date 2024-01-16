using Unity.Collections;
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
            var                 b        = system as SystemBase;
            fixed (SystemState* statePtr = &b.CheckedStateRef)
            return new FluentQuery
            {
                m_with           = new NativeList<ComponentType>(Allocator.TempJob),
                m_withEnabled    = new NativeList<ComponentType>(Allocator.TempJob),
                m_withDisabled   = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyEnabled     = new NativeList<ComponentType>(Allocator.TempJob),
                m_without        = new NativeList<ComponentType>(Allocator.TempJob),
                m_withoutEnabled = new NativeList<ComponentType>(Allocator.TempJob),
                m_targetState    = statePtr,
                m_targetManager  = default,
                m_options        = EntityQueryOptions.Default
            };
        }

        /// <summary>
        /// Starts the construction of an EntityQuery
        /// </summary>
        public static FluentQuery Fluent(this EntityManager em)
        {
            return new FluentQuery
            {
                m_with           = new NativeList<ComponentType>(Allocator.TempJob),
                m_withEnabled    = new NativeList<ComponentType>(Allocator.TempJob),
                m_withDisabled   = new NativeList<ComponentType>(Allocator.TempJob),
                m_anyEnabled     = new NativeList<ComponentType>(Allocator.TempJob),
                m_without        = new NativeList<ComponentType>(Allocator.TempJob),
                m_withoutEnabled = new NativeList<ComponentType>(Allocator.TempJob),
                m_targetState    = default,
                m_targetManager  = em,
                m_options        = EntityQueryOptions.Default
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
                    m_with           = new NativeList<ComponentType>(Allocator.TempJob),
                    m_withEnabled    = new NativeList<ComponentType>(Allocator.TempJob),
                    m_withDisabled   = new NativeList<ComponentType>(Allocator.TempJob),
                    m_anyEnabled     = new NativeList<ComponentType>(Allocator.TempJob),
                    m_without        = new NativeList<ComponentType>(Allocator.TempJob),
                    m_withoutEnabled = new NativeList<ComponentType>(Allocator.TempJob),
                    m_targetState    = statePtr,
                    m_targetManager  = default,
                    m_options        = EntityQueryOptions.Default
                };
            }
        }
    }

    /// <summary>
    /// A Fluent builder object for creating EntityQuery instances.
    /// </summary>
    public unsafe struct FluentQuery
    {
        internal NativeList<ComponentType> m_with;
        internal NativeList<ComponentType> m_withEnabled;
        internal NativeList<ComponentType> m_withDisabled;
        internal NativeList<ComponentType> m_anyEnabled;
        internal NativeList<ComponentType> m_without;
        internal NativeList<ComponentType> m_withoutEnabled;

        internal SystemState*  m_targetState;
        internal EntityManager m_targetManager;

        internal EntityQueryOptions m_options;

        /// <summary>
        /// Adds a required component to the query with the specified access. Enabled state is ignored.
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Is the component a chunk component for the query?</param>
        public FluentQuery With<T>(bool readOnly = false, bool isChunkComponent = false)
        {
            if (isChunkComponent)
            {
                if (readOnly)
                    m_with.Add(ComponentType.ChunkComponentReadOnly<T>());
                else
                    m_with.Add(ComponentType.ChunkComponent<T>());
            }
            else
            {
                if (readOnly)
                    m_with.Add(ComponentType.ReadOnly<T>());
                else
                    m_with.Add(ComponentType.ReadWrite<T>());
            }
            return this;
        }

        /// <summary>
        /// Adds required components to the query with the specified access. Enabled state is ignored.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Are the components chunk components for the query?</param>
        public FluentQuery With<T0, T1>(bool readOnly = false, bool isChunkComponent = false)
        {
            return With<T0>(readOnly, isChunkComponent).With<T1>(readOnly, isChunkComponent);
        }

        /// <summary>
        /// Adds required components to the query with the specified access. Enabled state is ignored.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <typeparam name="T2">The third type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Are the components chunk components for the query?</param>
        public FluentQuery With<T0, T1, T2>(bool readOnly = false, bool isChunkComponent = false)
        {
            return With<T0, T1>(readOnly, isChunkComponent).With<T2>(readOnly, isChunkComponent);
        }

        /// <summary>
        /// Adds a required component to the query with the specified access. The component must be enabled.
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        public FluentQuery WithEnabled<T>(bool readOnly = false) where T : IEnableableComponent
        {
            if (readOnly)
                m_withEnabled.Add(ComponentType.ReadOnly<T>());
            else
                m_withEnabled.Add(ComponentType.ReadWrite<T>());
            return this;
        }

        /// <summary>
        /// Adds required components to the query with the specified access. The components must be enabled.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        public FluentQuery WithEnabled<T0, T1>(bool readOnly = false) where T0 : IEnableableComponent where T1 : IEnableableComponent
        {
            return WithEnabled<T0>(readOnly).WithEnabled<T1>(readOnly);
        }

        /// <summary>
        /// Adds required components to the query with the specified access. The components must be enabled.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <typeparam name="T2">The third type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        public FluentQuery WithEnabled<T0, T1, T2>(bool readOnly = false) where T0 : IEnableableComponent where T1 : IEnableableComponent where T2 : IEnableableComponent
        {
            return WithEnabled<T0, T1>(readOnly).WithEnabled<T2>(readOnly);
        }

        /// <summary>
        /// Adds a required component to the query with the specified access. The component must be disabled.
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        public FluentQuery WithDisabled<T>(bool readOnly = false) where T : IEnableableComponent
        {
            if (readOnly)
                m_withDisabled.Add(ComponentType.ReadOnly<T>());
            else
                m_withDisabled.Add(ComponentType.ReadWrite<T>());
            return this;
        }

        /// <summary>
        /// Adds required components to the query with the specified access. The components must be disabled.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        public FluentQuery WithDisabled<T0, T1>(bool readOnly = false) where T0 : IEnableableComponent where T1 : IEnableableComponent
        {
            return WithDisabled<T0>(readOnly).WithDisabled<T1>(readOnly);
        }

        /// <summary>
        /// Adds required components to the query with the specified access. The components must be disabled.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <typeparam name="T2">The third type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        public FluentQuery WithDisabled<T0, T1, T2>(bool readOnly = false) where T0 : IEnableableComponent where T1 : IEnableableComponent where T2 : IEnableableComponent
        {
            return WithDisabled<T0, T1>(readOnly).WithDisabled<T2>(readOnly);
        }

        /// <summary>
        /// Adds a component to the WithAnyEnabled category of the query using the specified access unless the component
        /// has already been added to the With or WithEnabled categories (or added subsequently) in which case the WithAny category is dropped.
        /// Excluded or forced disabled categories with this same component will also cause this component to be removed
        /// from the WithAnyEnabled category.
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="readOnly">Should the component be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Is the component a chunk component for the query? Enabled state is ignored for chunk components.</param>
        public FluentQuery WithAnyEnabled<T>(bool readOnly = false, bool isChunkComponent = false)
        {
            if (isChunkComponent)
            {
                if (readOnly)
                    m_anyEnabled.Add(ComponentType.ChunkComponentReadOnly<T>());
                else
                    m_anyEnabled.Add(ComponentType.ChunkComponent<T>());
            }
            else
            {
                if (readOnly)
                    m_anyEnabled.Add(ComponentType.ReadOnly<T>());
                else
                    m_anyEnabled.Add(ComponentType.ReadWrite<T>());
            }
            return this;
        }

        /// <summary>
        /// Adds the components to the WithAnyEnabled category of the query using the specified access unless any of these components
        /// have already been added to the With or WithEnabled categories (or added subsequently) in which case the WithAny category is dropped.
        /// Excluded or forced disabled categories with this same components will also cause these components to be removed
        /// from the WithAnyEnabled category on a per-component basis.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Are the components chunk components for the query?</param>
        public FluentQuery WithAnyEnabled<T0, T1>(bool readOnly = false, bool isChunkComponent = false)
        {
            return WithAnyEnabled<T0>(readOnly, isChunkComponent).WithAnyEnabled<T1>(readOnly, isChunkComponent);
        }

        /// <summary>
        /// Adds the components to the WithAnyEnabled category of the query using the specified access unless any of these components
        /// have already been added to the With or WithEnabled categories (or added subsequently) in which case the WithAny category is dropped.
        /// Excluded or forced disabled categories with this same components will also cause these components to be removed
        /// from the WithAnyEnabled category on a per-component basis.
        /// </summary>
        /// <typeparam name="T0">The first type of component to add</typeparam>
        /// <typeparam name="T1">The second type of component to add</typeparam>
        /// <typeparam name="T2">The third type of component to add</typeparam>
        /// <param name="readOnly">Should the components be marked as ReadOnly?</param>
        /// <param name="isChunkComponent">Are the components chunk components for the query?</param>
        public FluentQuery WithAnyEnabled<T0, T1, T2>(bool readOnly = false, bool isChunkComponent = false)
        {
            return WithAnyEnabled<T0, T1>(readOnly, isChunkComponent).WithAnyEnabled<T2>(readOnly, isChunkComponent);
        }

        /// <summary>
        /// Adds a component to be explicitly excluded from the query. A disabled component is not excluded.
        /// </summary>
        /// <typeparam name="T">The type of component to exclude</typeparam>
        /// <param name="isChunkComponent">Is the component excluded a chunk component?</param>
        public FluentQuery Without<T>(bool isChunkComponent = false)
        {
            if (isChunkComponent)
                m_without.Add(ComponentType.ChunkComponentReadOnly<T>());
            else
                m_without.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Adds required components to be explicitly excluded from the query. Disabled components are not excluded.
        /// </summary>
        /// <typeparam name="T0">The first type of component to exclude</typeparam>
        /// <typeparam name="T1">The second type of component to exclude</typeparam>
        /// <param name="isChunkComponent">Are the components exclude chunk components?</param>
        public FluentQuery Without<T0, T1>(bool isChunkComponent = false)
        {
            return Without<T0>(isChunkComponent).Without<T1>(isChunkComponent);
        }

        /// <summary>
        /// Adds required components to be explicitly excluded from the query. Disabled components are not excluded.
        /// </summary>
        /// <typeparam name="T0">The first type of component to exclude</typeparam>
        /// <typeparam name="T1">The second type of component to exclude</typeparam>
        /// <typeparam name="T2">The third type of component to exclude</typeparam>
        /// <param name="isChunkComponent">Are the components exclude chunk components?</param>
        public FluentQuery Without<T0, T1, T2>(bool isChunkComponent = false)
        {
            return Without<T0, T1>(isChunkComponent).Without<T2>(isChunkComponent);
        }

        /// <summary>
        /// Adds a component to be explicitly excluded from the query. A disabled component is excluded.
        /// </summary>
        /// <typeparam name="T">The type of component to exclude</typeparam>
        public FluentQuery WithoutEnabled<T>() where T : IEnableableComponent
        {
            m_withoutEnabled.Add(ComponentType.ReadOnly<T>());
            return this;
        }

        /// <summary>
        /// Adds required components to be explicitly excluded from the query. Disabled components are excluded.
        /// </summary>
        /// <typeparam name="T0">The first type of component to exclude</typeparam>
        /// <typeparam name="T1">The second type of component to exclude</typeparam>
        public FluentQuery WithoutEnabled<T0, T1>() where T0 : IEnableableComponent where T1 : IEnableableComponent
        {
            return WithoutEnabled<T0>().WithoutEnabled<T1>();
        }

        /// <summary>
        /// Adds required components to be explicitly excluded from the query. Disabled components are excluded.
        /// </summary>
        /// <typeparam name="T0">The first type of component to exclude</typeparam>
        /// <typeparam name="T1">The second type of component to exclude</typeparam>
        /// <typeparam name="T2">The third type of component to exclude</typeparam>
        public FluentQuery WithoutEnabled<T0, T1, T2>() where T0 : IEnableableComponent where T1 : IEnableableComponent where T2 : IEnableableComponent
        {
            return WithoutEnabled<T0, T1>().WithoutEnabled<T2>();
        }

        /// <summary>
        /// Adds required components to the query to ensure valid access to a collection aspect.
        /// </summary>
        /// <typeparam name="T">The type of collection aspect to support</typeparam>
        public FluentQuery WithCollectionAspect<T>() where T : unmanaged, ICollectionAspect<T>
        {
            return default(T).AppendToQuery(this);
        }

        /// <summary>
        /// Adds the required components by the IAspect to the query, with any enableable components
        /// required to be enabled
        /// </summary>
        /// <typeparam name="T">The type of IAspect to add to the query</typeparam>
        public FluentQuery WithAspect<T>() where T : unmanaged, IAspect, IAspectCreate<T>
        {
            var tempList = new UnsafeList<ComponentType>(8, Allocator.Temp);
            default(T).AddComponentRequirementsTo(ref tempList);
            foreach (var component in tempList)
            {
                if (component.IsEnableable)
                    m_withEnabled.Add(in component);
                else
                    m_with.Add(in component);
            }
            return this;
        }

        /// <summary>
        /// Allows disabled entities to be included in the query
        /// </summary>
        public FluentQuery IncludeDisabledEntities()
        {
            m_options |= EntityQueryOptions.IncludeDisabledEntities;
            return this;
        }

        /// <summary>
        /// Allows prefab entities to be included in the query
        /// </summary>
        public FluentQuery IncludePrefabs()
        {
            m_options |= EntityQueryOptions.IncludePrefab;
            return this;
        }

        /// <summary>
        /// Allows system entities to be included in the query
        /// </summary>
        public FluentQuery IncludeSystemEntities()
        {
            m_options |= EntityQueryOptions.IncludeSystems;
            return this;
        }

        /// <summary>
        /// Allows entities belonging to meta chunks to be included in the query
        /// </summary>
        public FluentQuery IncludeMetaEntities()
        {
            m_options |= EntityQueryOptions.IncludeMetaChunks;
            return this;
        }

        /// <summary>
        /// Turns on write group filtering for this query
        /// </summary>
        public FluentQuery UseWriteGroups()
        {
            m_options |= EntityQueryOptions.FilterWriteGroup;
            return this;
        }

        /// <summary>
        /// Causes the EntityQuery to only check for the presence of components in the archetype
        /// and assumes that disabled components are included.
        /// </summary>
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
            if ((m_options & EntityQueryOptions.IgnoreComponentEnabledState) == EntityQueryOptions.IgnoreComponentEnabledState)
            {
                // Move any WithEnabled to With
                m_with.AddRange(m_withEnabled.AsArray());
                m_withEnabled.Clear();
                m_with.AddRange(m_withDisabled.AsArray());
                m_withDisabled.Clear();
                m_with.AddRange(m_withoutEnabled.AsArray());
                m_withoutEnabled.Clear();
            }

            // Remove all duplicates
            RemoveDuplicates(m_with);
            RemoveDuplicates(m_withEnabled);
            RemoveDuplicates(m_anyEnabled);
            RemoveDuplicates(m_without);
            RemoveDuplicates(m_withoutEnabled);
            RemoveDuplicates(m_withDisabled);

            // Filter and merge WithoutEnabled
            RemoveIfInList(m_withoutEnabled, m_without,      false);
            RemoveIfInList(m_withoutEnabled, m_withDisabled, false);

            // Filter and merge With
            RemoveEnableableIfInList(m_with, m_withEnabled,  true);
            RemoveEnableableIfInList(m_with, m_withDisabled, true);

            // Do WithAnyEnabled filtering
            if (!m_anyEnabled.IsEmpty)
            {
                var originalAnyCount = m_anyEnabled.Length;
                if ((m_options & EntityQueryOptions.IgnoreComponentEnabledState) == EntityQueryOptions.IgnoreComponentEnabledState)
                    RemoveIfInList(m_anyEnabled, m_with, true);
                else
                {
                    RemoveEnableableIfInList(m_anyEnabled, m_withEnabled, true);
                    RemoveNotEnableableIfInList(m_anyEnabled, m_with, true);
                }
                if (originalAnyCount != m_anyEnabled.Length)
                {
                    m_anyEnabled.Clear();
                }
                else
                {
                    RemoveEnableableIfInList(m_anyEnabled, m_withDisabled, false);
                    RemoveIfInList(m_anyEnabled, m_without,        false);
                    RemoveIfInList(m_anyEnabled, m_withoutEnabled, false);
                    if (m_anyEnabled.IsEmpty)
                        throw new System.InvalidOperationException(
                            $"Cannot build an EntityQuery when all types specified as WithAnyEnabled() are also excluded by WithDisabled(), Without(), and WithoutEnabled().");
                }
            }

            // Migrate special component types from Present to All to work around Unity bug:
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex   = TypeManager.GetTypeIndex<Prefab>();
            //var systemInstanceTypeIndex = TypeManager.GetTypeIndex<SystemInstance>();
            var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            for (int i = 0; i < m_with.Length; i++)
            {
                if (m_with[i].TypeIndex == disabledTypeIndex || m_with[i].TypeIndex == prefabTypeIndex || m_with[i].TypeIndex == chunkHeaderTypeIndex)
                {
                    m_withEnabled.Add(m_with[i]);
                    m_with.RemoveAtSwapBack(i);
                    i--;
                }
            }

            var builder = new EntityQueryBuilder(Allocator.Temp).WithPresent(ref m_with)
                          .WithAll(ref m_withEnabled)
                          .WithDisabled(ref m_withDisabled)
                          .WithAny(ref m_anyEnabled)
                          .WithAbsent(ref m_without)
                          .WithNone(ref m_withoutEnabled)
                          .WithOptions(m_options);

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
            m_with.Dispose();
            m_withEnabled.Dispose();
            m_withDisabled.Dispose();
            m_anyEnabled.Dispose();
            m_without.Dispose();
            m_withoutEnabled.Dispose();
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
                            a.AccessModeType = ComponentType.AccessMode.ReadWrite;
                            list[i]          = a;
                        }
                        list.RemoveAtSwapBack(j);
                        j--;
                    }
                }
            }
        }

        private void RemoveIfInList(NativeList<ComponentType> listToFilter, NativeList<ComponentType> typesToRemove, bool writePromote)
        {
            for (int i = 0; i < listToFilter.Length; i++)
            {
                var a = listToFilter[i];
                for (int j = 0; j < typesToRemove.Length; j++)
                {
                    var b = typesToRemove[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        if (writePromote && a.AccessModeType != b.AccessModeType)
                        {
                            b.AccessModeType = ComponentType.AccessMode.ReadWrite;
                            typesToRemove[i] = b;
                        }
                        listToFilter.RemoveAtSwapBack(i);
                        i--;
                        j = typesToRemove.Length;
                    }
                }
            }
        }

        private void RemoveEnableableIfInList(NativeList<ComponentType> listToFilter, NativeList<ComponentType> typesToRemove, bool writePromote)
        {
            for (int i = 0; i < listToFilter.Length; i++)
            {
                var a = listToFilter[i];
                if (!a.IsEnableable)
                    continue;

                for (int j = 0; j < typesToRemove.Length; j++)
                {
                    var b = typesToRemove[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        if (writePromote && a.AccessModeType != b.AccessModeType)
                        {
                            b.AccessModeType = ComponentType.AccessMode.ReadWrite;
                            typesToRemove[i] = b;
                        }
                        listToFilter.RemoveAtSwapBack(i);
                        i--;
                        j = typesToRemove.Length;
                    }
                }
            }
        }

        private void RemoveNotEnableableIfInList(NativeList<ComponentType> listToFilter, NativeList<ComponentType> typesToRemove, bool writePromote)
        {
            for (int i = 0; i < listToFilter.Length; i++)
            {
                var a = listToFilter[i];
                if (a.IsEnableable)
                    continue;

                for (int j = 0; j < typesToRemove.Length; j++)
                {
                    var b = typesToRemove[j];
                    if (a.TypeIndex == b.TypeIndex && a.IsChunkComponent == b.IsChunkComponent)
                    {
                        if (writePromote && a.AccessModeType != b.AccessModeType)
                        {
                            b.AccessModeType = ComponentType.AccessMode.ReadWrite;
                            typesToRemove[i] = b;
                        }
                        listToFilter.RemoveAtSwapBack(i);
                        i--;
                        j = typesToRemove.Length;
                    }
                }
            }
        }
    }
}

