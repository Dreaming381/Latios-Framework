using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Latios.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    [UpdateAfter(typeof(MergeBlackboardsSystem))]
    [BurstCompile]
    public unsafe partial struct CollectionComponentsReactiveSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        struct CollectionComponentDispatchable
        {
            public EntityQuery                                                                           addQuery;
            public EntityQuery                                                                           removeQuery;
            public FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> functionPtr;
        }

        NativeList<CollectionComponentDispatchable> m_dispatchables;

        delegate FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> GetFunctionPtrDelegate();

        public void OnCreate(ref SystemState state)
        {
            NativeList<ComponentType> cleanupTypes = default;
            GetAllTagCleanupComponentTypes(ref cleanupTypes, ref m_dispatchables, TypeManager.GetTypeCount());

            var cleanupInterfaceType = typeof(InternalSourceGen.StaticAPI.ICollectionComponentCleanup);
            foreach (var cleanupType in cleanupTypes)
            {
                var managedType = cleanupType.GetManagedType();
                if (!cleanupInterfaceType.IsAssignableFrom(managedType))
                    continue;

                var method    = managedType.GetMethod("GetBurstDispatchFunctionPtr", BindingFlags.Static | BindingFlags.Public);
                var invokable = method.CreateDelegate(typeof(GetFunctionPtrDelegate)) as GetFunctionPtrDelegate;
                m_dispatchables.Add(new CollectionComponentDispatchable
                {
                    functionPtr = invokable()
                });
            }

            OnCreateBurst(ref state, (CollectionComponentsReactiveSystem*)UnsafeUtility.AddressOf(ref this));
        }

        [BurstCompile]
        static void GetAllTagCleanupComponentTypes(ref NativeList<ComponentType> cleanupTypes, ref NativeList<CollectionComponentDispatchable> dispatchables, int typeCount)
        {
            cleanupTypes = new NativeList<ComponentType>(Allocator.Temp);

            //foreach (var type in TypeManager.AllTypes)
            for (int i = 0; i < typeCount; i++)
            {
                var typeIndex = TypeManager.GetTypeInfo(new TypeIndex { Value = i }).TypeIndex;
                if (typeIndex.IsComponentType && typeIndex.IsCleanupComponent && typeIndex.IsZeroSized && !typeIndex.IsManagedType && !typeIndex.IsEnableable)
                {
                    cleanupTypes.Add(ComponentType.FromTypeIndex(typeIndex));
                }
            }

            dispatchables = new NativeList<CollectionComponentDispatchable>(cleanupTypes.Length, Allocator.Persistent);
        }

        [BurstCompile]
        static void OnCreateBurst(ref SystemState state, CollectionComponentsReactiveSystem* thisPtr)
        {
            thisPtr->latiosWorld = state.GetLatiosWorldUnmanaged();

            for (int i = 0; i < thisPtr->m_dispatchables.Length; i++)
            {
                var dispatchable = thisPtr->m_dispatchables[i];
                InternalSourceGen.CollectionComponentOperations.CreateQueries(dispatchable.functionPtr, ref state, out var addQuery, out var removeQuery);
                dispatchable.addQuery       = addQuery;
                dispatchable.removeQuery    = removeQuery;
                thisPtr->m_dispatchables[i] = dispatchable;
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityHandle = SystemAPI.GetEntityTypeHandle();
            foreach (var component in m_dispatchables)
            {
                if (!component.addQuery.IsEmptyIgnoreFilter || !component.removeQuery.IsEmptyIgnoreFilter)
                {
                    InternalSourceGen.CollectionComponentOperations.SyncQueries(component.functionPtr, latiosWorld, entityHandle, component.addQuery, component.removeQuery);
                    entityHandle.Update(ref state);
                }
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            m_dispatchables.Dispose();
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderFirst = true)]
    [UpdateAfter(typeof(MergeBlackboardsSystem))]
    [BurstCompile]
    public unsafe partial struct ManagedStructComponentsReactiveSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        struct ManagedStructComponentDispatchable
        {
            public EntityQuery   addQuery;
            public EntityQuery   removeQuery;
            public ComponentType existType;
            public ComponentType cleanupType;
        }

        interface IManagedStructManipulator
        {
            public void Add(Entity entity, ManagedStructComponentStorage storage);
            public void Remove(Entity entity, ManagedStructComponentStorage storage);
            public ComponentType existType { get; }
            public ComponentType cleanupType { get; }
        }

        class ManagedStructManipulator<T> : IManagedStructManipulator where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            T m_default = default;

            public void Add(Entity entity, ManagedStructComponentStorage storage)
            {
                storage.AddComponent(entity, m_default);
            }

            public void Remove(Entity entity, ManagedStructComponentStorage storage)
            {
                storage.RemoveComponent<T>(entity);
            }

            public ComponentType existType => m_default.componentType;
            public ComponentType cleanupType => m_default.cleanupType;
        }

        NativeList<ManagedStructComponentDispatchable> m_dispatchables;
        GCHandle                                       m_manipulatorsHandle;
        //List<IManagedStructManipulator> m_manipulators;

        delegate System.Type GetManagedStructTypeDelegate();

        public void OnCreate(ref SystemState state)
        {
            NativeList<ComponentType> cleanupTypes = default;
            GetAllTagCleanupComponentTypes(ref cleanupTypes, ref m_dispatchables, TypeManager.GetTypeCount());

            var cleanupInterfaceType   = typeof(InternalSourceGen.StaticAPI.IManagedStructComponentCleanup);
            var manipulatorGenericType = typeof(ManagedStructManipulator<>);
            var manipulators           = new List<IManagedStructManipulator>();

            foreach (var cleanupType in cleanupTypes)
            {
                var managedType = cleanupType.GetManagedType();
                if (!cleanupInterfaceType.IsAssignableFrom(managedType))
                    continue;

                var typeMethod  = managedType.GetMethod("GetManagedStructComponentType", BindingFlags.Static | BindingFlags.Public);
                var structType  = (typeMethod.CreateDelegate(typeof(GetManagedStructTypeDelegate)) as GetManagedStructTypeDelegate)();
                var manipulator = Activator.CreateInstance(manipulatorGenericType.MakeGenericType(structType)) as IManagedStructManipulator;
                m_dispatchables.Add(new ManagedStructComponentDispatchable
                {
                    existType   = manipulator.existType,
                    cleanupType = manipulator.cleanupType
                });

                manipulators.Add(manipulator);
            }

            m_manipulatorsHandle = GCHandle.Alloc(manipulators, GCHandleType.Normal);

            OnCreateBurst(ref state, (ManagedStructComponentsReactiveSystem*)UnsafeUtility.AddressOf(ref this));
        }

        [BurstCompile]
        static void GetAllTagCleanupComponentTypes(ref NativeList<ComponentType> cleanupTypes, ref NativeList<ManagedStructComponentDispatchable> dispatchables, int typeCount)
        {
            cleanupTypes = new NativeList<ComponentType>(Allocator.Temp);

            //foreach (var type in TypeManager.AllTypes)
            for (int i = 0; i < typeCount; i++)
            {
                var typeIndex = TypeManager.GetTypeInfo(new TypeIndex { Value = i }).TypeIndex;
                if (typeIndex.IsComponentType && typeIndex.IsCleanupComponent && typeIndex.IsZeroSized && !typeIndex.IsManagedType && !typeIndex.IsEnableable)
                {
                    cleanupTypes.Add(ComponentType.FromTypeIndex(typeIndex));
                }
            }

            dispatchables = new NativeList<ManagedStructComponentDispatchable>(cleanupTypes.Length, Allocator.Persistent);
        }

        [BurstCompile]
        static void OnCreateBurst(ref SystemState state, ManagedStructComponentsReactiveSystem* thisPtr)
        {
            thisPtr->latiosWorld = state.GetLatiosWorldUnmanaged();

            var queryBuilder = new EntityQueryBuilder(Allocator.Temp);

            FixedList32Bytes<ComponentType> exist   = default;
            FixedList32Bytes<ComponentType> cleanup = default;

            for (int i = 0; i < thisPtr->m_dispatchables.Length; i++)
            {
                var dispatchable = thisPtr->m_dispatchables[i];
                exist.Add(dispatchable.existType);
                cleanup.Add(dispatchable.cleanupType);
                dispatchable.addQuery = queryBuilder.WithAll(ref exist).WithNone(ref cleanup).Build(ref state);
                queryBuilder.Reset();
                dispatchable.removeQuery = queryBuilder.WithAll(ref cleanup).WithNone(ref exist).Build(ref state);
                queryBuilder.Reset();
                thisPtr->m_dispatchables[i] = dispatchable;
                exist.Clear();
                cleanup.Clear();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            UnsafeList<int> addIndices    = default;
            UnsafeList<int> removeIndices = default;
            if (FindNonEmptyQueryIndices(m_dispatchables, ref addIndices, ref removeIndices))
            {
                if (!latiosWorld.isValid)
                {
                    UnityEngine.Debug.LogError("LatiosWorldUnmanaged is invalid inside managed struct component processing. This may be a bug. Please report!");
                    return;
                }

                var storage      = latiosWorld.GetManagedStructStorage();
                var manipulators = m_manipulatorsHandle.Target as List<IManagedStructManipulator>;
                if (addIndices.IsCreated)
                {
                    foreach (var i in addIndices)
                    {
                        var                 manipulator  = manipulators[i];
                        NativeArray<Entity> entities     = default;
                        ref var             dispatchable = ref m_dispatchables.ElementAt(i);
                        GetNativeArrayForQuery(dispatchable.addQuery, ref entities);
                        foreach (var entity in entities)
                        {
                            manipulator.Add(entity, storage);
                        }
                        var cleanupType = manipulator.cleanupType;
                        AddComponentToQuery(ref state, ref dispatchable, in cleanupType);
                    }
                }
                if (removeIndices.IsCreated)
                {
                    foreach (var i in addIndices)
                    {
                        var                 manipulator  = manipulators[i];
                        NativeArray<Entity> entities     = default;
                        ref var             dispatchable = ref m_dispatchables.ElementAt(i);
                        GetNativeArrayForQuery(dispatchable.removeQuery, ref entities);
                        foreach (var entity in entities)
                        {
                            manipulator.Remove(entity, storage);
                        }
                        var cleanupType = manipulator.cleanupType;
                        RemoveComponentFromQuery(ref state, ref dispatchable, in cleanupType);
                    }
                }
            }
        }

        [BurstCompile]
        static bool FindNonEmptyQueryIndices(in NativeList<ManagedStructComponentDispatchable> dispatchables, ref UnsafeList<int> addIndices, ref UnsafeList<int> removeIndices)
        {
            int i = 0;
            foreach (var component in dispatchables)
            {
                if (!component.addQuery.IsEmptyIgnoreFilter)
                {
                    if (!addIndices.IsCreated)
                        addIndices = new UnsafeList<int>(8, Allocator.Temp);
                    addIndices.Add(i);
                }
                if (!component.removeQuery.IsEmptyIgnoreFilter)
                {
                    if (!removeIndices.IsCreated)
                        removeIndices = new UnsafeList<int>(8, Allocator.Temp);
                    removeIndices.Add(i);
                }
                i++;
            }
            return addIndices.IsCreated || removeIndices.IsCreated;
        }

        [BurstCompile]
        static void GetNativeArrayForQuery(in EntityQuery query, ref NativeArray<Entity> entities)
        {
            entities = query.ToEntityArray(Allocator.Temp);
        }

        [BurstCompile]
        static void AddComponentToQuery(ref SystemState state, ref ManagedStructComponentDispatchable dispatchable, in ComponentType cleanupType)
        {
            state.EntityManager.AddComponent(dispatchable.addQuery, cleanupType);
        }

        [BurstCompile]
        static void RemoveComponentFromQuery(ref SystemState state, ref ManagedStructComponentDispatchable dispatchable, in ComponentType cleanupType)
        {
            state.EntityManager.RemoveComponent(dispatchable.addQuery, cleanupType);
        }

        public void OnDestroy(ref SystemState state)
        {
            m_dispatchables.Dispose();
            m_manipulatorsHandle.Free();
        }
    }
}

