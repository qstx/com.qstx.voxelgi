#ifndef QSTX_VOXEL_GI_TEMPORAL_INCLUDED
#define QSTX_VOXEL_GI_TEMPORAL_INCLUDED

TEXTURE2D(_VoxelGICurrentIrradiance);
SAMPLER(sampler_VoxelGICurrentIrradiance);
TEXTURE2D(_VoxelGIHistoryIrradiance);
SAMPLER(sampler_VoxelGIHistoryIrradiance);
TEXTURE2D(_VoxelGIMotionVectors);
SAMPLER(sampler_VoxelGIMotionVectors);
float _VoxelGITemporalCurrentFrameWeight;
float _VoxelGITemporalClampScale;
int _VoxelGIHistoryValid;

static const float2 VoxelGI_Neighborhood[9] =
{
    float2(-1, -1), float2(0, -1), float2(1, -1),
    float2(-1,  0), float2(0,  0), float2(1,  0),
    float2(-1,  1), float2(0,  1), float2(1,  1)
};

float4 VoxelGI_TemporalFragment(VoxelGIFullscreenVaryings input) : SV_Target
{
    // 先用 Motion Vector 重投影上一帧，再依据当前帧邻域方差裁剪历史并进行时序混合。
    float3 current = SAMPLE_TEXTURE2D(_VoxelGICurrentIrradiance, sampler_VoxelGICurrentIrradiance, input.uv).rgb;
    if (_VoxelGIHistoryValid == 0)
        return float4(current, 1.0);
    float2 motion = SAMPLE_TEXTURE2D(_VoxelGIMotionVectors, sampler_VoxelGIMotionVectors, input.uv).xy;
    float2 historyUV = input.uv - motion;
    if (any(historyUV < 0.0) || any(historyUV > 1.0))
        return float4(current, 1.0);

    float3 history = VoxelGI_RGBToYCoCg(
        SAMPLE_TEXTURE2D(_VoxelGIHistoryIrradiance, sampler_VoxelGIHistoryIrradiance, historyUV).rgb);
    float3 firstMoment = 0.0;
    float3 secondMoment = 0.0;
    [unroll]
    for (uint i = 0u; i < 9u; i++)
    {
        float2 sampleUV = input.uv + VoxelGI_Neighborhood[i] * _VoxelGIScreenSize.zw;
        float3 sampleValue = VoxelGI_RGBToYCoCg(
            SAMPLE_TEXTURE2D(_VoxelGICurrentIrradiance, sampler_VoxelGICurrentIrradiance, sampleUV).rgb);
        firstMoment += sampleValue;
        secondMoment += sampleValue * sampleValue;
    }
    firstMoment /= 9.0;
    secondMoment /= 9.0;
    float3 deviation = sqrt(abs(secondMoment - firstMoment * firstMoment));
    history = clamp(history, firstMoment - deviation * _VoxelGITemporalClampScale,
        firstMoment + deviation * _VoxelGITemporalClampScale);
    history = VoxelGI_YCoCgToRGB(history);
    float currentWeight = 1.0 - saturate((1.0 - _VoxelGITemporalCurrentFrameWeight) *
        (1.0 - length(motion) * 30.0));
    return float4(lerp(history, current, currentWeight), 1.0);
}

#endif
