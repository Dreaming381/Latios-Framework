using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Tangent calculation code is derived in the article:
// Lengyel, Eric. “Computing Tangent Space Basis Vectors for an Arbitrary Mesh”.
// Terathon Software 3D Graphics Library, 2001.
// http://www.terathon.com/code/tangent.html
//
// Hint: Use the WaybackMachine to see contents.
//
// The code here is slightly different to leverage
// Burst better. We also don't calculate or store
// vDir because Unity always assumes the bitangent
// is constant to the mesh.

namespace Latios.Kinemation
{
    public static unsafe class SkinningAlgorithms
    {
        #region Single Element
        /// <summary>
        /// Applies a single blend shape to a single vertex. This method is inefficient for lots of vertices and should only be used
        /// when a very small subset of vertices need to be deformed for object attachments or similar.
        /// </summary>
        /// <param name="vertexIndex">The vertex index in the mesh that should be deformed.</param>
        /// <param name="vertex">The vertex's current state that should be deformed.</param>
        /// <param name="shapeIndex">The index of the blend shape in the blob</param>
        /// <param name="shapeWeight">The weight of the blend shape (typically [0, 1])</param>
        /// <param name="blendShapesBlob">The part of the MeshDeformDataBlob that holds blend shapes</param>
        /// <returns>A deformed version of the vertex</returns>
        public static DynamicMeshVertex ApplyBlendShape(int vertexIndex, DynamicMeshVertex vertex, int shapeIndex, float shapeWeight, ref MeshBlendShapesBlob blendShapesBlob)
        {
            if (shapeWeight < 0f)
                return vertex;

            var range = blendShapesBlob.shapes[shapeIndex];
            for (int i = 0; i < range.count; i++)
            {
                if (Hint.Likely(blendShapesBlob.gpuData[(int)range.start + i].targetVertexIndex != vertexIndex))
                    continue;
                var shape        = blendShapesBlob.gpuData[(int)range.start + i];
                vertex.position += shape.positionDisplacement * shapeWeight;
                vertex.normal   += shape.normalDisplacement * shapeWeight;
                vertex.tangent  += shape.tangentDisplacement * shapeWeight;
                break;
            }
            return vertex;
        }

        /// <summary>
        /// Applies all blend shapes to a single vertex. This method is inefficient for lots of vertices and should only be used
        /// when a very small subset of vertices need to be deformed for object attachments or similar.
        /// </summary>
        /// <param name="vertexIndex">The vertex index in the mesh that should be deformed.</param>
        /// <param name="vertex">The vertex's current state that should be deformed.</param>
        /// <param name="weightsByShape">The weights for all blend shapes</param>
        /// <param name="blendShapesBlob">The part of the MeshDeformDataBlob that holds blend shapes</param>
        /// <returns>A deformed version of the vertex</returns>
        public static DynamicMeshVertex ApplyAllBlendShapes(int vertexIndex, DynamicMeshVertex vertex, NativeArray<float>.ReadOnly weightsByShape,
                                                            ref MeshBlendShapesBlob blendShapesBlob)
        {
            uint previousGroup         = 0;
            bool previousWeightWasZero = false;
            for (int i = 0; i < weightsByShape.Length; i++)
            {
                if (weightsByShape[i] == 0f)
                {
                    previousWeightWasZero = true;
                    previousGroup         = blendShapesBlob.shapes[i].permutationID;
                    continue;
                }
                if (previousWeightWasZero && blendShapesBlob.shapes[i].permutationID == previousGroup)
                    continue;
                previousGroup         = blendShapesBlob.shapes[i].permutationID;
                previousWeightWasZero = false;
                vertex                = ApplyBlendShape(vertexIndex, vertex, i, weightsByShape[i], ref blendShapesBlob);
            }
            return vertex;
        }

        /// <summary>
        /// Computes the skin matrix for a mesh bone given the bone's transform and the bindpose in the blob asset
        /// </summary>
        /// <param name="meshBoneIndex">The bone index relative to the mesh (not the skeleton).</param>
        /// <param name="rootRelativeTransform">The transform of the bone</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <returns>A skin matrix</returns>
        public static float3x4 GetSkinMatrix(int meshBoneIndex, in TransformQvvs rootRelativeTransform, ref MeshSkinningBlob skinningBlob)
        {
            var bindPose    = skinningBlob.bindPoses[meshBoneIndex];
            var bindPoseMat = new float4x4(new float4(bindPose.c0, 0f), new float4(bindPose.c1, 0f), new float4(bindPose.c2, 0f), new float4(bindPose.c3, 1f));
            var skinMatrix  = math.mul(qvvs.ToMatrix4x4(in rootRelativeTransform), bindPoseMat);
            return new float3x4(skinMatrix.c0.xyz, skinMatrix.c1.xyz, skinMatrix.c2.xyz, skinMatrix.c3.xyz);
        }

        /// <summary>
        /// Skins a single vertex using matrix skinning (linear blend skinning).
        /// </summary>
        /// <param name="vertexIndex">The index of the vertex in the mesh</param>
        /// <param name="srcVertex">The current state of the vertex to be skinned</param>
        /// <param name="skinMatrices">The mesh-relative skinning matrices</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <returns>The skinned vertex</returns>
        public static DynamicMeshVertex SkinVertex(int vertexIndex,
                                                   in DynamicMeshVertex srcVertex,
                                                   in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                   ref MeshSkinningBlob skinningBlob)
        {
            var      weightIndex = skinningBlob.boneWeightBatchStarts[vertexIndex / 1024] + (vertexIndex % 1024) + 1;
            bool     isEnd       = false;
            float3x4 deform      = default;
            while (!isEnd)
            {
                var boneWeight  = skinningBlob.boneWeights[(int)weightIndex];
                weightIndex    += boneWeight.next10Lds7Bone15 >> 22;
                weightIndex++;
                var boneIndex  = (int)boneWeight.next10Lds7Bone15 & 0x7fff;
                isEnd          = boneWeight.weight < 0f;
                deform        += math.abs(boneWeight.weight) * skinMatrices[boneIndex];
            }
            return new DynamicMeshVertex
            {
                position = math.mul(deform, new float4(srcVertex.position, 1f)),
                normal   = math.mul(deform, new float4(srcVertex.normal, 0f)),
                tangent  = math.mul(deform, new float4(srcVertex.tangent, 0f)),
            };
        }

        /// <summary>
        /// Skins a single vertex position using matrix skinning (linear blend skinning).
        /// </summary>
        /// <param name="vertexIndex">The index of the vertex in the mesh</param>
        /// <param name="srcVertex">The current position of the vertex to be skinned</param>
        /// <param name="skinMatrices">The mesh-relative skinning matrices</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <returns>The skinned vertex</returns>
        public static float3 SkinVertexPosition(int vertexIndex,
                                                in float3 srcVertex,
                                                in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                ref MeshSkinningBlob skinningBlob)
        {
            var      weightIndex = skinningBlob.boneWeightBatchStarts[vertexIndex / 1024] + (vertexIndex % 1024) + 1;
            bool     isEnd       = false;
            float3x4 deform      = default;
            while (!isEnd)
            {
                var boneWeight  = skinningBlob.boneWeights[(int)weightIndex];
                weightIndex    += boneWeight.next10Lds7Bone15 >> 22;
                weightIndex++;
                var boneIndex  = (int)boneWeight.next10Lds7Bone15 & 0x7fff;
                isEnd          = boneWeight.weight < 0f;
                deform        += math.abs(boneWeight.weight) * skinMatrices[boneIndex];
            }
            return math.mul(deform, new float4(srcVertex, 1f));
        }

        /// <summary>
        /// Computes the weighted (scaled) normal of the triangle using both area and angle weights.
        /// The angle weights are factored out as an out arg for better data compression.
        /// </summary>
        /// <param name="a">The first vertex position of the triangle</param>
        /// <param name="b">The second vertex position of the triangle</param>
        /// <param name="c">The third vertex position of the triangle</param>
        /// <param name="angleWeights">The angle weights x, y, and z for the vertices a, b, and c respectively</param>
        /// <returns>An area-weighted (scaled) normal of the triangle</returns>
        public static float3 GetTriangleAngleWeightedNormal(float3 a, float3 b, float3 c, out float3 angleWeights)
        {
            var ta   = new simdFloat3(a, b, c, c);
            var tb   = new simdFloat3(b, c, a, a);
            var tc   = new simdFloat3(c, a, b, b);
            var ab   = tb - ta;
            var ac   = tc - ta;
            var abn  = simd.normalizesafe(ab, float3.zero);
            var acn  = simd.normalizesafe(ac, float3.zero);
            var zero = (abn == 0f) | (acn == 0f);
            //var zero        = abn.Equals(float3.zero) || acn.Equals(float3.zero);
            var angleFactor = math.acos(simd.dot(abn, acn));
            angleWeights    = math.select(angleFactor, 0f, zero).xyz;
            return math.cross(ab.a, ac.a);
        }

        /// <summary>
        /// Computes the weighted (scaled) tangent of the triangle.
        /// </summary>
        /// <param name="a">The first vertex position of the triangle</param>
        /// <param name="b">The second vertex position of the triangle</param>
        /// <param name="c">The third vertex position of the triangle</param>
        /// <param name="uva">The first vertex UV of the triangle</param>
        /// <param name="uvb">The second vertex UV of the triangle</param>
        /// <param name="uvc">The third vertex UV of the triangle</param>
        /// <returns>A weighted (scaled) tangent vector that requires orthonormalization</returns>
        public static float3 GetTriangleWeightedTangent(float3 a, float3 b, float3 c, float2 uva, float2 uvb, float2 uvc)
        {
            // See Lengyel notice above
            var ab   = b - a;
            var ac   = c - a;
            var uvab = uvb - uva;
            var uvac = uvc - uva;
            var den  = uvab.x * uvac.y - uvab.y * uvac.x;
            var r    = math.select(0f, 1f / den, den != 0f);

            return (uvac.y * ab - uvab.y * ac) * r;
        }

        #endregion

        #region Batch
        /// <summary>
        /// Applies a single blend shape to the entire mesh.
        /// </summary>
        /// <param name="shapeIndex">The index of the shape within the blob asset</param>
        /// <param name="shapeWeight">The strenth to apply the shape, typically [0, 1]</param>
        /// <param name="meshVertices">The mesh vertices to modify</param>
        /// <param name="blendShapesBlob">The part of the MeshDeformDataBlob that holds blend shapes</param>
        public static void ApplyBlendShape(int shapeIndex, float shapeWeight, ref NativeArray<DynamicMeshVertex> meshVertices, ref MeshBlendShapesBlob blendShapesBlob)
        {
            if (shapeWeight < 0f)
                return;

            var range = blendShapesBlob.shapes[shapeIndex];
            for (int i = 0; i < range.count; i++)
            {
                var shape                                   = blendShapesBlob.gpuData[(int)range.start + i];
                var vertex                                  = meshVertices[(int)shape.targetVertexIndex];
                vertex.position                            += shape.positionDisplacement * shapeWeight;
                vertex.normal                              += shape.normalDisplacement * shapeWeight;
                vertex.tangent                             += shape.tangentDisplacement * shapeWeight;
                meshVertices[(int)shape.targetVertexIndex]  = vertex;
            }
        }

        /// <summary>
        /// Applies all blend shapes to the mesh based on their weights
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to modify</param>
        /// <param name="weightsByShape">A weights array with each weight corresponding to the blend shape at the same index</param>
        /// <param name="blendShapesBlob">The part of the MeshDeformDataBlob that holds blend shapes</param>
        public static void ApplyAllBlendShapes(ref NativeArray<DynamicMeshVertex> meshVertices, NativeArray<float>.ReadOnly weightsByShape,
                                               ref MeshBlendShapesBlob blendShapesBlob)
        {
            for (int i = 0; i < weightsByShape.Length; i++)
            {
                ApplyBlendShape(i, weightsByShape[i], ref meshVertices, ref blendShapesBlob);
            }
        }

        /// <summary>
        /// Extracts the unique positions of the vertices in the mesh and places them in a compacted array.
        /// </summary>
        /// <param name="uniquePositions">The destination array where the unique positions should be stored.
        /// You can get the expected size of this array from MeshDeformDataBlob.uniqueVertexPositionsCount.</param>
        /// <param name="meshVertices">The mesh vertices to extract from.</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void ExtractUniquePositions(ref NativeArray<float3>                 uniquePositions,
                                                  NativeArray<DynamicMeshVertex>.ReadOnly meshVertices,
                                                  ref MeshNormalizationBlob normalizationBlob)
        {
            int dst = 0;
            for (int src = 0; src < meshVertices.Length; src++)
            {
                if (normalizationBlob.IsPositionDuplicate(src))
                    continue;
                uniquePositions[dst] = meshVertices[src].position;
                dst++;
            }
        }

        /// <summary>
        /// Extracts the unique positions of the vertices in the undeformed mesh and places them in a compacted array.
        /// </summary>
        /// <param name="uniquePositions">The destination array where the unique positions should be stored.
        /// You can get the expected size of this array from MeshDeformDataBlob.uniqueVertexPositionsCount.</param>
        /// <param name="srcVertices">The blob containing the undeformed vertices</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void ExtractUniquePositions(ref NativeArray<float3> uniquePositions, ref MeshDeformDataBlob srcVertices, ref MeshNormalizationBlob normalizationBlob)
        {
            int dst = 0;
            for (int src = 0; src < srcVertices.undeformedVertices.Length; src++)
            {
                if (normalizationBlob.IsPositionDuplicate(src))
                    continue;
                uniquePositions[dst] = srcVertices.undeformedVertices[src].position;
                dst++;
            }
        }

        /// <summary>
        /// Computes all skin matrices for the mesh using the full skeleton pose and runtime binding indices.
        /// </summary>
        /// <param name="skinMatrices">The resulting skin matrices. The array length should match skinBoneBindingIndices.length</param>
        /// <param name="rootRelativeTransformsInSkeleton">The full skeleton transforms in root space</param>
        /// <param name="skinBoneBindingIndices">The runtime indices of the skeleton bones that each mesh bone (bindpose) maps to</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        public static void GetSkinMatrices(ref NativeArray<float3x4>              skinMatrices,
                                           in NativeArray<TransformQvvs>.ReadOnly rootRelativeTransformsInSkeleton,
                                           in NativeArray<short>.ReadOnly skinBoneBindingIndices,
                                           ref MeshSkinningBlob skinningBlob)
        {
            for (int i = 0; i < skinBoneBindingIndices.Length; i++)
            {
                skinMatrices[i] = GetSkinMatrix(i, rootRelativeTransformsInSkeleton[skinBoneBindingIndices[i]], ref skinningBlob);
            }
        }

        /// <summary>
        /// Applies skinning to all vertices in the mesh.
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to modify</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        public static void SkinAllVertices(ref NativeArray<DynamicMeshVertex> meshVertices, in NativeArray<float3x4>.ReadOnly skinMatrices, ref MeshSkinningBlob skinningBlob)
        {
            for (int i = 0; i < meshVertices.Length; i++)
            {
                meshVertices[i] = SkinVertex(i, meshVertices[i], skinMatrices, ref skinningBlob);
            }
        }

        /// <summary>
        /// Skins an undeformed mesh stored in the blob.
        /// </summary>
        /// <param name="dstVertices">The destination where the skinned vertices should be placed</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <param name="srcVerticesBlob">The same MeshDeformDataBlob which contains the original undeformed vertices</param>
        public static void SkinAllVertices(ref NativeArray<DynamicMeshVertex> dstVertices,
                                           in NativeArray<float3x4>.ReadOnly skinMatrices,
                                           ref MeshSkinningBlob skinningBlob,
                                           ref MeshDeformDataBlob srcVerticesBlob)
        {
            var srcVertices = (DynamicMeshVertex*)srcVerticesBlob.undeformedVertices.GetUnsafePtr();
            for (int i = 0; i < srcVerticesBlob.undeformedVertices.Length; i++)
            {
                dstVertices[i] = SkinVertex(i, in srcVertices[i], in skinMatrices, ref skinningBlob);
            }
        }

        /// <summary>
        /// Applies skinning to all unique vertices with unique positions in the mesh. This method may be slightly faster
        /// than skinning all vertices if you plan to apply normalization or other post-processing afterwards.
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to skin. This must be the full length of the mesh and
        /// not the unique subset.</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void SkinAllNonDuplicatedVertices(ref NativeArray<DynamicMeshVertex> meshVertices,
                                                        in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                        ref MeshSkinningBlob skinningBlob,
                                                        ref MeshNormalizationBlob normalizationBlob)
        {
            for (int i = 0; i < meshVertices.Length; i++)
            {
                if (Hint.Unlikely(normalizationBlob.IsPositionDuplicate(i)))
                    continue;
                meshVertices[i] = SkinVertex(i, meshVertices[i], skinMatrices, ref skinningBlob);
            }
        }

        /// <summary>
        /// Skins an undeformed mesh in the blob, only considering vertices with unique positions. This method may be slightly faster
        /// than skinning all vertices if you plan to apply normalization or other post-processing afterwards.
        /// </summary>
        /// <param name="dstVertices">The destination where all skinned vertices should be placed.
        /// This must be the full length of the mesh and not the unique subset. Duplicate vertices will not be
        /// populated and their contents will be whatever was in the array prior to this call.</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <param name="srcVerticesBlob">The same MeshDeformDataBlob which contains the original undeformed vertices</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void SkinAllNonDuplicatedVertices(ref NativeArray<DynamicMeshVertex> dstVertices,
                                                        in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                        ref MeshSkinningBlob skinningBlob,
                                                        ref MeshDeformDataBlob srcVerticesBlob,
                                                        ref MeshNormalizationBlob normalizationBlob)
        {
            var srcVertices = (DynamicMeshVertex*)srcVerticesBlob.undeformedVertices.GetUnsafePtr();
            for (int i = 0; i < srcVerticesBlob.undeformedVertices.Length; i++)
            {
                if (Hint.Unlikely(normalizationBlob.IsPositionDuplicate(i)))
                    continue;
                dstVertices[i] = SkinVertex(i, in srcVertices[i], in skinMatrices, ref skinningBlob);
            }
        }

        /// <summary>
        /// Applies skinning to all unique vertex positions in the mesh. This method may be slightly faster
        /// than skinning all vertices if you plan to apply normalization or other post-processing afterwards.
        /// </summary>
        /// <param name="uniquePositions">The mesh unique positions to skin. This must be the length of the unique
        /// positions and not the full length of the mesh.</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void SkinAllUniquePositions(ref NativeArray<float3>           uniquePositions,
                                                  in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                  ref MeshSkinningBlob skinningBlob,
                                                  ref MeshNormalizationBlob normalizationBlob)
        {
            int dst                = 0;
            var totalVerticesCount = (int)math.asuint(skinningBlob.boneWeights[0].weight);
            for (int src = 0; src < totalVerticesCount; src++)
            {
                if (Hint.Unlikely(normalizationBlob.IsPositionDuplicate(src)))
                    continue;
                uniquePositions[dst] = SkinVertexPosition(src, uniquePositions[src], skinMatrices, ref skinningBlob);
                dst++;
            }
        }

        /// <summary>
        /// Skins the undeformed mesh positions in the blob, only considering vertices with unique positions. This method may be slightly faster
        /// than skinning all vertices if you plan to apply normalization or other post-processing afterwards.
        /// </summary>
        /// <param name="dstPositions">The destination where all unique vertex positions should be placed. This must be the length of the unique
        /// positions and not the full length of the mesh. You can get the expected size of this array from MeshDeformDataBlob.uniqueVertexPositionsCount.</param>
        /// <param name="skinMatrices">The skin matrices computed from the mesh and the skeleton</param>
        /// <param name="skinningBlob">The part of the MeshDeformDataBlob that holds skinning data</param>
        /// <param name="srcVerticesBlob">The same MeshDeformDataBlob which contains the original undeformed vertices</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertex positions</param>
        /// <remarks>"Unique Positions" refers to vertices where their original undeformed position is the first instance
        /// in the mesh.</remarks>
        public static void SkinAllUniquePositions(ref NativeArray<float3>           dstPositions,
                                                  in NativeArray<float3x4>.ReadOnly skinMatrices,
                                                  ref MeshSkinningBlob skinningBlob,
                                                  ref MeshDeformDataBlob srcVerticesBlob,
                                                  ref MeshNormalizationBlob normalizationBlob)
        {
            int dst                = 0;
            var totalVerticesCount = (int)math.asuint(skinningBlob.boneWeights[0].weight);
            for (int src = 0; src < totalVerticesCount; src++)
            {
                if (Hint.Unlikely(normalizationBlob.IsPositionDuplicate(src)))
                    continue;
                dstPositions[dst] = SkinVertexPosition(src, srcVerticesBlob.undeformedVertices[src].position, skinMatrices, ref skinningBlob);
                dst++;
            }
        }

        /// <summary>
        /// Copies the unique positions to the mesh vertices and prepares the mesh vertices for a call to
        /// NormalizeMesh by duplicating positions of necessary vertices and clearing all normals and tangents.
        /// You may pass in "true" to NormalizeMesh after calling this method.
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to rewrite with the new unique positions</param>
        /// <param name="uniquePositions">The unique positions of the mesh that have been modified</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertices of various types</param>
        public static void ApplyPositionsWithUniqueNormals(ref NativeArray<DynamicMeshVertex> meshVertices,
                                                           in NativeArray<float3>.ReadOnly uniquePositions,
                                                           ref MeshNormalizationBlob normalizationBlob)
        {
            int src = 0;
            for (int dst = 0; dst < meshVertices.Length; dst++)
            {
                if (normalizationBlob.IsPositionDuplicate(dst))
                {
                    if (normalizationBlob.IsNormalDuplicate(dst))
                    {
                        meshVertices[dst] = default;
                    }
                    else
                    {
                        meshVertices[dst] = meshVertices[normalizationBlob.GetDuplicatePositionReferenceIndex(dst)];
                    }
                }
                else
                {
                    meshVertices[dst] = new DynamicMeshVertex
                    {
                        position = uniquePositions[src],
                        normal   = 0f,
                        tangent  = 0f
                    };
                    src++;
                }
            }
        }

        /// <summary>
        /// Copies the unique vertex positions to other smoothing groups and prepares the mesh vertices for a call to
        /// NormalizeMesh by duplicating positions of necessary vertices and clearing all normals and tangents.
        /// You may pass in "true" to NormalizeMesh after calling this method.
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to rewrite with the new unique positions</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertices of various types</param>
        public static void CopyDuplicatePositionsForSplitNormals(ref NativeArray<DynamicMeshVertex> meshVertices, ref MeshNormalizationBlob normalizationBlob)
        {
            for (int i = 0; i < normalizationBlob.duplicatePositionCount; i++)
            {
                normalizationBlob.GetDuplicatePositionAtRawIndex(i, out var duplicateIndex, out var referenceIndex);
                if (normalizationBlob.IsNormalDuplicate(duplicateIndex))
                    continue;
                var dv                       = meshVertices[duplicateIndex];
                dv.position                  = meshVertices[referenceIndex].position;
                meshVertices[duplicateIndex] = dv;
            }
        }

        /// <summary>
        /// Computes the normals and tangents for the deformed mesh using area and angle weighting.
        /// </summary>
        /// <param name="meshVertices">The mesh vertices to compute new normals and tangents for, replacing any existing values</param>
        /// <param name="normalizationBlob">The part of the MeshDeformDataBlob that identifies unique vertices of all types as well
        /// as all other information required for this algorithm to function correctly</param>
        /// <param name="preZeroedNormalsAndTangents">If true, the normals and tangents of meshVertices are assumed to be zero,
        /// and the step that overwrites these values with zero at the beginning of the algorithm is skipped.</param>
        /// <remarks>This method requires that the first vertex with a unique position AND normal have its position set appropriately.
        /// That means if two vertices share an undeformed position but belong to different smoothing groups (the vertex is a hard edge),
        /// then the position must be duplicated. All other "duplicate" vertices can have their positions left undefined.
        /// </remarks>
        public static void NormalizeMesh(ref NativeArray<DynamicMeshVertex> meshVertices, ref MeshNormalizationBlob normalizationBlob, bool preZeroedNormalsAndTangents = false)
        {
            bool resetNormals  = normalizationBlob.triangleCount > 0;
            bool resetTangents = normalizationBlob.uvs.Length != 0;
            if (!preZeroedNormalsAndTangents)
            {
                for (int i = 0; i < meshVertices.Length; i++)
                {
                    var v = meshVertices[i];
                    if (resetNormals)
                        v.normal = 0f;
                    if (resetTangents)
                        v.tangent   = 0f;
                    meshVertices[i] = v;
                }
            }

            for (int i = 0; i < normalizationBlob.triangleCount; i++)
            {
                var indices = normalizationBlob.GetIndicesForTriangle(i);
                var indexA  = indices.x;
                var indexB  = indices.y;
                var indexC  = indices.z;

                if (normalizationBlob.IsNormalDuplicate(indexA))
                    indexA = normalizationBlob.GetDuplicateNormalReferenceIndex(indexA);
                if (normalizationBlob.IsNormalDuplicate(indexB))
                    indexB = normalizationBlob.GetDuplicateNormalReferenceIndex(indexB);
                if (normalizationBlob.IsNormalDuplicate(indexC))
                    indexC = normalizationBlob.GetDuplicateNormalReferenceIndex(indexC);

                var a = meshVertices[indexA].position;
                var b = meshVertices[indexB].position;
                var c = meshVertices[indexC].position;

                var triNormal = GetTriangleAngleWeightedNormal(a, b, c, out var triNormalScales);

                float3 uDir = 0f;
                if (resetTangents)
                {
                    var uva = normalizationBlob.uvs[indices.x];
                    var uvb = normalizationBlob.uvs[indices.y];
                    var uvc = normalizationBlob.uvs[indices.z];

                    uDir = GetTriangleWeightedTangent(a, b, c, uva, uvb, uvc);
                }

                var va     = meshVertices[indexA];
                va.normal += triNormal * triNormalScales.x;
                if (indexA == indices.x)
                    va.tangent       += uDir;
                meshVertices[indexA]  = va;
                var vb                = meshVertices[indexB];
                vb.normal            += triNormal * triNormalScales.y;
                if (indexB == indices.y)
                    vb.tangent       += uDir;
                meshVertices[indexB]  = vb;
                var vc                = meshVertices[indexC];
                vc.normal            += triNormal * triNormalScales.z;
                if (indexC == indices.z)
                    vc.tangent       += uDir;
                meshVertices[indexC]  = vc;

                if (new int3(indexA, indexB, indexC).Equals(indices))
                    continue;

                if (resetTangents)
                {
                    if (!normalizationBlob.IsTangentDuplicate(indices.x))
                    {
                        va                       = meshVertices[indices.x];
                        va.tangent              += uDir;
                        meshVertices[indices.x]  = va;
                    }
                    if (!normalizationBlob.IsTangentDuplicate(indices.y))
                    {
                        vb                       = meshVertices[indices.y];
                        vb.tangent              += uDir;
                        meshVertices[indices.y]  = vb;
                    }
                    if (!normalizationBlob.IsTangentDuplicate(indices.z))
                    {
                        vc                       = meshVertices[indices.z];
                        vc.tangent              += uDir;
                        meshVertices[indices.z]  = vc;
                    }
                }
            }

            for (int i = 0; i < meshVertices.Length; i++)
            {
                if (Hint.Unlikely(normalizationBlob.IsNormalDuplicate(i)))
                    continue;
                var v           = meshVertices[i];
                v.normal        = math.normalize(v.normal);
                v.tangent       = math.normalize(v.tangent - v.normal * math.dot(v.normal, v.tangent));  // Gram-Schmidt orthogonalization. See Lengyel notice above.
                meshVertices[i] = v;
            }

            for (int i = 0; i < normalizationBlob.duplicateNormalCount; i++)
            {
                normalizationBlob.GetDuplicateNormalAtRawIndex(i, out var duplicateIndex, out var referenceIndex);
                if (normalizationBlob.IsTangentDuplicate(duplicateIndex))
                    continue;
                var v                        = meshVertices[referenceIndex];
                var t                        = meshVertices[duplicateIndex].tangent;
                v.tangent                    = math.normalize(t - v.normal * math.dot(v.normal, t));
                meshVertices[duplicateIndex] = v;
            }

            for (int i = 0; i < normalizationBlob.duplicateTangentCount; i++)
            {
                normalizationBlob.GetDuplicateTangentAtRawIndex(i, out var duplicateIndex, out var referenceIndex);
                meshVertices[duplicateIndex] = meshVertices[referenceIndex];
            }
        }

        /// <summary>
        /// Computes the maximum displacement of all vertices in the deformed mesh relative to the original
        /// which can be used to set DynamicMeshMaxVertexDisplacement for tight culling.
        /// </summary>
        /// <param name="deformedVertices">The deformed vertices of the mesh</param>
        /// <param name="originalVertices">The blob asset containing the original undeformed vertices</param>
        /// <returns>A single value that represents the max displacement any vertex has undergone across all vertices</returns>
        public static float FindMaxDisplacement(in NativeArray<DynamicMeshVertex>.ReadOnly deformedVertices, ref MeshDeformDataBlob originalVertices)
        {
            float maxDisplacementSq = 0f;
            for (int i = 0; i < originalVertices.undeformedVertices.Length; i++)
            {
                maxDisplacementSq = math.max(maxDisplacementSq, math.distancesq(deformedVertices[i].position, originalVertices.undeformedVertices[i].position));
            }
            return math.sqrt(maxDisplacementSq);
        }
        #endregion
    }
}

