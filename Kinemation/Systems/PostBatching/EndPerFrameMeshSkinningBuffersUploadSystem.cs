using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class EndPerFrameMeshSkinningBuffersUploadSystem : SubSystem
    {
        UnityEngine.ComputeShader m_verticesUploadShader;
        UnityEngine.ComputeShader m_matricesUploadShader;
        UnityEngine.ComputeShader m_bytesUploadShader;

        protected override void OnCreate()
        {
            m_verticesUploadShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadVertices");
            m_matricesUploadShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadMatrices");
            m_bytesUploadShader    = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadBytes");
        }

        protected override void OnUpdate()
        {
            var buffers = worldBlackboardEntity.GetCollectionComponent<GpuUploadBuffers>(false, out var jh);
            jh.Complete();

            if (!buffers.needsMeshCommitment && !buffers.needsBoneOffsetCommitment)
                return;

            if (buffers.needsMeshCommitment)
            {
                buffers.verticesUploadBuffer.EndWrite<VertexToSkin>(buffers.verticesUploadBufferWriteCount);
                buffers.verticesUploadMetaBuffer.EndWrite<uint3>(buffers.verticesUploadMetaBufferWriteCount);
                buffers.weightsUploadBuffer.EndWrite<BoneWeightLinkedList>(buffers.weightsUploadBufferWriteCount);
                buffers.weightsUploadMetaBuffer.EndWrite<uint3>(buffers.weightsUploadMetaBufferWriteCount);
                buffers.bindPosesUploadBuffer.EndWrite<float3x4>(buffers.bindPosesUploadBufferWriteCount);
                buffers.bindPosesUploadMetaBuffer.EndWrite<uint3>(buffers.bindPosesUploadMetaBufferWriteCount);

                m_verticesUploadShader.SetBuffer(0, "_dst",  buffers.verticesBuffer);
                m_verticesUploadShader.SetBuffer(0, "_src",  buffers.verticesUploadBuffer);
                m_verticesUploadShader.SetBuffer(0, "_meta", buffers.verticesUploadMetaBuffer);

                for (int dispatchesRemaining = buffers.verticesUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_verticesUploadShader.SetInt("_startOffset", offset);
                    m_verticesUploadShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                m_bytesUploadShader.SetBuffer(0, "_dst",  buffers.weightsBuffer);
                m_bytesUploadShader.SetBuffer(0, "_src",  buffers.weightsUploadBuffer);
                m_bytesUploadShader.SetBuffer(0, "_meta", buffers.weightsUploadMetaBuffer);
                m_bytesUploadShader.SetInt("_elementSizeInBytes", 8);

                for (int dispatchesRemaining = buffers.weightsUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_bytesUploadShader.SetInt("_startOffset", offset);
                    m_bytesUploadShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                m_matricesUploadShader.SetBuffer(0, "_dst",  buffers.bindPosesBuffer);
                m_matricesUploadShader.SetBuffer(0, "_src",  buffers.bindPosesUploadBuffer);
                m_matricesUploadShader.SetBuffer(0, "_meta", buffers.bindPosesUploadMetaBuffer);

                for (int dispatchesRemaining = buffers.bindPosesUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_matricesUploadShader.SetInt("_startOffset", offset);
                    m_matricesUploadShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
                buffers.needsMeshCommitment = false;
            }

            if (buffers.needsBoneOffsetCommitment)
            {
                buffers.boneOffsetsUploadBuffer.EndWrite<uint>(buffers.boneOffsetsUploadBufferWriteCount);
                buffers.boneOffsetsUploadMetaBuffer.EndWrite<uint3>(buffers.boneOffsetsUploadMetaBufferWriteCount);

                m_bytesUploadShader.SetBuffer(0, "_dst",  buffers.boneOffsetsBuffer);
                m_bytesUploadShader.SetBuffer(0, "_src",  buffers.boneOffsetsUploadBuffer);
                m_bytesUploadShader.SetBuffer(0, "_meta", buffers.boneOffsetsUploadMetaBuffer);
                m_bytesUploadShader.SetInt("_elementSizeInBytes", 4);

                for (int dispatchesRemaining = buffers.boneOffsetsUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_bytesUploadShader.SetInt("_startOffset", offset);
                    m_bytesUploadShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
                buffers.needsBoneOffsetCommitment = false;
            }

            worldBlackboardEntity.SetCollectionComponentAndDisposeOld(buffers);
        }
    }
}

