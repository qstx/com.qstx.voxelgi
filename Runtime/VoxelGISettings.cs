using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace QSTX.VoxelGI
{
    [Serializable]
    public sealed class VoxelGIConeQualityParameter : VolumeParameter<VoxelGIConeQuality>
    {
        public VoxelGIConeQualityParameter(VoxelGIConeQuality value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class VoxelGIJitterSequenceParameter : VolumeParameter<VoxelGIJitterSequence>
    {
        public VoxelGIJitterSequenceParameter(VoxelGIJitterSequence value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class VoxelGIUpdateModeParameter : VolumeParameter<VoxelGIUpdateMode>
    {
        public VoxelGIUpdateModeParameter(VoxelGIUpdateMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class VoxelGIDebugModeParameter : VolumeParameter<VoxelGIDebugMode>
    {
        public VoxelGIDebugModeParameter(VoxelGIDebugMode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("QSTX/Voxel GI")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class VoxelGISettings : VolumeComponent, IPostProcessComponent
    {
        internal const float MinTemporalCurrentFrameWeight = 0.02f;

        [Header("General")]
        [Tooltip("是否启用 Voxel GI 效果。关闭后不会插入 Voxel GI 的渲染流程。")]
        public BoolParameter enable = new BoolParameter(false);

        [Header("Voxelization")]
        [Tooltip("方向光 Shadow Map 的分辨率。数值越高阴影越精细，但显存和绘制开销也越大。")]
        public NoInterpClampedIntParameter shadowResolution = new NoInterpClampedIntParameter(512, 64, 4096);
        [Tooltip("体素网格的边长分辨率。分辨率越高，空间细节越好，但体素化和光照开销呈立方增长。")]
        public NoInterpClampedIntParameter voxelResolution = new NoInterpClampedIntParameter(128, 16, 256);
        [Tooltip("是否启用保守体素化，以扩大三角形覆盖范围并减少细小几何体漏体素。")]
        public BoolParameter conservativeRasterization = new BoolParameter(false);
        [Tooltip("保守体素化的扩张尺度。数值越大越不容易漏体素，但可能增加表面厚度和重叠。")]
        public ClampedFloatParameter conservativeScale = new ClampedFloatParameter(1.5f, 0f, 3f);
        [Tooltip("允许参与体素化的 Layer。未包含在此遮罩中的 Renderer 会被忽略。")]
        public LayerMaskParameter layerMask = new LayerMaskParameter(~0);
        [Tooltip("体素数据的更新策略：每帧更新、检测变化后更新，或仅在手动请求时更新。")]
        public VoxelGIUpdateModeParameter updateMode = new VoxelGIUpdateModeParameter(VoxelGIUpdateMode.EveryFrame);

        [Header("Direct Lighting")]
        [Tooltip("方向光直接照明强度倍率。")]
        public ClampedFloatParameter lightIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("体素中 Emissive 发光对直接辐射贡献的强度倍率。")]
        public ClampedFloatParameter emissiveIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("沿光照方向施加的 Shadow Bias，按体素尺寸缩放，用于减少阴影痤疮。")]
        public MinFloatParameter shadowSunBias = new MinFloatParameter(1f, 0f);
        [Tooltip("沿表面法线施加的 Shadow Bias，按体素尺寸缩放，用于减少阴影痤疮。")]
        public MinFloatParameter shadowNormalBias = new MinFloatParameter(1f, 0f);

        [Header("Indirect Lighting")]
        [Tooltip("是否计算一次二次间接反弹，并生成 Final Radiance。")]
        public BoolParameter secondBounce = new BoolParameter(true);
        [Tooltip("体素 Cone Tracing 的采样质量。质量越高，Cone 数量越多，噪声越低但开销越高。")]
        public VoxelGIConeQualityParameter indirectQuality = new VoxelGIConeQualityParameter(VoxelGIConeQuality.Medium);
        [Tooltip("每个间接光 Cone 的最大追踪步数。")]
        public ClampedIntParameter indirectMaxSteps = new ClampedIntParameter(8, 1, 32);
        [Tooltip("间接光 Cone 已累积辐射后的透明度衰减速度。")]
        public ClampedFloatParameter indirectAlphaAttenuation = new ClampedFloatParameter(4f, 1f, 10f);
        [Tooltip("间接反弹贡献的强度倍率。")]
        public ClampedFloatParameter indirectIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("间接光 Cone 的首个采样距离，以体素尺寸为单位。")]
        public ClampedFloatParameter indirectFirstStep = new ClampedFloatParameter(1f, 0.5f, 3f);
        [Tooltip("间接光 Cone 每一步的距离增长倍率。")]
        public ClampedFloatParameter indirectStepScale = new ClampedFloatParameter(1f, 1f, 3f);
        [Tooltip("间接光 Cone 的张角，决定每次采样覆盖的空间范围。")]
        public ClampedFloatParameter indirectConeAngle = new ClampedFloatParameter(120f, 20f, 150f);
        [Tooltip("间接光采样允许使用的最小体素 Mip 层级。数值越高越平滑，但细节越少。")]
        public ClampedIntParameter indirectMinMipLevel = new ClampedIntParameter(0, 0, 5);

        [Header("Screen Cone Tracing")]
        [Tooltip("屏幕空间 Cone Tracing 的采样质量。Temporal 开启时会使用单 Cone 并依赖时序累积。")]
        public VoxelGIConeQualityParameter screenQuality = new VoxelGIConeQualityParameter(VoxelGIConeQuality.Medium);
        [Tooltip("每个屏幕像素的最大 Cone 追踪步数。")]
        public ClampedIntParameter screenMaxSteps = new ClampedIntParameter(16, 1, 32);
        [Tooltip("屏幕空间 Cone 已累积辐射后的透明度衰减速度。")]
        public ClampedFloatParameter screenAlphaAttenuation = new ClampedFloatParameter(8f, 1f, 10f);
        [Tooltip("屏幕空间间接光的强度倍率。")]
        public ClampedFloatParameter screenIntensity = new ClampedFloatParameter(1.25f, 0f, 10f);
        [Tooltip("屏幕空间 Cone 的首个采样距离，以体素尺寸为单位。")]
        public ClampedFloatParameter screenFirstStep = new ClampedFloatParameter(1f, 0.5f, 3f);
        [Tooltip("屏幕空间 Cone 每一步的距离增长倍率。")]
        public ClampedFloatParameter screenStepScale = new ClampedFloatParameter(1f, 1f, 3f);
        [Tooltip("屏幕空间 Cone 的张角，决定每次采样覆盖的空间范围。")]
        public ClampedFloatParameter screenConeAngle = new ClampedFloatParameter(120f, 20f, 150f);
        [Tooltip("屏幕 Cone Tracing 使用的蓝噪声纹理。未指定时使用程序化 Hash 噪声。")]
        public TextureParameter blueNoise = new TextureParameter(null);
        [Tooltip("蓝噪声纹理在屏幕空间的缩放。")]
        public Vector2Parameter blueNoiseScale = new Vector2Parameter(Vector2.one);

        [Header("Temporal Filter")]
        [Tooltip("是否启用 Temporal 时序滤波。需要有效的 Motion Vector 才能正确重投影 History。")]
        public BoolParameter temporalFilter = new BoolParameter(false);
        [Tooltip("当前帧对 Temporal 结果的贡献比例。数值越低越平滑，但响应变化越慢；不要设为零，否则 History 会冻结。")]
        public ClampedFloatParameter temporalCurrentFrameWeight =
            new ClampedFloatParameter(0.5f, MinTemporalCurrentFrameWeight, 1f);
        [Tooltip("History 邻域裁剪范围的倍率。数值越高越不容易闪烁，但可能保留更多异常值。")]
        public MinFloatParameter temporalClampScale = new MinFloatParameter(1f, 0f);
        [Tooltip("Temporal 抖动序列类型，可选择黄金比例序列或 Halton 序列。")]
        public VoxelGIJitterSequenceParameter jitterSequence =
            new VoxelGIJitterSequenceParameter(VoxelGIJitterSequence.GoldenRatio);
        [Tooltip("Halton 抖动序列的循环长度。数值越大，跨帧采样方向重复得越慢。")]
        public ClampedIntParameter haltonLength = new ClampedIntParameter(4, 2, 64);

        [Header("Bilateral Filter")]
        [Tooltip("是否启用屏幕空间 Bilateral 边缘保持滤波。")]
        public BoolParameter bilateralFilter = new BoolParameter(true);
        [Tooltip("Bilateral 邻域采样半径，以屏幕像素为单位。")]
        public ClampedFloatParameter bilateralRadius = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("深度差异小于该值时完全接受邻域样本；超过该值后开始衰减。")]
        public MinFloatParameter depthThresholdLower = new MinFloatParameter(0.1f, 0f);
        [Tooltip("深度差异达到该值时完全拒绝邻域样本。")]
        public MinFloatParameter depthThresholdUpper = new MinFloatParameter(0.2f, 0f);
        [Tooltip("法线相似度低于该值时拒绝邻域样本；高于该值后开始接受。")]
        public ClampedFloatParameter normalThresholdLower = new ClampedFloatParameter(0.939f, 0f, 1f);
        [Tooltip("法线相似度达到该值时完全接受邻域样本。")]
        public ClampedFloatParameter normalThresholdUpper = new ClampedFloatParameter(0.948f, 0f, 1f);

        [Header("Debug")]
        [Tooltip("选择 VoxelGI 的调试输出。Temporal 模式显示时序滤波结果，ScreenTrace 模式显示滤波前结果。")]
        public VoxelGIDebugModeParameter debugMode = new VoxelGIDebugModeParameter(VoxelGIDebugMode.Disabled);
        [Tooltip("体素 Radiance 调试显示使用的 Mip 层级。")]
        public ClampedIntParameter debugMipLevel = new ClampedIntParameter(0, 0, 8);
        [Tooltip("Debug Ray March 的世界空间步长。数值越小越精细，但调试开销越高。")]
        public MinFloatParameter debugRayStep = new MinFloatParameter(0.1f, 0.001f);

        // Volume 栈中只有显式启用时才会插入 VoxelGI Render Graph 流程。
        public bool IsActive() => enable.value;

        internal VoxelGISettingsSnapshot Resolve()
        {
            // 将 Volume 参数归一化为本帧不可变快照，统一处理分辨率、阈值和各阶段运行时设置。
            int resolvedShadow = NormalizePowerOfTwo(shadowResolution.value, 64, 4096);
            int resolvedVoxel = NormalizePowerOfTwo(voxelResolution.value, 16, 256);
            float depthUpper = Mathf.Max(depthThresholdLower.value + 1e-5f, depthThresholdUpper.value);
            float normalUpper = Mathf.Max(normalThresholdLower.value + 1e-5f, normalThresholdUpper.value);
            // 旧 Volume Profile 可能仍序列化了 0；运行时再次约束，避免 History 被永久冻结。
            float resolvedTemporalCurrentFrameWeight = Mathf.Clamp(
                temporalCurrentFrameWeight.value, MinTemporalCurrentFrameWeight, 1f);

            return new VoxelGISettingsSnapshot(
                new VoxelGISettingsSnapshot.VoxelizationSettings(
                    resolvedShadow, resolvedVoxel, conservativeRasterization.value, conservativeScale.value,
                    layerMask.value, updateMode.value),
                new VoxelGISettingsSnapshot.DirectLightingSettings(
                    lightIntensity.value, emissiveIntensity.value, shadowSunBias.value, shadowNormalBias.value),
                new VoxelGISettingsSnapshot.IndirectLightingSettings(
                    secondBounce.value, indirectQuality.value, indirectMaxSteps.value, indirectAlphaAttenuation.value,
                    indirectIntensity.value, indirectFirstStep.value, indirectStepScale.value,
                    indirectConeAngle.value, indirectMinMipLevel.value),
                new VoxelGISettingsSnapshot.ScreenTracingSettings(
                    screenQuality.value, screenMaxSteps.value, screenAlphaAttenuation.value, screenIntensity.value,
                    screenFirstStep.value, screenStepScale.value, screenConeAngle.value,
                    blueNoise.value, blueNoiseScale.value),
                new VoxelGISettingsSnapshot.TemporalSettings(
                    temporalFilter.value, resolvedTemporalCurrentFrameWeight, temporalClampScale.value,
                    jitterSequence.value, haltonLength.value),
                new VoxelGISettingsSnapshot.BilateralSettings(
                    bilateralFilter.value, bilateralRadius.value,
                    new Vector2(depthThresholdLower.value, depthUpper),
                    new Vector2(normalThresholdLower.value, normalUpper)),
                new VoxelGISettingsSnapshot.DebugSettings(debugMode.value, debugMipLevel.value, debugRayStep.value));
        }

        internal static int NormalizePowerOfTwo(int value, int min, int max)
        {
            return Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), min, max);
        }
    }
}
