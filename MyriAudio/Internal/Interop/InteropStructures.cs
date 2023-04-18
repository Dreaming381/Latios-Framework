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

        // Written in DSP Thread
        public bool requiresUpdateWhenCulled;
        public bool requiresUpdateWhenInputFrameDisconnected;
        public bool isVirtualOutput;

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

        // Written in DSP Thread
        public bool requiresUpdateWhenCulled;
        public bool requiresUpdateWhenInputFrameDisconnected;

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

        // Written in DSP Thread
        public bool requiresUpdateWhenCulled;
        public bool requiresUpdateWhenInputFrameDisconnected;
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
        public UnsafeList<SourceStackMetadata.Ptr> updatedSourceStacks;
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

        internal unsafe struct Ptr
        {
            public SourceStackMetadata* ptr;
        }
    }

    #endregion

    #region Listener Stacks
    internal struct ListenerStacksUpdateBuffer
    {
        public UnsafeList<ListenerStackMetadata.Ptr> newListenerStacks;
        public UnsafeList<ListenerStackMetadata.Ptr> updatedListenerStacks;
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
        public ListenerPropertyPtr*                      listenerProperties;
        public DSP.StateVariableFilter.Channel*          listenerProfileFilters;
        public int                                       listenerId;
        public int                                       effectIDsCount;
        public int                                       listenerPropertiesCount;
        public uint                                      layerMask;

        // Written by DSP Thread
        public bool hasVirtualOutput;

        internal unsafe struct Ptr
        {
            public ListenerStackMetadata* ptr;
        }
    }

    internal unsafe struct ListenerPropertyPtr
    {
        public void*         propertyPtr;
        public ComponentType propertyType;
    }
    #endregion
}

