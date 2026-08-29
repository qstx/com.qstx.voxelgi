#ifndef QSTX_VOXEL_GI_FULLSCREEN_COMMON_INCLUDED
#define QSTX_VOXEL_GI_FULLSCREEN_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct VoxelGIFullscreenAttributes
{
    uint vertexID : SV_VertexID;
};

struct VoxelGIFullscreenVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VoxelGIFullscreenVaryings VoxelGI_FullscreenVertex(VoxelGIFullscreenAttributes input)
{
    VoxelGIFullscreenVaryings output;
    output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
    output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
    return output;
}

float3 VoxelGI_RGBToYCoCg(float3 value)
{
    float midpoint = (value.r + value.b) * 0.5;
    return float3((value.g + midpoint) * 0.5, (value.r - value.b) * 0.5, (value.g - midpoint) * 0.5);
}

float3 VoxelGI_YCoCgToRGB(float3 value)
{
    return float3(value.x + value.y - value.z, value.x + value.z, value.x - value.y - value.z);
}

float VoxelGI_Hash(float2 value)
{
    return frac(sin(dot(value, float2(12.9898, 78.233))) * 43758.5453);
}

float3x3 VoxelGI_GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    return float3x3(tangent, cross(normal, tangent), normal);
}

#endif
