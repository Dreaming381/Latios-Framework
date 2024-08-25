using System;
using System.Runtime.InteropServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A triMesh collider shape that can be scaled and stretched in local space efficiently.
    /// It is often derived from a Mesh.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct TriMeshCollider
    {
        /// <summary>
        /// The blob asset containing the raw triMesh hull data
        /// </summary>
        [FieldOffset(24)] public BlobAssetReference<TriMeshColliderBlob> triMeshColliderBlob;
        /// <summary>
        /// The premultiplied scale and stretch in local space
        /// </summary>
        [FieldOffset(0)] public float3 scale;

        /// <summary>
        /// Creates a new TriMeshCollider
        /// </summary>
        /// <param name="triMeshColliderBlob">The blob asset containing the raw triMesh hull data</param>
        public TriMeshCollider(BlobAssetReference<TriMeshColliderBlob> triMeshColliderBlob) : this(triMeshColliderBlob, new float3(1f, 1f, 1f))
        {
        }

        /// <summary>
        /// Creates a new TriMeshCollider
        /// </summary>
        /// <param name="triMeshColliderBlob">The blob asset containing the raw triMesh hull data</param>
        /// <param name="scale">The premultiplied scale and stretch in local space</param>
        public TriMeshCollider(BlobAssetReference<TriMeshColliderBlob> triMeshColliderBlob, float3 scale)
        {
            this.triMeshColliderBlob = triMeshColliderBlob;
            this.scale               = scale;
        }
    }

    /// <summary>
    /// The definition of a Triangle Mesh and associated spatial acceleration structures.
    /// </summary>
    public struct TriMeshColliderBlob
    {
        // This structure and the order of triangles are subject to change in a future version.
        // There are lots of data compression opportunities, along with a better acceleration
        // structure.
        internal BlobArray<float>            xmins;
        internal BlobArray<float>            xmaxs;
        internal BlobArray<float4>           yzminmaxs;
        internal BlobArray<IntervalTreeNode> intervalTree;

        /// <summary>
        /// The triangles of the mesh, in an undefined order
        /// </summary>
        public BlobArray<TriangleCollider> triangles;
        /// <summary>
        /// For each given triangle, the value of the sourceIndices at the same index is the index
        /// of the original triangle in the mesh
        /// </summary>
        public BlobArray<int> sourceIndices;
        /// <summary>
        /// The axis aligned bounding box around all the triangles in the collider's local space
        /// </summary>
        public Aabb localAabb;
        /// <summary>
        /// The name of the mesh used to bake this blob
        /// </summary>
        public FixedString128Bytes meshName;

        /// <summary>
        /// An interface used for processing found triangles in the TriMesh Collider Blob
        /// </summary>
        public interface IFindTrianglesProcessor
        {
            /// <summary>
            /// Runs once for each found triangle that overlaps the queried Aabb
            /// </summary>
            /// <param name="triangleIndex">The index of the triangle in the blob</param>
            /// <returns>True if the search should continue, false otherwise</returns>
            bool Execute(int triangleIndex);
        }

        /// <summary>
        /// Finds the triangles in the TriMesh Collider Blob which overlap the queried Aabb
        /// and invokes the processor for each triangle found.
        /// </summary>
        /// <typeparam name="T">An IFindTrianglesProcessor</typeparam>
        /// <param name="scaledTriMeshSpaceAabb">The Aabb in the blob's belonging collider's scaled space</param>
        /// <param name="processor">The processor which should handle each result found</param>
        /// <param name="triMeshScale">The scale of the collider</param>
        public void FindTriangles<T>(in Aabb scaledTriMeshSpaceAabb, ref T processor, float3 triMeshScale) where T : unmanaged, IFindTrianglesProcessor
        {
            if (math.any(math.isnan(scaledTriMeshSpaceAabb.min) | math.isnan(scaledTriMeshSpaceAabb.max) | math.isnan(triMeshScale)))
                return;

            var inverseScale  = math.rcp(triMeshScale);
            var aabb          = scaledTriMeshSpaceAabb;
            aabb.min         *= inverseScale;
            aabb.max         *= inverseScale;
            aabb.min          = math.select(aabb.min, float.MinValue, math.isnan(aabb.min));
            aabb.max          = math.select(aabb.max, float.MinValue, math.isnan(aabb.max));
            FindTriangles(in aabb, ref processor);
        }

        /// <summary>
        /// Finds the triangles in the TriMesh Collider Blob which overlap the queried Aabb
        /// and invokes the processor for each triangle found
        /// </summary>
        /// <typeparam name="T">An IFindTrianglesProcessor</typeparam>
        /// <param name="triMeshSpaceAabb">The Aabb in the blob's local (untransformed) space</param>
        /// <param name="processor">The processor which should handle each result found</param>
        public void FindTriangles<T>(in Aabb triMeshSpaceAabb, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            if (intervalTree.Length == 0)
                return;

            var linearSweepStartIndex = BinarySearchFirstGreaterOrEqual(triMeshSpaceAabb.min.x);

            var    qxmax     = triMeshSpaceAabb.max.x;
            float4 qyzMinMax = new float4(triMeshSpaceAabb.max.yz, -triMeshSpaceAabb.min.yz);

            for (int i = linearSweepStartIndex; i < xmins.Length && xmins[i] <= qxmax; i++)
            {
                if (Hint.Unlikely(math.bitmask(qyzMinMax < yzminmaxs[i]) == 0))
                {
                    if (processor.Execute(i) == false)
                        return;
                }
            }

            SearchTreeLooped(qyzMinMax, triMeshSpaceAabb.min.x, ref processor);
        }

        // Returns count if nothing is greater or equal
        //   The following function is a C# and Burst adaptation of Paul-Virak Khuong and Pat Morin's
        //   optimized sequential order binary search: https://github.com/patmorin/arraylayout/blob/master/src/sorted_array.h
        //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
        private unsafe int BinarySearchFirstGreaterOrEqual(float searchValue)
        {
            float* array = (float*)xmins.GetUnsafePtr();
            int    count = xmins.Length;

            for (int i = 1; i < count; i++)
            {
                Hint.Assume(array[i] >= array[i - 1]);
            }

            var  basePtr = array;
            uint n       = (uint)count;
            while (Hint.Likely(n > 1))
            {
                var half    = n / 2;
                n          -= half;
                var newPtr  = &basePtr[half];

                // As of Burst 1.8.0 prev 2
                // Burst never loads &basePtr[half] into a register for newPtr, and instead uses dual register addressing instead.
                // Because of this, instead of loading into the register, performing the comparison, using a cmov, and then a jump,
                // Burst immediately performs the comparison, conditionally jumps, uses a lea, and then a jump.
                // This is technically less instructions on average. But branch prediction may suffer as a result.
                basePtr = *newPtr < searchValue ? newPtr : basePtr;
            }

            if (*basePtr < searchValue)
                basePtr++;

            return (int)(basePtr - array);
        }

        private static uint GetLeftChildIndex(uint currentIndex) => 2 * currentIndex + 1;
        private static uint GetRightChildIndex(uint currentIndex) => 2 * currentIndex + 2;
        private static uint GetParentIndex(uint currentIndex) => (currentIndex - 1) / 2;

        struct StackFrame
        {
            public uint currentIndex;
            public uint checkpoint;
        }

        [SkipLocalsInit]
        private unsafe void SearchTreeLooped<T>(float4 qyzMinMax, float qxmin, ref T processor) where T : struct, IFindTrianglesProcessor
        {
            uint        currentFrameIndex = 0;
            StackFrame* stack             = stackalloc StackFrame[32];
            stack[0]                      = new StackFrame { currentIndex = 0, checkpoint = 0 };

            while (currentFrameIndex < 32)
            {
                var currentFrame = stack[currentFrameIndex];
                if (currentFrame.checkpoint == 0)
                {
                    if (currentFrame.currentIndex >= intervalTree.Length)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    var node = intervalTree[(int)currentFrame.currentIndex];
                    if (qxmin >= node.subtreeXmax)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    currentFrame.checkpoint  = 1;
                    stack[currentFrameIndex] = currentFrame;
                    currentFrameIndex++;
                    stack[currentFrameIndex].currentIndex = GetLeftChildIndex(currentFrame.currentIndex);
                    stack[currentFrameIndex].checkpoint   = 0;
                    continue;
                }
                else if (currentFrame.checkpoint == 1)
                {
                    var node = intervalTree[(int)currentFrame.currentIndex];
                    if (qxmin < node.xmin)
                    {
                        currentFrameIndex--;
                        continue;
                    }

                    if (qxmin > node.xmin && qxmin <= node.xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(qyzMinMax < yzminmaxs[node.bucketRelativeBodyIndex]) == 0))
                        {
                            if (processor.Execute(node.bucketRelativeBodyIndex) == false)
                                return;
                        }
                    }

                    currentFrame.checkpoint  = 2;
                    stack[currentFrameIndex] = currentFrame;
                    currentFrameIndex++;
                    stack[currentFrameIndex].currentIndex = GetRightChildIndex(currentFrame.currentIndex);
                    stack[currentFrameIndex].checkpoint   = 0;
                    continue;
                }
                else
                {
                    currentFrameIndex--;
                }
            }
        }
    }
}

