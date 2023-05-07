using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make these public on release
    internal unsafe struct EffectContext
    {
        public Entity                           effectEntity;
        public AllocatorManager.AllocatorHandle persistentAllocator;
        public AllocatorManager.AllocatorHandle tempAllocator;
        public int                              sampleRate;
        public int                              frameSize;
        public int                              currentFrame;

        internal SampleFramePool* sampleFramePool;
    }

    internal enum StackType : byte
    {
        Source,
        Listener
    }

    internal unsafe struct UpdateContext
    {
        public Entity stackEntity;
        public TransformQvvs stackTransform => *stackTransformPtr;
        public int       indexInStack;
        public uint      layerMask;
        public bool      isCulled;
        public StackType stackType;

        internal TransformQvvs*                                 stackTransformPtr;
        internal NativeHashMap<Entity, VirtualOutputEffect.Ptr> virtualOutputsMap;
        internal int                                            currentFrame;

        public bool TryGetVirtualOutput(Entity virtualOutputEffectEntity, out SampleFrame.ReadOnly virtualFrame)
        {
            if (virtualOutputsMap.TryGetValue(virtualOutputEffectEntity, out var ptr))
            {
                return ptr.ptr->TryGetFrame(stackEntity, indexInStack, currentFrame, out virtualFrame);
            }
            virtualFrame = default;
            return false;
        }
    }

    internal unsafe struct SpatialCullingContext
    {
        internal UnsafeList<Interop.ListenerStackMetadata.Ptr> listeners;
        internal Interop.SourceStackMetadata*                  sourcePtr;

        public int listenerCount => listeners.Length;
        public Entity sourceEntity => sourcePtr->sourceEntity;
        public TransformQvvs sourceTransform => sourcePtr->worldTransform;
        public byte sourceLayerIndex => sourcePtr->layerIndex;

        public Entity GetListenerEntity(int index) => listeners[index].ptr->listenerEntity;
        public TransformQvvs GetListenerTransform(int index) => listeners[index].ptr->worldTransform;
        public uint GetListenerLayerMask(int index) => listeners[index].ptr->layerMask;
        public bool TryGetListenerProperty<T>(int index, out DSPRef<T> property) where T : unmanaged, IListenerProperty
        {
            var ptr           = listeners[index];
            var requestedType = ComponentType.ReadWrite<T>();
            for (int i = 0; i < ptr.ptr->listenerPropertiesCount; i++)
            {
                if (ptr.ptr->listenerProperties[i].propertyType.TypeIndex == requestedType.TypeIndex)
                {
                    property = new DSPRef<T>((T*)ptr.ptr->listenerProperties[i].propertyPtr);
                    return true;
                }
            }

            property = default;
            return false;
        }
    }

    internal struct CullArray
    {
        internal NativeArray<bool> cullArray;

        public int length => cullArray.Length;
        public void Cull(int index) => cullArray[index] = false;
        public bool IsCulled(int index) => cullArray[index] == false;
    }

    internal unsafe struct SpatialUpdateContext
    {
        public Entity sourceEntity => sourcePtr->sourceEntity;
        public Entity listenerEntity => listenerPtr->listenerEntity;
        public TransformQvvs sourceTransform => sourcePtr->worldTransform;
        public TransformQvvs listenerTransform => listenerPtr->worldTransform;
        public uint listenerLayerMask => listenerPtr->layerMask;
        public byte sourceLayerIndex => sourcePtr->layerIndex;

        public int  indexInStack;
        public int  indexOfListener;
        public bool isCulled;

        internal Interop.SourceStackMetadata*                   sourcePtr;
        internal Interop.ListenerStackMetadata*                 listenerPtr;
        internal NativeHashMap<Entity, VirtualOutputEffect.Ptr> virtualOutputsMap;
        internal int                                            currentFrame;

        public bool TryGetListenerProperty<T>(out DSPRef<T> property) where T : unmanaged, IListenerProperty
        {
            var requestedType = ComponentType.ReadWrite<T>();
            for (int i = 0; i < listenerPtr->listenerPropertiesCount; i++)
            {
                if (listenerPtr->listenerProperties[i].propertyType.TypeIndex == requestedType.TypeIndex)
                {
                    property = new DSPRef<T>((T*)listenerPtr->listenerProperties[i].propertyPtr);
                    return true;
                }
            }

            property = default;
            return false;
        }

        public bool TryGetVirtualOutput(Entity virtualOutputEffectEntity, out SampleFrame.ReadOnly virtualFrame)
        {
            if (virtualOutputsMap.TryGetValue(virtualOutputEffectEntity, out var ptr))
            {
                return ptr.ptr->TryGetFrame(sourceEntity, indexInStack, currentFrame, out virtualFrame);
            }
            virtualFrame = default;
            return false;
        }
    }

    internal unsafe readonly ref struct DSPRef<T> where T : unmanaged
    {
        readonly T* m_t;

        internal DSPRef(T* t)
        {
            m_t = t;
        }

        public ref readonly T Value => ref *m_t;
    }

    // Keep internal
    internal struct SampleFramePool : IDisposable
    {
        AllocatorManager.AllocatorHandle m_allocator;
        UnsafeList<SampleFrame>          m_unusedFrames;

        public SampleFramePool(AllocatorManager.AllocatorHandle allocator)
        {
            m_allocator    = allocator;
            m_unusedFrames = new UnsafeList<SampleFrame>(16, m_allocator);
        }

        public void Dispose()
        {
            foreach (var frame in m_unusedFrames)
            {
                CollectionHelper.Dispose(frame.left);
                CollectionHelper.Dispose(frame.right);
            }
            m_unusedFrames.Dispose();
        }

        public SampleFrame Acquire(int frameSize)
        {
            if (m_unusedFrames.IsEmpty)
            {
                return new SampleFrame
                {
                    left  = CollectionHelper.CreateNativeArray<float>(frameSize, m_allocator, NativeArrayOptions.UninitializedMemory),
                    right = CollectionHelper.CreateNativeArray<float>(frameSize, m_allocator, NativeArrayOptions.UninitializedMemory),
                };
            }
            var result = m_unusedFrames[0];
            m_unusedFrames.RemoveAtSwapBack(0);
            return result;
        }

        public void Release(SampleFrame frame)
        {
            m_unusedFrames.Add(in frame);
        }
    }
}

