
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Unity.Entities.Exposed
{
    public static class IBakerExposedExtensions
    {
        public static int GetAuthoringInstancedID(this IBaker baker)
        {
            return baker._State.AuthoringSource.GetInstanceID();
        }

        public static UnityEngine.Object GetAuthoringObjectForDebugDiagnostics(this IBaker baker)
        {
            if (baker._State.AuthoringSource == null)
                return baker._State.AuthoringObject;
            else
                return baker._State.AuthoringSource;
        }
    }

    public static class BlobAssetStoreExposedExtensions
    {
        public static uint GetTypeHashForBurst<T>(this BlobAssetStore bas) => BlobAssetStore.ComputeTypeHash(typeof(T));
        public static bool TryAddBlobAssetWithBurstHash<T>(this BlobAssetStore bas, uint typeHash, ref BlobAssetReference<T> blob) where T : unmanaged => bas.TryAdd(ref blob,
                                                                                                                                                                     typeHash);
        public static bool TryAddBlobAssetWithBurstHash<T>(this BlobAssetStore bas, Hash128 customHash, uint typeHash,
                                                           ref BlobAssetReference<T> blob) where T : unmanaged => bas.TryAdd(customHash, typeHash, ref blob);
        public static bool TryGetBlobAssetWithBurstHash<T>(this BlobAssetStore bas, Hash128 customHash, uint typeHash,
                                                           out BlobAssetReference<T> blob) where T : unmanaged => bas.TryGet(customHash, typeHash, out blob);
    }

    /// <summary>
    /// Overrides the global list of bakers either adding new ones or replacing old ones.
    /// This is used for tests. Always make sure to dispose to revert the global state back to what it was.
    /// </summary>
    public struct OverrideBakers : IDisposable
    {
        BakerDataUtility.OverrideBakers m_override;

        public OverrideBakers(bool replaceExistingBakers, params Type[] bakerTypes)
        {
            m_override = new BakerDataUtility.OverrideBakers(replaceExistingBakers, bakerTypes);
            // Sort the bakers so that the bakers for base authoring components are evaluated before bakers for derived types.
            // This guarantees that the type hierarchy chain authoring components is respected, regardless of the order in which
            // the bakers are defined in code.
            var fieldInfo = typeof(BakerDataUtility).GetField("_IndexToBakerInstances", BindingFlags.Static | BindingFlags.NonPublic);
            var dict      = fieldInfo.GetValue(null) as Dictionary<TypeIndex, BakerDataUtility.BakerData[]>;
            foreach (var bakers in dict.Values)
            {
                Array.Sort(bakers, (a, b) => b.CompatibleComponentCount.CompareTo(a.CompatibleComponentCount));
            }
        }

        public void Dispose()
        {
            m_override.Dispose();
        }

        public static List<Type> GetDefaultBakerTypes()
        {
            var candidateBakers = new List<System.Type>();

#if UNITY_EDITOR
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom(typeof(Baker<>)))
            {
                if (!type.IsAbstract && !type.IsDefined(typeof(DisableAutoCreationAttribute)))
                {
                    candidateBakers.Add(type);
                }
            }
            foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom(typeof(GameObjectBaker)))
            {
                if (!type.IsAbstract && !type.IsDefined(typeof(DisableAutoCreationAttribute)))
                {
                    candidateBakers.Add(type);
                }
            }
#endif

            return candidateBakers;
        }
    }
}

