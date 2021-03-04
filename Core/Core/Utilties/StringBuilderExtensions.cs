using System;
using System.Text;
using Unity.Collections;

public static class StringBuilderExtensions
{
    public static void Append(this StringBuilder builder, FixedString32 fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString64 fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString128 fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString512 fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }

    public static void Append(this StringBuilder builder, FixedString4096 fixedString)
    {
        foreach (var c in fixedString)
        {
            builder.Append((char)c.value);
        }
    }
}

