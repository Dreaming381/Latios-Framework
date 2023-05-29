using System;
using Latios.Myri.DSP;
using Latios.Myri.InternalSourceGen;
using Latios.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    internal unsafe partial struct MyriMegaKernel
    {
        struct SamplingCache : IDisposable
        {
            public UnsafeList<VirtualOutputEffect.Ptr> sourceVirtualOutputs;
            public NativeList<bool>                    cullList;

            public SamplingCache(AllocatorManager.AllocatorHandle allocator)
            {
                sourceVirtualOutputs = new UnsafeList<VirtualOutputEffect.Ptr>(8, allocator);
                cullList             = new NativeList<bool>(8, allocator);
            }

            public void Dispose()
            {
                sourceVirtualOutputs.Dispose();
                cullList.Dispose();
            }
        }

        void SampleSources()
        {
            var preSplitFrame  = m_framePool.Acquire(m_frameSize);
            var postSplitFrame = m_framePool.Acquire(m_frameSize);

            EffectOperations.CullUpdateOpData updateContexts = default;
            updateContexts.effectContext.currentFrame        = m_currentFrame;
            updateContexts.effectContext.frameSize           = m_frameSize;
            updateContexts.effectContext.persistentAllocator = Allocator.AudioKernel;
            updateContexts.effectContext.sampleFramePool     = (SampleFramePool*)UnsafeUtility.AddressOf(ref m_framePool);
            updateContexts.effectContext.sampleRate          = m_sampleRate;
            //updateContexts.effectContext.tempAllocator       = m_rewindableAllocator.Allocator.Handle;

            updateContexts.updateContext.currentFrame      = m_currentFrame;
            updateContexts.updateContext.stackType         = StackType.Source;
            updateContexts.updateContext.virtualOutputsMap = m_entityToVirtualOutputMap;

            updateContexts.spatialUpdateContext.currentFrame      = m_currentFrame;
            updateContexts.spatialUpdateContext.virtualOutputsMap = m_entityToVirtualOutputMap;

            for (int sourceStackId = 0; sourceStackId < m_sourceStackIdToPtrMap.length; sourceStackId++)
            {
                var sourcePtr = m_sourceStackIdToPtrMap[sourceStackId].ptr;
                if (sourcePtr == null)
                    continue;
                ref var sourceStackMeta = ref *sourcePtr;

                // Setup cull data
                var listeners = m_listenersByLayer[sourceStackMeta.layerIndex];
                m_samplingCache.cullList.Clear();
                if (listeners.Length > 0 )
                    m_samplingCache.cullList.AddReplicate(true, listeners.Length);

                // Setup state from source meta
                updateContexts.updateContext.layerMask         = 1u << sourceStackMeta.layerIndex;
                updateContexts.updateContext.stackEntity       = sourceStackMeta.sourceEntity;
                updateContexts.updateContext.stackTransformPtr = (TransformQvvs*)UnsafeUtility.AddressOf(ref sourceStackMeta.worldTransform);

                updateContexts.spatialCullingContext.listeners = listeners;
                updateContexts.spatialCullingContext.sourcePtr = sourcePtr;

                updateContexts.cullArray.cullArray = m_samplingCache.cullList.AsArray();

                updateContexts.spatialUpdateContext.sourcePtr = sourcePtr;

                // Cull Muted Listeners
                for (int i = 0; i < listeners.Length; i++)
                {
                    var listener = listeners[i].ptr;
                    if (!listener->hasVirtualOutput && listener->limiterSettings.preGain == 0f)
                        updateContexts.cullArray.Cull(i);
                }

                // Cull Spatial Effects
                int firstSpatialIndex = -1;
                for (int i = 0; i < sourceStackMeta.effectIDsCount; i++)
                {
                    if (sourceStackMeta.effectIDs[i].isSpatialEffect)
                    {
                        if (firstSpatialIndex < 0)
                            firstSpatialIndex = i;

                        ref var effectMeta                        = ref *m_spatialEffectIdToPtrMap[sourceStackMeta.effectIDs[i].effectId].ptr;
                        updateContexts.effectContext.effectEntity = effectMeta.effectEntity;
                        updateContexts.parametersPtr              = effectMeta.parametersPtr;
                        updateContexts.effectPtr                  = effectMeta.effectPtr;

                        EffectOperations.CullSpatialEffect(ref updateContexts, effectMeta.functionPtr);
                    }
                }

                // Update pre-split
                preSplitFrame.connected       = false;
                preSplitFrame.frameIndex      = m_currentFrame;
                updateContexts.sampleFramePtr = &preSplitFrame;

                bool fullyCulled                      = updateContexts.cullArray.cullArray.Contains(true);
                updateContexts.updateContext.isCulled = fullyCulled;

                if (firstSpatialIndex < 0)
                    firstSpatialIndex = sourceStackMeta.effectIDsCount;

                for (int i = 0; i <= firstSpatialIndex; i++)
                {
                    var  effect          = sourceStackMeta.effectIDs[i];
                    bool requiresUpdate  = effect.requiresUpdateWhenCulled || !fullyCulled;
                    requiresUpdate      &= effect.requiresUpdateWhenInputFrameDisconnected || preSplitFrame.connected;
                    if (!requiresUpdate)
                        continue;

                    ref var effectMeta                        = ref *m_effectIdToPtrMap[effect.effectId].ptr;
                    updateContexts.effectContext.effectEntity = effectMeta.effectEntity;
                    updateContexts.updateContext.indexInStack = i;
                    updateContexts.parametersPtr              = effectMeta.parametersPtr;
                    updateContexts.effectPtr                  = effectMeta.effectPtr;

                    EffectOperations.UpdateEffect(ref updateContexts, effectMeta.functionPtr);
                }

                // Post splits
                for (int listenerIndex = 0; listenerIndex < listeners.Length; listenerIndex++)
                {
                    var  listener = listeners[listenerIndex];
                    bool isCulled = updateContexts.cullArray.IsCulled(listenerIndex);

                    // Update context about listener
                    updateContexts.updateContext.isCulled               = isCulled;
                    updateContexts.spatialUpdateContext.isCulled        = isCulled;
                    updateContexts.spatialUpdateContext.indexOfListener = listenerIndex;
                    updateContexts.spatialUpdateContext.listenerPtr     = listener.ptr;

                    // Forward the pre-split sample frame
                    ref var splitFrame = ref preSplitFrame;
                    if (listeners.Length != 1)
                    {
                        splitFrame                    = ref postSplitFrame;
                        splitFrame.frameIndex         = m_currentFrame;
                        updateContexts.sampleFramePtr = &postSplitFrame;
                        if (preSplitFrame.connected)
                        {
                            splitFrame.left.CopyFrom(preSplitFrame.left);
                            splitFrame.right.CopyFrom(preSplitFrame.right);
                            splitFrame.connected = true;
                        }
                        else
                        {
                            splitFrame.connected = false;
                        }
                    }

                    // Update post-splits
                    for (int i = firstSpatialIndex; i <= sourceStackMeta.effectIDsCount; i++)
                    {
                        var  effect          = sourceStackMeta.effectIDs[i];
                        bool requiresUpdate  = effect.requiresUpdateWhenCulled || isCulled;
                        requiresUpdate      &= effect.requiresUpdateWhenInputFrameDisconnected || preSplitFrame.connected;
                        if (!requiresUpdate)
                            continue;

                        if (effect.isSpatialEffect)
                        {
                            ref var effectMeta                               = ref *m_spatialEffectIdToPtrMap[effect.effectId].ptr;
                            updateContexts.effectContext.effectEntity        = effectMeta.effectEntity;
                            updateContexts.spatialUpdateContext.indexInStack = i;
                            updateContexts.parametersPtr                     = effectMeta.parametersPtr;
                            updateContexts.effectPtr                         = effectMeta.effectPtr;

                            EffectOperations.UpdateSpatialEffect(ref updateContexts, effectMeta.functionPtr);
                        }
                        else
                        {
                            ref var effectMeta                        = ref *m_effectIdToPtrMap[effect.effectId].ptr;
                            updateContexts.effectContext.effectEntity = effectMeta.effectEntity;
                            updateContexts.updateContext.indexInStack = i;
                            updateContexts.parametersPtr              = effectMeta.parametersPtr;
                            updateContexts.effectPtr                  = effectMeta.effectPtr;

                            EffectOperations.UpdateEffect(ref updateContexts, effectMeta.functionPtr);
                        }
                    }

                    // Add to the listener's sampleFrame if necessary.
                    if (splitFrame.connected)
                    {
                        ref var state = ref m_listenerStackIdToStateMap[listener.ptr->listenerId];
                        if (!state.sampleFrame.left.IsCreated)
                        {
                            state.sampleFrame            = m_framePool.Acquire(m_frameSize);
                            state.sampleFrame.frameIndex = m_currentFrame - 1;
                        }
                        if (state.sampleFrame.frameIndex != m_currentFrame)
                        {
                            state.sampleFrame.left.CopyFrom(splitFrame.left);
                            state.sampleFrame.right.CopyFrom(splitFrame.right);
                        }
                        else
                        {
                            var lin  = splitFrame.left;
                            var rin  = splitFrame.right;
                            var lout = state.sampleFrame.left;
                            var rout = state.sampleFrame.right;

                            for (int i = 0; i < m_frameSize; i++)
                            {
                                lout[i] += lin[i];
                                rout[i] += rin[i];
                            }
                        }
                        state.sampleFrame.frameIndex = m_currentFrame;
                    }
                }
            }

            m_framePool.Release(preSplitFrame);
            m_framePool.Release(postSplitFrame);
        }

        void ProcessPresampledChannels()
        {
            ListenerState dummy              = default;
            ref var       listener           = ref dummy;
            int           listenerId         = -1;
            int           inputSamplesOffset = m_frameSize * (m_currentFrame - m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.frame);

            foreach (var presampledChannel in m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.presampledBuffersForListeners)
            {
                if (listenerId != presampledChannel.listenerId)
                {
                    listener   = ref m_listenerStackIdToStateMap[presampledChannel.listenerId];
                    listenerId = presampledChannel.listenerId;
                }

                if (!listener.sampleFrame.left.IsCreated)
                {
                    listener.sampleFrame            = m_framePool.Acquire(m_frameSize);
                    listener.sampleFrame.frameIndex = m_currentFrame - 1;
                }
                if (listener.sampleFrame.frameIndex != m_currentFrame)
                {
                    listener.sampleFrame.left.AsSpan().Clear();
                    listener.sampleFrame.right.AsSpan().Clear();
                    listener.sampleFrame.frameIndex = m_currentFrame;
                }

                ref var blob        = ref listener.listenerMetadataPtr->listenerProfileBlob.Value;
                ref var blobChannel = ref presampledChannel.channelIndex >= blob.rightChannelsOffset ?
                                      ref blob.channelDspsRight[presampledChannel.channelIndex - blob.rightChannelsOffset] :
                                      ref blob.channelDspsLeft[presampledChannel.channelIndex];
                var output     = presampledChannel.channelIndex >= blob.rightChannelsOffset ? listener.sampleFrame.right : listener.sampleFrame.left;
                var filtersPtr = listener.listenerMetadataPtr->listenerProfileFilters + math.select(0,
                                                                                                    blob.totalFiltersInLeftChannels,
                                                                                                    presampledChannel.channelIndex >= blob.rightChannelsOffset);

                for (int sampleIndex = 0; sampleIndex < m_frameSize; sampleIndex++)
                {
                    float inSample    = presampledChannel.samples[sampleIndex + inputSamplesOffset];
                    float outSample   = 0f;
                    int   filterIndex = 0;
                    for (int sequenceIndex = 0; sequenceIndex < blobChannel.filterSequences.Length; sequenceIndex++)
                    {
                        ref var coefficientsArray = ref blobChannel.filterSequences[sequenceIndex];
                        var     sample            = inSample;

                        for (int coeffIndex = 0; coeffIndex < coefficientsArray.Length; coeffIndex++)
                        {
                            sample = StateVariableFilter.ProcessSample(ref filtersPtr[filterIndex], coefficientsArray[coeffIndex], sample);
                        }
                        outSample += sample;
                    }
                    output[sampleIndex] = outSample;
                }
            }
        }
    }
}

