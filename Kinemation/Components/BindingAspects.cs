using System.Diagnostics;
using Latios.Kinemation.InternalSourceGen;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Why are these aspects and not exposed types?
//
// Kinemation's binding system is very fragile, and the data should not be tampered with.
// For this reason, Kinemation tries to make mutating such data relatively hard.
// Additionally, the use cases where one would need to access this data are uncommon,
// and typically the access is cheap relative to the computation typically involved for
// such use cases. Even a complete cache miss is typically not a big deal for such use cases.

namespace Latios.Kinemation
{
    /// <summary>
    /// Provides read-only access to skeleton binding data for this skinned mesh entity.
    /// </summary>
    public readonly partial struct SkinnedMeshBindingAspect : IAspect
    {
        readonly RefRO<SkeletonDependent> m_skeletonDependent;

        /// <summary>
        /// An index into the SkinBoneBindingsCollectionAspect. The lifecycle is guaranteed to be
        /// valid as long as both this mesh and its bound skeleton are valid. Beyond that,
        /// attempts at use will result in underfined behavior without safety checks.
        /// </summary>
        public struct SkinBoneBindingsIndex
        {
            internal int index;
        }

        /// <summary>
        /// A BoneBindingsIndex which can be used to index into a SkinBoneBindingsCollectionAspect.
        /// </summary>
        public SkinBoneBindingsIndex boneBindingsIndex => new SkinBoneBindingsIndex { index = m_skeletonDependent.ValueRO.boneOffsetEntryIndex };
        /// <summary>
        /// The skeleton this skinned mesh is bound to.
        /// </summary>
        public EntityWith<SkeletonRootTag> skeletonEntity => m_skeletonDependent.ValueRO.root;
        /// <summary>
        /// The index this mesh occupies in the skeleton's mesh bindings buffer.
        /// </summary>
        public int meshIndexInSkeleton => m_skeletonDependent.ValueRO.indexInDependentSkinnedMeshesBuffer;
    }

    public readonly partial struct SkeletonSkinBindingsAspect : IAspect
    {
        readonly DynamicBuffer<DependentSkinnedMesh> m_meshes;

        /// <summary>
        /// An index into the SkinBoneBindingsCollectionAspect. The lifecycle is guaranteed to be
        /// valid as long as both this skeleton and the bound mesh at the originating index are valid.
        /// Beyond that, attempts at use will result in underfined behavior without safety checks.
        /// </summary>
        public struct SkinBoneBindingsIndex
        {
            internal uint start;
            internal uint count;
        }

        /// <summary>
        /// A struct containing all the safely exposable properties of a bound mesh that the skeleton
        /// knows about.
        /// </summary>
        public struct BoundMeshData
        {
            /// <summary>
            /// The bound skinned mesh entity
            /// </summary>
            public EntityWith<BindSkeletonRoot> meshEntity;
            /// <summary>
            /// A BoneBindingsIndex which can be used to index into a SkinBoneBindingsCollectionAspect.
            /// </summary>
            public SkinBoneBindingsIndex boneBindingsIndex;
        }

        /// <summary>
        /// Acquires all the safely exposable properties for a skinned mesh bound to the skeleton
        /// stored at the specified index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public BoundMeshData this[int index]
        {
            get
            {
                var element = m_meshes[index];
                return new BoundMeshData
                {
                    meshEntity        = element.skinnedMesh.entity,
                    boneBindingsIndex = new SkinBoneBindingsIndex
                    {
                        start = element.boneOffsetsStart,
                        count = element.boneOffsetsCount
                    }
                };
            }
        }
    }

    /// <summary>
    /// A collection aspect that lives on the worldBlackboardEntity and provides readonly access
    /// to the skin bone binding indices for mapping skinned mesh bindpose bones to skeleton bones.
    /// </summary>
    public struct SkinBoneBindingsCollectionAspect : ICollectionAspect<SkinBoneBindingsCollectionAspect>
    {
        [ReadOnly] NativeList<BoneOffsetsEntry>        m_entries;
        [ReadOnly] NativeList<short>                   m_offsets;
        [ReadOnly] NativeHashMap<PathMappingPair, int> m_pathPairToEntryMap;

        /// <summary>
        /// Provides the indices of the skin bones within the skeleton's bone list, with the indices
        /// ordered to correspond to the mesh's bindposes.
        /// Warning: Safety checks are not bulletproof.
        /// </summary>
        public NativeArray<short>.ReadOnly this[SkinnedMeshBindingAspect.SkinBoneBindingsIndex index]
        {
            get
            {
                var entry = m_entries[index.index];
                CheckEntryIsValid(in entry);
                return m_offsets.AsArray().GetSubArray((int)entry.start, entry.count).AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the indices of the skin bones within the skeleton's bone list, with the indices
        /// ordered to correspond to the mesh's bindposes.
        /// Warning: Safety checks are not bulletproof.
        /// </summary>
        public NativeArray<short>.ReadOnly this[SkeletonSkinBindingsAspect.SkinBoneBindingsIndex index]
        {
            get
            {
                return m_offsets.AsArray().GetSubArray((int)index.start, (int)index.count).AsReadOnly();
            }
        }

        /// <summary>
        /// Attempts to provide the indices of the skin bones within the skeleton's bone list,
        /// with the indices ordered to correspond to the mesh's bindposes. This method uses a
        /// bindings cache and will succeed if a skeleton-mesh pair exist with these binding paths
        /// and were successfully bound. If this fails, use BindingUtilities.TrySolveBindings()
        /// to get new bindings.
        /// </summary>
        /// <param name="skeletonPaths">The bone paths for the skeleton</param>
        /// <param name="meshPaths">The bone paths for the mesh</param>
        /// <param name="skinBoneIndices">The output skin bone indices into the skeleton</param>
        /// <returns>True if the skin bone indices were found in the cache. False if no such binding is in use.</returns>
        public bool TryGetSkinBindings(BlobAssetReference<SkeletonBindingPathsBlob> skeletonPaths,
                                       BlobAssetReference<MeshBindingPathsBlob>     meshPaths,
                                       out NativeArray<short>.ReadOnly skinBoneIndices)
        {
            skinBoneIndices                                                          = default;
            if (m_pathPairToEntryMap.TryGetValue(new PathMappingPair { skeletonPaths = skeletonPaths, meshPaths = meshPaths}, out var entryIndex))
            {
                var entry       = m_entries[entryIndex];
                skinBoneIndices = m_offsets.AsArray().GetSubArray((int)entry.start, entry.count).AsReadOnly();
                return true;
            }
            return false;
        }

        public FluentQuery AppendToQuery(FluentQuery query)
        {
            return query.With<BoneOffsetsGpuManager.ExistComponent>(true);
        }

        public SkinBoneBindingsCollectionAspect CreateCollectionAspect(LatiosWorldUnmanaged latiosWorld, EntityManager entityManager, Entity entity)
        {
            var manager = latiosWorld.GetCollectionComponent<BoneOffsetsGpuManager>(entity, true);
            return new SkinBoneBindingsCollectionAspect
            {
                m_entries            = manager.entries,
                m_offsets            = manager.offsets,
                m_pathPairToEntryMap = manager.pathPairToEntryMap,
            };
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckEntryIsValid(in BoneOffsetsEntry entry)
        {
            if (!entry.isValid)
                throw new System.InvalidOperationException("The SkinBoneBindingsIndex is not valid.");
        }
    }
}

