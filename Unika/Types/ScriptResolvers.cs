using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A base script resolver interface used by various resolve methods. Prefer to implement IScriptResolver or ICachedScriptResolver instead.
    /// </summary>
    public interface IScriptResolverBase
    {
        /// <summary>
        /// Attempts to resolve the EntityScriptCollection on the entity.
        /// </summary>
        /// <param name="entity">The entity that may possess a UnikaScripts buffer</param>
        /// <param name="allScripts">The resolved EntityScriptCollection which can enumerate scripts</param>
        /// <param name="throwSafetyErrorIfNotFound">If true and resolution fails, an exception should be thrown, otherwise this method returns false.
        /// This allows the resolver to throw a more specific error message if resolution fails in a context the caller does not expect it to fail.</param>
        /// <returns>True if the resolve is successful, false otherwise.</returns>
        bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false);
    }

    /// <summary>
    /// A script resolver interface you can implement to create your own resolvers
    /// </summary>
    public interface IScriptResolver : IScriptResolverBase
    {
    }

    /// <summary>
    /// A script resolver interface containing cached state from previous resolves that you can implement to create your own cached resolvers
    /// </summary>
    public interface ICachedScriptResolver : IScriptResolverBase, IDisposable
    {
        bool isCreated { get; }
        void Clear();
    }

    /// <summary>
    /// A script resolver which uses an EntityManager and will either grant ReadWrite or ReadOnly access to the scripts.
    /// </summary>
    public struct EntityManagerScriptResolver : IScriptResolver
    {
        EntityManager em;
        bool          ro;

        /// <summary>
        /// The EntityManager this resolver uses
        /// </summary>
        public EntityManager entityManager => em;

        /// <summary>
        /// Creates an EntityManagerScriptResolver
        /// </summary>
        /// <param name="entityManager">The EntityManager this resolver should use</param>
        /// <param name="readOnly">Specifies whether script buffers are accessed in read-only mode. Setting this to true disables calling methods through an Interface</param>
        public EntityManagerScriptResolver(EntityManager entityManager, bool readOnly = false)
        {
            em = entityManager;
            ro = readOnly;
        }

        /// <inheritdoc/>
        public bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false)
        {
            if (throwSafetyErrorIfNotFound || em.HasBuffer<UnikaScripts>(entity))
            {
                var result = em.GetBuffer<UnikaScripts>(entity, ro);
                allScripts = result.AllScripts(entity);
                return true;
            }
            allScripts = default;
            return false;
        }
    }

    /// <summary>
    /// A script resolver which uses a BufferLookup<UnikaScripts>
    /// </summary>
    public struct LookupScriptResolver : IScriptResolver
    {
        BufferLookup<UnikaScripts> lookup;

        /// <summary>
        /// The buffer lookup handle this resolver uses
        /// </summary>
        public BufferLookup<UnikaScripts> unikaScriptsLookup => lookup;

        /// <summary>
        /// Creates a resolver using the specified buffer lookup
        /// </summary>
        /// <param name="bufferLookup"></param>
        public LookupScriptResolver(BufferLookup<UnikaScripts> bufferLookup)
        {
            lookup = bufferLookup;
        }

        /// <inheritdoc/>
        public bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false)
        {
            if (throwSafetyErrorIfNotFound)
            {
                var result = lookup[entity];
                allScripts = result.AllScripts(entity);
                return true;
            }
            if (lookup.TryGetBuffer(entity, out var buffer))
            {
                allScripts = buffer.AllScripts(entity);
                return true;
            }
            allScripts = default;
            return false;
        }
    }

    /// <summary>
    /// A wrapper type around any IScriptResolver which adds a simple caching layer.
    /// This can be used to avoid archetype lookup cache thrashing
    /// </summary>
    /// <typeparam name="T">The type of base script resolver to wrap</typeparam>
    public unsafe struct Cached<T> : ICachedScriptResolver where T : unmanaged, IScriptResolver, IDisposable
    {
        T                                              br;
        UnsafeHashMap<Entity, EntityScriptCollection>* map;
        EntityScriptCollection                         lastAccessed;
        AllocatorManager.AllocatorHandle               allocatorHandle;

        /// <summary>
        /// Gets the base resolver instance used by this cached wrapper
        /// </summary>
        public T baseResolver => br;

        /// <summary>
        /// Creates a cached wrapper resolver using the base resolver
        /// </summary>
        /// <param name="baseResolver">The base resolver instance</param>
        /// <param name="allocator">The allocator which should be used for the cache</param>
        public Cached(T baseResolver, AllocatorManager.AllocatorHandle allocator)
        {
            br              = baseResolver;
            map             = AllocatorManager.Allocate<UnsafeHashMap<Entity, EntityScriptCollection> >(allocator, 1);
            lastAccessed    = default;
            allocatorHandle = allocator;
        }

        /// <summary>
        /// Returns true if the internal cache is created, false otherwise.
        /// </summary>
        public bool isCreated => map != null;

        /// <summary>
        /// Disposes the cache storage
        /// </summary>
        public void Dispose()
        {
            if (map != null)
            {
                map->Dispose();
                AllocatorManager.Free(allocatorHandle, map, 1);
            }
        }

        /// <summary>
        /// Clears the cache storage, which may be useful if cached resolves may no longer be thread-safe
        /// </summary>
        public void Clear()
        {
            if (map != null)
                map->Clear();
        }

        /// <inheritdoc/>
        public bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false)
        {
            if (entity == lastAccessed.entity)
            {
                allScripts = lastAccessed;
                return true;
            }
            CheckMapNotNull();
            if (map->TryGetValue(entity, out allScripts))
            {
                lastAccessed = allScripts;
                return true;
            }
            if (br.TryGet(entity, out allScripts, throwSafetyErrorIfNotFound))
            {
                lastAccessed = allScripts;
                return true;
            }
            allScripts = default;
            return false;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckMapNotNull()
        {
            if (map == null)
                throw new InvalidOperationException("Cached script resolver is uninitialized.");
        }
    }
}

