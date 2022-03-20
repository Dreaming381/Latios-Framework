using System;
using System.Text;
using Unity.Collections;

public static class StringBuilderExtensions
{
    public static void Append(this StringBuilder builder, FixedString32Bytes fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString64Bytes fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString128Bytes fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString512Bytes fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString4096Bytes fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }
}

