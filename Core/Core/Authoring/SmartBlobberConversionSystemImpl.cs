using System.Collections.Generic;
using GameObject = UnityEngine.GameObject;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Authoring
{
    internal interface ISmartBlobberHandleResolver<TBlobType> : ISmartBlobberHasProcessed where TBlobType : unmanaged
    {
        internal BlobAssetReference<TBlobType> Resolve(int index);
    }

    internal interface ISmartBlobberHandleResolverUntyped : ISmartBlobberHasProcessed
    {
        internal UnsafeUntypedBlobAssetReference ResolveUntyped(int index);
    }

    internal interface ISmartBlobberHasProcessed
    {
        internal bool HasProcessed(int index);
    }
}
namespace Latios.Authoring.Systems
{
    public abstract partial class SmartBlobberConversionSystem<TBlobType, TManagedInputType, TUnmanagedConversionType> : GameObjectConversionSystem,
        ISmartBlobberHandleResolver<TBlobType>,
        ISmartBlobberHandleResolverUntyped
        where TBlobType : unmanaged
        where TManagedInputType : struct
        where TUnmanagedConversionType : unmanaged, ISmartBlobberSimpleBuilder<TBlobType>
    {
        struct InputElement
        {
            public TManagedInputType input;
            public GameObject        gameObject;
        }

        List<InputElement>                   m_inputs                = new List<InputElement>();
        List<int>                            m_handleToOutputIndices = new List<int>();
        List<BlobAssetReference<TBlobType> > m_outputBlobs           = new List<BlobAssetReference<TBlobType> >();
        bool                                 m_inputsAreLocked       = false;

        BlobAssetReference<TBlobType> ISmartBlobberHandleResolver<TBlobType>.Resolve(int index)
        {
            if (m_handleToOutputIndices[index] < 0)
                return default;

            return m_outputBlobs[m_handleToOutputIndices[index]];
        }

        UnsafeUntypedBlobAssetReference ISmartBlobberHandleResolverUntyped.ResolveUntyped(int index)
        {
            if (m_handleToOutputIndices[index] < 0)
                return default;

            var blob = m_outputBlobs[m_handleToOutputIndices[index]];
            return UnsafeUntypedBlobAssetReference.Create(blob);
        }

        bool ISmartBlobberHasProcessed.HasProcessed(int index) => index < m_handleToOutputIndices.Count;

        protected sealed override void OnUpdate()
        {
            GatherInputs();

            if (m_inputs.Count <= 0)
                return;

            // Step 1: Process inputs
            var converters = new NativeArray<TUnmanagedConversionType>(m_inputs.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var indices    = new NativeArray<int>(m_inputs.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            try
            {
                m_inputsAreLocked = true;
                for (int i = 0; i < m_inputs.Count; i++)
                {
                    if (Filter(m_inputs[i].input, m_inputs[i].gameObject, out var converter))
                    {
                        converters[i] = converter;
                        indices[i]    = 1;
                    }
                    else
                    {
                        indices[i] = -1;
                    }
                }
                m_inputsAreLocked = false;
            }
            catch (System.Exception)
            {
                converters.Dispose();
                indices.Dispose();
                m_inputsAreLocked = false;
                throw;
            }

            // Step 2: Build blobs and hashes
            var blobs  = new NativeArray<BlobAssetReference<TBlobType> >(converters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var hashes = new NativeArray<Hash128>(converters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            new ConvertBlobsJob
            {
                converters = converters,
                blobs      = blobs,
                hashes     = hashes,
                indices    = indices,
            }.ScheduleParallel(converters.Length, 1, default).Complete();

            // Step 3: filter with BlobAssetComputationContext
            var compData = new NativeList<CompData>(Allocator.TempJob);
            new MakeCompDataJob
            {
                indices  = indices,
                compData = compData
            }.Run();

            var computationContext = new BlobAssetComputationContext<CompData, TBlobType>(BlobAssetStore, 128, Allocator.TempJob);
            foreach (var cd in compData)
            {
                var hash = hashes[cd.index];
                computationContext.AssociateBlobAssetWithUnityObject(hash, m_inputs[cd.index].gameObject);
                if (computationContext.NeedToComputeBlobAsset(hash))
                {
                    computationContext.AddBlobAssetToCompute(hash, cd);
                }
            }

            var filteredCompData = computationContext.GetSettings(Allocator.TempJob);
            foreach (var cd in filteredCompData)
            {
                computationContext.AddComputedBlobAsset(hashes[cd.index], blobs[cd.index]);
            }

            // Step 4: Dispose unused blobs
            var disposeBlobMask = new NativeBitArray(converters.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            new MakeDisposeMaskJob
            {
                compData         = compData,
                filteredCompData = filteredCompData,
                disposeBlobMask  = disposeBlobMask
            }.Run();

            new DisposeUnusedBlobsJob
            {
                blobs           = blobs,
                disposeBlobMask = disposeBlobMask
            }.ScheduleParallel(converters.Length, 1, default).Complete();

            // Step 5: Collect final blobs
            foreach (var cd in compData)
            {
                computationContext.GetBlobAsset(hashes[cd.index], out var blob);
                blobs[cd.index] = blob;
            }

            var filteredBlobs = new NativeArray<BlobAssetReference<TBlobType> >(compData.Length, Allocator.TempJob);
            new MakeFinalIndicesAndBlobsJob
            {
                indices   = indices,
                srcBlobs  = blobs,
                dstBlobs  = filteredBlobs,
                baseIndex = m_handleToOutputIndices.Count
            }.Run();

            m_handleToOutputIndices.AddRange(indices);
            m_outputBlobs.AddRange(filteredBlobs);

            converters.Dispose();
            indices.Dispose();
            blobs.Dispose();
            hashes.Dispose();
            compData.Dispose();
            computationContext.Dispose();
            filteredCompData.Dispose();
            disposeBlobMask.Dispose();
            filteredBlobs.Dispose();

            m_inputs.Clear();

            FinalizeOutputs();
        }

        [BurstCompile]
        struct ConvertBlobsJob : IJobFor
        {
            public NativeArray<TUnmanagedConversionType>       converters;
            public NativeArray<BlobAssetReference<TBlobType> > blobs;
            public NativeArray<Hash128>                        hashes;
            public NativeArray<int>                            indices;

            public unsafe void Execute(int i)
            {
                if (indices[i] < 0)
                {
                    blobs[i] = default;
                    return;
                }

                var blob   = converters[i].BuildBlob();
                var length = blob.GetLength();
                if (length <= 0)
                {
                    // The blob is null, so modify the index and return out.
                    indices[i] = -1;
                    return;
                }

                blobs[i]   = blob;
                indices[i] = i;
                var hash64 = xxHash3.Hash64(blob.GetUnsafePtr(), length);
                hashes[i]  = new Hash128(hash64.x, hash64.y, (uint)length, 0);
            }
        }

        struct CompData
        {
            public int index;
        }

        [BurstCompile]
        struct MakeCompDataJob : IJob
        {
            [ReadOnly] public NativeArray<int> indices;
            public NativeList<CompData>        compData;

            public void Execute()
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] >= 0)
                    {
                        compData.Add(new CompData { index = i });
                    }
                }
            }
        }

        [BurstCompile]
        struct MakeDisposeMaskJob : IJob
        {
            [ReadOnly] public NativeArray<CompData> compData;
            [ReadOnly] public NativeArray<CompData> filteredCompData;
            public NativeBitArray                   disposeBlobMask;

            public void Execute()
            {
                for (int i = 0; i < compData.Length; i++)
                {
                    disposeBlobMask.Set(compData[i].index, true);
                }

                for (int i = 0; i < filteredCompData.Length; i++)
                {
                    disposeBlobMask.Set(filteredCompData[i].index, false);
                }
            }
        }

        [BurstCompile]
        struct DisposeUnusedBlobsJob : IJobFor
        {
            public NativeArray<BlobAssetReference<TBlobType> > blobs;
            [ReadOnly] public NativeBitArray                   disposeBlobMask;

            public void Execute(int i)
            {
                if (disposeBlobMask.IsSet(i))
                {
                    var blob = blobs[i];
                    blob.Dispose();
                    blobs[i] = default;
                }
            }
        }

        [BurstCompile]
        struct MakeFinalIndicesAndBlobsJob : IJob
        {
            public NativeArray<int>                                       indices;
            public NativeArray<BlobAssetReference<TBlobType> >            dstBlobs;
            [ReadOnly] public NativeArray<BlobAssetReference<TBlobType> > srcBlobs;
            public int                                                    baseIndex;

            public void Execute()
            {
                int dst = 0;
                for (int src = 0; src < indices.Length; src++)
                {
                    if (indices[src] >= 0)
                    {
                        indices[src]  = dst + baseIndex;
                        dstBlobs[dst] = srcBlobs[src];
                        dst++;
                    }
                }
            }
        }
    }

    public abstract partial class SmartBlobberConversionSystem<TBlobType, TManagedInputType, TUnmanagedConversionType, TContextType> : GameObjectConversionSystem,
        ISmartBlobberHandleResolver<TBlobType>,
        ISmartBlobberHandleResolverUntyped
        where TBlobType : unmanaged
        where TManagedInputType : struct
        where TUnmanagedConversionType : unmanaged, ISmartBlobberContextBuilder<TBlobType, TContextType>
        where TContextType : struct, System.IDisposable
    {
        internal struct InputElement
        {
            public TManagedInputType input;
            public GameObject        gameObject;
        }

        List<InputElement>                   m_inputs                = new List<InputElement>();
        List<int>                            m_handleToOutputIndices = new List<int>();
        List<BlobAssetReference<TBlobType> > m_outputBlobs           = new List<BlobAssetReference<TBlobType> >();
        bool                                 m_inputsAreLocked       = false;

        BlobAssetReference<TBlobType> ISmartBlobberHandleResolver<TBlobType>.Resolve(int index)
        {
            if (m_handleToOutputIndices[index] < 0)
                return default;

            return m_outputBlobs[m_handleToOutputIndices[index]];
        }

        UnsafeUntypedBlobAssetReference ISmartBlobberHandleResolverUntyped.ResolveUntyped(int index)
        {
            if (m_handleToOutputIndices[index] < 0)
                return default;

            var blob = m_outputBlobs[m_handleToOutputIndices[index]];
            return UnsafeUntypedBlobAssetReference.Create(blob);
        }

        bool ISmartBlobberHasProcessed.HasProcessed(int index) => index < m_handleToOutputIndices.Count;

        public partial struct InputAccess
        {
            internal List<InputElement> m_inputs;
            internal NativeArray<int>   m_filteredToInputMapping;

            internal TManagedInputType GetInput(int index)
            {
                if (m_filteredToInputMapping.IsCreated)
                    return m_inputs[m_filteredToInputMapping[index]].input;
                return m_inputs[index].input;
            }

            internal void SetInput(int index, TManagedInputType input)
            {
                if (m_filteredToInputMapping.IsCreated)
                    index       = m_filteredToInputMapping[index];
                var v           = m_inputs[index];
                v.input         = input;
                m_inputs[index] = v;
            }
        }

        public partial struct GameObjectAccess
        {
            internal List<InputElement> m_inputs;
            internal NativeArray<int>   m_filteredToInputMapping;

            internal GameObject GetInput(int index)
            {
                if (m_filteredToInputMapping.IsCreated)
                    return m_inputs[m_filteredToInputMapping[index]].gameObject;
                return m_inputs[index].gameObject;
            }
        }

        protected sealed override void OnUpdate()
        {
            GatherInputs();

            if (m_inputs.Count <= 0)
                return;

            // Step 1: Filter inputs
            m_inputsAreLocked = true;

            var inputConverters                    = new NativeArray<TUnmanagedConversionType>(m_inputs.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var inputToFilteredMapping             = new NativeArray<int>(m_inputs.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            new CreateIncrementingArrayJob { array = inputToFilteredMapping }.Run();

            TContextType context           = default;
            var          filterBlobberData = new FilterBlobberData
            {
                associatedObject = new GameObjectAccess { m_inputs = m_inputs },
                input                                              = new InputAccess { m_inputs = m_inputs },
                converters                                         = inputConverters
            };
            try
            {
                Filter(filterBlobberData, ref context, inputToFilteredMapping);
            }
            catch (System.Exception e)
            {
                inputConverters.Dispose();
                inputToFilteredMapping.Dispose();
                context.Dispose();
                m_inputsAreLocked = false;
                throw e;
            }

            // Step 2: Generate filtered list
            var filteredConverters     = new NativeList<TUnmanagedConversionType>(1, Allocator.TempJob);
            var filteredToInputMapping = new NativeList<int>(1, Allocator.TempJob);
            var filterError            = new NativeReference<FilterError>(new FilterError { errorIndex = -1, reason = FilterError.Reason.None }, Allocator.TempJob);
            new GenerateFilteredConvertersJob
            {
                inputConverters        = inputConverters,
                inputToFilteredMapping = inputToFilteredMapping,
                filteredConverters     = filteredConverters,
                filteredToInputMapping = filteredToInputMapping,
                filterError            = filterError
            }.Run();

            // Don't need these anymore.
            inputConverters.Dispose();

            if (filterError.Value.reason != FilterError.Reason.None)
            {
                filteredConverters.Dispose();
                filteredToInputMapping.Dispose();

                if (filterError.Value.reason == FilterError.Reason.ForwardIndex)
                {
                    int offendingIndex = filterError.Value.errorIndex;
                    int offendingValue = inputToFilteredMapping[offendingIndex];
                    inputToFilteredMapping.Dispose();
                    filterError.Dispose();
                    throw new System.InvalidOperationException($"inputToFilteredMapping was initialized with a forward index of {offendingValue} at position {offendingIndex}");
                }
                if (filterError.Value.reason == FilterError.Reason.CopyOfCopy)
                {
                    int offendingIndex       = filterError.Value.errorIndex;
                    int offendingValue       = inputToFilteredMapping[offendingIndex];
                    int offendingValuesValue = inputToFilteredMapping[offendingValue];
                    inputToFilteredMapping.Dispose();
                    filterError.Dispose();
                    throw new System.InvalidOperationException(
                        $"inputToFilteredMapping at position {offendingIndex} creates a deduplication reference to position {offendingValue} which is already a deduplication reference to position {offendingValuesValue}");
                }
            }
            filterError.Dispose();

            //Step 3: PostFilter
            var postFilter = new PostFilterBlobberData
            {
                converters             = filteredConverters,
                input                  = new InputAccess { m_inputs = m_inputs, m_filteredToInputMapping = filteredToInputMapping },
                filteredToInputMapping = filteredToInputMapping.AsArray().AsReadOnly()
            };
            try
            {
                PostFilter(postFilter, ref context);
            }
            catch(System.Exception e)
            {
                inputToFilteredMapping.Dispose();
                context.Dispose();
                filteredConverters.Dispose();
                filteredToInputMapping.Dispose();
                throw e;
            }

            // Step 4: Build blobs and hashes
            var blobs  = new NativeArray<BlobAssetReference<TBlobType> >(filteredConverters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var hashes = new NativeArray<Hash128>(filteredConverters.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            new ConvertBlobsJob
            {
                converters             = filteredConverters,
                blobs                  = blobs,
                hashes                 = hashes,
                filteredToInputMapping = filteredToInputMapping,
                context                = context
            }.Run(filteredConverters.Length);  //.ScheduleParallel(filteredConverters.Length, 1, default).Complete();
            context.Dispose();

            // Step 5: filter with BlobAssetComputationContext
            var compData = new NativeList<CompData>(Allocator.TempJob);
            new MakeCompDataJob
            {
                indices  = filteredToInputMapping,
                compData = compData
            }.Run();
            filteredToInputMapping.Dispose();

            var computationContext = new BlobAssetComputationContext<CompData, TBlobType>(BlobAssetStore, 128, Allocator.TempJob);
            for (int i = 0; i < inputToFilteredMapping.Length; i++)
            {
                var filteredIndex = inputToFilteredMapping[i];
                if (filteredIndex < 0)
                    continue;

                computationContext.AssociateBlobAssetWithUnityObject(hashes[filteredIndex], m_inputs[i].gameObject);
            }
            foreach (var cd in compData)
            {
                if (computationContext.NeedToComputeBlobAsset(hashes[cd.index]))
                {
                    computationContext.AddBlobAssetToCompute(hashes[cd.index], cd);
                }
            }

            var filteredCompData = computationContext.GetSettings(Allocator.TempJob);
            foreach (var cd in filteredCompData)
            {
                computationContext.AddComputedBlobAsset(hashes[cd.index], blobs[cd.index]);
            }

            // Step 6: Dispose unused blobs
            var disposeBlobMask = new NativeBitArray(filteredConverters.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            new MakeDisposeMaskJob
            {
                compData         = compData,
                filteredCompData = filteredCompData,
                disposeBlobMask  = disposeBlobMask
            }.Run();
            filteredCompData.Dispose();

            new DisposeUnusedBlobs
            {
                blobs           = blobs,
                disposeBlobMask = disposeBlobMask
            }.ScheduleParallel(filteredConverters.Length, 1, default).Complete();
            disposeBlobMask.Dispose();

            // Step 7: Collect final blobs
            foreach (var cd in compData)
            {
                computationContext.GetBlobAsset(hashes[cd.index], out var blob);
                blobs[cd.index] = blob;
            }
            computationContext.Dispose();

            var filteredBlobs = new NativeArray<BlobAssetReference<TBlobType> >(compData.Length, Allocator.TempJob);
            compData.Dispose();

            new MakeFinalIndicesAndBlobsJob
            {
                inputToFilteredMapping = inputToFilteredMapping,
                srcBlobs               = blobs,
                dstBlobs               = filteredBlobs,
                baseIndex              = m_handleToOutputIndices.Count
            }.Run();
            blobs.Dispose();
            hashes.Dispose();
            filteredConverters.Dispose();

            m_handleToOutputIndices.AddRange(inputToFilteredMapping);
            m_outputBlobs.AddRange(filteredBlobs);

            inputToFilteredMapping.Dispose();
            filteredBlobs.Dispose();

            FinalizeOutputs();
        }

        [BurstCompile]
        struct CreateIncrementingArrayJob : IJob
        {
            public NativeArray<int> array;

            public void Execute()
            {
                for (int i = 0; i < array.Length; i++)
                    array[i] = i;
            }
        }

        struct FilterError
        {
            public int    errorIndex;
            public Reason reason;

            public enum Reason
            {
                None,
                ForwardIndex,
                CopyOfCopy
            }
        }

        [BurstCompile]
        struct GenerateFilteredConvertersJob : IJob
        {
            [ReadOnly] public NativeArray<TUnmanagedConversionType> inputConverters;
            public NativeArray<int>                                 inputToFilteredMapping;
            public NativeList<TUnmanagedConversionType>             filteredConverters;
            public NativeList<int>                                  filteredToInputMapping;
            public NativeReference<FilterError>                     filterError;

            public void Execute()
            {
                int count = 0;
                for (int i = 0; i < inputToFilteredMapping.Length; i++)
                {
                    if (inputToFilteredMapping[i] == i)
                        count++;
                    else if (inputToFilteredMapping[i] > i)
                    {
                        filterError.Value = new FilterError { errorIndex = i, reason = FilterError.Reason.ForwardIndex };
                        return;
                    }
                    else if (inputToFilteredMapping[i] >= 0)
                    {
                        int dedupIndex = inputToFilteredMapping[i];
                        if (inputToFilteredMapping[dedupIndex] != dedupIndex)
                        {
                            filterError.Value = new FilterError { errorIndex = i, reason = FilterError.Reason.CopyOfCopy };
                            return;
                        }
                    }
                }

                filteredConverters.ResizeUninitialized(count);
                filteredToInputMapping.ResizeUninitialized(count);
                int dst = 0;
                for (int src = 0; src < inputToFilteredMapping.Length; src++)
                {
                    if (inputToFilteredMapping[src] == src)
                    {
                        filteredToInputMapping[dst] = src;
                        filteredConverters[dst]     = inputConverters[src];
                        inputToFilteredMapping[src] = dst;
                        dst++;
                    }
                    else if (inputToFilteredMapping[src] >= 0)
                        inputToFilteredMapping[src] = inputToFilteredMapping[inputToFilteredMapping[src]];
                }
            }
        }

        [BurstCompile]
        struct ConvertBlobsJob : IJobFor
        {
            public NativeArray<TUnmanagedConversionType>       converters;
            public NativeArray<BlobAssetReference<TBlobType> > blobs;
            public NativeArray<Hash128>                        hashes;
            public NativeArray<int>                            filteredToInputMapping;
            public TContextType                                context;

            public unsafe void Execute(int i)
            {
                if (filteredToInputMapping[i] < 0)
                {
                    blobs[i] = default;
                    return;
                }

                var blob   = converters[i].BuildBlob(filteredToInputMapping[i], i, ref context);
                var length = blob.GetLength();
                if (length <= 0)
                {
                    // The blob is null, so modify the index and return out.
                    filteredToInputMapping[i] = -1;
                    return;
                }

                blobs[i]   = blob;
                var hash64 = xxHash3.Hash64(blob.GetUnsafePtr(), length);
                hashes[i]  = new Hash128(hash64.x, hash64.y, (uint)length, 0);
            }
        }

        struct CompData
        {
            public int index;
        }

        [BurstCompile]
        struct MakeCompDataJob : IJob
        {
            [ReadOnly] public NativeArray<int> indices;
            public NativeList<CompData>        compData;

            public void Execute()
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] >= 0)
                    {
                        compData.Add(new CompData { index = i });
                    }
                }
            }
        }

        [BurstCompile]
        struct MakeDisposeMaskJob : IJob
        {
            [ReadOnly] public NativeArray<CompData> compData;
            [ReadOnly] public NativeArray<CompData> filteredCompData;
            public NativeBitArray                   disposeBlobMask;

            public void Execute()
            {
                for (int i = 0; i < compData.Length; i++)
                {
                    disposeBlobMask.Set(compData[i].index, true);
                }

                for (int i = 0; i < filteredCompData.Length; i++)
                {
                    disposeBlobMask.Set(filteredCompData[i].index, false);
                }
            }
        }

        [BurstCompile]
        struct DisposeUnusedBlobs : IJobFor
        {
            public NativeArray<BlobAssetReference<TBlobType> > blobs;
            [ReadOnly] public NativeBitArray                   disposeBlobMask;

            public void Execute(int i)
            {
                if (disposeBlobMask.IsSet(i))
                {
                    var blob = blobs[i];
                    blob.Dispose();
                    blobs[i] = default;
                }
            }
        }

        [BurstCompile]
        struct MakeFinalIndicesAndBlobsJob : IJob
        {
            public NativeArray<int>                                       inputToFilteredMapping;
            public NativeArray<BlobAssetReference<TBlobType> >            dstBlobs;
            [ReadOnly] public NativeArray<BlobAssetReference<TBlobType> > srcBlobs;
            public int                                                    baseIndex;

            public void Execute()
            {
                int dst = 0;
                for (int src = 0; src < inputToFilteredMapping.Length; src++)
                {
                    if (inputToFilteredMapping[src] == dst)
                    {
                        int filteredIndex            = inputToFilteredMapping[src];
                        inputToFilteredMapping[src] += baseIndex;
                        dstBlobs[dst]                = srcBlobs[filteredIndex];
                        dst++;
                    }
                    else if (inputToFilteredMapping[src] >= 0)
                    {
                        inputToFilteredMapping[src] += baseIndex;
                    }
                }
            }
        }
    }
}

