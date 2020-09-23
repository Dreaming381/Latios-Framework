using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios
{
    /// <summary>
    /// A toolkit for copying components based on ComponentType.
    /// </summary>
    public unsafe class EntityDataCopyKit
    {
        private EntityManager m_em;

        private delegate void EmSetSharedComponentDataRaw(Entity entity, int typeIndex, object componentData);
        private delegate void EmSetComponentDataRaw(Entity entity, int typeIndex, void* data, int size);
        private delegate void* EmGetComponentDataRawRO(Entity entity, int typeIndex);
        private delegate void* EmGetComponentDataRawRW(Entity entity, int typeIndex);
        private delegate object EmGetSharedComponentData(Entity entity, int typeIndex);
        private delegate void* EmGetBufferRawRW(Entity entity, int typeIndex);
        private delegate void* EmGetBufferRawRO(Entity entity, int typeIndex);
        private delegate int EmGetBufferLength(Entity entity, int typeIndex);

        private EmSetSharedComponentDataRaw emSetSharedComponentDataRaw;
        private EmSetComponentDataRaw       emSetComponentDataRaw;
        private EmGetComponentDataRawRO     emGetComponentDataRawRO;
        private EmGetComponentDataRawRW     emGetComponentDataRawRW;
        private EmGetSharedComponentData    emGetSharedComponentData;
        private EmGetBufferRawRW            emGetBufferRawRW;
        private EmGetBufferRawRO            emGetBufferRawRO;
        private EmGetBufferLength           emGetBufferLength;

        private Dictionary<Type, Type> m_typeTagsToTypesCache = new Dictionary<Type, Type>();

        public EntityDataCopyKit(EntityManager entityManager)
        {
            m_em = entityManager;

            var emType                  = typeof(EntityManager);
            var methodInfo              = GetMethod("SetSharedComponentDataBoxedDefaultMustBeNull", 3);
            emSetSharedComponentDataRaw = methodInfo.CreateDelegate(typeof(EmSetSharedComponentDataRaw), m_em) as EmSetSharedComponentDataRaw;
            methodInfo                  = emType.GetMethod("SetComponentDataRaw", BindingFlags.Instance | BindingFlags.NonPublic);
            emSetComponentDataRaw       = methodInfo.CreateDelegate(typeof(EmSetComponentDataRaw), m_em) as EmSetComponentDataRaw;
            methodInfo                  = emType.GetMethod("GetComponentDataRawRO", BindingFlags.Instance | BindingFlags.NonPublic);
            emGetComponentDataRawRO     = methodInfo.CreateDelegate(typeof(EmGetComponentDataRawRO), m_em) as EmGetComponentDataRawRO;
            methodInfo                  = emType.GetMethod("GetComponentDataRawRW", BindingFlags.Instance | BindingFlags.NonPublic);
            emGetComponentDataRawRW     = methodInfo.CreateDelegate(typeof(EmGetComponentDataRawRW), m_em) as EmGetComponentDataRawRW;
            methodInfo                  = GetMethod("GetSharedComponentData", 2);
            emGetSharedComponentData    = methodInfo.CreateDelegate(typeof(EmGetSharedComponentData), m_em) as EmGetSharedComponentData;
            methodInfo                  = emType.GetMethod("GetBufferRawRW", BindingFlags.Instance | BindingFlags.NonPublic);
            emGetBufferRawRW            = methodInfo.CreateDelegate(typeof(EmGetBufferRawRW), m_em) as EmGetBufferRawRW;
            methodInfo                  = emType.GetMethod("GetBufferRawRO", BindingFlags.Instance | BindingFlags.NonPublic);
            emGetBufferRawRO            = methodInfo.CreateDelegate(typeof(EmGetBufferRawRO), m_em) as EmGetBufferRawRO;
            methodInfo                  = emType.GetMethod("GetBufferLength", BindingFlags.Instance | BindingFlags.NonPublic);
            emGetBufferLength           = methodInfo.CreateDelegate(typeof(EmGetBufferLength), m_em) as EmGetBufferLength;
        }

        /// <summary>
        /// Copies the data stored in the componentType from the src entity to the dst entity.>
        /// </summary>
        /// <param name="src">The source entity</param>
        /// <param name="dst">The destination entity</param>
        /// <param name="componentType">The type of data to be copied</param>
        /// <param name="copyCollections">Should collection components be shallow copied (true) or ignored (false)?</param>
        public void CopyData(Entity src, Entity dst, ComponentType componentType, bool copyCollections = false)
        {
            //Check to ensure dst has componentType
            if (!m_em.HasComponent(dst, componentType))
                m_em.AddComponent(dst, componentType);
            if (componentType.IsSharedComponent)
                CopyScd(src, dst, componentType);
            else if (componentType.IsBuffer)
                CopyBuffer(src, dst, componentType);
            else
            {
                if (copyCollections)
                {
                    var type = componentType.GetManagedType();
                    if (type.IsConstructedGenericType)
                    {
                        var genType = type.GetGenericTypeDefinition();
                        if (genType == typeof(ManagedComponentSystemStateTag<>))
                        {
                            if (!m_typeTagsToTypesCache.TryGetValue(type, out Type managedType))
                            {
                                managedType                  = type.GenericTypeArguments[0];
                                m_typeTagsToTypesCache[type] = managedType;
                            }
                            LatiosWorld lw = m_em.World as LatiosWorld;
                            lw.ManagedStructStorage.CopyComponent(src, dst, managedType);
                        }
                        else if (genType == typeof(CollectionComponentSystemStateTag<>))
                        {
                            if (!m_typeTagsToTypesCache.TryGetValue(type, out Type managedType))
                            {
                                managedType                  = type.GenericTypeArguments[0];
                                m_typeTagsToTypesCache[type] = managedType;
                            }
                            LatiosWorld lw = m_em.World as LatiosWorld;
                            lw.CollectionComponentStorage.CopyComponent(src, dst, managedType);
                        }
                    }
                }
                CopyIcd(src, dst, componentType);
            }
        }

        private void CopyIcd(Entity src, Entity dst, ComponentType componentType)
        {
            if (componentType.IsZeroSized)
                return;

            var typeInfo = TypeManager.GetTypeInfo(componentType.TypeIndex);
            var size     = typeInfo.SizeInChunk;
            var data     = emGetComponentDataRawRO(src, componentType.TypeIndex);
            emSetComponentDataRaw(dst, componentType.TypeIndex, data, size);
        }

        private void CopyScd(Entity src, Entity dst, ComponentType componentType)
        {
            var data = emGetSharedComponentData(src, componentType.TypeIndex);
            emSetSharedComponentDataRaw(dst, componentType.TypeIndex, data);
        }

        private void CopyBuffer(Entity src, Entity dst, ComponentType componentType)
        {
            int length      = emGetBufferLength(src, componentType.TypeIndex);
            var typeInfo    = TypeManager.GetTypeInfo(componentType.TypeIndex);
            var elementSize = typeInfo.ElementSize;
            var alignment   = typeInfo.AlignmentInBytes;

            FakeBufferHeader* dstHeader = (FakeBufferHeader*)emGetComponentDataRawRW(dst, componentType.TypeIndex);
            FakeBufferHeader.EnsureCapacity(dstHeader, length, elementSize, alignment, FakeBufferHeader.TrashMode.RetainOldData, false, 0);
            var dstBufferPtr = emGetBufferRawRW(dst, componentType.TypeIndex);
            var srcBufferPtr = emGetBufferRawRO(src, componentType.TypeIndex);
            UnsafeUtility.MemCpy(dstBufferPtr, srcBufferPtr, elementSize * length);
            dstHeader->Length = length;
        }

        private MethodInfo GetMethod(string methodName, int numOfArgs)
        {
            var methods = typeof(EntityManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.Name == methodName && method.GetParameters().Length == numOfArgs)
                    return method;
            }
            return null;
        }
    }
}

