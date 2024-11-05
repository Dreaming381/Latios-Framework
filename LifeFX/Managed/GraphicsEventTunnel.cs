using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Latios.LifeFX
{
    /// <summary>
    /// Subclass this type to create a GPU event tunnel class of a particular event type.
    /// Then, create instances of the class in the editor and connect them between GameObjects and entities.
    /// </summary>
    /// <typeparam name="T">The GPU event type</typeparam>
    public abstract class GraphicsEventTunnel<T> : GraphicsEventTunnelBase where T : unmanaged
    {
        internal sealed override TypeInfo GetEventType() => new TypeInfo
        {
            type      = typeof(T),
            size      = UnsafeUtility.SizeOf<T>(),
            alignment = UnsafeUtility.AlignOf<T>(),
        };

        internal sealed override int GetEventIndex()
        {
            GraphicsEventTypeRegistry.Init();
            return GraphicsEventTypeRegistry.TypeToIndex<T>.typeToIndex;
        }
    }
}

