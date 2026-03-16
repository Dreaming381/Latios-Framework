using System;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.HarfBuzz
{
    internal class FontDebug
    {
        public static void GetNameTags(Face face)
        {
            var language = Language.OpentypeTagToHBLanguage(Harfbuzz.HB_TAG('E', 'N', 'G', ' '));
            var result = new FixedString128Bytes();
            var values = Enum.GetValues(typeof(NameID));
            foreach (NameID value in values)
            {
                result = face.GetName(value, language);
                Debug.Log($"{value}: {result}");
                result.Clear();
            }
        }
    }
}
