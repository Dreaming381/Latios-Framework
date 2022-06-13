using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class BeginPerFrameMeshSkinningBuffersUploadSystem : SubSystem
    {
        protected override void OnCreate()
        {
            worldBlackboardEntity.AddCollectionComponent(new ComputeBufferManager
            {
                pool = new ComputeBufferTrackingPool(),
            }, true);

            worldBlackboardEntity.AddCollectionComponent(new GpuUploadBuffers());
        }

        protected override void OnUpdate()
        {
            var meshGpuManager        = worldBlackboardEntity.GetCollectionComponent<MeshGpuManager>(false, out var meshGpuJH);
            var boneOffsetsGpuManager = worldBlackboardEntity.GetCollectionComponent<BoneOffsetsGpuManager>(false, out var boneOffsetsGpuJH);
            var bufferManager         = worldBlackboardEntity.GetCollectionComponent<ComputeBufferManager>(false, out var computeBufferJH);
            worldBlackboardEntity.GetCollectionComponent<GpuUploadBuffers>(false, out var meshUploadJH);

            bufferManager.pool.Update();
            var jhs = new NativeList<JobHandle>(4, Allocator.Temp);
            jhs.Add(meshGpuJH);
            jhs.Add(boneOffsetsGpuJH);
            jhs.Add(computeBufferJH);
            jhs.Add(meshUploadJH);
            CompleteDependency();
            JobHandle.CompleteAll(jhs);

            JobHandle jhv = default;
            JobHandle jhw = default;
            JobHandle jhb = default;

            GpuUploadBuffers newBuffers = default;

            var requiredSizes          = meshGpuManager.requiredBufferSizes.Value;
            newBuffers.verticesBuffer  = bufferManager.pool.GetMeshVerticesBuffer(requiredSizes.requiredVertexBufferSize);
            newBuffers.weightsBuffer   = bufferManager.pool.GetMeshWeightsBuffer(requiredSizes.requiredWeightBufferSize);
            newBuffers.bindPosesBuffer = bufferManager.pool.GetMeshBindPosesBuffer(requiredSizes.requiredBindPoseBufferSize);

            if (!meshGpuManager.uploadCommands.IsEmpty)
            {
                var uploadCount                                = meshGpuManager.uploadCommands.Length;
                newBuffers.verticesUploadBuffer                = bufferManager.pool.GetMeshVerticesUploadBuffer(requiredSizes.requiredVertexUploadSize);
                newBuffers.weightsUploadBuffer                 = bufferManager.pool.GetMeshWeightsUploadBuffer(requiredSizes.requiredWeightUploadSize);
                newBuffers.bindPosesUploadBuffer               = bufferManager.pool.GetMeshBindPosesUploadBuffer(requiredSizes.requiredBindPoseUploadSize);
                newBuffers.verticesUploadMetaBuffer            = bufferManager.pool.GetUploadMetaBuffer(uploadCount);
                newBuffers.weightsUploadMetaBuffer             = bufferManager.pool.GetUploadMetaBuffer(uploadCount);
                newBuffers.bindPosesUploadMetaBuffer           = bufferManager.pool.GetUploadMetaBuffer(uploadCount);
                newBuffers.verticesUploadBufferWriteCount      = requiredSizes.requiredVertexUploadSize;
                newBuffers.weightsUploadBufferWriteCount       = requiredSizes.requiredWeightUploadSize;
                newBuffers.bindPosesUploadBufferWriteCount     = requiredSizes.requiredBindPoseUploadSize;
                newBuffers.verticesUploadMetaBufferWriteCount  = uploadCount;
                newBuffers.weightsUploadMetaBufferWriteCount   = uploadCount;
                newBuffers.bindPosesUploadMetaBufferWriteCount = uploadCount;
                var mappedVertices                             = newBuffers.verticesUploadBuffer.BeginWrite<VertexToSkin>(0, requiredSizes.requiredVertexUploadSize);
                var mappedWeights                              = newBuffers.weightsUploadBuffer.BeginWrite<BoneWeightLinkedList>(0, requiredSizes.requiredWeightUploadSize);  // Unity uses T for sizing so we don't need to *2 here.
                var mappedBindPoses                            = newBuffers.bindPosesUploadBuffer.BeginWrite<float3x4>(0, requiredSizes.requiredBindPoseUploadSize);
                var mappedVerticesMeta                         = newBuffers.verticesUploadMetaBuffer.BeginWrite<uint3>(0, uploadCount);
                var mappedWeightsMeta                          = newBuffers.weightsUploadMetaBuffer.BeginWrite<uint3>(0, uploadCount);
                var mappedBindPosesMeta                        = newBuffers.bindPosesUploadMetaBuffer.BeginWrite<uint3>(0, uploadCount);
                newBuffers.needsMeshCommitment                 = true;

                ref var allocator     = ref World.UpdateAllocator;
                var     verticesSums  = allocator.AllocateNativeArray<int>(meshGpuManager.uploadCommands.Length);
                var     weightsSums   = allocator.AllocateNativeArray<int>(meshGpuManager.uploadCommands.Length);
                var     bindPosesSums = allocator.AllocateNativeArray<int>(meshGpuManager.uploadCommands.Length);
                jhv                   = new PrefixSumVerticesCountsJob
                {
                    commands = meshGpuManager.uploadCommands,
                    sums     = verticesSums
                }.Schedule();
                jhw = new PrefixSumWeightsCountsJob
                {
                    commands = meshGpuManager.uploadCommands,
                    sums     = weightsSums
                }.Schedule();
                jhb = new PrefixSumBindPosesCountsJob
                {
                    commands = meshGpuManager.uploadCommands,
                    sums     = bindPosesSums
                }.Schedule();
                jhv = new UploadMeshesVerticesJob
                {
                    commands       = meshGpuManager.uploadCommands,
                    prefixSums     = verticesSums,
                    mappedVertices = mappedVertices,
                    mappedMeta     = mappedVerticesMeta
                }.ScheduleParallel(uploadCount, 1, jhv);
                jhw = new UploadMeshesWeightsJob
                {
                    commands      = meshGpuManager.uploadCommands,
                    prefixSums    = weightsSums,
                    mappedWeights = mappedWeights,
                    mappedMeta    = mappedWeightsMeta
                }.ScheduleParallel(uploadCount, 1, jhw);
                jhb = new UploadMeshesBindPosesJob
                {
                    commands        = meshGpuManager.uploadCommands,
                    prefixSums      = bindPosesSums,
                    mappedBindPoses = mappedBindPoses,
                    mappedMeta      = mappedBindPosesMeta
                }.ScheduleParallel(uploadCount, 1, jhb);
            }

            JobHandle jho = default;

            int boneOffsetsSize          = boneOffsetsGpuManager.offsets.Length;
            int boneOffsetsGpuSize       = boneOffsetsSize / 2;
            newBuffers.boneOffsetsBuffer = bufferManager.pool.GetBoneOffsetsBuffer(boneOffsetsGpuSize);

            if (boneOffsetsGpuManager.isDirty.Value)
            {
                int metaCount                                    = (int)math.ceil(boneOffsetsGpuSize / 64f);
                newBuffers.boneOffsetsUploadBuffer               = bufferManager.pool.GetBoneOffsetsUploadBuffer(boneOffsetsGpuSize);
                newBuffers.boneOffsetsUploadMetaBuffer           = bufferManager.pool.GetUploadMetaBuffer(boneOffsetsGpuSize);
                newBuffers.boneOffsetsUploadBufferWriteCount     = boneOffsetsGpuSize;
                newBuffers.boneOffsetsUploadMetaBufferWriteCount = metaCount;
                var mappedBoneOffsets                            = newBuffers.boneOffsetsUploadBuffer.BeginWrite<uint>(0, boneOffsetsGpuSize);
                var mappedBoneOffsetsMeta                        = newBuffers.boneOffsetsUploadMetaBuffer.BeginWrite<uint3>(0, metaCount);
                newBuffers.needsBoneOffsetCommitment             = true;

                jho = new UploadBoneOffsetsJob
                {
                    mappedBoneOffsets = mappedBoneOffsets,
                    mappedMeta        = mappedBoneOffsetsMeta,
                    offsets           = boneOffsetsGpuManager.offsets
                }.ScheduleBatch(boneOffsetsGpuManager.offsets.Length, 128);
            }

            jhs.Clear();
            jhs.Add(jhv);
            jhs.Add(jhw);
            jhs.Add(jhb);
            jhs.Add(jho);
            Dependency = JobHandle.CombineDependencies(jhs);

            if (newBuffers.needsMeshCommitment || newBuffers.needsBoneOffsetCommitment)
            {
                worldBlackboardEntity.SetCollectionComponentAndDisposeOld(newBuffers);
                Dependency = new ClearCommandsJob { commands = meshGpuManager.uploadCommands, isDirty = boneOffsetsGpuManager.isDirty }.Schedule(Dependency);
            }
        }

        [BurstCompile]
        struct PrefixSumVerticesCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<int>                             sums;

            public void Execute()
            {
                int s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += commands[i].blob.Value.verticesToSkin.Length;
                }
            }
        }

        [BurstCompile]
        struct PrefixSumWeightsCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<int>                             sums;

            public void Execute()
            {
                int s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += commands[i].blob.Value.boneWeights.Length;
                }
            }
        }

        [BurstCompile]
        struct PrefixSumBindPosesCountsJob : IJob
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            public NativeArray<int>                             sums;

            public void Execute()
            {
                int s = 0;
                for (int i = 0; i < commands.Length; i++)
                {
                    sums[i]  = s;
                    s       += commands[i].blob.Value.bindPoses.Length;
                }
            }
        }

        [BurstCompile]
        struct UploadMeshesVerticesJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<int>                  prefixSums;
            public NativeArray<VertexToSkin>                    mappedVertices;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                int size          = commands[index].blob.Value.verticesToSkin.Length;
                mappedMeta[index] = (uint3) new int3(prefixSums[index], commands[index].verticesIndex, size);
                var blobData      = commands[index].blob.Value.verticesToSkin.GetUnsafePtr();
                var subArray      = mappedVertices.GetSubArray(prefixSums[index], size);
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, size * sizeof(VertexToSkin));
            }
        }

        [BurstCompile]
        struct UploadMeshesWeightsJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<int>                  prefixSums;
            public NativeArray<BoneWeightLinkedList>            mappedWeights;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                int size          = commands[index].blob.Value.boneWeights.Length;
                mappedMeta[index] = (uint3) new int3(prefixSums[index], commands[index].weightsIndex, size);
                var blobData      = commands[index].blob.Value.boneWeights.GetUnsafePtr();
                var subArray      = mappedWeights.GetSubArray(prefixSums[index], size);
                UnsafeUtility.MemCpy(subArray.GetUnsafePtr(), blobData, size * sizeof(BoneWeightLinkedList));
            }
        }

        [BurstCompile]
        struct UploadMeshesBindPosesJob : IJobFor
        {
            [ReadOnly] public NativeArray<MeshGpuUploadCommand> commands;
            [ReadOnly] public NativeArray<int>                  prefixSums;
            public NativeArray<float3x4>                        mappedBindPoses;
            public NativeArray<uint3>                           mappedMeta;

            public unsafe void Execute(int index)
            {
                int size          = commands[index].blob.Value.bindPoses.Length;
                mappedMeta[index] = (uint3) new int3(prefixSums[index], commands[index].bindPosesIndex, size);
                ref var blobData  = ref commands[index].blob.Value.bindPoses;
                var     subArray  = mappedBindPoses.GetSubArray(prefixSums[index], size);

                for (int i = 0; i < size; i++)
                {
                    subArray[i] = Shrink(blobData[i]);
                }
            }

            float3x4 Shrink(float4x4 a)
            {
                return new float3x4(a.c0.xyz, a.c1.xyz, a.c2.xyz, a.c3.xyz);
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
#pragma warning disable CS0675  // Bitwise-or operator used on a sign-extended operand
                    uint packed = (((uint)offsets[i + 1]) << 16) | ((uint)offsets[i]);
#pragma warning restore CS0675  // Bitwise-or operator used on a sign-extended operand
                    mappedBoneOffsets[i / 2] = packed;
                }
            }
        }

        [BurstCompile]
        struct ClearCommandsJob : IJob
        {
            public NativeList<MeshGpuUploadCommand> commands;
            public NativeReference<bool>            isDirty;

            public void Execute()
            {
                commands.Clear();
                isDirty.Value = false;
            }
        }
    }
}

