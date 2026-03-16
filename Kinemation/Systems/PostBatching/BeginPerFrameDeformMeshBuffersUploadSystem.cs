using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial struct BeginPerFrameDeformMeshBuffersUploadSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            latiosWorld.worldBlackboardEntity.AddComponentData(new MeshGpuUploadBuffers());
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new MeshGpuUploadBuffersMapped());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var meshGpuManager        = latiosWorld.worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(false);
            var boneOffsetsGpuManager = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BoneOffsetsGpuManager>(false);
            var broker                = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();

            state.CompleteDependency();

            JobHandle jhv = default;
            JobHandle jhw = default;
            JobHandle jhp = default;
            JobHandle jhs = default;

            MeshGpuUploadBuffers       newBuffers       = default;
            MeshGpuUploadBuffersMapped newBuffersMapped = default;

            var requiredSizes            = meshGpuManager.requiredBufferSizes.Value;
            newBuffers.verticesBuffer    = broker.GetMeshVerticesBuffer(requiredSizes.requiredVertexBufferSize);
            newBuffers.weightsBuffer     = broker.GetMeshWeightsBuffer(requiredSizes.requiredWeightBufferSize);
            newBuffers.bindPosesBuffer   = broker.GetMeshBindPosesBuffer(requiredSizes.requiredBindPoseBufferSize);
            newBuffers.blendShapesBuffer = broker.GetMeshBlendShapesBuffer(requiredSizes.requiredBlendShapesBufferSize);

            if (!meshGpuManager.uploadCommands.IsEmpty)
            {
                var uploadCount                                        = (uint)meshGpuManager.uploadCommands.Length;
                newBuffers.verticesUploadBuffer                        = broker.GetMeshVerticesUploadBuffer(requiredSizes.requiredVertexUploadSize);
                newBuffers.weightsUploadBuffer                         = broker.GetMeshWeightsUploadBuffer(requiredSizes.requiredWeightUploadSize);
                newBuffers.bindPosesUploadBuffer                       = broker.GetMeshBindPosesUploadBuffer(requiredSizes.requiredBindPoseUploadSize);
                newBuffers.blendShapesUploadBuffer                     = broker.GetMeshBlendShapesUploadBuffer(requiredSizes.requiredBlendShapesUploadSize);
                newBuffers.verticesUploadMetaBuffer                    = broker.GetMetaUint3UploadBuffer(uploadCount);
                newBuffers.weightsUploadMetaBuffer                     = broker.GetMetaUint3UploadBuffer(uploadCount);
                newBuffers.bindPosesUploadMetaBuffer                   = broker.GetMetaUint3UploadBuffer(uploadCount);
                newBuffers.blendShapesUploadMetaBuffer                 = broker.GetMetaUint3UploadBuffer(uploadCount);
                newBuffersMapped.verticesUploadBufferWriteCount        = requiredSizes.requiredVertexUploadSize;
                newBuffersMapped.weightsUploadBufferWriteCount         = requiredSizes.requiredWeightUploadSize;
                newBuffersMapped.bindPosesUploadBufferWriteCount       = requiredSizes.requiredBindPoseUploadSize;
                newBuffersMapped.blendShapesUploadBufferWriteCount     = requiredSizes.requiredBlendShapesUploadSize;
                newBuffersMapped.verticesUploadMetaBufferWriteCount    = uploadCount;
                newBuffersMapped.weightsUploadMetaBufferWriteCount     = uploadCount;
                newBuffersMapped.bindPosesUploadMetaBufferWriteCount   = uploadCount;
                newBuffersMapped.blendShapesUploadMetaBufferWriteCount = uploadCount;

                var mappedVertices    = newBuffers.verticesUploadBuffer.LockBufferForWrite<UndeformedVertex>(0, (int)requiredSizes.requiredVertexUploadSize);
                var mappedWeights     = newBuffers.weightsUploadBuffer.LockBufferForWrite<BoneWeightLinkedList>(0, (int)requiredSizes.requiredWeightUploadSize);  // Unity uses T for sizing so we don't need to *2 here.
                var mappedBindPoses   = newBuffers.bindPosesUploadBuffer.LockBufferForWrite<float3x4>(0, (int)requiredSizes.requiredBindPoseUploadSize);
                var mappedBlendShapes =
                    newBuffers.blendShapesUploadBuffer.LockBufferForWrite<BlendShapeVertexDisplacement>(0, (int)requiredSizes.requiredBlendShapesUploadSize);
                var mappedVerticesMeta    = newBuffers.verticesUploadMetaBuffer.LockBufferForWrite<uint3>(0, (int)uploadCount);
                var mappedWeightsMeta     = newBuffers.weightsUploadMetaBuffer.LockBufferForWrite<uint3>(0, (int)uploadCount);
                var mappedBindPosesMeta   = newBuffers.bindPosesUploadMetaBuffer.LockBufferForWrite<uint3>(0, (int)uploadCount);
                var mappedBlendShapesMeta = newBuffers.blendShapesUploadMetaBuffer.LockBufferForWrite<uint3>(0, (int)uploadCount);

                newBuffersMapped.needsMeshCommitment = true;

                var verticesSums    = CollectionHelper.CreateNativeArray<uint>(meshGpuManager.uploadCommands.Length, state.WorldUpdateAllocator);
                var weightsSums     = CollectionHelper.CreateNativeArray<uint>(meshGpuManager.uploadCommands.Length, state.WorldUpdateAllocator);
                var bindPosesSums   = CollectionHelper.CreateNativeArray<uint>(meshGpuManager.uploadCommands.Length, state.WorldUpdateAllocator);
                var blendShapesSums = CollectionHelper.CreateNativeArray<uint>(meshGpuManager.uploadCommands.Length, state.WorldUpdateAllocator);
                jhv                 = new PrefixSumVerticesCountsJob
                {
                    commands = meshGpuManager.uploadCommands.AsArray(),
                    sums     = verticesSums
                }.Schedule();
                jhw = new PrefixSumWeightsCountsJob
                {
                    commands = meshGpuManager.uploadCommands.AsArray(),
                    sums     = weightsSums
                }.Schedule();
                jhp = new PrefixSumBindPosesCountsJob
                {
                    commands = meshGpuManager.uploadCommands.AsArray(),
                    sums     = bindPosesSums
                }.Schedule();
                jhs = new PrefixSumBlendShapesCountsJob
                {
                    commands = meshGpuManager.uploadCommands.AsArray(),
                    sums     = blendShapesSums
                }.Schedule();
                jhv = new UploadMeshesVerticesJob
                {
                    commands       = meshGpuManager.uploadCommands.AsArray(),
                    prefixSums     = verticesSums,
                    mappedVertices = mappedVertices,
                    mappedMeta     = mappedVerticesMeta
                }.ScheduleParallel((int)uploadCount, 1, jhv);
                jhw = new UploadMeshesWeightsJob
                {
                    commands      = meshGpuManager.uploadCommands.AsArray(),
                    prefixSums    = weightsSums,
                    mappedWeights = mappedWeights,
                    mappedMeta    = mappedWeightsMeta
                }.ScheduleParallel((int)uploadCount, 1, jhw);
                jhp = new UploadMeshesBindPosesJob
                {
                    commands        = meshGpuManager.uploadCommands.AsArray(),
                    prefixSums      = bindPosesSums,
                    mappedBindPoses = mappedBindPoses,
                    mappedMeta      = mappedBindPosesMeta
                }.ScheduleParallel((int)uploadCount, 1, jhp);
                jhs = new UploadMeshesBlendShapesJob
                {
                    commands          = meshGpuManager.uploadCommands.AsArray(),
                    prefixSums        = blendShapesSums,
                    mappedBlendShapes = mappedBlendShapes,
                    mappedMeta        = mappedBlendShapesMeta
                }.ScheduleParallel((int)uploadCount, 1, jhs);
            }

            JobHandle jho = default;

            uint boneOffsetsSize         = (uint)boneOffsetsGpuManager.offsets.Length;
            uint boneOffsetsGpuSize      = boneOffsetsSize / 2 + (boneOffsetsSize & 1);
            newBuffers.boneOffsetsBuffer = broker.GetBoneOffsetsBuffer(boneOffsetsGpuSize);

            if (boneOffsetsGpuManager.isDirty.Value && boneOffsetsGpuSize > 0)
            {
                uint metaCount                                         = (uint)math.ceil(boneOffsetsGpuSize / 64f);
                newBuffers.boneOffsetsUploadBuffer                     = broker.GetBoneOffsetsUploadBuffer(boneOffsetsGpuSize);
                newBuffers.boneOffsetsUploadMetaBuffer                 = broker.GetMetaUint3UploadBuffer(boneOffsetsGpuSize);
                newBuffersMapped.boneOffsetsUploadBufferWriteCount     = boneOffsetsGpuSize;
                newBuffersMapped.boneOffsetsUploadMetaBufferWriteCount = metaCount;
                var mappedBoneOffsets                                  = newBuffers.boneOffsetsUploadBuffer.LockBufferForWrite<uint>(0, (int)boneOffsetsGpuSize);
                var mappedBoneOffsetsMeta                              = newBuffers.boneOffsetsUploadMetaBuffer.LockBufferForWrite<uint3>(0, (int)metaCount);
                newBuffersMapped.needsBoneOffsetCommitment             = true;

                jho = new UploadBoneOffsetsJob
                {
                    mappedBoneOffsets = mappedBoneOffsets,
                    mappedMeta        = mappedBoneOffsetsMeta,
                    offsets           = boneOffsetsGpuManager.offsets.AsArray()
                }.ScheduleBatch(boneOffsetsGpuManager.offsets.Length, 128);
            }

            var jhAll = new NativeList<JobHandle>(5, Allocator.Temp);
            jhAll.Add(jhv);
            jhAll.Add(jhw);
            jhAll.Add(jhp);
            jhAll.Add(jhs);
            jhAll.Add(jho);
            state.Dependency = JobHandle.CombineDependencies(jhAll.AsArray());

            if (newBuffersMapped.needsMeshCommitment || newBuffersMapped.needsBoneOffsetCommitment)
            {
                latiosWorld.worldBlackboardEntity.SetComponentData(newBuffers);
                latiosWorld.worldBlackboardEntity.SetCollectionComponentAndDisposeOld(newBuffersMapped);
                state.Dependency = new ClearCommandsJob
                {
                    commands      = meshGpuManager.uploadCommands,
                    requiredSizes = meshGpuManager.requiredBufferSizes,
                    isDirty       = boneOffsetsGpuManager.isDirty
                }.Schedule(state.Dependency);
            }
        }

        [BurstCompile]
        struct PrefixSumVerticesCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<uint>                            sums;

            public void Execute()
            {
                uint s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += (uint)commands[i].blob.Value.undeformedVertices.Length;
                }
            }
        }

        [BurstCompile]
        struct PrefixSumWeightsCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<uint>                            sums;

            public void Execute()
            {
                uint s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += (uint)commands[i].blob.Value.skinningData.boneWeights.Length;
                }
            }
        }

        [BurstCompile]
        struct PrefixSumBindPosesCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<uint>                            sums;

            public void Execute()
            {
                uint s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]               = s;
                    ref var skinningData  = ref commands[i].blob.Value.skinningData;
                    s                    += (uint)(skinningData.bindPoses.Length + skinningData.bindPosesDQ.Length);
                }
            }
        }

        [BurstCompile]
        struct PrefixSumBlendShapesCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<uint>                            sums;

            public void Execute()
            {
                uint s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += (uint)commands[i].blob.Value.blendShapesData.gpuData.Length;
                }
            }
        }

        [BurstCompile]
        struct UploadMeshesVerticesJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<uint>                 prefixSums;
            public NativeArray<UndeformedVertex>                mappedVertices;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                uint size = (uint)commands[index].blob.Value.undeformedVertices.Length;
                if (size == 0)
                {
                    mappedMeta[index] = uint3.zero;
                    return;
                }
                mappedMeta[index] = new uint3(prefixSums[index], commands[index].verticesIndex, size);
                var blobData      = commands[index].blob.Value.undeformedVertices.GetUnsafePtr();
                var subArray      = mappedVertices.GetSubArray((int)prefixSums[index], (int)size);
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, size * sizeof(UndeformedVertex));
            }
        }

        [BurstCompile]
        struct UploadMeshesWeightsJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<uint>                 prefixSums;
            public NativeArray<BoneWeightLinkedList>            mappedWeights;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                uint size = (uint)commands[index].blob.Value.skinningData.boneWeights.Length;
                if (size == 0)
                {
                    mappedMeta[index] = uint3.zero;
                    return;
                }
                mappedMeta[index] = new uint3(prefixSums[index], commands[index].weightsIndex, size);
                var blobData      = commands[index].blob.Value.skinningData.boneWeights.GetUnsafePtr();
                var subArray      = mappedWeights.GetSubArray((int)prefixSums[index], (int)size);
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, size * sizeof(BoneWeightLinkedList));
            }
        }

        [BurstCompile]
        struct UploadMeshesBindPosesJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<uint>                 prefixSums;
            public NativeArray<float3x4>                        mappedBindPoses;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                ref var skinningData = ref commands[index].blob.Value.skinningData;
                uint    size         = (uint)(skinningData.bindPoses.Length + skinningData.bindPosesDQ.Length);
                if (size == 0)
                {
                    mappedMeta[index] = uint3.zero;
                    return;
                }
                mappedMeta[index] = new uint3(prefixSums[index], commands[index].bindPosesIndex, size);
                var blobData      = skinningData.bindPoses.GetUnsafePtr();
                var subArray      = mappedBindPoses.GetSubArray((int)prefixSums[index], (int)size);
                int matsSize      = skinningData.bindPoses.Length;
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, matsSize * sizeof(float3x4));
                subArray = subArray.GetSubArray(matsSize, (int)size - matsSize);
                blobData = skinningData.bindPosesDQ.GetUnsafePtr();
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, ((int)size - matsSize) * sizeof(float3x4));
            }
        }

        [BurstCompile]
        struct UploadMeshesBlendShapesJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<uint>                 prefixSums;
            public NativeArray<BlendShapeVertexDisplacement>    mappedBlendShapes;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                uint size = (uint)commands[index].blob.Value.blendShapesData.gpuData.Length;
                if (size == 0)
                {
                    mappedMeta[index] = uint3.zero;
                    return;
                }
                mappedMeta[index] = new uint3(prefixSums[index], commands[index].blendShapesIndex, size);
                var blobData      = commands[index].blob.Value.blendShapesData.gpuData.GetUnsafePtr();
                var subArray      = mappedBlendShapes.GetSubArray((int)prefixSums[index], (int)size);
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, size * sizeof(BlendShapeVertexDisplacement));
            }
        }

        [BurstCompile]
        struct UploadBoneOffsetsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<short>                            offsets;
            [NativeDisableParallelForRestriction] public NativeArray<uint>  mappedBoneOffsets;
            [NativeDisableParallelForRestriction] public NativeArray<uint3> mappedMeta;

            public void Execute(int startIndex, int count)
            {
                int index         = startIndex / 128;
                mappedMeta[index] = (uint3) new int3(startIndex / 2, startIndex / 2, count / 2);
                for (int i = startIndex; i < startIndex + count; i += 2)
                {
                    uint packed              = (((uint)offsets[i + 1] & 0xffff) << 16) | ((uint)offsets[i] & 0xffff);
                    mappedBoneOffsets[i / 2] = packed;
                }
            }
        }

        [BurstCompile]
        struct ClearCommandsJob : IJob
        {
            public NativeList<MeshGpuUploadCommand>      commands;
            public NativeReference<MeshGpuRequiredSizes> requiredSizes;
            public NativeReference<bool>                 isDirty;

            public void Execute()
            {
                commands.Clear();
                var rs                           = requiredSizes.Value;
                rs.requiredVertexUploadSize      = 0;
                rs.requiredWeightUploadSize      = 0;
                rs.requiredBindPoseUploadSize    = 0;
                rs.requiredBlendShapesUploadSize = 0;
                requiredSizes.Value              = rs;
                isDirty.Value                    = false;
            }
        }
    }
}

