using UnityEngine;

namespace QSTX.VoxelGI
{
    internal static class VoxelGIShaderIDs
    {
        // 体素空间与分辨率参数。
        public static readonly int WorldToVoxel = Shader.PropertyToID("_VoxelGIWorldToVoxel");
        public static readonly int VoxelToWorld = Shader.PropertyToID("_VoxelGIVoxelToWorld");
        public static readonly int Resolution = Shader.PropertyToID("_VoxelGIResolution");
        public static readonly int VoxelSize = Shader.PropertyToID("_VoxelGISize");
        public static readonly int ScreenSize = Shader.PropertyToID("_VoxelGIScreenSize");

        // 体素材质、辐射度和方向光阴影纹理。
        public static readonly int AlbedoOpacity = Shader.PropertyToID("_VoxelGIAlbedoOpacity");
        public static readonly int Normal = Shader.PropertyToID("_VoxelGINormal");
        public static readonly int Emissive = Shader.PropertyToID("_VoxelGIEmissive");
        public static readonly int DirectRadiance = Shader.PropertyToID("_VoxelGIDirectRadiance");
        public static readonly int FinalRadiance = Shader.PropertyToID("_VoxelGIFinalRadiance");
        public static readonly int ShadowMap = Shader.PropertyToID("_VoxelGIShadowMap");
        public static readonly int WorldToShadow = Shader.PropertyToID("_VoxelGIWorldToShadow");

        // 屏幕追踪、Temporal/Composite 输入和调试参数。
        public static readonly int CurrentIrradiance = Shader.PropertyToID("_VoxelGICurrentIrradiance");
        public static readonly int HistoryIrradiance = Shader.PropertyToID("_VoxelGIHistoryIrradiance");
        public static readonly int IndirectIrradiance = Shader.PropertyToID("_VoxelGIIndirectIrradiance");
        public static readonly int SceneColor = Shader.PropertyToID("_VoxelGISceneColor");
        public static readonly int DebugMode = Shader.PropertyToID("_VoxelGIDebugMode");
        public static readonly int DebugMipLevel = Shader.PropertyToID("_VoxelGIDebugMipLevel");
        public static readonly int DebugRayStep = Shader.PropertyToID("_VoxelGIDebugRayStep");
    }

    internal static class VoxelGIKernelNames
    {
        // Compute Shader 中各阶段 Kernel 的固定名称。
        public const string Clear = "ClearVoxelAccumulation";
        public const string Voxelize = "VoxelizeMesh";
        public const string Resolve = "ResolveVoxelAccumulation";
        public const string DirectLighting = "VoxelDirectLighting";
        public const string IndirectLighting = "VoxelIndirectLighting";
        public const string GenerateMip = "MipmapGeneration";
        public const string CopyMip = "CopyTexture3D";
        public const string Bilateral = "BilateralFiltering";
    }

    internal static class VoxelGIShaderPassNames
    {
        // 全屏 Shader 中各个材质 Pass 的固定名称。
        public const string ScreenTrace = "ScreenTrace";
        public const string Temporal = "TemporalFilter";
        public const string Composite = "Composite";
        public const string Debug = "DebugVisualization";
    }
}
