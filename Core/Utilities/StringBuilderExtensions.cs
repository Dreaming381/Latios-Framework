using System;
using System.Text;
using Unity.Collections;

namespace Latios
{
    public static unsafe class StringBuilderExtensions
    {
        public static void Append(this StringBuilder builder, in FixedString32Bytes fixedString)
        {
            AppendImpl(builder, fixedString);
        }

        public static void Append(this StringBuilder builder, in FixedString64Bytes fixedString)
        {
            AppendImpl(builder, fixedString);
        }

        public static void Append(this StringBuilder builder, in FixedString128Bytes fixedString)
        {
            AppendImpl(builder, fixedString);
        }

        public static void Append(this StringBuilder builder, in FixedString512Bytes fixedString)
        {
            AppendImpl(builder, fixedString);
        }

        public static void Append(this StringBuilder builder, in FixedString4096Bytes fixedString)
        {
            AppendImpl(builder, fixedString);
        }

        static void AppendImpl<T>(this StringBuilder builder, in T fixedString) where T : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            var buffer = stackalloc char[fixedString.Length];
            Unicode.Utf8ToUtf16(fixedString.GetUnsafePtr(), fixedString.Length, buffer, out var utf16Length, fixedString.Length);
            for (int i = 0; i < utf16Length; i++)
            {
                builder.Append(buffer[i]);
            }
        }
    }
}

