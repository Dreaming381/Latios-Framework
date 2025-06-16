using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal struct BlobRetainer : IDisposable
    {
        struct Entry
        {
            public Entity                                 retainEntity;
            public EntityManagerExposed.BlobAssetOwnerPtr owner;
            public int                                    lastSeenBuffer;
        }

        NativeList<Entry>                                          entries;
        NativeHashMap<EntityManagerExposed.BlobAssetOwnerPtr, int> ownerToEntryMap;
        EntityManager                                              retainEntityManager;

        public void Init()
        {
            entries             = new NativeList<Entry>(8, Allocator.Persistent);
            ownerToEntryMap     = new NativeHashMap<EntityManagerExposed.BlobAssetOwnerPtr, int>(8, Allocator.Persistent);
            retainEntityManager = new World("MyriBlobRetainWorld", WorldFlags.None).EntityManager;
        }

        public void Dispose()
        {
            entries.Dispose();
            ownerToEntryMap.Dispose();
            retainEntityManager.World.Dispose();
        }

        public void Update(EntityManager mainWorldEntityManager, int scheduledBuffer, int consumedBuffer)
        {
            // We don't update entries on the frame they disappeared, even though their disappearance needs to be
            // captured in a buffer. We increment this so that it refers to the next possible buffer when the entry
            // could disappear.
            scheduledBuffer++;

            // Update still alive blob owners
            var aliveOwners = mainWorldEntityManager.GetAllUniqueBlobAssetOwners();
            foreach (var alive in aliveOwners)
            {
                if (ownerToEntryMap.TryGetValue(alive, out var entryIndex))
                {
                    var entry            = entries[entryIndex];
                    entry.lastSeenBuffer = scheduledBuffer;
                    entries[entryIndex]  = entry;
                }
                else
                {
                    entryIndex = -1;
                    for (int i = 0; i < entries.Length; i++)
                    {
                        if (entries[i].retainEntity == Entity.Null)
                        {
                            entryIndex = i;
                            break;
                        }
                    }
                    if (entryIndex == -1)
                    {
                        entryIndex = entries.Length;
                        entries.Add(default);
                    }
                    var newRetainEntity = retainEntityManager.CreateEntity();
                    retainEntityManager.AddBlobAssetOwner(newRetainEntity, alive);
                    entries[entryIndex] = new Entry { retainEntity = newRetainEntity, owner = alive, lastSeenBuffer = scheduledBuffer };
                    ownerToEntryMap.Add(alive, entryIndex);
                }
            }

            // Cull dead blob owners
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.retainEntity != Entity.Null && consumedBuffer >= entry.lastSeenBuffer)
                {
                    retainEntityManager.DestroyEntity(entry.retainEntity);
                    ownerToEntryMap.Remove(entry.owner);
                    entries[i] = default;
                }
            }
        }
    }
}

