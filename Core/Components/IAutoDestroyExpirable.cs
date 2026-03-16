using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// This interface marks a component as expirable. When the component is disabled,
    /// it is assumed to be expired. When an entity that has one or more expirable components
    /// reaches a point when all the expirable components are disabled (expired), the entity
    /// is automatically destroyed.
    /// </summary>
    public interface IAutoDestroyExpirable : IEnableableComponent
    {
    }

    public partial struct AutoDestroyExpirationJournal : ICollectionComponent
    {
        public struct RemovedFromLinkedEntityGroup
        {
            public Entity linkedEntityGroupOwner;
            public Entity entityRemoved;
        }

        public NativeStream destroyedEntitiesStream;  // Contains Entity instances
        public NativeStream removedFromLinkedEntityGroupStream;  // Contains RemovedFromLinkedEntityGroup instances

        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;  // Uses WorldUpdateAllocator
    }
}

