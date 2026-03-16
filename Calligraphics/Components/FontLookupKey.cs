using System;
using Unity.Collections;

namespace Latios.Calligraphics
{
    // Note: In TextMeshDOTS, this is named FontAssetRef.

    /// <summary>
    /// FontLookupKey is a small key structure for a loaded font. It is used internally to lookup font face
    /// resources, but you can also use it for your own logic. FontLookupKey consists of a hash representing the
    /// font family, and variation axis used during typesetting such as weight ("normal", "bold", semibold"),
    /// width ("condensed", normal"), and italic. Slant is ignored in such font matching (see GetHashcode)
    /// because slant value cannot be "guessed" and requested by user during typesetting
    /// </summary>
    [Serializable]
    public struct FontLookupKey : IEquatable<FontLookupKey>
    {
        //Font selection logic: https://www.high-logic.com/font-editor/fontcreator/tutorials/font-family-settings
        public int   familyHash;  //default to typeographic family, and fall-back to family if it does not exist
        public float weight;
        public float width;
        public bool  isItalic;
        public float slant;

        public FontLookupKey(FixedString128Bytes fontFamily, FixedString128Bytes typographicFamily, float weight, float width, bool isItalic, float slant)
        {
            this.familyHash = typographicFamily.IsEmpty ? TextHelper.GetHashCodeCaseInsensitive(fontFamily) : TextHelper.GetHashCodeCaseInsensitive(typographicFamily);
            this.weight     = weight;
            this.width      = width;
            this.isItalic   = isItalic;
            this.slant      = slant;
        }
        public FontLookupKey(int familyNameHashCode, float weight, float width, bool isItalic, float slant = 0)
        {
            this.familyHash = familyNameHashCode;
            this.weight     = weight;
            this.width      = width;
            this.isItalic   = isItalic;
            this.slant      = slant;
        }

        public override bool Equals(object obj) => obj is FontLookupKey other && Equals(other);

        public bool Equals(FontLookupKey other)
        {
            return GetHashCode() == other.GetHashCode();
        }

        public static bool operator ==(FontLookupKey e1, FontLookupKey e2)
        {
            return e1.GetHashCode() == e2.GetHashCode();
        }
        public static bool operator !=(FontLookupKey e1, FontLookupKey e2)
        {
            return e1.GetHashCode() != e2.GetHashCode();
        }

        public override int GetHashCode()
        {
            int hashCode = 2055808453;
            hashCode     = hashCode * -1521134295 + familyHash;
            hashCode     = hashCode * -1521134295 + (int)weight;
            hashCode     = hashCode * -1521134295 + width.GetHashCode();
            hashCode     = hashCode * -1521134295 + isItalic.GetHashCode();
            //fonts are searched at runtime via FontLookupKey match. As slant angle cannot be guessed, do not include this in hash
            //hashCode = hashCode * -1521134295 + slant.GetHashCode();
            return hashCode;
        }
        public override string ToString()
        {
            return $"FamilyHash {familyHash} weigth {weight} width {width} isItalic {isItalic}";
        }
    }
}

