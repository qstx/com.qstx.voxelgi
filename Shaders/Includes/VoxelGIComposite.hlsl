#ifndef QSTX_VOXEL_GI_COMPOSITE_INCLUDED
#define QSTX_VOXEL_GI_COMPOSITE_INCLUDED

TEXTURE2D(_VoxelGISceneColor);
SAMPLER(sampler_VoxelGISceneColor);
TEXTURE2D(_VoxelGIIndirectIrradiance);
SAMPLER(sampler_VoxelGIIndirectIrradiance);

float4 VoxelGI_CompositeFragment(VoxelGIFullscreenVaryings input) : SV_Target
{
    // 将屏幕空间间接光叠加到 URP 已完成不透明物体绘制的场景颜色上。
    float3 sceneColor = SAMPLE_TEXTURE2D(_VoxelGISceneColor, sampler_VoxelGISceneColor, input.uv).rgb;
    float3 indirect = SAMPLE_TEXTURE2D(_VoxelGIIndirectIrradiance, sampler_VoxelGIIndirectIrradiance, input.uv).rgb;
    return float4(sceneColor + indirect, 1.0);
}

#endif
