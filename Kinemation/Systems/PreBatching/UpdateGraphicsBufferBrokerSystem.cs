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
    public partial struct UpdateGraphicsBufferBrokerSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        GraphicsBufferBroker broker;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            broker = new GraphicsBufferBroker(Allocator.Persistent);
            latiosWorld.worldBlackboardEntity.AddComponentData(broker);
            latiosWorld.worldBlackboardEntity.AddManagedStructComponent(new GraphicsBufferBrokerDeferredDisposer { broker = broker });

            var copyVerticesShader        = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("CopyVertices");
            var copyTransformUnionsShader = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("CopyTransformUnions");
            var copyBlendShapesShader     = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("CopyBlendShapes");
            var copyByteAddressShader     = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<ComputeShader>("CopyBytes");

            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.skinningTransformsID,
                                              3 * 4 * 1024,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.deformedVerticesID,
                                              256 * 1024,
                                              3 * 3 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyVerticesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshVerticesID,
                                              64 * 1024,
                                              3 * 3 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyVerticesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshWeightsID,
                                              2 * 4 * 64 * 1024,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBindPosesID,
                                              1024,
                                              3 * 4 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyTransformUnionsShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBlendShapesID,
                                              16 * 1024,
                                              10 * 4,
                                              GraphicsBuffer.Target.Structured,
                                              copyBlendShapesShader);
            broker.InitializePersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.boneOffsetsID,
                                              512,
                                              4,
                                              GraphicsBuffer.Target.Raw,
                                              copyByteAddressShader);

            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshVerticesUploadID,    3 * 3 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshWeightsUploadID,     4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBindPosesUploadID,   3 * 4 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBlendShapesUploadID, 10 * 4,    GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.boneOffsetsUploadID,     4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.bonesUploadID,           3 * 4 * 4, GraphicsBuffer.Target.Structured);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.metaUint3UploadID,       4,         GraphicsBuffer.Target.Raw);
            broker.InitializeUploadPool(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.metaUint4UploadID,       4,         GraphicsBuffer.Target.Raw);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var broker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();
            broker.Update();
        }

        public void OnDestroy(ref SystemState state)
        {
            //broker.Dispose();
        }

        partial struct GraphicsBufferBrokerDeferredDisposer : IManagedStructComponent
        {
            public GraphicsBufferBroker broker;

            public void Dispose() => broker.Dispose();
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
        const uint kMinDispatchMetaSize          = 128;
        const uint kMinUploadMetaSize            = 128;

        public static GraphicsBufferUnmanaged GetSkinningTransformsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.skinningTransformsID, requiredSize * 3 * 4);
        }

        public static GraphicsBufferUnmanaged GetDeformBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.deformedVerticesID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshVerticesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshVerticesID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshWeightsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshWeightsID, requiredSize * 2);
        }

        public static GraphicsBufferUnmanaged GetMeshWeightsBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshWeightsID);

        public static GraphicsBufferUnmanaged GetMeshBindPosesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBindPosesID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshBindPosesBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBindPosesID);

        public static GraphicsBufferUnmanaged GetMeshBlendShapesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBlendShapesID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshBlendShapesBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBlendShapesID);

        public static GraphicsBufferUnmanaged GetBoneOffsetsBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            return broker.GetPersistentBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.boneOffsetsID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetBoneOffsetsBufferRO(this GraphicsBufferBroker broker) => broker.GetPersistentBufferNoResize(
            DeformationGraphicsBufferBrokerExtensions.s_ids.Data.boneOffsetsID);

        public static GraphicsBufferUnmanaged GetMeshVerticesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshVerticesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshVerticesUploadID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshWeightsUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshWeightsUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshWeightsUploadID, requiredSize * 2);
        }

        public static GraphicsBufferUnmanaged GetMeshBindPosesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBindPosesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBindPosesUploadID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetMeshBlendShapesUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinMeshBlendShapesUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.meshBlendShapesUploadID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetBoneOffsetsUploadBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBoneOffsetsUploadSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.boneOffsetsUploadID, requiredSize);
        }

        public static GraphicsBufferUnmanaged GetBonesBuffer(this GraphicsBufferBroker broker, uint requiredSize)
        {
            requiredSize = math.max(requiredSize, kMinBonesSize);
            return broker.GetUploadBuffer(DeformationGraphicsBufferBrokerExtensions.s_ids.Data.bonesUploadID, requiredSize);
        }
    }
}

