using Latios.Transforms;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;

namespace Latios.LifeFX
{
    /// <summary>
    /// A base class or standalone provider of a GPU events sent to a specific tunnel in the form of an element range within a GraphicsBuffer.
    /// Attach this to the same GameObject that has a GameObjectEntity component.
    /// </summary>
    [AddComponentMenu("Latios/LifeFX/Graphics Event Buffer Receptor (LifeFX)")]
    public class GraphicsEventBufferReceptor : MonoBehaviour, IInitializeGameObjectEntity
    {
        [SerializeField] private GraphicsEventTunnelBase tunnel;

        public delegate void OnGraphicsEventPublishedDelegate(GraphicsBuffer graphicsBuffer, int startIndex, int count);
        public event OnGraphicsEventPublishedDelegate                 OnGraphicsEventPublished;
        [SerializeField] private UnityEvent<GraphicsBuffer, int, int> OnGraphicsEventPublishedSerialized;

        // Called from an ECS system
        public virtual void Publish(GraphicsBuffer graphicsBuffer, int startIndex, int count)
        {
        }

        // IInitializeGameObjectEntity method, automatically called.
        public void Initialize(LatiosWorld latiosWorld, Entity gameObjectEntity)
        {
            if (tunnel == null)
                return;
            DynamicBuffer<GraphicsEventTunnelDestination> buffer;
            if (latiosWorld.EntityManager.HasBuffer<GraphicsEventTunnelDestination>(gameObjectEntity))
                buffer = latiosWorld.EntityManager.GetBuffer<GraphicsEventTunnelDestination>(gameObjectEntity);
            else
                buffer = latiosWorld.EntityManager.AddBuffer<GraphicsEventTunnelDestination>(gameObjectEntity);
            buffer.Add(new GraphicsEventTunnelDestination
            {
                tunnel         = GraphicsEventTypeRegistry.s_eventHashManager.Data[tunnel],
                requestor      = this,
                eventTypeIndex = tunnel.GetEventIndex(),
            });
        }

        internal void PublishInternal(GraphicsBuffer graphicsBuffer, int startIndex, int count)
        {
            Publish(graphicsBuffer, startIndex, count);
            OnGraphicsEventPublished?.Invoke(graphicsBuffer, startIndex, count);
            OnGraphicsEventPublishedSerialized?.Invoke(graphicsBuffer, startIndex, count);
        }
    }
}

