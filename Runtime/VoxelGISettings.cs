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
        [Header("General")]
        public BoolParameter enable = new BoolParameter(false);

        [Header("Voxelization")]
        public NoInterpClampedIntParameter shadowResolution = new NoInterpClampedIntParameter(512, 64, 4096);
        public NoInterpClampedIntParameter voxelResolution = new NoInterpClampedIntParameter(128, 16, 256);
        public BoolParameter conservativeRasterization = new BoolParameter(false);
        public ClampedFloatParameter conservativeScale = new ClampedFloatParameter(1.5f, 0f, 3f);
        public LayerMaskParameter layerMask = new LayerMaskParameter(~0);
        public VoxelGIUpdateModeParameter updateMode = new VoxelGIUpdateModeParameter(VoxelGIUpdateMode.EveryFrame);

        [Header("Direct Lighting")]
        public ClampedFloatParameter lightIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        public ClampedFloatParameter emissiveIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        public MinFloatParameter shadowSunBias = new MinFloatParameter(1f, 0f);
        public MinFloatParameter shadowNormalBias = new MinFloatParameter(1f, 0f);

        [Header("Indirect Lighting")]
        public BoolParameter secondBounce = new BoolParameter(true);
        public VoxelGIConeQualityParameter indirectQuality = new VoxelGIConeQualityParameter(VoxelGIConeQuality.Medium);
        public ClampedIntParameter indirectMaxSteps = new ClampedIntParameter(8, 1, 32);
        public ClampedFloatParameter indirectAlphaAttenuation = new ClampedFloatParameter(4f, 1f, 10f);
        public ClampedFloatParameter indirectIntensity = new ClampedFloatParameter(1f, 0f, 10f);
        public ClampedFloatParameter indirectFirstStep = new ClampedFloatParameter(1f, 0.5f, 3f);
        public ClampedFloatParameter indirectStepScale = new ClampedFloatParameter(1f, 1f, 3f);
        public ClampedFloatParameter indirectConeAngle = new ClampedFloatParameter(120f, 20f, 150f);
        public ClampedIntParameter indirectMinMipLevel = new ClampedIntParameter(0, 0, 5);

        [Header("Screen Cone Tracing")]
        public VoxelGIConeQualityParameter screenQuality = new VoxelGIConeQualityParameter(VoxelGIConeQuality.Medium);
        public ClampedIntParameter screenMaxSteps = new ClampedIntParameter(16, 1, 32);
        public ClampedFloatParameter screenAlphaAttenuation = new ClampedFloatParameter(8f, 1f, 10f);
        public ClampedFloatParameter screenIntensity = new ClampedFloatParameter(1.25f, 0f, 10f);
        public ClampedFloatParameter screenFirstStep = new ClampedFloatParameter(1f, 0.5f, 3f);
        public ClampedFloatParameter screenStepScale = new ClampedFloatParameter(1f, 1f, 3f);
        public ClampedFloatParameter screenConeAngle = new ClampedFloatParameter(120f, 20f, 150f);

        [Header("Temporal Filter")]
        public BoolParameter temporalFilter = new BoolParameter(false);
        public TextureParameter blueNoise = new TextureParameter(null);
        public ClampedFloatParameter temporalCurrentFrameWeight = new ClampedFloatParameter(0.5f, 0f, 1f);
        public MinFloatParameter temporalClampScale = new MinFloatParameter(1f, 0f);
        public Vector2Parameter blueNoiseScale = new Vector2Parameter(Vector2.one);
        public VoxelGIJitterSequenceParameter jitterSequence =
            new VoxelGIJitterSequenceParameter(VoxelGIJitterSequence.GoldenRatio);
        public ClampedIntParameter haltonLength = new ClampedIntParameter(4, 2, 64);

        [Header("Bilateral Filter")]
        public BoolParameter bilateralFilter = new BoolParameter(true);
        public ClampedFloatParameter bilateralRadius = new ClampedFloatParameter(1f, 0f, 10f);
        public MinFloatParameter depthThresholdLower = new MinFloatParameter(0.1f, 0f);
        public MinFloatParameter depthThresholdUpper = new MinFloatParameter(0.2f, 0f);
        public ClampedFloatParameter normalThresholdLower = new ClampedFloatParameter(0.939f, 0f, 1f);
        public ClampedFloatParameter normalThresholdUpper = new ClampedFloatParameter(0.948f, 0f, 1f);

        [Header("Debug")]
        public VoxelGIDebugModeParameter debugMode = new VoxelGIDebugModeParameter(VoxelGIDebugMode.Disabled);
        public ClampedIntParameter debugMipLevel = new ClampedIntParameter(0, 0, 8);
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
                    screenFirstStep.value, screenStepScale.value, screenConeAngle.value),
                new VoxelGISettingsSnapshot.TemporalSettings(
                    temporalFilter.value, blueNoise.value, temporalCurrentFrameWeight.value, temporalClampScale.value,
                    blueNoiseScale.value, jitterSequence.value, haltonLength.value),
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
