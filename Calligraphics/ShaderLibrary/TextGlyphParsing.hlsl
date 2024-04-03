#ifndef LATIOS_TEXT_GLYPH_PARSING_INCLUDED
#define LATIOS_TEXT_GLYPH_PARSING_INCLUDED

struct GlyphVertex
{
    float3 position;
    float3 normal;
    float3 tangent;
    float4 uvA;
    float2 uvB;
    float4 color;
    uint unicode;
};

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
uniform ByteAddressBuffer _latiosTextBuffer;
uniform ByteAddressBuffer _latiosTextMaskBuffer;
#endif

GlyphVertex sampleGlyph(uint vertexId, uint textBase, uint glyphCount, uint maskBase)
{
    GlyphVertex vertex = (GlyphVertex)0;
#if defined(UNITY_DOTS_INSTANCING_ENABLED)

    if (glyphCount <= (vertexId >> 2))
    {
        vertex.position = asfloat(~0u);
        return vertex;
    }
    uint glyphBase = 96 * (textBase + (vertexId >> 2));
    if (maskBase > 0)
    {
        uint mask = _latiosTextMaskBuffer.Load(4 * (maskBase + (vertexId >> 6)));
        uint bit = (vertexId >> 2) & 0xf;
        bit += 16;
        if ((mask & (1 << bit)) == 0)
        {
            vertex.position = asfloat(~0u);
            return vertex;
        }
        bit -= 16;
        glyphBase = 96 * (textBase + (mask & 0xffff) + bit);
    }

    const bool isBottomLeft = (vertexId & 0x3) == 0;
    const bool isTopLeft = (vertexId & 0x3) == 1;
    const bool isTopRight = (vertexId & 0x3) == 2;
    const bool isBottomRight = (vertexId & 0x3) == 3;

    const uint4 glyphMeta = _latiosTextBuffer.Load4(glyphBase + 80);
    
    vertex.normal = float3(0, 0, -1);
    vertex.tangent = float3(1, 0, 0);
    vertex.position.z = 0;
    vertex.uvA.z = 0;
    vertex.uvA.w = asfloat(glyphMeta.z);
    vertex.unicode = glyphMeta.x;

    uint color = 0;

    if (isBottomLeft)
    {
        vertex.position.xy = asfloat(_latiosTextBuffer.Load2(glyphBase));
        vertex.uvA.xy = asfloat(_latiosTextBuffer.Load2(glyphBase + 16));
        vertex.uvB = asfloat(_latiosTextBuffer.Load2(glyphBase + 32));
        color = _latiosTextBuffer.Load(glyphBase + 64);
    }
    else if (isTopLeft)
    {
        vertex.position.x = asfloat(_latiosTextBuffer.Load(glyphBase)) + asfloat(glyphMeta.y);
        vertex.position.y = asfloat(_latiosTextBuffer.Load(glyphBase + 12));
        vertex.uvA.x = asfloat(_latiosTextBuffer.Load(glyphBase + 16));
        vertex.uvA.y = asfloat(_latiosTextBuffer.Load(glyphBase + 28));
        vertex.uvB = asfloat(_latiosTextBuffer.Load2(glyphBase + 40));
        color = _latiosTextBuffer.Load(glyphBase + 68);
    }
    else if (isTopRight)
    {
        vertex.position.xy = asfloat(_latiosTextBuffer.Load2(glyphBase + 8));
        vertex.uvA.xy = asfloat(_latiosTextBuffer.Load2(glyphBase + 24));
        vertex.uvB = asfloat(_latiosTextBuffer.Load2(glyphBase + 48));
        color = _latiosTextBuffer.Load(glyphBase + 72);
    }
    else // if (isBottomRight)
    {
        float2 position = asfloat(_latiosTextBuffer.Load2(glyphBase + 4).yx);
        vertex.position.x = position.x - asfloat(glyphMeta.y);
        vertex.position.y = position.y;
        vertex.uvA.xy = asfloat(_latiosTextBuffer.Load2(glyphBase + 20).yx);
        vertex.uvB = asfloat(_latiosTextBuffer.Load2(glyphBase + 56));
        color = _latiosTextBuffer.Load(glyphBase + 76);
    }

    if (glyphMeta.w != 0)
    {
        // Todo: What is the most optimal way to precompute parts of this in the compute shader
        // when we only have 4 bytes of storage?
        float angle = asfloat(glyphMeta.w);
        float cosine = cos(angle);
        float sine = sin(angle);
        
        float4 corners = asfloat(_latiosTextBuffer.Load4(glyphBase));
        float2 center = (corners.xy + corners.zw) * 0.5;
        float2 relative = vertex.position.xy - center;
        float newX = relative.x * cosine - relative.y * sine;
        float newY = relative.x * sine + relative.y * cosine;
        vertex.position.x = center.x + newX;
        vertex.position.y = center.y + newY;
    }

    vertex.color.x = (color & 0xff) / 255.;
    vertex.color.y = ((color >> 8) & 0xff) / 255.;
    vertex.color.z = ((color >> 16) & 0xff) / 255.;
    vertex.color.w = ((color >> 24) & 0xff) / 255.;

#endif
    return vertex;
}

#endif