using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Language
    {
        public IntPtr ptr;

        /// <summary> Converts str representing a BCP 47 language tag to the corresponding hb_language_t object </summary>
        public Language(FixedString128Bytes language)
        {
            unsafe
            {
                ptr = Harfbuzz.hb_language_from_string(language.GetUnsafePtr(), language.Length);
            }
        }
        public FixedString128Bytes LanguageToFixedString()
        {
            FixedString128Bytes result;
            unsafe
            {
                var tmp = Harfbuzz.hb_language_to_string(ptr);
                result  = Harfbuzz.GetFixedString128(tmp);
            }
            return result;
        }
        /// <summary>
        /// Converts captial letter <see href="https://learn.microsoft.com/en-us/typography/opentype/spec/languagetags">Opentype language tags</see> into BCP 47 language subtags
        /// </summary>
        public static Language OpentypeTagToHBLanguage(uint tag)
        {
            return new Language { ptr = Harfbuzz.hb_ot_tag_to_language(tag) };
        }
        public static void OtTagsFromScriptAndLanguage(Script script, Language language, out NativeList<uint> script_tags,  out NativeList<uint> language_tags)
        {
            uint scriptCount     = 16;
            uint languageCount   = 16;
            script_tags          = new NativeList<uint>((int)scriptCount, Allocator.Temp);
            script_tags.Length   = (int)scriptCount;
            language_tags        = new NativeList<uint>((int)languageCount, Allocator.Temp);
            language_tags.Length = (int)scriptCount;
            unsafe
            {
                Harfbuzz.hb_ot_tags_from_script_and_language(script, language, ref scriptCount, script_tags.GetUnsafePtr(), ref languageCount, language_tags.GetUnsafePtr());
            }
            script_tags.Length   = (int)scriptCount;
            language_tags.Length = (int)languageCount;
        }

        static public Language English => Language.OpentypeTagToHBLanguage(Harfbuzz.HB_TAG('E', 'N', 'G', ' '));
        static public Language Undefined => new Language("und");
        public static Language Default => new Language { ptr = Harfbuzz.hb_language_get_default() };

        public override string ToString()
        {
            string result;
            unsafe
            {
                result = Marshal.PtrToStringUTF8((IntPtr)Harfbuzz.hb_language_to_string(ptr));
            }
            return result;
        }
    }
}

