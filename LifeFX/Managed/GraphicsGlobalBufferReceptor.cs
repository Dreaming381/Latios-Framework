using Latios.Transforms;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;

namespace Latios.LifeFX
{
    /// <summary>
    /// A base class or standalone provider of a GraphicsBuffer populated by entity data and bound to a global shader property.
    /// Attach this to the same GameObject that has a GameObjectEntity component.
    /// </summary>
    /// <remarks>
    /// Supported built-in shader properties:
    /// _latiosDeformBuffer
    /// _latiosBoneTransforms
    /// </remarks>
    [AddComponentMenu("Latios/LifeFX/Graphics Global Buffer Receptor (LifeFX)")]
    public class GraphicsGlobalBufferReceptor : MonoBehaviour, IInitializeGameObjectEntity
    {
        [SerializeField] private string m_bufferShaderProperty;
        private int                     m_propertyId;

        public delegate void OnGraphicsGlobalPublishedDelegate(GraphicsBuffer graphicsBuffer);
        public event OnGraphicsGlobalPublishedDelegate      OnGraphicsGlobalPublished;
        [SerializeField] private UnityEvent<GraphicsBuffer> OnGraphicsGlobalPublishedSerialized;

        // Called from an ECS system
        public virtual void Publish(GraphicsBuffer graphicsBuffer)
        {
        }

        // IInitializeGameObjectEntity method, automatically called.
        public void Initialize(LatiosWorld latiosWorld, Entity gameObjectEntity)
        {
            if (string.IsNullOrEmpty(m_bufferShaderProperty))
                return;

            m_propertyId = Shader.PropertyToID(m_bufferShaderProperty);
            DynamicBuffer<GraphicsGlobalBufferDestination> buffer;
            if (latiosWorld.EntityManager.HasBuffer<GraphicsGlobalBufferDestination>(gameObjectEntity))
                buffer = latiosWorld.EntityManager.GetBuffer<GraphicsGlobalBufferDestination>(gameObjectEntity);
            else
                buffer = latiosWorld.EntityManager.AddBuffer<GraphicsGlobalBufferDestination>(gameObjectEntity);
            buffer.Add(new GraphicsGlobalBufferDestination
            {
                requestor        = this,
                shaderPropertyId = m_propertyId,
            });
        }

        internal void PublishInternal(GraphicsBuffer graphicsBuffer)
        {
            Publish(graphicsBuffer);
            OnGraphicsGlobalPublished?.Invoke(graphicsBuffer);
            OnGraphicsGlobalPublishedSerialized?.Invoke(graphicsBuffer);
        }
    }
}

