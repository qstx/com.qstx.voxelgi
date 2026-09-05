#ifndef QSTX_VOXEL_GI_DEBUG_INCLUDED
#define QSTX_VOXEL_GI_DEBUG_INCLUDED

TEXTURE3D(_VoxelGIAlbedoOpacity);
SAMPLER(sampler_VoxelGIAlbedoOpacity);
TEXTURE3D(_VoxelGINormal);
SAMPLER(sampler_VoxelGINormal);
TEXTURE3D(_VoxelGIEmissive);
SAMPLER(sampler_VoxelGIEmissive);
TEXTURE3D(_VoxelGIDirectRadiance);
SAMPLER(sampler_VoxelGIDirectRadiance);
TEXTURE3D(_VoxelGIFinalRadiance);
SAMPLER(sampler_VoxelGIFinalRadiance);
TEXTURE2D(_VoxelGIDebugTexture);
SAMPLER(sampler_VoxelGIDebugTexture);
TEXTURE2D(_VoxelGIShadowMap);
SAMPLER(sampler_VoxelGIShadowMap);

float4x4 _VoxelGIInverseViewProjection;
float3 _VoxelGICameraPosition;
float _VoxelGIGridWorldSize;
int _VoxelGIDebugMode;
int _VoxelGIDebugMipLevel;
float _VoxelGIDebugRayStep;

bool VoxelGI_IntersectUnitBox(float3 origin, float3 direction, out float enter, out float exit)
{
    // 使用 slab 法计算相机射线与归一化体素盒的进入/离开距离。
    float3 inverseDirection = rcp(direction);
    float3 first = (0.0 - origin) * inverseDirection;
    float3 second = (1.0 - origin) * inverseDirection;
    float3 minimum = min(first, second);
    float3 maximum = max(first, second);
    enter = max(max(minimum.x, minimum.y), minimum.z);
    exit = min(min(maximum.x, maximum.y), maximum.z);
    enter = max(enter, 0.0);
    return exit >= enter;
}

float4 VoxelGI_DebugSample(float3 uvw)
{
    // 根据 DebugMode 选择体素材质或 Direct/Final Radiance，并按设置采样指定 Mip。
    if (_VoxelGIDebugMode == 1)
        return SAMPLE_TEXTURE3D_LOD(_VoxelGIAlbedoOpacity, sampler_VoxelGIAlbedoOpacity, uvw, 0.0);
    if (_VoxelGIDebugMode == 2)
    {
        float4 value = SAMPLE_TEXTURE3D_LOD(_VoxelGINormal, sampler_VoxelGINormal, uvw, 0.0);
        return float4(value.rgb, value.a);
    }
    if (_VoxelGIDebugMode == 3)
        return SAMPLE_TEXTURE3D_LOD(_VoxelGIEmissive, sampler_VoxelGIEmissive, uvw, 0.0);
    if (_VoxelGIDebugMode == 5)
        return SAMPLE_TEXTURE3D_LOD(_VoxelGIDirectRadiance, sampler_VoxelGIDirectRadiance, uvw,
            _VoxelGIDebugMipLevel);
    return SAMPLE_TEXTURE3D_LOD(_VoxelGIFinalRadiance, sampler_VoxelGIFinalRadiance, uvw,
        _VoxelGIDebugMipLevel);
}

float4 VoxelGI_DebugFragment(VoxelGIFullscreenVaryings input) : SV_Target
{
    // Debug 的屏幕入口：特殊模式直接显示 2D 结果，其余模式沿相机射线合成体素数据。
    if (_VoxelGIDebugMode == 4)
    {
        float depth = SAMPLE_TEXTURE2D(_VoxelGIShadowMap, sampler_VoxelGIShadowMap, input.uv).r;
        return float4(depth.xxx, 1.0);
    }
    if (_VoxelGIDebugMode >= 7)
        return SAMPLE_TEXTURE2D(_VoxelGIDebugTexture, sampler_VoxelGIDebugTexture, input.uv);

#if UNITY_REVERSED_Z
    const float farDepth = 0.0;
#else
    const float farDepth = 1.0;
#endif
    // 从当前像素和相机远平面深度重建世界空间远点；farDepth 必须匹配平台的 Reversed-Z 约定。
    float3 farWorld = ComputeWorldSpacePosition(input.uv, farDepth, _VoxelGIInverseViewProjection);

    // 构造从相机位置穿过当前屏幕像素的世界空间射线方向。
    float3 directionWorld = normalize(farWorld - _VoxelGICameraPosition);

    // 将相机射线起点从世界空间变换到归一化体素空间 [0, 1]；除以分辨率后，
    // 体素网格坐标被转换为可用于 3D Texture 采样的 UVW 坐标。
    float3 origin = mul(_VoxelGIWorldToVoxel, float4(_VoxelGICameraPosition, 1.0)).xyz / _VoxelGIResolution;

    // 方向向量只使用 WorldToVoxel 的线性部分，不能包含平移；同样缩放到归一化体素空间。
    float3 direction = mul((float3x3)_VoxelGIWorldToVoxel, directionWorld) / _VoxelGIResolution;
    float enter;
    float exit;

    // 先求射线与单位体素盒的进入/离开距离。射线完全不穿过体素范围时无需执行后续采样。
    if (!VoxelGI_IntersectUnitBox(origin, direction, enter, exit))
        return float4(0.0, 0.0, 0.0, 1.0);

    float normalizedStep = max(_VoxelGIDebugRayStep / max(_VoxelGIGridWorldSize, 1e-5), 1.0 / _VoxelGIResolution);
    float4 accumulated = 0.0;
    [loop]
    for (float distance = enter; distance <= exit && accumulated.a < 0.99; distance += normalizedStep)
    {
        float3 uvw = origin + direction * distance;
        float4 sampleValue = VoxelGI_DebugSample(uvw);
        accumulated.rgb += (1.0 - accumulated.a) * sampleValue.rgb * sampleValue.a;
        accumulated.a += (1.0 - accumulated.a) * sampleValue.a;
    }
    return float4(accumulated.rgb, 1.0);
}

#endif
