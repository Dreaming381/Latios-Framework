using Latios.Calligraphics.Rendering;
using Unity.Collections;
using Unity.Entities;


namespace Latios.Calligraphics
{
    internal unsafe struct FontMaterialSet
    {
        FixedList4096Bytes<FontMaterial>            m_fontMaterialArray;
        FixedList512Bytes<byte>                     m_fontToEntityIndexArray;
        DynamicBuffer<FontMaterialSelectorForGlyph> m_selectorBuffer;
        bool                                        m_hasMultipleFonts;

        public ref FontBlob this[int index] => ref m_fontMaterialArray[index].font;

        public int length => m_fontMaterialArray.Length;

        public void WriteFontMaterialIndexForGlyph(int index)
        {
            if (!m_hasMultipleFonts)
                return;
            var remap                                                                 = m_fontToEntityIndexArray[index];
            m_selectorBuffer.Add(new FontMaterialSelectorForGlyph { fontMaterialIndex = remap });
        }

        public void Initialize(BlobAssetReference<FontBlob> singleFont)
        {
            m_hasMultipleFonts = false;
            m_fontMaterialArray.Clear();
            m_fontMaterialArray.Add(new FontMaterial(singleFont));
        }

        public void Initialize(BlobAssetReference<FontBlob>                baseFont,
                               DynamicBuffer<FontMaterialSelectorForGlyph> selectorBuffer,
                               DynamicBuffer<AdditionalFontMaterialEntity> entities,
                               ref ComponentLookup<FontBlobReference>      blobLookup)
        {
            Initialize(baseFont);
            m_selectorBuffer = selectorBuffer;
            m_selectorBuffer.Clear();
            m_hasMultipleFonts = true;
            m_fontToEntityIndexArray.Clear();
            m_fontToEntityIndexArray.Add(0);// Index 0 is this entity. Index 1 is the first entity in AdditionalFontMaterialEntity buffer.
            for (int i = 0; i < entities.Length; i++)
            {
                if (blobLookup.TryGetComponent(entities[i].entity, out var blobRef))
                {
                    if (blobRef.blob.IsCreated)
                    {
                        m_fontMaterialArray.Add(new FontMaterial(blobRef.blob));
                        m_fontToEntityIndexArray.Add((byte)(i + 1));
                    }
                }
            }
        }

        unsafe struct FontMaterial
        {
            FontBlob* m_fontBlobPtr;

            public ref FontBlob font => ref *m_fontBlobPtr;

            public FontMaterial(BlobAssetReference<FontBlob> blobRef)
            {
                m_fontBlobPtr = (FontBlob*)blobRef.GetUnsafePtr();
            }
        }
    }
}

