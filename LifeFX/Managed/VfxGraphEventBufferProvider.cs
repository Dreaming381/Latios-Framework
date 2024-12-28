using UnityEngine;
using UnityEngine.VFX;

namespace Latios.LifeFX
{
    /// <summary>
    /// Feeds GPU events from ECS data to a VFX Graph. Attach this to a VFX Graph GameObject with the GameObjectEntity component.
    /// </summary>
    [AddComponentMenu("Latios/LifeFX/VFX Graph Event Buffer Provider (LifeFX)")]
    public class VfxGraphEventBufferProvider : GraphicsEventBufferReceptor
    {
        [Header("VFX Graph Properties")]
        [SerializeField] private string buffer;
        [SerializeField] private string start;
        [SerializeField] private string count;

        private int bufferId;
        private int startId;
        private int countId;

        private bool hasBuffer     = false;
        private bool hasStart      = false;
        private bool hasCount      = false;
        private bool isInitialized = false;

        private VisualEffect effect;

        public override void Publish(GraphicsBuffer graphicsBuffer, int startIndex, int eventCount)
        {
            if (!isInitialized)
            {
                isInitialized = true;
                TryGetComponent(out effect);
                if (effect == null)
                    return;

                if (!string.IsNullOrEmpty(buffer))
                {
                    hasBuffer = true;
                    bufferId  = Shader.PropertyToID(buffer);
                }
                if (!string.IsNullOrEmpty(start))
                {
                    hasStart = true;
                    startId  = Shader.PropertyToID(start);
                }
                if (!string.IsNullOrEmpty(count))
                {
                    hasCount = true;
                    countId  = Shader.PropertyToID(count);
                }
            }

            if (effect == null)
                return;

            if (hasBuffer)
                effect.SetGraphicsBuffer(bufferId, graphicsBuffer);
            if (hasStart)
                effect.SetInt(startId, startIndex);
            if (hasCount)
                effect.SetInt(countId, eventCount);
        }
    }
}

