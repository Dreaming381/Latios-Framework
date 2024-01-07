using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios
{
    /// <summary>
    /// A toolkit for copying components based on ComponentType.
    /// </summary>
    public unsafe class EntityDataCopyKit
    {
        private EntityManager m_em;

        private Dictionary<Type, Type> m_typeTagsToTypesCache = new Dictionary<Type, Type>();

        public EntityDataCopyKit(EntityManager entityManager)
        {
            m_em = entityManager;
        }

        /// <summary>
        /// Copies the data stored in the componentType from the src entity to the dst entity.>
        /// </summary>
        /// <param name="src">The source entity</param>
        /// <param name="dst">The destination entity</param>
        /// <param name="componentType">The type of data to be copied</param>
        public void CopyData(Entity src, Entity dst, ComponentType componentType)
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
                // Todo: Support copying managed struct components
                CopyIcd(src, dst, componentType);
            }
        }

        private void CopyIcd(Entity src, Entity dst, ComponentType componentType)
        {
            m_em.CopyComponentData(src, dst, componentType);
        }

        private void CopyScd(Entity src, Entity dst, ComponentType componentType)
        {
            m_em.CopySharedComponent(src, dst, componentType);
        }

        private void CopyBuffer(Entity src, Entity dst, ComponentType componentType)
        {
            m_em.CopyDynamicBuffer(src, dst, componentType);
        }
    }
}

