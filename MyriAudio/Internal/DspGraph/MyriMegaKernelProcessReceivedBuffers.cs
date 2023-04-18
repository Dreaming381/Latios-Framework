using System.Threading;
using Latios.Myri.DSP;
using Latios.Myri.InternalSourceGen;
using Latios.Myri.Interop;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct MyriMegaKernelInitUpdate : IAudioKernelUpdate<MyriMegaKernel.Parameters, MyriMegaKernel.SampleProviders, MyriMegaKernel>
    {
        [NativeDisableUnsafePtrRestriction]
        public long* ptr;

        public void Update(ref MyriMegaKernel audioKernel)
        {
            audioKernel.SetPackedFrameCounterBuffer(ptr);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct MyriMegaKernelBufferUpdate : IAudioKernelUpdate<MyriMegaKernel.Parameters, MyriMegaKernel.SampleProviders, MyriMegaKernel>
    {
        public DspUpdateBuffer newBuffer;

        public void Update(ref MyriMegaKernel audioKernel)
        {
            audioKernel.AddDspUpdateBuffer(newBuffer);
        }
    }

    internal unsafe partial struct MyriMegaKernel
    {
        public void SetPackedFrameCounterBuffer(long* ptr)
        {
            m_packedFrameCounterBufferId = ptr;
        }

        public void AddDspUpdateBuffer(in DspUpdateBuffer buffer)
        {
            m_queuedDspUpdateBuffers.Add(in buffer);
        }

        void ProcessReceivedBuffers()
        {
            m_profilingReceivedBuffers.Begin();

            m_currentFrame++;

            if (m_currentFrame >= m_nextUpdateFrame && !m_queuedDspUpdateBuffers.IsEmpty)
            {
                int bestIndex = -1;
                for (int i = 0; i < m_queuedDspUpdateBuffers.Length; i++)
                {
                    if (m_queuedDspUpdateBuffers[i].presampledAndTimingUpdateBuffer.frame <= m_currentFrame)
                    {
                        bestIndex = i;
                    }
                }
                if (bestIndex >= 0)
                {
                    for (int i = 0; i <= bestIndex; i++)
                    {
                        ref var updateBuffer = ref m_queuedDspUpdateBuffers.ElementAt(i);
                        ProcessDestroyedEffects(ref updateBuffer);
                        ProcessDestroyedSpatialEffects(ref updateBuffer);
                        ProcessDestroyedSourceStacks(ref updateBuffer);
                        ProcessDestroyedListenerStacks(ref updateBuffer);

                        ProcessCreatedEffects(ref updateBuffer);
                        ProcessCreatedSpatialEffects(ref updateBuffer);
                        ProcessCreatedSourceStacks(ref updateBuffer);
                        ProcessCreatedListenerStacks(ref updateBuffer);

                        ProcessUpdatedEffects(ref updateBuffer);
                        ProcessUpdatedSpatialEffects(ref updateBuffer);
                        ProcessFullyUpdatedSourceStacks(ref updateBuffer);
                        ProcessTransformUpdatedSourceStacks(ref updateBuffer);
                        ProcessFullyUpdatedListenerStacks(ref updateBuffer);
                        ProcessTransformUpdatedListenerStacks(ref updateBuffer);
                    }
                    m_dspUpdateBuffer = m_queuedDspUpdateBuffers[bestIndex];
                    m_queuedDspUpdateBuffers.RemoveRange(0, bestIndex + 1);
                    m_hasFirstDspBuffer = true;
                    m_hasValidDspBuffer = true;

                    UpdateListenersWithPresampledChannels(ref m_dspUpdateBuffer);
                    UpdateListenersLayers();

                    m_lastProcessedBufferID = m_dspUpdateBuffer.bufferId;  // We need to report the buffer we just consumed. The audio system knows to keep that one around yet.
                    m_nextUpdateFrame       = m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.frame + m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.framesPerUpdate;
                }
            }

            if (m_hasValidDspBuffer)
            {
                // Check that we didn't run out of our current buffer
                if (m_currentFrame - m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.frame >= m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.framesInBuffer)
                {
                    m_hasValidDspBuffer = false;
                }
            }
            if (m_hasFirstDspBuffer && !m_hasValidDspBuffer && m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.warnIfStarved)
            {
                UnityEngine.Debug.LogWarning(
                    $"Dsp buffer starved. Kernel frame: {m_currentFrame}, DspUpdateBuffer frame: {m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.frame}, DspUpdateBuffer Id: {m_dspUpdateBuffer.bufferId}, frames in buffer: {m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.framesInBuffer}, next update frame: {m_nextUpdateFrame}, frames per update: {m_dspUpdateBuffer.presampledAndTimingUpdateBuffer.framesPerUpdate}");
            }

            // Report that we just played the buffer back to AudioSystem
            long     packed   = m_currentFrame + (((long)m_lastProcessedBufferID) << 32);
            ref long location = ref UnsafeUtility.AsRef<long>(m_packedFrameCounterBufferId);
            Interlocked.Exchange(ref location, packed);

            m_profilingReceivedBuffers.Begin();
        }

        #region Effects
        void ProcessCreatedEffects(ref DspUpdateBuffer updateBuffer)
        {
            var op = new EffectOperations.InitDestroyOpData
            {
                effectContext = new EffectContext
                {
                    currentFrame        = m_currentFrame,
                    effectEntity        = default,
                    frameSize           = m_frameSize,
                    sampleRate          = m_sampleRate,
                    sampleFramePool     = (SampleFramePool*)UnsafeUtility.AddressOf(ref m_framePool),
                    persistentAllocator = Allocator.AudioKernel,
                    //tempAllocator       = m_rewindableAllocator.Allocator.Handle
                },
                effectPtr                                = null,
                parametersPtr                            = null,
                requiresUpdateWhenCulled                 = false,
                requiresUpdateWhenInputFrameDisconnected = false,
                isVirtualOutput                          = false,
            };

            foreach (var newEffectPtr in updateBuffer.effectsUpdateBuffer.newEffects)
            {
                op.effectContext.effectEntity = newEffectPtr.ptr->effectEntity;
                op.parametersPtr              = newEffectPtr.ptr->parametersPtr;
                op.effectPtr                  = newEffectPtr.ptr->effectPtr;
                EffectOperations.InitEffect(ref op, newEffectPtr.ptr->functionPtr);
                newEffectPtr.ptr->requiresUpdateWhenCulled                 = op.requiresUpdateWhenCulled;
                newEffectPtr.ptr->requiresUpdateWhenInputFrameDisconnected = op.requiresUpdateWhenInputFrameDisconnected;
                newEffectPtr.ptr->isVirtualOutput                          = op.isVirtualOutput;
                m_effectIdToPtrMap[newEffectPtr.ptr->effectId]             = newEffectPtr;

                if (op.isVirtualOutput)
                {
                    m_entityToVirtualOutputMap.Add(op.effectContext.effectEntity, new VirtualOutputEffect.Ptr
                    {
                        ptr = (VirtualOutputEffect*)op.effectPtr,
                    });
                }
            }
        }

        void ProcessDestroyedEffects(ref DspUpdateBuffer updateBuffer)
        {
            var op = new EffectOperations.InitDestroyOpData
            {
                effectContext = new EffectContext
                {
                    currentFrame        = m_currentFrame,
                    effectEntity        = default,
                    frameSize           = m_frameSize,
                    sampleRate          = m_sampleRate,
                    sampleFramePool     = (SampleFramePool*)UnsafeUtility.AddressOf(ref m_framePool),
                    persistentAllocator = Allocator.AudioKernel,
                    //tempAllocator       = m_rewindableAllocator.Allocator.Handle
                },
                effectPtr     = null,
                parametersPtr = null,
            };

            foreach (var deadEffectId in updateBuffer.effectsUpdateBuffer.deadEffectIDs)
            {
                ref var storedPtr             = ref m_effectIdToPtrMap[deadEffectId];
                op.effectContext.effectEntity = storedPtr.ptr->effectEntity;
                op.parametersPtr              = storedPtr.ptr->parametersPtr;
                op.effectPtr                  = storedPtr.ptr->effectPtr;
                EffectOperations.DestroyEffect(ref op, storedPtr.ptr->functionPtr);

                if (storedPtr.ptr->isVirtualOutput)
                {
                    m_entityToVirtualOutputMap.Remove(op.effectContext.effectEntity);
                }

                storedPtr = default;
            }
        }

        void ProcessUpdatedEffects(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var updatedEffect in updateBuffer.effectsUpdateBuffer.updatedEffects)
            {
                ((EffectMetadata*)updatedEffect.effectMetadataPtr)->parametersPtr = updatedEffect.newParametersPtr;
            }
        }

        void ProcessCreatedSpatialEffects(ref DspUpdateBuffer updateBuffer)
        {
            var op = new EffectOperations.InitDestroyOpData
            {
                effectContext = new EffectContext
                {
                    currentFrame        = m_currentFrame,
                    effectEntity        = default,
                    frameSize           = m_frameSize,
                    sampleRate          = m_sampleRate,
                    sampleFramePool     = (SampleFramePool*)UnsafeUtility.AddressOf(ref m_framePool),
                    persistentAllocator = Allocator.AudioKernel,
                    //tempAllocator       = m_rewindableAllocator.Allocator.Handle
                },
                effectPtr     = null,
                parametersPtr = null,
            };

            foreach (var newEffectPtr in updateBuffer.effectsUpdateBuffer.newSpatialEffects)
            {
                op.effectContext.effectEntity = newEffectPtr.ptr->effectEntity;
                op.parametersPtr              = newEffectPtr.ptr->parametersPtr;
                op.effectPtr                  = newEffectPtr.ptr->effectPtr;
                EffectOperations.InitSpatialEffect(ref op, newEffectPtr.ptr->functionPtr);
                newEffectPtr.ptr->requiresUpdateWhenCulled                 = op.requiresUpdateWhenCulled;
                newEffectPtr.ptr->requiresUpdateWhenInputFrameDisconnected = op.requiresUpdateWhenInputFrameDisconnected;
                m_spatialEffectIdToPtrMap[newEffectPtr.ptr->effectId]      = newEffectPtr;
            }
        }

        void ProcessDestroyedSpatialEffects(ref DspUpdateBuffer updateBuffer)
        {
            var op = new EffectOperations.InitDestroyOpData
            {
                effectContext = new EffectContext
                {
                    currentFrame        = m_currentFrame,
                    effectEntity        = default,
                    frameSize           = m_frameSize,
                    sampleRate          = m_sampleRate,
                    sampleFramePool     = (SampleFramePool*)UnsafeUtility.AddressOf(ref m_framePool),
                    persistentAllocator = Allocator.AudioKernel,
                    //tempAllocator       = m_rewindableAllocator.Allocator.Handle
                },
                effectPtr     = null,
                parametersPtr = null,
            };

            foreach (var deadEffectId in updateBuffer.effectsUpdateBuffer.deadSpatialEffectIDs)
            {
                ref var storedPtr             = ref m_spatialEffectIdToPtrMap[deadEffectId];
                op.effectContext.effectEntity = storedPtr.ptr->effectEntity;
                op.parametersPtr              = storedPtr.ptr->parametersPtr;
                op.effectPtr                  = storedPtr.ptr->effectPtr;
                EffectOperations.DestroySpatialEffect(ref op, storedPtr.ptr->functionPtr);
                storedPtr = default;
            }
        }

        void ProcessUpdatedSpatialEffects(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var updatedEffect in updateBuffer.effectsUpdateBuffer.updatedSpatialEffects)
            {
                ((SpatialEffectMetadata*)updatedEffect.effectMetadataPtr)->parametersPtr = updatedEffect.newParametersPtr;
            }
        }
        #endregion

        #region Source Stacks
        void ProcessCreatedSourceStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var newStack in updateBuffer.sourceStacksUpdateBuffer.newSourceStacks)
            {
                for (int i = 0; i < newStack.ptr->effectIDsCount; i++)
                {
                    ref var element = ref newStack.ptr->effectIDs[i];

                    if (element.isSpatialEffect)
                    {
                        var ptr                                          = m_spatialEffectIdToPtrMap[element.effectId].ptr;
                        element.requiresUpdateWhenCulled                 = ptr->requiresUpdateWhenCulled;
                        element.requiresUpdateWhenInputFrameDisconnected = ptr->requiresUpdateWhenInputFrameDisconnected;
                    }
                    else
                    {
                        var ptr                                          = m_effectIdToPtrMap[element.effectId].ptr;
                        element.requiresUpdateWhenCulled                 = ptr->requiresUpdateWhenCulled;
                        element.requiresUpdateWhenInputFrameDisconnected = ptr->requiresUpdateWhenInputFrameDisconnected;
                    }
                }
                m_sourceStackIdToPtrMap[newStack.ptr->sourceId] = newStack;
            }
        }

        void ProcessDestroyedSourceStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var destroyedStack in updateBuffer.sourceStacksUpdateBuffer.deadSourceStackIDs)
            {
                m_sourceStackIdToPtrMap[destroyedStack] = default;
            }
        }

        void ProcessFullyUpdatedSourceStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var newStack in updateBuffer.sourceStacksUpdateBuffer.updatedSourceStacks)
            {
                ref var storedStackPtr = ref m_sourceStackIdToPtrMap[newStack.ptr->sourceId];
                var     oldPtr         = storedStackPtr.ptr;
                storedStackPtr         = newStack;
                if (oldPtr->effectIDs != newStack.ptr->effectIDs)
                {
                    for (int i = 0; i < newStack.ptr->effectIDsCount; i++)
                    {
                        ref var element = ref newStack.ptr->effectIDs[i];

                        if (element.isSpatialEffect)
                        {
                            var ptr                                          = m_spatialEffectIdToPtrMap[element.effectId].ptr;
                            element.requiresUpdateWhenCulled                 = ptr->requiresUpdateWhenCulled;
                            element.requiresUpdateWhenInputFrameDisconnected = ptr->requiresUpdateWhenInputFrameDisconnected;
                        }
                        else
                        {
                            var ptr                                          = m_effectIdToPtrMap[element.effectId].ptr;
                            element.requiresUpdateWhenCulled                 = ptr->requiresUpdateWhenCulled;
                            element.requiresUpdateWhenInputFrameDisconnected = ptr->requiresUpdateWhenInputFrameDisconnected;
                        }
                    }
                }
            }
        }

        void ProcessTransformUpdatedSourceStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var stackTransform in updateBuffer.sourceStacksUpdateBuffer.updatedSourceStackTransforms)
            {
                ((SourceStackMetadata*)stackTransform.StackMetadataPtr)->worldTransform = stackTransform.worldTransform;
            }
        }
        #endregion

        #region Listener Stacks
        void ProcessCreatedListenerStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var newStack in updateBuffer.listenerStacksUpdateBuffer.newListenerStacks)
            {
                newStack.ptr->hasVirtualOutput = false;
                for (int i = 0; i < newStack.ptr->effectIDsCount; i++)
                {
                    if (m_effectIdToPtrMap[newStack.ptr->effectIDs[i].effectId].ptr->isVirtualOutput)
                    {
                        newStack.ptr->hasVirtualOutput = true;
                        break;
                    }
                }
                ref var state             = ref m_listenerStackIdToStateMap[newStack.ptr->listenerId];
                state                     = default;
                state.listenerMetadataPtr = newStack.ptr;
                m_listenersDirty          = true;
            }
        }

        void ProcessDestroyedListenerStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var deadStackId in updateBuffer.listenerStacksUpdateBuffer.deadListenerStackIDs)
            {
                m_listenerStackIdToStateMap[deadStackId].Dispose(ref m_framePool);
                m_listenersDirty = true;
            }
        }

        void ProcessFullyUpdatedListenerStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var newStack in updateBuffer.listenerStacksUpdateBuffer.updatedListenerStacks)
            {
                m_listenersDirty                     = true;
                ref var storedStackState             = ref m_listenerStackIdToStateMap[newStack.ptr->listenerId];
                var     oldPtr                       = storedStackState.listenerMetadataPtr;
                storedStackState.listenerMetadataPtr = newStack.ptr;
                if (oldPtr->effectIDs != newStack.ptr->effectIDs)
                {
                    newStack.ptr->hasVirtualOutput = false;
                    for (int i = 0; i < newStack.ptr->effectIDsCount; i++)
                    {
                        if (m_effectIdToPtrMap[newStack.ptr->effectIDs[i].effectId].ptr->isVirtualOutput)
                        {
                            newStack.ptr->hasVirtualOutput = true;
                            break;
                        }
                    }
                }
                else
                {
                    newStack.ptr->hasVirtualOutput = oldPtr->hasVirtualOutput;
                }
            }
        }

        void ProcessTransformUpdatedListenerStacks(ref DspUpdateBuffer updateBuffer)
        {
            foreach (var stackTransform in updateBuffer.listenerStacksUpdateBuffer.updatedListenerStackTransforms)
            {
                ((ListenerStackMetadata*)stackTransform.StackMetadataPtr)->worldTransform = stackTransform.worldTransform;
            }
        }
        #endregion

        #region ListenersBatched
        void UpdateListenersWithPresampledChannels(ref DspUpdateBuffer updateBuffer)
        {
            for (int i = 0; i < m_listenerStackIdToStateMap.length; i++)
            {
                m_listenerStackIdToStateMap[i].presampledCount = 0;
            }

            for (int i = 0; i < updateBuffer.presampledAndTimingUpdateBuffer.presampledBuffersForListeners.Length; i++)
            {
                var     buffer   = updateBuffer.presampledAndTimingUpdateBuffer.presampledBuffersForListeners[i];
                ref var listener = ref m_listenerStackIdToStateMap[buffer.listenerId];
                if (listener.presampledCount == 0)
                    listener.presampledStart = i;
                listener.presampledCount++;
            }
        }

        void UpdateListenersLayers()
        {
            if (!m_listenersDirty)
                return;

            m_listenersDirty = false;

            for (int i = 0; i < m_listenersByLayer.Length; i++)
            {
                m_listenersByLayer.ElementAt(i).Clear();
            }

            for (int i = 0; i < m_listenerStackIdToStateMap.length; i++)
            {
                ref var state = ref m_listenerStackIdToStateMap[i];
                if (state.listenerMetadataPtr == null)
                    continue;

                BitField32 layerMask = default;
                layerMask.Value      = state.listenerMetadataPtr->layerMask;

                for (int bit = 0; bit < 32; bit++)
                {
                    if (layerMask.IsSet(bit))
                    {
                        m_listenersByLayer.ElementAt(bit).Add(new ListenerStackMetadata.Ptr { ptr = state.listenerMetadataPtr });
                    }
                }
            }
        }
        #endregion
    }
}

