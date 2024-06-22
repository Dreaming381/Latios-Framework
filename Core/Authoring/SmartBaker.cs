using System;
using System.Collections;
using System.Collections.Generic;
using Latios.Authoring.Systems;
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
    /// You must also add the [BakingType] attribute to the type implementing this interface and create an associated
    /// subclass of SmartBaker for this to be effective. The SmartBaker will create instances of this type for you.
    /// </summary>
    /// <typeparam name="TAuthoring"></typeparam>
    public interface ISmartBakeItem<TAuthoring> : ISmartPostProcessItem where TAuthoring : Component
    {
        /// <summary>
        /// Perform the initial bake step on the authoring component.
        /// </summary>
        /// <param name="authoring">The target authoring component to bake</param>
        /// <param name="baker">The baker to use for baking. You can cast this to the SmartBaker subclass if necessary.</param>
        /// <returns>False if this item's "memory" should be discarded and not processed later.</returns>
        public bool Bake(TAuthoring authoring, IBaker baker);
    }

    /// <summary>
    /// Implement this interface directly to contain data that should be applied by a baking system later.
    /// You can add instances of these via the IBaker.AddPostProcessItem extension method.
    /// You must also add the [BakingType] attribute to the type implementing this interface.
    /// A baking system will discover and process these automatically.
    /// </summary>
    public interface ISmartPostProcessItem : IComponentData
    {
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
    public abstract partial class SmartBaker<TAuthoring, TSmartBakeItem> : Baker<TAuthoring>
        where TAuthoring : Component where TSmartBakeItem : unmanaged, ISmartBakeItem<TAuthoring>
    {
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
    }

    public static class SmartBakerExtensions
    {
        /// <summary>
        /// Adds an ISmartPostProcessItem targeting the entity to run after Smart Blobbers.
        /// It is safe to call this more than once for the same target entity.
        /// </summary>
        /// <param name="entity">The target entity that will be passed into the PostProcessBlobRequests callback</param>
        /// <param name="smartPostProcessItem">The ISmartPostProcessItem containing SmartBlobberHandles</param>
        public static void AddPostProcessItem<T>(this IBaker baker, Entity entity, T smartPostProcessItem) where T : unmanaged, ISmartPostProcessItem
        {
            var smartBakerEntity = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            baker.AddComponent(smartBakerEntity, smartPostProcessItem);
            baker.AddComponent(smartBakerEntity, new SmartBakerTargetEntityReference { targetEntity = entity });
        }
    }

    [UpdateInGroup(typeof(SmartBakerBakingGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    internal partial class SmartBakerSystem : SystemBase
    {
        abstract class BaseItem
        {
            public abstract void OnCreate(ref SystemState state);
            public abstract void OnUpdate(ref SystemState state);
            public abstract void OnAfterUpdate(ref SystemState state);
        }

        class Item<TSmartItem> : BaseItem where TSmartItem : unmanaged, ISmartPostProcessItem
        {
            EntityQuery m_query;

            public override void OnCreate(ref SystemState state)
            {
                m_query = new EntityQueryBuilder(Allocator.Temp)
                          .WithAllRW<TSmartItem>()
                          .WithAll<SmartBakerTargetEntityReference>()
                          .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                          .Build(ref state);
            }

            public override void OnUpdate(ref SystemState state)
            {
                if (m_query.IsEmptyIgnoreFilter)
                    return;

                var targetReferences = m_query.ToComponentDataArray<SmartBakerTargetEntityReference>(Allocator.Temp);
                var smartDataArray   = m_query.ToComponentDataArray<TSmartItem>(Allocator.Temp);

                for (int i = 0; i < targetReferences.Length; i++)
                {
                    var smartData = smartDataArray[i];
                    smartData.PostProcessBlobRequests(state.EntityManager, targetReferences[i].targetEntity);
                    smartDataArray[i] = smartData;
                }
            }

            public override void OnAfterUpdate(ref SystemState state)
            {
                state.EntityManager.RemoveComponent<TSmartItem>(m_query);
            }
        }

        List<BaseItem>             m_items     = new List<BaseItem>();
        static List<ComponentType> s_itemTypes = null;

        protected override void OnCreate()
        {
            if (s_itemTypes == null)
            {
                var itemType = typeof(ISmartPostProcessItem);
                s_itemTypes  = new List<ComponentType>();
                foreach (var type in TypeManager.AllTypes)
                {
                    if (itemType.IsAssignableFrom(type.Type))
                    {
                        s_itemTypes.Add(ComponentType.ReadOnly(type.TypeIndex));
                    }
                }
            }

            var genericType = typeof(Item<>);
            foreach (var t in s_itemTypes)
            {
                m_items.Add(Activator.CreateInstance(genericType.MakeGenericType(t.GetManagedType())) as BaseItem);
            }

            ref var state = ref CheckedStateRef;
            foreach (var t in m_items)
            {
                t.OnCreate(ref state);
            }
        }

        protected override void OnUpdate()
        {
            ref var state = ref CheckedStateRef;
            state.CompleteDependency();
            foreach (var t in m_items)
            {
                t.OnUpdate(ref state);
            }
            foreach (var t in m_items)
            {
                t.OnAfterUpdate(ref state);
            }
        }
    }

    [TemporaryBakingType]
    internal struct SmartBakerTargetEntityReference : IComponentData
    {
        public Entity targetEntity;
    }
}

