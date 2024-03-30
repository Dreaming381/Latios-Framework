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
        public Entity stackEntity => stackType == StackType.Source ? sourcePtr->sourceEntity : listenerPtr->listenerEntity;
        public TransformQvvs stackTransform => stackType == StackType.Source ? sourcePtr->worldTransform : listenerPtr->worldTransform;
        public int indexInStack;
        public uint layerMask => stackType == StackType.Source ? (1u << sourcePtr->layerIndex) : listenerPtr->layerMask;
        public bool      isCulled;
        public StackType stackType;

        internal void* metadataPtr;
        internal Interop.SourceStackMetadata* sourcePtr => (Interop.SourceStackMetadata*)metadataPtr;
        internal Interop.ListenerStackMetadata* listenerPtr => (Interop.ListenerStackMetadata*)metadataPtr;
        internal NativeHashMap<Entity, VirtualOutputEffect.Ptr> virtualOutputsMap;
        internal NativeHashMap<ResourceKey, ResourceValue>      resourcesMap;
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

        public bool TryGetResourceComponent<T>(Entity resourceEntity, out DSPRef<T> resource) where T : unmanaged, IResourceComponent
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceComponentMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new DSPRef<T>((T*)metadataPtr->componentPtr);
                return true;
            }
            resource = default;
            return false;
        }

        public bool TryGetResourceBuffer<T>(Entity resourceEntity, out ReadOnlySpan<T> resource) where T : unmanaged, IResourceBuffer
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceBufferMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new ReadOnlySpan<T>(metadataPtr->bufferPtr, metadataPtr->elementCount);
                return true;
            }
            resource = default;
            return false;
        }
    }

    internal unsafe struct SpatialCullingContext
    {
        internal UnsafeList<Interop.ListenerStackMetadata.Ptr> listeners;
        internal Interop.SourceStackMetadata*                  sourcePtr;
        internal NativeHashMap<ResourceKey, ResourceValue>     resourcesMap;

        public int listenerCount => listeners.Length;
        public Entity sourceEntity => sourcePtr->sourceEntity;
        public TransformQvvs sourceTransform => sourcePtr->worldTransform;
        public byte sourceLayerIndex => sourcePtr->layerIndex;

        public Entity GetListenerEntity(int index) => listeners[index].ptr->listenerEntity;
        public TransformQvvs GetListenerTransform(int index) => listeners[index].ptr->worldTransform;
        public uint GetListenerLayerMask(int index) => listeners[index].ptr->layerMask;

        public bool TryGetResourceComponent<T>(Entity resourceEntity, out DSPRef<T> resource) where T : unmanaged, IResourceComponent
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceComponentMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new DSPRef<T>((T*)metadataPtr->componentPtr);
                return true;
            }
            resource = default;
            return false;
        }

        public bool TryGetResourceBuffer<T>(Entity resourceEntity, out ReadOnlySpan<T> resource) where T : unmanaged, IResourceBuffer
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceBufferMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new ReadOnlySpan<T>(metadataPtr->bufferPtr, metadataPtr->elementCount);
                return true;
            }
            resource = default;
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
        internal NativeHashMap<ResourceKey, ResourceValue>      resourcesMap;
        internal int                                            currentFrame;

        public bool TryGetResourceComponent<T>(Entity resourceEntity, out DSPRef<T> resource) where T : unmanaged, IResourceComponent
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceComponentMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new DSPRef<T>((T*)metadataPtr->componentPtr);
                return true;
            }
            resource = default;
            return false;
        }

        public bool TryGetResourceBuffer<T>(Entity resourceEntity, out ReadOnlySpan<T> resource) where T : unmanaged, IResourceBuffer
        {
            var key         = new ResourceKey { componentType = ComponentType.ReadWrite<T>(), entity = resourceEntity };
            var result                                                                               = resourcesMap.TryGetValue(key, out var candidate);
            var metadataPtr                                                                          = (Interop.ResourceBufferMetadata*)candidate.metadataPtr;
            if (metadataPtr->enabled)
            {
                resource = new ReadOnlySpan<T>(metadataPtr->bufferPtr, metadataPtr->elementCount);
                return true;
            }
            resource = default;
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

