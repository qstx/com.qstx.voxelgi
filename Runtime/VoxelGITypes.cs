using UnityEngine;

namespace QSTX.VoxelGI
{
    public enum VoxelGIConeQuality
    {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public enum VoxelGIJitterSequence
    {
        GoldenRatio = 0,
        Halton = 1
    }

    public enum VoxelGIUpdateMode
    {
        EveryFrame = 0,
        OnChange = 1,
        Manual = 2
    }

    public enum VoxelGIDebugMode
    {
        Disabled = 0,
        Albedo = 1,
        Normal = 2,
        Emissive = 3,
        Shadow = 4,
        DirectRadiance = 5,
        FinalRadiance = 6,
        ScreenTrace = 7,
        Temporal = 8,
        Bilateral = 9
    }

    internal readonly struct VoxelGISettingsSnapshot
    {
        public readonly VoxelizationSettings Voxelization;
        public readonly DirectLightingSettings DirectLighting;
        public readonly IndirectLightingSettings IndirectLighting;
        public readonly ScreenTracingSettings ScreenTracing;
        public readonly TemporalSettings Temporal;
        public readonly BilateralSettings Bilateral;
        public readonly DebugSettings Debug;

        public VoxelGISettingsSnapshot(VoxelizationSettings voxelization, DirectLightingSettings directLighting,
            IndirectLightingSettings indirectLighting, ScreenTracingSettings screenTracing,
            TemporalSettings temporal, BilateralSettings bilateral, DebugSettings debug)
        {
            Voxelization = voxelization;
            DirectLighting = directLighting;
            IndirectLighting = indirectLighting;
            ScreenTracing = screenTracing;
            Temporal = temporal;
            Bilateral = bilateral;
            Debug = debug;
        }

        internal readonly struct VoxelizationSettings
        {
            public readonly int ShadowResolution;
            public readonly int Resolution;
            public readonly bool ConservativeRasterization;
            public readonly float ConservativeScale;
            public readonly LayerMask LayerMask;
            public readonly VoxelGIUpdateMode UpdateMode;

            public VoxelizationSettings(int shadowResolution, int resolution, bool conservativeRasterization,
                float conservativeScale, LayerMask layerMask, VoxelGIUpdateMode updateMode)
            {
                ShadowResolution = shadowResolution;
                Resolution = resolution;
                ConservativeRasterization = conservativeRasterization;
                ConservativeScale = conservativeScale;
                LayerMask = layerMask;
                UpdateMode = updateMode;
            }
        }

        internal readonly struct DirectLightingSettings
        {
            public readonly float LightIntensity;
            public readonly float EmissiveIntensity;
            public readonly float ShadowSunBias;
            public readonly float ShadowNormalBias;

            public DirectLightingSettings(float lightIntensity, float emissiveIntensity, float shadowSunBias,
                float shadowNormalBias)
            {
                LightIntensity = lightIntensity;
                EmissiveIntensity = emissiveIntensity;
                ShadowSunBias = shadowSunBias;
                ShadowNormalBias = shadowNormalBias;
            }
        }

        internal readonly struct IndirectLightingSettings
        {
            public readonly bool SecondBounce;
            public readonly VoxelGIConeQuality Quality;
            public readonly int MaxSteps;
            public readonly float AlphaAttenuation;
            public readonly float Intensity;
            public readonly float FirstStep;
            public readonly float StepScale;
            public readonly float ConeAngle;
            public readonly int MinMipLevel;

            public IndirectLightingSettings(bool secondBounce, VoxelGIConeQuality quality, int maxSteps,
                float alphaAttenuation, float intensity, float firstStep, float stepScale, float coneAngle,
                int minMipLevel)
            {
                SecondBounce = secondBounce;
                Quality = quality;
                MaxSteps = maxSteps;
                AlphaAttenuation = alphaAttenuation;
                Intensity = intensity;
                FirstStep = firstStep;
                StepScale = stepScale;
                ConeAngle = coneAngle;
                MinMipLevel = minMipLevel;
            }
        }

        internal readonly struct ScreenTracingSettings
        {
            public readonly VoxelGIConeQuality Quality;
            public readonly int MaxSteps;
            public readonly float AlphaAttenuation;
            public readonly float Intensity;
            public readonly float FirstStep;
            public readonly float StepScale;
            public readonly float ConeAngle;

            public ScreenTracingSettings(VoxelGIConeQuality quality, int maxSteps, float alphaAttenuation,
                float intensity, float firstStep, float stepScale, float coneAngle)
            {
                Quality = quality;
                MaxSteps = maxSteps;
                AlphaAttenuation = alphaAttenuation;
                Intensity = intensity;
                FirstStep = firstStep;
                StepScale = stepScale;
                ConeAngle = coneAngle;
            }
        }

        internal readonly struct TemporalSettings
        {
            public readonly bool Enabled;
            public readonly Texture BlueNoise;
            public readonly float CurrentFrameWeight;
            public readonly float ClampScale;
            public readonly Vector2 BlueNoiseScale;
            public readonly VoxelGIJitterSequence JitterSequence;
            public readonly int HaltonLength;

            public TemporalSettings(bool enabled, Texture blueNoise, float currentFrameWeight, float clampScale,
                Vector2 blueNoiseScale, VoxelGIJitterSequence jitterSequence, int haltonLength)
            {
                Enabled = enabled;
                BlueNoise = blueNoise;
                CurrentFrameWeight = currentFrameWeight;
                ClampScale = clampScale;
                BlueNoiseScale = blueNoiseScale;
                JitterSequence = jitterSequence;
                HaltonLength = haltonLength;
            }
        }

        internal readonly struct BilateralSettings
        {
            public readonly bool Enabled;
            public readonly float Radius;
            public readonly Vector2 DepthThreshold;
            public readonly Vector2 NormalThreshold;

            public BilateralSettings(bool enabled, float radius, Vector2 depthThreshold, Vector2 normalThreshold)
            {
                Enabled = enabled;
                Radius = radius;
                DepthThreshold = depthThreshold;
                NormalThreshold = normalThreshold;
            }
        }

        internal readonly struct DebugSettings
        {
            public readonly VoxelGIDebugMode Mode;
            public readonly int MipLevel;
            public readonly float RayStep;

            public DebugSettings(VoxelGIDebugMode mode, int mipLevel, float rayStep)
            {
                Mode = mode;
                MipLevel = mipLevel;
                RayStep = rayStep;
            }
        }
    }
}
