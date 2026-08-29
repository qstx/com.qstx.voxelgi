#ifndef QSTX_VOXEL_GI_FILTERING_INCLUDED
#define QSTX_VOXEL_GI_FILTERING_INCLUDED

Texture2D<float4> _VoxelGIFilterInput;
Texture2D<float> _VoxelGIDepthTexture;
Texture2D<float4> _VoxelGIScreenNormalTexture;
RWTexture2D<float4> _VoxelGIFilterOutput;
SamplerState sampler_VoxelGIFilterInput;
SamplerState sampler_VoxelGIDepthTexture;
SamplerState sampler_VoxelGIScreenNormalTexture;
float4 _VoxelGIScreenSize;
float4 _VoxelGIBilateralThresholds;
float _VoxelGIBilateralRadius;
float4 _ZBufferParams;

static const float2 VoxelGI_PoissonDisk[8] =
{
    float2( 0.402211,  0.126575),
    float2( 0.297056,  0.616830),
    float2(-0.066918, -0.367739),
    float2(-0.955010,  0.372377),
    float2( 0.800057,  0.120602),
    float2(-0.749494,  0.182799),
    float2(-0.857289, -0.416908),
    float2( 0.104546,  0.965765)
};

float VoxelGI_LinearEyeDepth(float rawDepth)
{
    return rcp(_ZBufferParams.z * rawDepth + _ZBufferParams.w);
}

void VoxelGI_LoadDepthNormal(float2 uv, out float depth, out float3 normal)
{
    depth = VoxelGI_LinearEyeDepth(_VoxelGIDepthTexture.SampleLevel(sampler_VoxelGIDepthTexture, uv, 0.0));
    normal = normalize(_VoxelGIScreenNormalTexture.SampleLevel(sampler_VoxelGIScreenNormalTexture, uv, 0.0).xyz);
}

[numthreads(8, 8, 1)]
void BilateralFiltering(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= (uint2)_VoxelGIScreenSize.xy)) return;
    float2 uv = (id.xy + 0.5) * _VoxelGIScreenSize.zw;
    float centerDepth;
    float3 centerNormal;
    VoxelGI_LoadDepthNormal(uv, centerDepth, centerNormal);

    float4 accumulated = _VoxelGIFilterInput.SampleLevel(sampler_VoxelGIFilterInput, uv, 0.0);
    float totalWeight = 1.0;
    float depthRange = max(_VoxelGIBilateralThresholds.y - _VoxelGIBilateralThresholds.x, 1e-5);
    float normalRange = max(_VoxelGIBilateralThresholds.w - _VoxelGIBilateralThresholds.z, 1e-5);
    [unroll]
    for (uint i = 0u; i < 8u; i++)
    {
        float2 sampleUV = uv + VoxelGI_PoissonDisk[i] * _VoxelGIBilateralRadius * _VoxelGIScreenSize.zw;
        float sampleDepth;
        float3 sampleNormal;
        VoxelGI_LoadDepthNormal(sampleUV, sampleDepth, sampleNormal);
        float depthWeight = 1.0 - saturate((abs(sampleDepth - centerDepth) - _VoxelGIBilateralThresholds.x) /
            depthRange);
        float normalWeight = saturate((dot(sampleNormal, centerNormal) - _VoxelGIBilateralThresholds.z) /
            normalRange);
        float weight = depthWeight * normalWeight;
        accumulated += _VoxelGIFilterInput.SampleLevel(sampler_VoxelGIFilterInput, sampleUV, 0.0) * weight;
        totalWeight += weight;
    }
    _VoxelGIFilterOutput[id.xy] = float4(accumulated.rgb / max(totalWeight, 1e-5), 1.0);
}

#endif
