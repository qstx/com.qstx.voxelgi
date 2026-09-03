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
            // 总调度入口：先解析当前相机的 Volume 和体素范围，再按数据是否失效决定
            // 体素化、阴影、光照和屏幕空间后处理是否需要在本帧执行。
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

            // 每个相机独立持有体素纹理和时空 History；分辨率变化会重建资源并使 History 失效。
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

            // 根据更新模式、场景 Renderer、体素范围和光照参数判断是否需要重新生成体素数据，
            // 以及是否只需重新计算光照。重新体素化会同时使 Shadow、Lighting 和 History 失效。
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

            // 以下阶段将体素辐射投影到屏幕：ScreenTrace 负责空间采样，Temporal/Bilateral 负责降噪，
            // 最后 Composite 将间接光叠加回 URP 当前颜色目标；Debug 模式会在对应阶段提前返回。
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
            // 将以体素为单位的网格坐标转换到世界空间；原点对应体素化包围盒的最小点。
            public readonly Matrix4x4 VoxelToWorld;

            // 将世界空间坐标转换到体素网格坐标，是 VoxelToWorld 的逆矩阵。
            public readonly Matrix4x4 WorldToVoxel;

            // 将世界空间坐标转换到方向光的观察空间。
            public readonly Matrix4x4 ShadowView;

            // 将方向光观察空间坐标转换到裁剪空间，供阴影深度绘制使用。
            public readonly Matrix4x4 ShadowProjection;

            // 将世界空间坐标直接转换到 [0, 1] 阴影纹理坐标，其中 XY 为 UV、Z 为比较深度。
            public readonly Matrix4x4 WorldToShadow;

            // 单个体素边长对应的世界空间长度。
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

                // 对方向光来说，Transform 的位置并不能定义阴影体积，只有光照方向有意义。
                // 因此以体素包围盒为中心构造一台虚拟光源相机，保证整个体积位于其远近裁剪面之间。
                float range = bounds.extents.magnitude;
                Vector3 forward = light.transform.forward.normalized;
                Vector3 position = bounds.center - forward * range;

                // LookRotation 需要一个不与观察方向共线的上方向参考。
                // 当光照方向接近世界上方向时，改用世界前方向来构造稳定的正交基。
                Quaternion rotation = Quaternion.LookRotation(forward, Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                    ? Vector3.forward
                    : Vector3.up);

                // Transform 逆矩阵描述的是以 +Z 为前方的局部空间，而 Unity 渲染的观察空间
                // 遵循 OpenGL 相机约定，以 -Z 为观察前方。将索引为 2 的矩阵行取反，
                // 等价于在左侧乘以 Scale(1, 1, -1)。
                Matrix4x4 view = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
                view.SetRow(2, -view.GetRow(2));
                Matrix4x4 projection = Matrix4x4.Ortho(-range, range, -range, range, 0.01f, range * 2f + 0.01f);
                Matrix4x4 shadowProjection = projection;

                // 阴影采样矩阵的深度方向必须与当前平台的阴影纹理一致。
                if (SystemInfo.usesReversedZBuffer)
                {
                    shadowProjection.m20 = -shadowProjection.m20;
                    shadowProjection.m21 = -shadowProjection.m21;
                    shadowProjection.m22 = -shadowProjection.m22;
                    shadowProjection.m23 = -shadowProjection.m23;
                }

                // 将齐次 NDC 从 [-1, 1] 映射到纹理空间 [0, 1]。透视除法后，XY 是阴影纹理 UV，
                // Z 是用于阴影比较的深度。这里的 scale/bias 是坐标缩放和平移，
                // 并不是用于抑制阴影痤疮的法线偏移或光照方向偏移。
                Matrix4x4 scaleBias = Matrix4x4.identity;
                scaleBias.m00 = scaleBias.m11 = scaleBias.m22 = 0.5f;
                scaleBias.m03 = scaleBias.m13 = scaleBias.m23 = 0.5f;
                return new VoxelGIFrameMatrices(voxelToWorld, worldToVoxel, view, projection,
                    scaleBias * shadowProjection * view, voxelSize);
            }
        }
    }
}
