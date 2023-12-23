using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation
{
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    [DisableAutoCreation]
    public partial class UpdateGraphicsBufferBrokerSystem : SubSystem
    {
        protected override void OnCreate()
        {
            var broker = new GraphicsBufferBroker();
            worldBlackboardEntity.AddManagedStructComponent(new GraphicsBufferBrokerReference
            {
                graphicsBufferBroker = broker,
            });

            var copyVerticesShader        = Resources.Load<ComputeShader>("CopyVertices");
            var copyTransformUnionsShader = Resources.Load<ComputeShader>("CopyTransformUnions");
            var copyBlendShapesShader     = Resources.Load<ComputeShader>("CopyBlendShapes");
            var copyByteAddressShader     = Resources.Load<ComputeShader>("CopyBytes");

            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_skinningTransformsID,
                                              3 * 4 * 1024,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_deformedVerticesID,
                                              256 * 1024,
                                              3 * 3 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyVerticesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshVerticesID,
                                              64 * 1024,
                                              3 * 3 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyVerticesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshWeightsID,
                                              2 * 4 * 64 * 1024,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBindPosesID,
                                              1024,
                                              3 * 4 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyTransformUnionsShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBlendShapesID,
                                              16 * 1024,
                                              10 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyBlendShapesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_boneOffsetsID,
                                              512,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);

            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_meshVerticesUploadID,    3 * 3 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_meshWeightsUploadID,     4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_meshBindPosesUploadID,   3 * 4 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_meshBlendShapesUploadID, 10 * 4,    GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_boneOffsetsUploadID,     4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_bonesUploadID,           3 * 4 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_metaUint3UploadID,       4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_metaUint4UploadID,       4,         GraphicsBuffer.Target.Raw);
        }

        protected override void OnUpdate()
        {
            var broker = worldBlackboardEntity.GetManagedStructComponent<GraphicsBufferBrokerReference>();
            broker.graphicsBufferBroker.Update();
        }
    }

    internal static class InternalBrokerExtensions
    {
        const uint kMinMeshVerticesUploadSize    = 16 * 1024;
        const uint kMinMeshWeightsUploadSize     = 4 * 16 * 1024;
        const uint kMinMeshBindPosesUploadSize   = 256;
        const uint kMinMeshBlendShapesUploadSize = 1024;
        const uint kMinBoneOffsetsUploadSize     = 128;
        const uint kMinBonesSize                 = 128 * 128;
        const uint kMinGlyphsUploadSize          = 128;
        const uint kMinDispatchMetaSize          = 128;
        const uint kMinUploadMetaSize            = 128;

        public static GraphicsBuffer GetSkinningTransformsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_skinningTransformsID, requiredSize * 3 * 4);
        }

        public static GraphicsBuffer GetDeformBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_deformedVerticesID, requiredSize);
        }

        public static GraphicsBuffer GetMeshVerticesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshVerticesID, requiredSize);
        }

        public static GraphicsBuffer GetMeshWeightsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshWeightsID, requiredSize * 2);
        }

        public static GraphicsBuffer GetMeshWeightsBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_meshWeightsID);

        public static GraphicsBuffer GetMeshBindPosesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBindPosesID, requiredSize);
        }

        public static GraphicsBuffer GetMeshBindPosesBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_meshBindPosesID);

        public static GraphicsBuffer GetMeshBlendShapesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBlendShapesID, requiredSize);
        }

        public static GraphicsBuffer GetMeshBlendShapesBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_meshBlendShapesID);

        public static GraphicsBuffer GetBoneOffsetsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_boneOffsetsID, requiredSize);
        }

        public static GraphicsBuffer GetBoneOffsetsBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_boneOffsetsID);

        public static GraphicsBuffer GetMeshVerticesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshVerticesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshVerticesUploadID, requiredSize);
        }

        public static GraphicsBuffer GetMeshWeightsUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshWeightsUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshWeightsUploadID, requiredSize * 2);
        }

        public static GraphicsBuffer GetMeshBindPosesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBindPosesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBindPosesUploadID, requiredSize);
        }

        public static GraphicsBuffer GetMeshBlendShapesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBlendShapesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_meshBlendShapesUploadID, requiredSize);
        }

        public static GraphicsBuffer GetBoneOffsetsUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBoneOffsetsUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_boneOffsetsUploadID, requiredSize);
        }

        public static GraphicsBuffer GetBonesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBonesSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_bonesUploadID, requiredSize);
        }
    }
}

