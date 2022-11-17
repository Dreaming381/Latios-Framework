using Unity.Entities;

namespace Latios.Myri.Authoring
{
    public static class ListenerProfileBlobberAPIExtensions
    {
        /// <summary>
        /// Builds a BlobAsset for the audio listener profile immediately using a baker and using the Baker's blob asset tracking
        /// </summary>
        /// <typeparam name="T">The type of builder to generate the blob asset</typeparam>
        /// <param name="builder">The builder used to generate the blob</param>
        /// <returns>A BlobAssetReference that can be assigned immediately to components or buffers via the baker</returns>
        /// <remarks>For anyone wondering why this isn't using a Smart Blobber, the reason is that there are typically very few instances of these,
        /// and most of the work may depend on variables not accessible to baking systems. The final blob generation piece is still Burst-compiled.</remarks>
        public static BlobAssetReference<ListenerProfileBlob> BuildAndRegisterListenerProfileBlob<T>(this IBaker baker, T builder) where T : IListenerProfileBuilder
        {
            var context = new ListenerProfileBuildContext();
            context.Initialize();
            builder.BuildProfile(ref context);
            var blob = context.ComputeBlobAndDispose();
            baker.AddBlobAsset(ref blob, out _);
            return blob;
        }
    }
}

