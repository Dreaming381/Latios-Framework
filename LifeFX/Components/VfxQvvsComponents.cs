using Latios.Transforms;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    /// <summary>
    /// Add this to an entity to have it automatically track the WorldTransform/LocalToWorld for changes and upload those
    /// changes to the GPU. The QVVS structured buffer is named "_latiosTrackedWorldTransforms" which you can provide to a
    /// GraphicsGlobalBufferReceptor or a VfxGraphGlobalBufferProvider.
    /// This component will be enabled if this is considered a new entity to be tracked, so that you can send the
    /// index to the GPU via a Graphics Event. To do this, update your system within KinemationCustomGraphicsSetupSuperSystem
    /// (and after UpdateTrackedWorldTransformSystem if you use OrderFirst = true). Note that the bits 30 and 31 of worldIndex
    /// are overwritten. Bit 31 specifies the entity is alive, and bit 30 is the value of TrackedWorldTransformEnableFlag if
    /// present, or 1 otherwise.
    /// </summary>
    public struct TrackedWorldTransform : IComponentData, IEnableableComponent
    {
        internal int index;

        /// <summary>
        /// The index of the transform within the global _latiosTrackedWorldTransforms GraphicsBuffer (structured)
        /// </summary>
        public int transformIndexInBuffer => index;
    }

    /// <summary>
    /// This is an optional companion which can be added in tandem with TrackedWorldTransform. When this component is present
    /// and disabled, initial tracking will be blocked, as will frame updates. However, if tracking was enabled for the previous
    /// frame, one additional update will be sent to report to the GPU the tracked transform has been disabled.
    /// </summary>
    public struct TrackedWorldTransformEnableFlag : IComponentData, IEnableableComponent { }

    internal partial struct TrackedTransformUploadList : ICollectionComponent
    {
        public NativeList<TransformQvvs>    trackedTransforms;
        public UnsafeParallelBlockList<int> uploadIndices;

        // worldUpdateAllocator / owned explicitly by UpdateTrackedWorldTransformSystem
        public JobHandle TryDispose(JobHandle inputDeps) => inputDeps;
    }
}

