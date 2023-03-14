#ifndef LATIOS_DEFORM_BUFFER_SAMPLE_INCLUDED
#define LATIOS_DEFORM_BUFFER_SAMPLE_INCLUDED

struct Vertex
{
    float3 position;
    float3 normal;
    float3 tangent;
};

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
uniform StructuredBuffer<Vertex> _latiosDeformBuffer;
#endif

void sampleDeform(uint vertexId, uint meshBase, inout float3 position, inout float3 normal, inout float3 tangent)
{
#if defined(UNITY_DOTS_INSTANCING_ENABLED)
    Vertex vertex = _latiosDeformBuffer[vertexId + meshBase];
    position = vertex.position;
    normal = vertex.normal;
    tangent = vertex.tangent;
#endif
}

#endif