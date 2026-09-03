#ifndef QSTX_VOXEL_GI_LIGHTING_INCLUDED
#define QSTX_VOXEL_GI_LIGHTING_INCLUDED

Texture3D<float4> _VoxelGIAlbedoOpacity;
Texture3D<float4> _VoxelGINormalTexture;
Texture3D<float4> _VoxelGIEmissiveTexture;
Texture3D<float4> _VoxelGIInputRadiance;
RWTexture3D<float4> _VoxelGIDirectRadiance;
RWTexture3D<float4> _VoxelGIFinalRadiance;

Texture2D<float> _VoxelGIShadowMap;
SamplerState sampler_VoxelGIShadowMap;
float4x4 _VoxelGIWorldToShadow;
float3 _VoxelGISunDirection;
float3 _VoxelGISunColor;
float _VoxelGISunIntensity;
float _VoxelGILightIntensity;
float _VoxelGIEmissiveIntensity;
float _VoxelGIShadowSunBias;
float _VoxelGIShadowNormalBias;
uint _VoxelGIHasDirectionalLight;
uint _VoxelGIReversedZ;

float _VoxelGIIndirectMaxMip;
uint _VoxelGIIndirectMaxSteps;
float _VoxelGIIndirectAlphaAttenuation;
float _VoxelGIIndirectIntensity;
float _VoxelGIIndirectConeAngle;
float _VoxelGIIndirectFirstStep;
float _VoxelGIIndirectStepScale;
uint _VoxelGIIndirectMinMip;

Texture3D<float4> _VoxelGIMipSource;
RWTexture3D<float4> _VoxelGIMipDestination;
uint _VoxelGISourceMip;
uint _VoxelGIDestinationResolution;
Texture3D<float4> _VoxelGICopySource;
RWTexture3D<float4> _VoxelGICopyDestination;
uint _VoxelGICopyMip;

SamplerState sampler_VoxelGILinearClamp;

float VoxelGI_SampleShadow(float3 worldPosition, float3 normal)
{
    // 将带偏移的世界位置投影到方向光 ShadowMap，并按平台 Reversed-Z 约定比较深度。
    if (_VoxelGIHasDirectionalLight == 0u) return 1.0;
    float3 biased = worldPosition + normal * (_VoxelGIShadowNormalBias * _VoxelGISize)
                    - normalize(_VoxelGISunDirection) * (_VoxelGIShadowSunBias * _VoxelGISize);
    float4 shadowPosition = mul(_VoxelGIWorldToShadow, float4(biased, 1.0));
    shadowPosition.xyz /= max(shadowPosition.w, 1e-6);
    if (any(shadowPosition.xy < 0.0) || any(shadowPosition.xy > 1.0) ||
        shadowPosition.z < 0.0 || shadowPosition.z > 1.0)
        return 1.0;
    float storedDepth = _VoxelGIShadowMap.SampleLevel(sampler_VoxelGIShadowMap, shadowPosition.xy, 0.0);
    bool occluded = _VoxelGIReversedZ != 0u ? shadowPosition.z < storedDepth : shadowPosition.z > storedDepth;
    return occluded ? 0.0 : 1.0;
}

[numthreads(4, 4, 4)]
void VoxelDirectLighting(uint3 id : SV_DispatchThreadID)
{
    // 为每个已占用体素计算方向光直射与 Emissive，并覆盖写入 DirectRadiance。
    if (any(id >= (uint)_VoxelGIResolution)) return;
    float4 albedoOpacity = _VoxelGIAlbedoOpacity[id];
    if (albedoOpacity.a <= 0.0)
    {
        _VoxelGIDirectRadiance[id] = 0.0;
        return;
    }

    float3 normal = normalize(_VoxelGINormalTexture[id].xyz * 2.0 - 1.0);
    float3 worldPosition = mul(_VoxelGIVoxelToWorld, float4(float3(id) + 0.5, 1.0)).xyz;
    float3 emissive = _VoxelGIEmissiveTexture[id].rgb * _VoxelGIEmissiveIntensity;
    float3 direct = 0.0;
    if (_VoxelGIHasDirectionalLight != 0u)
    {
        float3 lightDirection = -normalize(_VoxelGISunDirection);
        float ndotl = saturate(dot(normal, lightDirection));
        direct = albedoOpacity.rgb * ndotl * _VoxelGISunColor * _VoxelGISunIntensity *
                 _VoxelGILightIntensity * VoxelGI_SampleShadow(worldPosition, normal);
    }
    _VoxelGIDirectRadiance[id] = float4(emissive + direct, albedoOpacity.a);
}

float3 VoxelGI_FibonacciHemisphere(uint index, uint count)
{
    // 使用 Fibonacci/黄金角序列在半球上生成均匀分布的 Cone 方向。
    const float goldenAngle = 2.39996322972865332;
    float z = (index + 0.5) / max((float)count, 1.0);
    float radius = sqrt(saturate(1.0 - z * z));
    float angle = index * goldenAngle;
    return float3(cos(angle) * radius, sin(angle) * radius, z);
}

#if defined(_VOXEL_GI_INDIRECT_HIGH)
    #define VOXEL_GI_CONE_COUNT 16
#elif defined(_VOXEL_GI_INDIRECT_MEDIUM)
    #define VOXEL_GI_CONE_COUNT 8
#elif defined(_VOXEL_GI_INDIRECT_LOW)
    #define VOXEL_GI_CONE_COUNT 4
#else
    #define VOXEL_GI_CONE_COUNT 1
#endif

float3 VoxelGI_TraceIndirect(float3 voxelPosition, float3 normal)
{
    // 在体素空间沿法线半球追踪多个 Cone，按距离和 Cone 直径采样 Radiance Mip 并累加。
    float3 origin = voxelPosition / _VoxelGIResolution;
    float3x3 basis = VoxelGI_GetTangentBasis(normal);
    float coneTangent = tan(radians(_VoxelGIIndirectConeAngle * 0.5));
    float3 accumulated = 0.0;
    float weightSum = 0.0;

    [unroll]
    for (uint coneIndex = 0u; coneIndex < VOXEL_GI_CONE_COUNT; coneIndex++)
    {
        float3 direction = normalize(mul(VoxelGI_FibonacciHemisphere(coneIndex, VOXEL_GI_CONE_COUNT), basis));
        float stepLength = _VoxelGIIndirectFirstStep / _VoxelGIResolution;
        float offset = stepLength;
        float4 cone = 0.0;

        [loop]
        for (uint stepIndex = 0u; stepIndex < _VoxelGIIndirectMaxSteps && cone.a < 0.95; stepIndex++)
        {
            float3 coordinate = origin + direction * offset;
            if (VoxelGI_BoxDistance(coordinate) <= 0.0) break;
            float diameter = max(offset * coneTangent * _VoxelGIResolution * 2.0, 1.0);
            float mip = clamp(VoxelGI_CalculateMip(diameter), (float)_VoxelGIIndirectMinMip,
                _VoxelGIIndirectMaxMip);
            float4 sampleValue = _VoxelGIInputRadiance.SampleLevel(sampler_VoxelGILinearClamp, coordinate, mip);
            cone += (1.0 - pow(saturate(cone.a), _VoxelGIIndirectAlphaAttenuation)) * sampleValue;
            stepLength *= _VoxelGIIndirectStepScale;
            offset += stepLength;
        }

        float weight = saturate(dot(direction, normal));
        accumulated += cone.rgb * weight;
        weightSum += weight;
    }
    return accumulated / max(weightSum, 1e-4);
}

[numthreads(8, 8, 8)]
void VoxelIndirectLighting(uint3 id : SV_DispatchThreadID)
{
    // 将 DirectRadiance 与一次间接反弹相加，写入供屏幕追踪使用的 FinalRadiance。
    if (any(id >= (uint)_VoxelGIResolution)) return;
    float4 albedoOpacity = _VoxelGIAlbedoOpacity[id];
    if (albedoOpacity.a <= 0.0)
    {
        _VoxelGIFinalRadiance[id] = 0.0;
        return;
    }
    float3 normal = normalize(_VoxelGINormalTexture[id].xyz * 2.0 - 1.0);
    float3 direct = _VoxelGIInputRadiance[id].rgb;
    float3 bounce = VoxelGI_TraceIndirect(float3(id) + 0.5, normal);
    _VoxelGIFinalRadiance[id] = float4(direct + bounce * albedoOpacity.rgb * _VoxelGIIndirectIntensity,
        albedoOpacity.a);
}

[numthreads(8, 8, 8)]
void MipmapGeneration(uint3 id : SV_DispatchThreadID)
{
    // 将上一层的 2x2x2 体素平均到下一层，生成 Cone Tracing 所需的 3D Mip。
    if (any(id >= _VoxelGIDestinationResolution)) return;
    float4 value = 0.0;
    [unroll]
    for (uint z = 0u; z < 2u; z++)
    [unroll]
    for (uint y = 0u; y < 2u; y++)
    [unroll]
    for (uint x = 0u; x < 2u; x++)
        value += _VoxelGIMipSource.Load(int4(id * 2u + uint3(x, y, z), _VoxelGISourceMip));
    _VoxelGIMipDestination[id] = value * 0.125;
}

[numthreads(8, 8, 8)]
void CopyTexture3D(uint3 id : SV_DispatchThreadID)
{
    // 将 Scratch 中生成的 Mip 拷贝回目标 3D 纹理对应层级。
    uint resolution = max(1u, (uint)_VoxelGIResolution >> _VoxelGICopyMip);
    if (any(id >= resolution)) return;
    _VoxelGICopyDestination[id] = _VoxelGICopySource.Load(int4(id, _VoxelGICopyMip));
}

#endif
