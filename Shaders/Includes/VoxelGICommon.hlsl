#ifndef QSTX_VOXEL_GI_COMMON_INCLUDED
#define QSTX_VOXEL_GI_COMMON_INCLUDED

float4x4 _VoxelGIVoxelToWorld;
float4x4 _VoxelGIWorldToVoxel;
int _VoxelGIResolution;
float _VoxelGISize;

float3x3 VoxelGI_GetTangentBasis(float3 normal)
{
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    return float3x3(tangent, cross(normal, tangent), normal);
}

float VoxelGI_CalculateMip(float diameter)
{
    return diameter <= 1.0 ? 0.0 : log2(diameter);
}

float VoxelGI_BoxDistance(float3 position)
{
    float3 inside = 0.5 - abs(position - 0.5);
    return min(inside.x, min(inside.y, inside.z));
}

#endif
