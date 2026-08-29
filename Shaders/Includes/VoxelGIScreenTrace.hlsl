#ifndef QSTX_VOXEL_GI_SCREEN_TRACE_INCLUDED
#define QSTX_VOXEL_GI_SCREEN_TRACE_INCLUDED

TEXTURE2D(_VoxelGIScreenDepth);
SAMPLER(sampler_VoxelGIScreenDepth);
TEXTURE2D(_VoxelGIScreenNormals);
SAMPLER(sampler_VoxelGIScreenNormals);
TEXTURE2D(_VoxelGIBlueNoise);
SAMPLER(sampler_VoxelGIBlueNoise);
TEXTURE3D(_VoxelGITraceRadiance);
SAMPLER(sampler_VoxelGITraceRadiance);

float4x4 _VoxelGIWorldToVoxel;
int _VoxelGIResolution;
float _VoxelGIScreenMaxMip;
int _VoxelGIScreenMaxSteps;
float _VoxelGIScreenAlphaAttenuation;
float _VoxelGIScreenIntensity;
float _VoxelGIScreenConeAngle;
float _VoxelGIScreenFirstStep;
float _VoxelGIScreenStepScale;
int _VoxelGIScreenQuality;
int _VoxelGITemporalEnabled;
int _VoxelGIHasBlueNoise;
float4 _VoxelGIScreenSize;
float4 _VoxelGIBlueNoiseSize;
float4 _VoxelGIBlueNoiseScale;
float2 _VoxelGIJitter;

float3 VoxelGI_SampleHemisphere(uint index, uint count, float2 jitter)
{
    float u = frac((index + 0.5) / max((float)count, 1.0) + jitter.x);
    float v = frac(index * 0.61803398875 + jitter.y);
    float radius = sqrt(u);
    float angle = TWO_PI * v;
    float2 disk = radius * float2(cos(angle), sin(angle));
    return float3(disk, sqrt(saturate(1.0 - dot(disk, disk))));
}

float3 VoxelGI_TraceCone(float3 origin, float3 direction, float jitter)
{
    float coneTangent = tan(radians(_VoxelGIScreenConeAngle * 0.5));
    float stepLength = _VoxelGIScreenFirstStep / _VoxelGIResolution;
    float offset = stepLength * lerp(0.5, 1.5, jitter);
    float4 accumulated = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < _VoxelGIScreenMaxSteps && accumulated.a < 0.95; stepIndex++)
    {
        float3 coordinate = origin + direction * offset;
        if (any(coordinate <= 0.0) || any(coordinate >= 1.0)) break;
        float diameter = max(offset * coneTangent * _VoxelGIResolution * 2.0, 1.0);
        float mip = clamp(log2(diameter), 0.0, _VoxelGIScreenMaxMip);
        float4 sampleValue = SAMPLE_TEXTURE3D_LOD(_VoxelGITraceRadiance, sampler_VoxelGITraceRadiance,
            coordinate, mip);
        accumulated += (1.0 - pow(saturate(accumulated.a), _VoxelGIScreenAlphaAttenuation)) * sampleValue;
        stepLength *= _VoxelGIScreenStepScale;
        offset += stepLength;
    }
    return accumulated.rgb;
}

float4 VoxelGI_ScreenTraceFragment(VoxelGIFullscreenVaryings input) : SV_Target
{
    float rawDepth = SAMPLE_TEXTURE2D(_VoxelGIScreenDepth, sampler_VoxelGIScreenDepth, input.uv).r;
#if UNITY_REVERSED_Z
    if (rawDepth <= 0.0) return float4(0.0, 0.0, 0.0, 1.0);
#else
    if (rawDepth >= 1.0) return float4(0.0, 0.0, 0.0, 1.0);
#endif
    float3 worldPosition = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
    float3 voxel = mul(_VoxelGIWorldToVoxel, float4(worldPosition, 1.0)).xyz / _VoxelGIResolution;
    if (any(voxel <= 0.0) || any(voxel >= 1.0)) return float4(0.0, 0.0, 0.0, 1.0);
    float3 normal = normalize(SAMPLE_TEXTURE2D(_VoxelGIScreenNormals, sampler_VoxelGIScreenNormals, input.uv).xyz);
    float2 pixel = input.uv * _VoxelGIScreenSize.xy;
    float2 noise = float2(VoxelGI_Hash(pixel + _VoxelGIJitter), VoxelGI_Hash(pixel.yx + _VoxelGIJitter.yx + 17.0));
    if (_VoxelGIHasBlueNoise != 0)
    {
        float2 noiseUV = input.uv * _VoxelGIScreenSize.xy * _VoxelGIBlueNoiseSize.zw *
                         _VoxelGIBlueNoiseScale.xy + _VoxelGIJitter;
        noise = SAMPLE_TEXTURE2D_LOD(_VoxelGIBlueNoise, sampler_VoxelGIBlueNoise, noiseUV, 0).xy;
    }

    int coneCount = _VoxelGITemporalEnabled != 0 ? 1 : (_VoxelGIScreenQuality == 0 ? 1 :
        _VoxelGIScreenQuality == 1 ? 2 : _VoxelGIScreenQuality == 2 ? 4 : 8);
    float3x3 basis = VoxelGI_GetTangentBasis(normal);
    float3 result = 0.0;
    [loop]
    for (int coneIndex = 0; coneIndex < coneCount; coneIndex++)
    {
        float3 direction = normalize(mul(VoxelGI_SampleHemisphere(coneIndex, coneCount, noise), basis));
        result += VoxelGI_TraceCone(voxel, direction, noise.x) * saturate(dot(direction, normal));
    }
    return float4(result * (_VoxelGIScreenIntensity / max(coneCount, 1)), 1.0);
}

#endif
