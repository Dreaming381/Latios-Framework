using System.Collections;
using Unity.Entities.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Entities.Exposed
{
    public static class BlobAssetReferenceExtensions
    {
        public static unsafe int GetLength(this in UnsafeUntypedBlobAssetReference blobReference)
        {
            if (blobReference.m_data.m_Ptr == null)
                return 0;

            return blobReference.m_data.Header->Length;
        }

        public static unsafe int GetLength<T>(this in BlobAssetReference<T> blobReference) where T : unmanaged
        {
            if (blobReference.m_data.m_Ptr == null)
                return 0;

            return blobReference.m_data.Header->Length;
        }

        public static unsafe ulong GetHash64(this in UnsafeUntypedBlobAssetReference blobReference)
        {
            if (blobReference.m_data.m_Ptr == null)
                return 0;

            return blobReference.m_data.Header->Hash;
        }

        public static unsafe ulong GetHash64<T>(this in BlobAssetReference<T> blobReference) where T : unmanaged
        {
            if (blobReference.m_data.m_Ptr == null)
                return 0;

            return blobReference.m_data.Header->Hash;
        }
    }
}

