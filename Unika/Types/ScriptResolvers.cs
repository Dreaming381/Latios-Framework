using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public interface IScriptResolverBase
    {
        bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false);
    }

    public interface IScriptResolver : IScriptResolverBase
    {
    }

    public interface ICachedScriptResolver : IScriptResolverBase, IDisposable
    {
        bool isCreated { get; }
        void Clear();
    }

    public struct EntityManagerScriptResolver : IScriptResolver
    {
        EntityManager em;
        bool          ro;

        public EntityManager entityManager => em;

        public EntityManagerScriptResolver(EntityManager entityManager, bool readOnly = false)
        {
            em = entityManager;
            ro = readOnly;
        }

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

    public struct LookupScriptResolver : IScriptResolver
    {
        BufferLookup<UnikaScripts> lookup;

        public BufferLookup<UnikaScripts> unikaScriptsLookup => lookup;

        public LookupScriptResolver(BufferLookup<UnikaScripts> bufferLookup)
        {
            lookup = bufferLookup;
        }

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

    public unsafe struct Cached<T> : ICachedScriptResolver where T : unmanaged, IScriptResolver
    {
        T                                              br;
        UnsafeHashMap<Entity, EntityScriptCollection>* map;
        EntityScriptCollection                         lastAccessed;
        AllocatorManager.AllocatorHandle               allocatorHandle;

        public T baseResolver => br;

        public Cached(T baseResolver, AllocatorManager.AllocatorHandle allocator)
        {
            br              = baseResolver;
            map             = AllocatorManager.Allocate<UnsafeHashMap<Entity, EntityScriptCollection> >(allocator, 1);
            lastAccessed    = default;
            allocatorHandle = allocator;
        }

        public bool isCreated => map != null;

        public void Dispose()
        {
            if (map != null)
            {
                map->Dispose();
                AllocatorManager.Free(allocatorHandle, map, 1);
            }
        }

        public void Clear()
        {
            if (map != null)
                map->Clear();
        }

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

