using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace Latios.Authoring
{
    /// <summary>
    /// Implement this interface to specify a type that can "remember" SmartBlobberHandles or
    /// other data (like additional entities) and applying them to entities later inside of a generated Baking System.
    /// You must also subclass SmartBaker for this to do anything. The SmartBaker will create instances of this type for you.
    /// Lastly, you must add the TemporaryBakingType attribute to your type implementing this interface.
    /// </summary>
    /// <typeparam name="TAuthoring"></typeparam>
    [TemporaryBakingType]
    public interface ISmartBakeItem<TAuthoring> : IComponentData where TAuthoring : Component
    {
        /// <summary>
        /// Perform the initial bake step on the authoring component.
        /// </summary>
        /// <param name="authoring">The target authoring component to bake</param>
        /// <param name="baker">The baker to use for baking. You can cast this to the SmartBaker subclass if necessary.</param>
        /// <returns>False if this item's "memory" should be discarded and not processed later.</returns>
        public bool Bake(TAuthoring authoring, IBaker baker);
        /// <summary>
        /// This method is called by a baking system and provides an opportunity to resolve SmartBlobberHandles.
        /// If you wish to do more than assign BlobReferences via entityManager.SetComponent, see remarks for detailed rules.
        /// </summary>
        /// <param name="entityManager">An EntityManager which you can use to manipulate the baked world.</param>
        /// <param name="entity">The primary entity associated with the baker that was passed to Bake()</param>
        /// <remarks>
        /// You must be very careful with the types of operations you perform here, or else live bake previews of open subscenes
        /// will have incorrect results. Closed subscenes will always be correct, so if that is all you care about, ignore the following:
        /// It is safe to read from any entity's component or buffer that is not a [TemporaryBakingType].
        /// It is safe to read a [TemporaryBakingType] that was added by this bake item (both the type and the entities involved must be considered).
        /// It is safe to set or remove a component or buffer that was added by this bake item (both the type and the entities involved must be considered).
        /// It is safe to add a [TemporaryBakingType] to either the primary entity or additionally created entities created by this bake item. Usage of such
        /// type in a later Baking System must follow Baking System rules for correctness.
        /// It is not safe to do any other operation, as such operations are not undoable.
        /// </remarks>
        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity);
    }

    /// <summary>
    /// Subclass this type to define a SmartBaker which should generate a BakeItem for each instance of Authoring.
    /// </summary>
    /// <typeparam name="TAuthoring">The authoring type this baker should apply for</typeparam>
    /// <typeparam name="TSmartBakeItem">The bake item type to generate an instance of and invoke Bake() on</typeparam>
    [BurstCompile]
    public abstract partial class SmartBaker<TAuthoring, TSmartBakeItem> : Baker<TAuthoring>,
        ICreateSmartBakerSystem where TAuthoring : Component where TSmartBakeItem : unmanaged,
        ISmartBakeItem<TAuthoring>
    {
        /// <summary>
        /// Currently non-functional. Burst is not used for Post-Processing.
        /// </summary>
        /// <returns></returns>
        public virtual bool RunPostProcessInBurst()
        {
            return true;
        }

        public sealed override void Bake(TAuthoring authoring)
        {
            // Todo: May need to cache construction of this to avoid GC.
            // However, that might have been a "struct" limitation and not an "unmanaged" limitation.
            var data = new TSmartBakeItem();
            if (data.Bake(authoring, this))
            {
                var smartBakerEntity = CreateAdditionalEntity(TransformUsageFlags.None, true);
                AddComponent(smartBakerEntity, data);
                AddComponent(smartBakerEntity, new SmartBakerTargetEntityReference { targetEntity = GetEntityWithoutDependency() });
            }
        }

        unsafe void ICreateSmartBakerSystem.Create(World world, ComponentSystemGroup addToThis)
        {
            TypeManager.GetSystemTypeIndex(typeof(SmartBakerSystem<TAuthoring, TSmartBakeItem>));
            var system        = world.GetOrCreateSystemManaged<SmartBakerSystem<TAuthoring, TSmartBakeItem> >();
            system.runInBurst = RunPostProcessInBurst();
            addToThis.AddSystemToUpdateList(system);
        }

        // These jobs are here but the system is split out due to a bug in source generators dropping the generics
        // on the wrapper type.
        [BurstCompile]
        internal struct ProcessSmartBakeDataBurstedJob : IJob
        {
            public EntityManager                                           em;
            [ReadOnly] public NativeArray<SmartBakerTargetEntityReference> targetReferences;
            public NativeArray<TSmartBakeItem>                             smartDataArray;

            public void Execute()
            {
                for (int i = 0; i < targetReferences.Length; i++)
                {
                    var smartData = smartDataArray[i];
                    smartData.PostProcessBlobRequests(em, targetReferences[i].targetEntity);
                    smartDataArray[i] = smartData;
                }
            }
        }

        [BurstCompile]
        internal struct WriteBackBakedDataJob : IJobFor
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<TSmartBakeItem> smartDataLookup;
            [ReadOnly] public NativeArray<Entity>                                        entities;
            [ReadOnly] public NativeArray<TSmartBakeItem>                                smartDataArray;

            public void Execute(int i)
            {
                smartDataLookup[entities[i]] = smartDataArray[i];
            }
        }
    }

    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    internal partial class SmartBakerSystem<TAuthoring, TSmartBakeItem> : SystemBase where TAuthoring : Component where TSmartBakeItem : unmanaged,
        ISmartBakeItem<TAuthoring>
    {
        public bool runInBurst;

        EntityQuery m_query;

        protected override void OnCreate()
        {
            m_query = new EntityQueryBuilder(Allocator.Temp)
                      .WithAllRW<TSmartBakeItem>()
                      .WithAll<SmartBakerTargetEntityReference>()
                      .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                      .Build(this);
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var entities         = m_query.ToEntityArray(Allocator.TempJob);
            var targetReferences = m_query.ToComponentDataArray<SmartBakerTargetEntityReference>(Allocator.TempJob);
            var smartDataArray   = m_query.ToComponentDataArray<TSmartBakeItem>(Allocator.TempJob);

            var processJob = new SmartBaker<TAuthoring, TSmartBakeItem>.ProcessSmartBakeDataBurstedJob
            {
                em               = EntityManager,
                targetReferences = targetReferences,
                smartDataArray   = smartDataArray
            };

            // Todo: Figure out how to get safety to not complain here.
            //if (runInBurst)
            //    processJob.Run();
            //else
            processJob.Execute();

            var writeBackJob = new SmartBaker<TAuthoring, TSmartBakeItem>.WriteBackBakedDataJob
            {
                smartDataLookup = GetComponentLookup<TSmartBakeItem>(),
                entities        = entities,
                smartDataArray  = smartDataArray
            };
            writeBackJob.RunByRef(entities.Length);

            entities.Dispose();
            targetReferences.Dispose();
            smartDataArray.Dispose();
        }
    }

    internal interface ICreateSmartBakerSystem
    {
        internal void Create(World world, ComponentSystemGroup addToThis);
    }

    [TemporaryBakingType]
    internal struct SmartBakerTargetEntityReference : IComponentData
    {
        public Entity targetEntity;
    }
}

