using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Latios.Kinemation")]

namespace Unity.Rendering
{
    internal static class AssetHashExtras
    {
        public static void UpdateAsset<T>(ref Collections.xxHash3.StreamingState hash, Entities.UnityObjectRef<T> objectRef) where T : UnityEngine.Object
        {
            AssetHash.UpdateAsset(ref hash, objectRef.Id);
        }
    }
}

