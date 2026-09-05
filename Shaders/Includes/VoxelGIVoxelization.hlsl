#ifndef QSTX_VOXEL_GI_VOXELIZATION_INCLUDED
#define QSTX_VOXEL_GI_VOXELIZATION_INCLUDED

ByteAddressBuffer _VoxelGIPositionBuffer;
ByteAddressBuffer _VoxelGINormalBuffer;
ByteAddressBuffer _VoxelGIUVBuffer;
ByteAddressBuffer _VoxelGIIndexBuffer;
RWStructuredBuffer<uint> _VoxelGIAlbedoAccumulation;
RWStructuredBuffer<uint> _VoxelGINormalAccumulation;
RWStructuredBuffer<uint4> _VoxelGIEmissiveAccumulation;
RWStructuredBuffer<uint> _VoxelGIOpacityAccumulation;
RWTexture3D<float4> _VoxelGIAlbedoOutput;
RWTexture3D<float4> _VoxelGINormalOutput;
RWTexture3D<float4> _VoxelGIEmissiveOutput;

Texture2D<float4> _VoxelGIBaseMap;
Texture2D<float4> _VoxelGIEmissionMap;
SamplerState sampler_VoxelGIBaseMap;
SamplerState sampler_VoxelGIEmissionMap;

uint _VoxelGIElementCount;
uint _VoxelGIIndexStart;
uint _VoxelGIIndexCount;
uint _VoxelGIBaseVertex;
uint _VoxelGIIndexFormat;
uint _VoxelGIPositionStride;
uint _VoxelGIPositionOffset;
uint _VoxelGIPositionFormat;
uint _VoxelGIPositionDimension;
uint _VoxelGINormalStride;
uint _VoxelGINormalOffset;
uint _VoxelGINormalFormat;
uint _VoxelGINormalDimension;
uint _VoxelGIUVStride;
uint _VoxelGIUVOffset;
uint _VoxelGIUVFormat;
uint _VoxelGIUVDimension;
uint _VoxelGIHasNormals;
uint _VoxelGIHasUV;
uint _VoxelGIConservativeRasterization;
float _VoxelGIConservativeScale;
float4x4 _VoxelGIObjectToWorld;
float4x4 _VoxelGINormalToWorld;
float4 _VoxelGIBaseColor;
float4 _VoxelGIEmissionColor;
float4 _VoxelGIBaseMapST;
float4 _VoxelGIEmissionMapST;
uint _VoxelGIAlphaClip;
float _VoxelGIAlphaCutoff;
uint _VoxelGIOpacityOnly;
uint _VoxelGIEmissionEnabled;

uint VoxelGI_LoadRawWord(ByteAddressBuffer source, uint byteAddress)
{
    return source.Load(byteAddress & ~3u);
}

float VoxelGI_LoadVertexComponent(ByteAddressBuffer source, uint byteAddress, uint format)
{
    uint word = VoxelGI_LoadRawWord(source, byteAddress);
    uint byteShift = (byteAddress & 3u) * 8u;
    uint halfShift = (byteAddress & 2u) * 8u;
    if (format == 0u) return asfloat(source.Load(byteAddress));
    if (format == 1u) return f16tof32((word >> halfShift) & 0xffffu);
    if (format == 2u) return float((word >> byteShift) & 0xffu) / 255.0;
    if (format == 3u) return max(float((int)((word >> byteShift) << 24) >> 24) / 127.0, -1.0);
    if (format == 4u) return float((word >> halfShift) & 0xffffu) / 65535.0;
    if (format == 5u) return max(float((int)((word >> halfShift) << 16) >> 16) / 32767.0, -1.0);
    if (format == 6u) return float((word >> byteShift) & 0xffu);
    if (format == 7u) return float((int)((word >> byteShift) << 24) >> 24);
    if (format == 8u) return float((word >> halfShift) & 0xffffu);
    if (format == 9u) return float((int)((word >> halfShift) << 16) >> 16);
    if (format == 10u) return float(source.Load(byteAddress));
    return float(asint(source.Load(byteAddress)));
}

uint VoxelGI_VertexFormatSize(uint format)
{
    if (format == 0u || format == 10u || format == 11u) return 4u;
    if (format == 1u || format == 4u || format == 5u || format == 8u || format == 9u) return 2u;
    return 1u;
}

float4 VoxelGI_LoadVertexAttribute(ByteAddressBuffer source, uint vertexIndex, uint stride, uint offset,
    uint format, uint dimension)
{
    float4 result = 0.0;
    uint address = vertexIndex * stride + offset;
    uint componentSize = VoxelGI_VertexFormatSize(format);
    [unroll]
    for (uint component = 0u; component < 4u; component++)
    {
        if (component < dimension)
            result[component] = VoxelGI_LoadVertexComponent(source, address + component * componentSize, format);
    }
    return result;
}

uint VoxelGI_LoadIndex(uint logicalIndex)
{
    uint index = _VoxelGIIndexStart + logicalIndex;
    if (_VoxelGIIndexFormat != 0u)
        return _VoxelGIIndexBuffer.Load(index * 4u) + _VoxelGIBaseVertex;
    uint byteAddress = index * 2u;
    uint packed = VoxelGI_LoadRawWord(_VoxelGIIndexBuffer, byteAddress);
    return ((packed >> ((byteAddress & 2u) * 8u)) & 0xffffu) + _VoxelGIBaseVertex;
}

uint VoxelGI_EncodeAverage(float3 value, uint count)
{
    uint3 rgb = (uint3)round(saturate(value) * 255.0);
    return (rgb.x << 24u) | (rgb.y << 16u) | (rgb.z << 8u) | min(count, 255u);
}

float3 VoxelGI_DecodeAverage(uint packed)
{
    return float3((packed >> 24u) & 255u, (packed >> 16u) & 255u, (packed >> 8u) & 255u) / 255.0;
}

#define VOXEL_GI_ATOMIC_AVERAGE(NAME, BUFFER) \
void NAME(uint address, float3 value) \
{ \
    /* BUFFER 将 RGB 各量化为 8 位，并使用最低 8 位保存当前样本数。 */ \
    uint previous = BUFFER[address]; \
    [allow_uav_condition] \
    /* 使用 CAS 自旋循环，确保多个线程同时写入同一体素时不会丢失更新。 */ \
    for (;;) \
    { \
        uint count = previous & 255u; \
        uint nextCount = min(count + 1u, 255u); \
        float3 previousValue = VoxelGI_DecodeAverage(previous); \
        /* 未达到计数上限时计算增量平均值；达到 255 后改用固定权重的平滑更新，避免计数溢出。 */ \
        float3 average = count == 0u ? value : (count < 255u \
            ? (previousValue * count + value) / max((float)nextCount, 1.0) \
            : lerp(previousValue, value, 1.0 / 255.0)); \
        uint replacement = VoxelGI_EncodeAverage(average, nextCount); \
        uint observed; \
        /* 仅当 BUFFER[address] 仍等于 previous 时写入 replacement，并返回实际观察到的旧值。 */ \
        InterlockedCompareExchange(BUFFER[address], previous, replacement, observed); \
        /* observed == previous 表示 CAS 成功；否则基于其他线程的新值重新计算并重试。 */ \
        if (observed == previous) break; \
        previous = observed; \
    } \
}

VOXEL_GI_ATOMIC_AVERAGE(VoxelGI_AtomicAverageAlbedo, _VoxelGIAlbedoAccumulation)
VOXEL_GI_ATOMIC_AVERAGE(VoxelGI_AtomicAverageNormal, _VoxelGINormalAccumulation)

float VoxelGI_Edge(float2 a, float2 b, float2 p)
{
    return (p.x - a.x) * (b.y - a.y) - (p.y - a.y) * (b.x - a.x);
}

float2 VoxelGI_Project(float3 value, uint axis)
{
    if (axis == 0u) return value.yz;
    if (axis == 1u) return value.xz;
    return value.xy;
}

void VoxelGI_Store(int3 coordinate, float3 albedo, float3 normal, float opacity, float3 emissive)
{
    // 将三角形覆盖到的体素写入原子累积 Buffer：材质颜色/法线做平均，Opacity 取最大值，Emissive 求和。
    if (any(coordinate < 0) || any(coordinate >= _VoxelGIResolution)) return;
    uint address = coordinate.x + _VoxelGIResolution * (coordinate.y + _VoxelGIResolution * coordinate.z);
    if (_VoxelGIOpacityOnly != 0u)
    {
        uint ignored;
        InterlockedMax(_VoxelGIOpacityAccumulation[address], 65535u, ignored);
        return;
    }
    VoxelGI_AtomicAverageAlbedo(address, albedo);
    VoxelGI_AtomicAverageNormal(address, normal * 0.5 + 0.5);
    uint ignored;
    InterlockedMax(_VoxelGIOpacityAccumulation[address], (uint)round(saturate(opacity) * 65535.0), ignored);
    uint3 encodedEmission = (uint3)round(min(max(emissive, 0.0), 64.0) * 1024.0);
    InterlockedAdd(_VoxelGIEmissiveAccumulation[address].x, encodedEmission.x, ignored);
    InterlockedAdd(_VoxelGIEmissiveAccumulation[address].y, encodedEmission.y, ignored);
    InterlockedAdd(_VoxelGIEmissiveAccumulation[address].z, encodedEmission.z, ignored);
    InterlockedAdd(_VoxelGIEmissiveAccumulation[address].w, 1u, ignored);
}

[numthreads(256, 1, 1)]
void ClearVoxelAccumulation(uint3 id : SV_DispatchThreadID)
{
    // 每个线程清理一个线性体素地址，为本帧并发体素化准备初始状态。
    if (id.x >= _VoxelGIElementCount) return;
    _VoxelGIAlbedoAccumulation[id.x] = 0u;
    _VoxelGINormalAccumulation[id.x] = 0u;
    _VoxelGIEmissiveAccumulation[id.x] = 0u;
    _VoxelGIOpacityAccumulation[id.x] = 0u;
}

[numthreads(64, 1, 1)]
void VoxelizeMesh(uint3 id : SV_DispatchThreadID)
{
    // 每个线程负责一个三角形。线程索引按三个索引组成一个三角形，超出当前子网格范围的线程直接退出。
    uint firstIndex = id.x * 3u;
    if (firstIndex + 2u >= _VoxelGIIndexCount) return;

    // 读取三角形的三个顶点索引；VoxelGI_LoadIndex 同时处理 16/32 位索引和 BaseVertex 偏移。
    uint3 indices = uint3(VoxelGI_LoadIndex(firstIndex), VoxelGI_LoadIndex(firstIndex + 1u),
        VoxelGI_LoadIndex(firstIndex + 2u));
    float3 objectPosition[3];
    float3 voxelPosition[3];
    float3 worldNormal[3];
    float2 uv[3];
    [unroll]
    for (uint vertex = 0u; vertex < 3u; vertex++)
    {
        // 从 Raw Vertex Buffer 读取对象空间位置，再依次变换到世界空间和体素网格空间。
        objectPosition[vertex] = VoxelGI_LoadVertexAttribute(_VoxelGIPositionBuffer, indices[vertex],
            _VoxelGIPositionStride, _VoxelGIPositionOffset, _VoxelGIPositionFormat, _VoxelGIPositionDimension).xyz;
        float3 worldPosition = mul(_VoxelGIObjectToWorld, float4(objectPosition[vertex], 1.0)).xyz;
        voxelPosition[vertex] = mul(_VoxelGIWorldToVoxel, float4(worldPosition, 1.0)).xyz;

        // 法线使用对象变换的逆转置矩阵转换到世界空间；没有法线属性时暂存零值，后面使用面法线补齐。
        float3 sourceNormal = _VoxelGIHasNormals != 0u
            ? VoxelGI_LoadVertexAttribute(_VoxelGINormalBuffer, indices[vertex], _VoxelGINormalStride,
                _VoxelGINormalOffset, _VoxelGINormalFormat, _VoxelGINormalDimension).xyz
            : 0.0;
        worldNormal[vertex] = normalize(mul((float3x3)_VoxelGINormalToWorld, sourceNormal));

        // 读取 UV。没有 UV 的网格使用零坐标，材质采样将退化为纹理的固定位置。
        uv[vertex] = _VoxelGIHasUV != 0u
            ? VoxelGI_LoadVertexAttribute(_VoxelGIUVBuffer, indices[vertex], _VoxelGIUVStride,
                _VoxelGIUVOffset, _VoxelGIUVFormat, _VoxelGIUVDimension).xy
            : 0.0;
    }

    // 计算体素空间面法线，并选择法线绝对值最大的轴作为投影轴，减少三角形投影面积退化。
    float3 faceNormal = cross(voxelPosition[1] - voxelPosition[0], voxelPosition[2] - voxelPosition[0]);
    float3 absNormal = abs(faceNormal);
    uint axis = absNormal.x > absNormal.y ? (absNormal.x > absNormal.z ? 0u : 2u)
                                          : (absNormal.y > absNormal.z ? 1u : 2u);
    float2 projected[3] = { VoxelGI_Project(voxelPosition[0], axis), VoxelGI_Project(voxelPosition[1], axis),
        VoxelGI_Project(voxelPosition[2], axis) };
    float signedArea = VoxelGI_Edge(projected[0], projected[1], projected[2]);
    // 投影后三点共线时没有可覆盖的像素，避免后续重心坐标除零。
    if (abs(signedArea) < 1e-6) return;

    // 根据保守光栅化开关扩张二维包围盒和边函数容差，避免细小/斜向三角形漏掉相邻体素。
    float expansion = _VoxelGIConservativeRasterization != 0u ? max(_VoxelGIConservativeScale, 0.5) : 0.0;
    int2 minimum = clamp((int2)floor(min(projected[0], min(projected[1], projected[2])) - expansion),
        0, _VoxelGIResolution - 1);
    int2 maximum = clamp((int2)floor(max(projected[0], max(projected[1], projected[2])) + expansion),
        0, _VoxelGIResolution - 1);
    float orientation = signedArea >= 0.0 ? 1.0 : -1.0;
    float3 edgeTolerance = expansion * float3(length(projected[2] - projected[1]),
        length(projected[0] - projected[2]), length(projected[1] - projected[0]));

    [loop]
    for (int y = minimum.y; y <= maximum.y; y++)
    {
        [loop]
        for (int x = minimum.x; x <= maximum.x; x++)
        {
            // 以体素中心作为采样点，通过三个边函数判断其是否落在三角形（含保守扩张区域）内。
            float2 samplePosition = float2(x, y) + 0.5;
            float3 edge = float3(VoxelGI_Edge(projected[1], projected[2], samplePosition),
                VoxelGI_Edge(projected[2], projected[0], samplePosition),
                VoxelGI_Edge(projected[0], projected[1], samplePosition)) * orientation;
            if (any(edge < -edgeTolerance)) continue;
            float3 barycentric = max(edge, 0.0);
            barycentric /= max(barycentric.x + barycentric.y + barycentric.z, 1e-6);

            // 使用二维投影得到的重心坐标插值第三维位置，恢复当前覆盖体素的完整三维坐标。
            float3 voxel = voxelPosition[0] * barycentric.x + voxelPosition[1] * barycentric.y +
                           voxelPosition[2] * barycentric.z;
            int3 coordinate = axis == 0u ? int3((int)floor(voxel.x), x, y)
                            : axis == 1u ? int3(x, (int)floor(voxel.y), y)
                                         : int3(x, y, (int)floor(voxel.z));

            // 用同一组重心坐标插值 UV 和世界法线，并应用材质的纹理缩放/偏移。
            float2 interpolatedUV = uv[0] * barycentric.x + uv[1] * barycentric.y + uv[2] * barycentric.z;
            float2 baseUV = interpolatedUV * _VoxelGIBaseMapST.xy + _VoxelGIBaseMapST.zw;
            float2 emissionUV = interpolatedUV * _VoxelGIEmissionMapST.xy + _VoxelGIEmissionMapST.zw;
            float4 surface = _VoxelGIBaseMap.SampleLevel(sampler_VoxelGIBaseMap, baseUV, 0.0) * _VoxelGIBaseColor;
            // Alpha Clip 材质的透明区域不写入体素；否则将其表面颜色和发光写入累积数据。
            if (_VoxelGIAlphaClip != 0u && surface.a < _VoxelGIAlphaCutoff) continue;
            // 与 URP/Lit 的 _EMISSION 变体保持一致，关闭时不消费材质中残留的颜色或贴图属性。
            float3 emissive = _VoxelGIEmissionEnabled != 0u
                ? _VoxelGIEmissionMap.SampleLevel(sampler_VoxelGIEmissionMap, emissionUV, 0.0).rgb *
                  _VoxelGIEmissionColor.rgb
                : 0.0;
            float3 normal = worldNormal[0] * barycentric.x + worldNormal[1] * barycentric.y +
                            worldNormal[2] * barycentric.z;
            if (_VoxelGIHasNormals == 0u || dot(normal, normal) < 1e-6)
                // 缺少顶点法线时，使用对象空间三角形面法线作为兜底。
                normal = normalize(mul((float3x3)_VoxelGINormalToWorld,
                    normalize(cross(objectPosition[1] - objectPosition[0], objectPosition[2] - objectPosition[0]))));
            else
                normal = normalize(normal);
            VoxelGI_Store(coordinate, surface.rgb, normal, surface.a, emissive);
        }
    }
}

[numthreads(8, 8, 8)]
void ResolveVoxelAccumulation(uint3 id : SV_DispatchThreadID)
{
    // 将并发累积的打包整数解码为归一化材质、法线、发光和占用率 3D 纹理。
    if (any(id >= (uint)_VoxelGIResolution)) return;
    uint address = id.x + _VoxelGIResolution * (id.y + _VoxelGIResolution * id.z);
    uint albedoPacked = _VoxelGIAlbedoAccumulation[address];
    uint normalPacked = _VoxelGINormalAccumulation[address];
    uint4 emissionPacked = _VoxelGIEmissiveAccumulation[address];
    bool hasSurface = (albedoPacked & 255u) != 0u;
    float occupied = _VoxelGIOpacityAccumulation[address] / 65535.0;
    float emissionCount = max((float)emissionPacked.w, 1.0);
    float3 emissive = float3(emissionPacked.xyz) / (1024.0 * emissionCount);
    float3 albedo = hasSurface ? VoxelGI_DecodeAverage(albedoPacked) : 0.0;
    float3 normal = hasSurface ? VoxelGI_DecodeAverage(normalPacked) : float3(0.5, 1.0, 0.5);
    _VoxelGIAlbedoOutput[id] = float4(albedo, occupied);
    _VoxelGINormalOutput[id] = float4(normal, occupied);
    _VoxelGIEmissiveOutput[id] = float4(emissive, occupied);
}

#endif
