#ifndef QSTX_VOXEL_GI_COMMON_INCLUDED
#define QSTX_VOXEL_GI_COMMON_INCLUDED

float4x4 _VoxelGIVoxelToWorld;
float4x4 _VoxelGIWorldToVoxel;
int _VoxelGIResolution;
float _VoxelGISize;

float3x3 VoxelGI_GetTangentBasis(float3 normal)
{
    // 为法线构造稳定的切线空间，供半球/Cone 采样将局部方向转换到世界或体素空间。
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    return float3x3(tangent, cross(normal, tangent), normal);
}

float VoxelGI_CalculateMip(float diameter)
{
    // 根据光锥直径估算应采样的体素 Mip 层级，直径每扩大一倍就降低一级细节。
    return diameter <= 1.0 ? 0.0 : log2(diameter);
}

float VoxelGI_BoxDistance(float3 position)
{
    // 计算归一化体素盒内点到边界的最小距离；非正值表示已离开体素体积。
    float3 inside = 0.5 - abs(position - 0.5);
    return min(inside.x, min(inside.y, inside.z));
}

#endif
