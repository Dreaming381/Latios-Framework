using System;
using System.Runtime.InteropServices;
using Latios.Calci;
using Latios.Transforms;
using Latios.Unsafe;
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
        public unsafe void FindTriangles<T>(in Aabb triMeshSpaceAabb, ref T processor) where T : unmanaged, IFindTrianglesProcessor
        {
            if (intervalTree.Length == 0)
                return;

            var linearSweepStartIndex = BinarySearch.FirstGreaterOrEqual((float*)xmins.GetUnsafePtr(), xmins.Length, triMeshSpaceAabb.min.x);

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

        /// <summary>
        /// Constructs a Blob Asset for the specified mesh triangles. The user is responsible for the lifecycle
        /// of the resulting blob asset. Calling in a Baker may not result in correct incremental behavior.
        /// </summary>
        /// <param name="builder">The initialized BlobBuilder to create the blob asset with</param>
        /// <param name="vertices">The vertices of the mesh</param>
        /// <param name="indices">Triangle indices of the mesh</param>
        /// <param name="name">The name of the mesh which will be stored in the blob</param>
        /// <param name="allocator">The allocator used for the finally BlobAsset, typically Persistent</param>
        /// <returns>A reference to the created Blob Asset which is user-owned</returns>
        public static unsafe BlobAssetReference<TriMeshColliderBlob> BuildBlob(ref BlobBuilder builder,
                                                                               ReadOnlySpan<float3>             vertices,
                                                                               ReadOnlySpan<int3>               indices,
                                                                               in FixedString128Bytes name,
                                                                               AllocatorManager.AllocatorHandle allocator)
        {
            using ThreadStackAllocator tsa = ThreadStackAllocator.GetAllocator();

            var bodiesCachePtr = tsa.Allocate<ColliderBody>(indices.Length);
            var bodiesCache    = CollectionHelper.ConvertExistingDataToNativeArray<ColliderBody>(bodiesCachePtr, indices.Length, Allocator.None, true);
            for (int i = 0; i < bodiesCache.Length; i++)
            {
                var    tri = indices[i];
                float3 a   = vertices[tri.x];
                float3 b   = vertices[tri.y];
                float3 c   = vertices[tri.z];

                bodiesCache[i] = new ColliderBody { collider = new TriangleCollider(a, b, c), entity = default, transform = TransformQvvs.identity };
            }

            ref var blobRoot  = ref builder.ConstructRoot<TriMeshColliderBlob>();
            blobRoot.meshName = name;

            // Todo: Need ThreadStackAllocator to support AllocatorHandle
            Physics.BuildCollisionLayer(bodiesCache).WithSubdivisions(1).RunImmediate(out var layer, Allocator.Temp);

            builder.ConstructFromNativeArray(ref blobRoot.xmins,         layer.xmins.AsArray());
            builder.ConstructFromNativeArray(ref blobRoot.xmaxs,         layer.xmaxs.AsArray());
            builder.ConstructFromNativeArray(ref blobRoot.yzminmaxs,     layer.yzminmaxs.AsArray());
            builder.ConstructFromNativeArray(ref blobRoot.intervalTree,  layer.intervalTrees.AsArray());
            builder.ConstructFromNativeArray(ref blobRoot.sourceIndices, layer.srcIndices.AsArray());

            var triangles = builder.Allocate(ref blobRoot.triangles, layer.count);
            var aabb      = new Aabb(float.MaxValue, float.MinValue);
            for (int i = 0; i < layer.count; i++)
            {
                TriangleCollider triangle = layer.colliderBodies[i].collider;
                aabb.min                  = math.min(math.min(aabb.min, triangle.pointA), math.min(triangle.pointB, triangle.pointC));
                aabb.max                  = math.max(math.max(aabb.max, triangle.pointA), math.max(triangle.pointB, triangle.pointC));
                triangles[i]              = triangle;
            }

            blobRoot.localAabb = aabb;

            return builder.CreateBlobAssetReference<TriMeshColliderBlob>(allocator);
        }

        /// <summary>
        /// Constructs a Blob Asset for the specified MeshData. The user is responsible for the lifecycle
        /// of the resulting blob asset. Calling in a Baker may not result in correct incremental behavior.
        /// Submeshes that are not triangles are ignored.
        /// </summary>
        /// <param name="builder">The initialized BlobBuilder to create the blob asset with</param>
        /// <param name="mesh">The input mesh to build the blob from</param>
        /// <param name="name">The name of the mesh which will be stored in the blob</param>
        /// <param name="allocator">The allocator used for the finally BlobAsset, typically Persistent</param>
        /// <returns>A reference to the created Blob Asset which is user-owned</returns>
        public static unsafe BlobAssetReference<TriMeshColliderBlob> BuildBlob(ref BlobBuilder builder,
                                                                               UnityEngine.Mesh.MeshData mesh,
                                                                               in FixedString128Bytes name,
                                                                               AllocatorManager.AllocatorHandle allocator)
        {
            using ThreadStackAllocator tsa = ThreadStackAllocator.GetAllocator();

            int triCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var descriptor = mesh.GetSubMesh(i);
                if (descriptor.topology != UnityEngine.MeshTopology.Triangles)
                    continue;
                triCount += descriptor.indexCount / 3;
            }

            var vector3CachePtr = tsa.Allocate<UnityEngine.Vector3>(mesh.vertexCount);
            var vector3Cache    = CollectionHelper.ConvertExistingDataToNativeArray<UnityEngine.Vector3>(vector3CachePtr, mesh.vertexCount, Allocator.None, true);

            var indicesCachePtr = tsa.Allocate<int>(triCount * 3);
            var indicesCache    = CollectionHelper.ConvertExistingDataToNativeArray<int>(indicesCachePtr, triCount * 3, Allocator.None, true);

            mesh.GetVertices(vector3Cache);

            int trisSoFar = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var descriptor = mesh.GetSubMesh(i);
                if (descriptor.topology != UnityEngine.MeshTopology.Triangles)
                    continue;
                mesh.GetIndices(indicesCache.GetSubArray(trisSoFar * 3, descriptor.indexCount), i);
                trisSoFar += descriptor.indexCount;
            }

            return BuildBlob(ref builder, vector3Cache.Reinterpret<float3>().AsReadOnlySpan(), indicesCache.Reinterpret<int3>(4).AsReadOnlySpan(), in name, allocator);
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

