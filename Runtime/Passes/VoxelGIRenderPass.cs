using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace QSTX.VoxelGI
{
    internal sealed partial class VoxelGIRenderPass : ScriptableRenderPass
    {
        readonly VoxelGIRendererFeature m_Feature;

        public VoxelGIRenderPass(VoxelGIRendererFeature feature)
        {
            m_Feature = feature;
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal |
                           ScriptableRenderPassInput.Motion);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            VoxelGIRuntimeResources resources = m_Feature.RuntimeResources;
            if (resources == null)
                return;

            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera camera = cameraData.camera;
            VoxelGISettings volumeSettings = VolumeManager.instance.stack?.GetComponent<VoxelGISettings>();
            if (volumeSettings == null || !volumeSettings.IsActive() ||
                !VoxelGIVolume.TryGetActive(camera, out VoxelGIVolume volume, out Bounds bounds))
                return;

            VoxelGISettingsSnapshot settings = volumeSettings.Resolve();
            VoxelGICameraContext cameraContext = resources.GetContext(camera);
            bool resourcesChanged = cameraContext.EnsureVoxelResources(
                settings.Voxelization.Resolution, settings.Voxelization.ShadowResolution);
            if (resourcesChanged)
                cameraContext.InvalidateHistory();

            IReadOnlyList<VoxelGIRendererEntry> renderers =
                VoxelGIRendererRegistry.GetEntries(m_Feature.RendererRescanInterval);
            Light directionalLight = FindDirectionalLight();
            VoxelGIFrameMatrices matrices = VoxelGIFrameMatrices.Create(bounds, settings.Voxelization.Resolution,
                directionalLight);
            var frame = new VoxelGIFrame(
                cameraData, settings, volume, bounds, directionalLight, matrices, renderers,
                VoxelGIRendererRegistry.Version, cameraContext, resources);

            bool voxelize = cameraContext.ShouldVoxelize(settings, bounds, volume, directionalLight,
                renderers, frame.RegistryVersion);
            bool relight = voxelize || cameraContext.ShouldRelight(settings, directionalLight);
            if (voxelize)
            {
                if (directionalLight != null)
                    RecordShadow(renderGraph, frame);
                RecordVoxelization(renderGraph, frame);
                cameraContext.MarkVoxelized(settings, bounds, volume, directionalLight, renderers,
                    frame.RegistryVersion);
                cameraContext.InvalidateHistory();
            }
            else if (relight && directionalLight != null && cameraContext.ShouldUpdateShadow(directionalLight))
            {
                RecordShadow(renderGraph, frame);
            }

            if (relight)
            {
                RecordLighting(renderGraph, frame);
                cameraContext.MarkRelit(settings, directionalLight);
                cameraContext.InvalidateHistory();
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            VoxelGIDebugMode debugMode = settings.Debug.Mode;
            if (debugMode is >= VoxelGIDebugMode.Albedo and <= VoxelGIDebugMode.FinalRadiance)
            {
                RecordDebug(renderGraph, resourceData, frame, TextureHandle.nullHandle);
                resources.ReleaseUnusedContexts();
                return;
            }

            if (!resourceData.cameraDepthTexture.IsValid() || !resourceData.cameraNormalsTexture.IsValid())
                return;

            TextureHandle screenTrace = RecordScreenTrace(renderGraph, resourceData, frame);
            if (debugMode == VoxelGIDebugMode.ScreenTrace)
            {
                RecordDebug(renderGraph, resourceData, frame, screenTrace);
                resources.ReleaseUnusedContexts();
                return;
            }

            TextureHandle current = screenTrace;
            if (settings.Temporal.Enabled && resourceData.motionVectorColor.IsValid())
            {
                current = RecordTemporal(renderGraph, resourceData, frame, current);
                if (debugMode == VoxelGIDebugMode.Temporal)
                {
                    RecordDebug(renderGraph, resourceData, frame, current);
                    resources.ReleaseUnusedContexts();
                    return;
                }
            }

            if (settings.Bilateral.Enabled)
                current = RecordBilateral(renderGraph, resourceData, frame, current);

            if (debugMode == VoxelGIDebugMode.Bilateral)
                RecordDebug(renderGraph, resourceData, frame, current);
            else
                RecordComposite(renderGraph, resourceData, frame, current);

            resources.ReleaseUnusedContexts();
        }

        static Light FindDirectionalLight()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled &&
                RenderSettings.sun.type == LightType.Directional)
                return RenderSettings.sun;

            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                    return light;
            }
            return null;
        }

        internal readonly struct VoxelGIFrame
        {
            public readonly UniversalCameraData CameraData;
            public readonly VoxelGISettingsSnapshot Settings;
            public readonly VoxelGIVolume Volume;
            public readonly Bounds Bounds;
            public readonly Light DirectionalLight;
            public readonly VoxelGIFrameMatrices Matrices;
            public readonly IReadOnlyList<VoxelGIRendererEntry> Renderers;
            public readonly int RegistryVersion;
            public readonly VoxelGICameraContext CameraContext;
            public readonly VoxelGIRuntimeResources Resources;

            public VoxelGIFrame(UniversalCameraData cameraData, VoxelGISettingsSnapshot settings,
                VoxelGIVolume volume, Bounds bounds, Light directionalLight, VoxelGIFrameMatrices matrices,
                IReadOnlyList<VoxelGIRendererEntry> renderers, int registryVersion,
                VoxelGICameraContext cameraContext, VoxelGIRuntimeResources resources)
            {
                CameraData = cameraData;
                Settings = settings;
                Volume = volume;
                Bounds = bounds;
                DirectionalLight = directionalLight;
                Matrices = matrices;
                Renderers = renderers;
                RegistryVersion = registryVersion;
                CameraContext = cameraContext;
                Resources = resources;
            }
        }

        internal readonly struct VoxelGIFrameMatrices
        {
            public readonly Matrix4x4 VoxelToWorld;
            public readonly Matrix4x4 WorldToVoxel;
            public readonly Matrix4x4 ShadowView;
            public readonly Matrix4x4 ShadowProjection;
            public readonly Matrix4x4 WorldToShadow;
            public readonly float VoxelSize;

            VoxelGIFrameMatrices(Matrix4x4 voxelToWorld, Matrix4x4 worldToVoxel, Matrix4x4 shadowView,
                Matrix4x4 shadowProjection, Matrix4x4 worldToShadow, float voxelSize)
            {
                VoxelToWorld = voxelToWorld;
                WorldToVoxel = worldToVoxel;
                ShadowView = shadowView;
                ShadowProjection = shadowProjection;
                WorldToShadow = worldToShadow;
                VoxelSize = voxelSize;
            }

            public static VoxelGIFrameMatrices Create(Bounds bounds, int resolution, Light light)
            {
                float side = bounds.size.x;
                float voxelSize = side / resolution;
                Matrix4x4 voxelToWorld = Matrix4x4.TRS(bounds.min, Quaternion.identity, Vector3.one * voxelSize);
                Matrix4x4 worldToVoxel = voxelToWorld.inverse;
                if (light == null)
                    return new VoxelGIFrameMatrices(voxelToWorld, worldToVoxel, Matrix4x4.identity,
                        Matrix4x4.identity, Matrix4x4.identity, voxelSize);

                float range = bounds.extents.magnitude;
                Vector3 forward = light.transform.forward.normalized;
                Vector3 position = bounds.center - forward * range;
                Quaternion rotation = Quaternion.LookRotation(forward, Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                    ? Vector3.forward
                    : Vector3.up);
                Matrix4x4 view = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
                view.SetRow(2, -view.GetRow(2));
                Matrix4x4 projection = Matrix4x4.Ortho(-range, range, -range, range, 0.01f, range * 2f + 0.01f);
                Matrix4x4 shadowProjection = projection;
                if (SystemInfo.usesReversedZBuffer)
                {
                    shadowProjection.m20 = -shadowProjection.m20;
                    shadowProjection.m21 = -shadowProjection.m21;
                    shadowProjection.m22 = -shadowProjection.m22;
                    shadowProjection.m23 = -shadowProjection.m23;
                }
                Matrix4x4 scaleBias = Matrix4x4.identity;
                scaleBias.m00 = scaleBias.m11 = scaleBias.m22 = 0.5f;
                scaleBias.m03 = scaleBias.m13 = scaleBias.m23 = 0.5f;
                return new VoxelGIFrameMatrices(voxelToWorld, worldToVoxel, view, projection,
                    scaleBias * shadowProjection * view, voxelSize);
            }
        }
    }
}
