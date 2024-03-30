using System;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal unsafe struct IldBufferChannel
    {
        public float* buffer;
    }

    internal unsafe struct IldBuffer
    {
        [NativeDisableUnsafePtrRestriction]
        public IldBufferChannel* bufferChannels;
        public int               channelCount;
        public int               frame;
        public int               bufferId;
        public int               framesInBuffer;
        public int               framesPerUpdate;
        public bool              warnIfStarved;
    }
}

namespace Latios.Myri.Interop
{
    internal unsafe struct DspUpdateBuffer
    {
        public int bufferId;

        public PresampledAndTimingUpdateBuffer presampledAndTimingUpdateBuffer;
        public EffectsUpdateBuffer             effectsUpdateBuffer;
        public SourceStacksUpdateBuffer        sourceStacksUpdateBuffer;
        public ListenerStacksUpdateBuffer      listenerStacksUpdateBuffer;
        public ResourcesUpdateBuffer           resourcesUpdateBuffer;
        public EnabledStatesUpdateBuffer       enabledStatesUpdateBuffer;

        public BrickwallLimiterSettings masterLimiterSettings;
    }

    #region Pre-sampled
    internal unsafe struct PresampledAndTimingUpdateBuffer
    {
        public UnsafeList<PresampledBufferForListener> presampledBuffersForListeners;
        public int                                     frame;
        public int                                     framesInBuffer;
        public int                                     framesPerUpdate;
        public bool                                    warnIfStarved;
    }

    internal unsafe struct PresampledBufferForListener : IComparable<PresampledBufferForListener>
    {
        public float* samples;
        public int    listenerId;
        public int    channelIndex;

        public int CompareTo(PresampledBufferForListener other)
        {
            var result = listenerId.CompareTo(other.listenerId);
            if (result == 0)
                return channelIndex.CompareTo(other.channelIndex);
            return result;
        }
    }
    #endregion

    #region Effects
    internal unsafe struct EffectsUpdateBuffer
    {
        public UnsafeList<EffectMetadata.Ptr>     newEffects;
        public UnsafeList<int>                    deadEffectIDs;
        public UnsafeList<EffectParametersUpdate> updatedEffects;

        public UnsafeList<SpatialEffectMetadata.Ptr> newSpatialEffects;
        public UnsafeList<int>                       deadSpatialEffectIDs;
        public UnsafeList<EffectParametersUpdate>    updatedSpatialEffects;
    }

    // Created in worker threads, then modification ownership is transferred to DSP until after DSP processes the effect being destroyed.
    internal unsafe struct EffectMetadata
    {
        public Entity                                                                   effectEntity;
        public FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchEffectDelegate> functionPtr;
        public void*                                                                    parametersPtr;
        public void*                                                                    effectPtr;
        public int                                                                      effectId;
        public bool                                                                     enabled;
        public bool                                                                     requiresUpdateWhenCulled;
        public bool                                                                     requiresUpdateWhenInputFrameDisconnected;
        public bool                                                                     isVirtualOutput;

        internal unsafe struct Ptr
        {
            public EffectMetadata* ptr;
        }
    }

    // Created in worker threads, then modification ownership is transferred to DSP until after DSP processes the effect being destroyed.
    internal unsafe struct SpatialEffectMetadata
    {
        public Entity                                                                          effectEntity;
        public FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchSpatialEffectDelegate> functionPtr;
        public void*                                                                           parametersPtr;
        public void*                                                                           effectPtr;
        public int                                                                             effectId;
        public bool                                                                            enabled;
        public bool                                                                            requiresUpdateWhenCulled;
        public bool                                                                            requiresUpdateWhenInputFrameDisconnected;

        internal unsafe struct Ptr
        {
            public SpatialEffectMetadata* ptr;
        }
    }

    internal unsafe struct EffectParametersUpdate
    {
        public void* effectMetadataPtr;
        public void* newParametersPtr;
    }
    #endregion

    #region Stacks Common
    internal struct EffectIDInStack
    {
        public int  effectId;
        public bool isSpatialEffect;
        public bool requiresUpdateWhenCulled;
        public bool requiresUpdateWhenInputFrameDisconnected;
        public bool isVirtualOutput;
    }

    internal unsafe struct StackTransformUpdate
    {
        public TransformQvvs worldTransform;
        public void*         StackMetadataPtr;
    }
    #endregion

    #region Source Stacks
    internal unsafe struct SourceStacksUpdateBuffer
    {
        public UnsafeList<SourceStackMetadata.Ptr> newSourceStacks;
        public UnsafeList<SourceStackUpdate>       updatedSourceStacks;
        public UnsafeList<StackTransformUpdate>    updatedSourceStackTransforms;
        public UnsafeList<int>                     deadSourceStackIDs;
    }

    internal unsafe struct SourceStackMetadata
    {
        public TransformQvvs    worldTransform;
        public Entity           sourceEntity;
        public EffectIDInStack* effectIDs;
        public int              sourceId;
        public int              effectIDsCount;
        public byte             layerIndex;
        public bool             enabled;

        internal unsafe struct Ptr
        {
            public SourceStackMetadata* ptr;
        }
    }

    internal unsafe struct SourceStackUpdate
    {
        public EffectIDInStack* effectIDs;
        public int              sourceId;
        public int              effectIDsCount;
        public byte             layerIndex;
    }

    #endregion

    #region Listener Stacks
    internal struct ListenerStacksUpdateBuffer
    {
        public UnsafeList<ListenerStackMetadata.Ptr> newListenerStacks;
        public UnsafeList<ListenerStackUpdate>       updatedListenerStacks;
        public UnsafeList<StackTransformUpdate>      updatedListenerStackTransforms;
        public UnsafeList<int>                       deadListenerStackIDs;
    }

    internal unsafe struct ListenerStackMetadata
    {
        public TransformQvvs                             worldTransform;
        public BrickwallLimiterSettings                  limiterSettings;
        public BlobAssetReference<ListenerProfileBlobV2> listenerProfileBlob;
        public Entity                                    listenerEntity;
        public EffectIDInStack*                          effectIDs;
        public DSP.StateVariableFilter.Channel*          listenerProfileFilters;
        public int                                       listenerId;
        public int                                       effectIDsCount;
        public uint                                      layerMask;
        public bool                                      listenerEnabled;
        public bool                                      stackEnabled;
        public bool                                      hasVirtualOutput;

        internal unsafe struct Ptr
        {
            public ListenerStackMetadata* ptr;
        }
    }

    internal unsafe struct ListenerStackUpdate
    {
        public BrickwallLimiterSettings                  limiterSettings;
        public BlobAssetReference<ListenerProfileBlobV2> listenerProfileBlob;
        public EffectIDInStack*                          effectIDs;
        public DSP.StateVariableFilter.Channel*          listenerProfileFilters;
        public int                                       listenerId;
        public int                                       effectIDsCount;
        public uint                                      layerMask;
        public bool                                      hasVirtualOutput;
    }
    #endregion

    #region Resources
    internal struct ResourcesUpdateBuffer
    {
        public UnsafeList<ResourceComponentMetadata.Ptr> newComponentResources;
        public UnsafeList<ResourceComponentUpdate>       updatedComponentResources;
        public UnsafeList<int>                           deadComponentResourceIDs;
        public UnsafeList<ResourceBufferMetadata.Ptr>    newBufferResources;
        public UnsafeList<ResourceBufferUpdate>          updatedBufferResources;
        public UnsafeList<int>                           deadBufferResourceIDs;
    }

    internal unsafe struct ResourceComponentMetadata
    {
        public void*         componentPtr;
        public Entity        resourceEntity;
        public ComponentType resourceType;
        public int           resourceComponentId;
        public bool          enabled;

        internal unsafe struct Ptr
        {
            public ResourceComponentMetadata* ptr;
        }
    }

    internal unsafe struct ResourceComponentUpdate
    {
        public ResourceComponentMetadata* metadataPtr;
        public void*                      newComponentPtr;
    }

    internal unsafe struct ResourceBufferMetadata
    {
        public void*         bufferPtr;
        public Entity        resourceEntity;
        public ComponentType resourceType;
        public int           resourceBufferId;
        public int           elementCount;
        public bool          enabled;

        internal unsafe struct Ptr
        {
            public ResourceBufferMetadata* ptr;
        }
    }

    internal unsafe struct ResourceBufferUpdate
    {
        public ResourceBufferMetadata* metadataPtr;
        public void*                   newBufferPtr;
        public int                     newElementCount;
    }
    #endregion

    #region EnabledStatuses
    internal struct EnabledStatesUpdateBuffer
    {
        public UnsafeList<EnabledStatusUpdate> updatedEnabledStates;
    }

    internal enum EnabledStatusMetadataType
    {
        Effect,
        SpatialEffect,
        SourceStack,
        Listener,
        ListenerStack,
        ResourceComponent,
        ResourceBuffer,
    }

    internal unsafe struct EnabledStatusUpdate
    {
        public void*                     metadataPtr;
        public EnabledStatusMetadataType type;
        public bool                      enabled;
    }
    #endregion
}

