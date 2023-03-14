using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class EndPerFrameMeshDeformBuffersUploadSystem : SubSystem
    {
        UnityEngine.ComputeShader m_verticesUploadShader;
        UnityEngine.ComputeShader m_transformUnionsUploadShader;
        UnityEngine.ComputeShader m_blendShapesUploadShader;
        UnityEngine.ComputeShader m_bytesUploadShader;

        protected override void OnCreate()
        {
            m_verticesUploadShader        = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadVertices");
            m_transformUnionsUploadShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadTransformUnions");
            m_blendShapesUploadShader     = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadBlendShapes");
            m_bytesUploadShader           = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("UploadBytes");
        }

        protected override void OnUpdate()
        {
            var buffersMapped = worldBlackboardEntity.GetCollectionComponent<MeshGpuUploadBuffersMapped>(false);
            var buffers       = worldBlackboardEntity.GetManagedStructComponent<MeshGpuUploadBuffers>();
            CompleteDependency();

            if (!buffersMapped.needsMeshCommitment && !buffersMapped.needsBoneOffsetCommitment)
                return;

            if (buffersMapped.needsMeshCommitment)
            {
                buffers.verticesUploadBuffer.UnlockBufferAfterWrite<UndeformedVertex>((int)buffersMapped.verticesUploadBufferWriteCount);
                buffers.verticesUploadMetaBuffer.UnlockBufferAfterWrite<uint3>((int)buffersMapped.verticesUploadMetaBufferWriteCount);
                buffers.weightsUploadBuffer.UnlockBufferAfterWrite<BoneWeightLinkedList>((int)buffersMapped.weightsUploadBufferWriteCount);
                buffers.weightsUploadMetaBuffer.UnlockBufferAfterWrite<uint3>((int)buffersMapped.weightsUploadMetaBufferWriteCount);
                buffers.blendShapesUploadBuffer.UnlockBufferAfterWrite<BlendShapeVertexDisplacement>((int)buffersMapped.blendShapesUploadBufferWriteCount);
                buffers.blendShapesUploadMetaBuffer.UnlockBufferAfterWrite<uint3>((int)buffersMapped.blendShapesUploadMetaBufferWriteCount);
                buffers.bindPosesUploadBuffer.UnlockBufferAfterWrite<float3x4>((int)buffersMapped.bindPosesUploadBufferWriteCount);
                buffers.bindPosesUploadMetaBuffer.UnlockBufferAfterWrite<uint3>((int)buffersMapped.bindPosesUploadMetaBufferWriteCount);

                m_verticesUploadShader.SetBuffer(0, "_dst",  buffers.verticesBuffer);
                m_verticesUploadShader.SetBuffer(0, "_src",  buffers.verticesUploadBuffer);
                m_verticesUploadShader.SetBuffer(0, "_meta", buffers.verticesUploadMetaBuffer);

                for (uint dispatchesRemaining = buffersMapped.verticesUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_verticesUploadShader.SetInt("_startOffset", (int)offset);
                    m_verticesUploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                m_bytesUploadShader.SetBuffer(0, "_dst",  buffers.weightsBuffer);
                m_bytesUploadShader.SetBuffer(0, "_src",  buffers.weightsUploadBuffer);
                m_bytesUploadShader.SetBuffer(0, "_meta", buffers.weightsUploadMetaBuffer);
                m_bytesUploadShader.SetInt("_elementSizeInBytes", 8);

                for (uint dispatchesRemaining = buffersMapped.weightsUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_bytesUploadShader.SetInt("_startOffset", (int)offset);
                    m_bytesUploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                m_transformUnionsUploadShader.SetBuffer(0, "_dst",  buffers.bindPosesBuffer);
                m_transformUnionsUploadShader.SetBuffer(0, "_src",  buffers.bindPosesUploadBuffer);
                m_transformUnionsUploadShader.SetBuffer(0, "_meta", buffers.bindPosesUploadMetaBuffer);

                for (uint dispatchesRemaining = buffersMapped.bindPosesUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_transformUnionsUploadShader.SetInt("_startOffset", (int)offset);
                    m_transformUnionsUploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                m_blendShapesUploadShader.SetBuffer(0, "_dst",  buffers.blendShapesBuffer);
                m_blendShapesUploadShader.SetBuffer(0, "_src",  buffers.blendShapesUploadBuffer);
                m_blendShapesUploadShader.SetBuffer(0, "_meta", buffers.blendShapesUploadMetaBuffer);

                for (uint dispatchesRemaining = buffersMapped.blendShapesUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_blendShapesUploadShader.SetInt("_startOffset", (int)offset);
                    m_blendShapesUploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }

                buffersMapped.needsMeshCommitment = false;
            }

            if (buffersMapped.needsBoneOffsetCommitment)
            {
                buffers.boneOffsetsUploadBuffer.UnlockBufferAfterWrite<uint>((int)buffersMapped.boneOffsetsUploadBufferWriteCount);
                buffers.boneOffsetsUploadMetaBuffer.UnlockBufferAfterWrite<uint3>((int)buffersMapped.boneOffsetsUploadMetaBufferWriteCount);

                m_bytesUploadShader.SetBuffer(0, "_dst",  buffers.boneOffsetsBuffer);
                m_bytesUploadShader.SetBuffer(0, "_src",  buffers.boneOffsetsUploadBuffer);
                m_bytesUploadShader.SetBuffer(0, "_meta", buffers.boneOffsetsUploadMetaBuffer);
                m_bytesUploadShader.SetInt("_elementSizeInBytes", 4);

                for (uint dispatchesRemaining = buffersMapped.boneOffsetsUploadMetaBufferWriteCount, offset = 0; dispatchesRemaining > 0;)
                {
                    uint dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_bytesUploadShader.SetInt("_startOffset", (int)offset);
                    m_bytesUploadShader.Dispatch(0, (int)dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
                buffersMapped.needsBoneOffsetCommitment = false;
            }

            worldBlackboardEntity.SetCollectionComponentAndDisposeOld(buffersMapped);
        }
    }
}

