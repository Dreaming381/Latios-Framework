using UnityEngine;

namespace Latios.LifeFX
{
    /// <summary>
    /// Don't touch this. See GraphicsEventTunnel<T> instead.
    /// </summary>
    public abstract class GraphicsEventTunnelBase : ScriptableObject
    {
        internal abstract TypeInfo GetEventType();

        internal abstract int GetEventIndex();

        internal struct TypeInfo
        {
            public System.Type type;
            public int         size;
            public int         alignment;
        }
    }
}

