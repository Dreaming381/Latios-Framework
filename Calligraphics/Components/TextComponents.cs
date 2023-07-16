using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    public struct FontBlobReference : IComponentData
    {
        public BlobAssetReference<FontBlob> blob;
    }

    public struct TextBaseConfiguration : IComponentData
    {
        public float fontSize;
        public Color32 color;
    }

    [InternalBufferCapacity(0)]
    public struct AdditionalFontMaterialEntity : IBufferElementData
    {
        public EntityWith<FontBlobReference> entity;
    }

    [InternalBufferCapacity(0)]
    public struct CalliByte : IBufferElementData
    {
        public byte element;
    }
}

