using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
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
        public NativeArray<short>.ReadOnly this[SkeletonDependent skeletonDependent]
        {
            get
            {
                var entry = m_entries[skeletonDependent.boneOffsetEntryIndex];
                CheckEntryIsValid(in entry);
                return m_offsets.AsArray().GetSubArray((int)entry.start, entry.count).AsReadOnly();
            }
        }

        /// <summary>
        /// Provides the indices of the skin bones within the skeleton's bone list, with the indices
        /// ordered to correspond to the mesh's bindposes.
        /// Warning: Safety checks are not bulletproof.
        /// </summary>
        public NativeArray<short>.ReadOnly this[DependentSkinnedMesh dependentSkinnedMesh]
        {
            get
            {
                return m_offsets.AsArray().GetSubArray((int)dependentSkinnedMesh.boneOffsetsStart, (int)dependentSkinnedMesh.boneOffsetsCount).AsReadOnly();
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

