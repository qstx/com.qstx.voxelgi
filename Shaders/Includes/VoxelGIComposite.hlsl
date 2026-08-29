#ifndef QSTX_VOXEL_GI_COMPOSITE_INCLUDED
#define QSTX_VOXEL_GI_COMPOSITE_INCLUDED

TEXTURE2D(_VoxelGISceneColor);
SAMPLER(sampler_VoxelGISceneColor);
TEXTURE2D(_VoxelGIIndirectIrradiance);
SAMPLER(sampler_VoxelGIIndirectIrradiance);

float4 VoxelGI_CompositeFragment(VoxelGIFullscreenVaryings input) : SV_Target
{
    float3 sceneColor = SAMPLE_TEXTURE2D(_VoxelGISceneColor, sampler_VoxelGISceneColor, input.uv).rgb;
    float3 indirect = SAMPLE_TEXTURE2D(_VoxelGIIndirectIrradiance, sampler_VoxelGIIndirectIrradiance, input.uv).rgb;
    return float4(sceneColor + indirect, 1.0);
}

#endif
