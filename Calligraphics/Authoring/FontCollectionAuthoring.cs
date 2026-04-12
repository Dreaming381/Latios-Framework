#if UNITY_EDITOR
using Latios.Authoring;
using Unity.Entities;
using UnityEngine;

namespace Latios.Calligraphics.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Calligraphics/Font Collection (Calligraphics)")]
    public class FontCollectionAuthoring : MonoBehaviour
    {
        public FontCollectionAsset fontCollectionAsset;
    }

    [TemporaryBakingType]
    struct FontCollectionAuthoringSmartBakeItem : ISmartBakeItem<FontCollectionAuthoring>
    {
        SmartBlobberHandle<FontLoadDescriptionsBlob> handle;

        public bool Bake(FontCollectionAuthoring authoring, IBaker baker) => Bake(authoring.fontCollectionAsset, baker);

        public bool Bake(FontCollectionAsset fontCollectionAsset, IBaker baker)
        {
            if (fontCollectionAsset == null)
                return false;
            baker.DependsOn(fontCollectionAsset);
            var fontCount = fontCollectionAsset.fontLoadDescriptions.Count;
            if (fontCount == 0)
                return false;
            handle = baker.RequestCreateBlobAsset(fontCollectionAsset);
            if (!handle.IsValid)
                return false;
            baker.AddComponent<FontLoadDescriptionsBlobReference>(baker.GetEntity(TransformUsageFlags.None));
            return true;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            var blob = handle.Resolve(entityManager);
            entityManager.SetComponentData(entity, new FontLoadDescriptionsBlobReference { blob = blob });
        }
    }

    class FontCollectionBaker : SmartBaker<FontCollectionAuthoring, FontCollectionAuthoringSmartBakeItem>
    {
    }
}
#endif

