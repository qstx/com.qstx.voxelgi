using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace QSTX.VoxelGI
{
    [DisallowMultipleRendererFeature]
    public sealed class VoxelGIRendererFeature : ScriptableRendererFeature
    {
        [Header("Resources")]
        [SerializeField, Tooltip("Hidden/QSTX/VoxelGI fullscreen shader.")]
        Shader m_FullscreenShader;

        [SerializeField, Tooltip("VoxelGI compute shader containing the eight runtime kernels.")]
        ComputeShader m_ComputeShader;

        [Header("Performance")]
        [SerializeField, Min(0f), Tooltip("Fallback interval for discovering runtime Renderers without a Contributor. Set to zero to disable periodic rescans.")]
        float m_RendererRescanInterval = 2f;

        VoxelGIRenderPass m_RenderPass;
        VoxelGIRuntimeResources m_RuntimeResources;
        bool m_Supported;
        bool m_LoggedUnsupported;

        internal VoxelGIRuntimeResources RuntimeResources => m_RuntimeResources;
        internal float RendererRescanInterval => m_RendererRescanInterval;

        public override void Create()
        {
            // Renderer Feature 创建时加载 Shader、初始化运行时资源，并构造 Render Graph Pass。
            // 重新创建 Feature 前先释放旧资源，避免 ComputeBuffer、RTHandle 等 GPU 资源泄漏。
            ReleaseResources();
            m_Supported = CheckSupport();
            if (!m_Supported)
                return;

            Shader fullscreenShader = m_FullscreenShader != null
                ? m_FullscreenShader
                : Shader.Find("Hidden/QSTX/VoxelGI");
            if (fullscreenShader == null || m_ComputeShader == null)
            {
                Debug.LogError("VoxelGI requires Hidden/QSTX/VoxelGI and VoxelGICompute.compute references.", this);
                return;
            }

            try
            {
                m_RuntimeResources = new VoxelGIRuntimeResources(fullscreenShader, m_ComputeShader);
                m_RenderPass = new VoxelGIRenderPass(this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                ReleaseResources();
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // VoxelGI 只对游戏相机的 Base Camera 生效，避免在 Preview、Overlay 或编辑器相机中重复执行。
            CameraData cameraData = renderingData.cameraData;
            if (!m_Supported || m_RenderPass == null || cameraData.cameraType != CameraType.Game ||
                cameraData.renderType != CameraRenderType.Base)
                return;
            renderer.EnqueuePass(m_RenderPass);
        }

        bool CheckSupport()
        {
            // 运行时依赖 Compute Shader、3D UAV 纹理以及 RGBA16F 的 Load/Store 能力。
            // 任一能力缺失时整项功能关闭，并只输出一次错误日志。
            bool supported = SystemInfo.supportsComputeShaders && SystemInfo.supports3DTextures &&
                             SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat,
                                 GraphicsFormatUsage.LoadStore);
            if (!supported && !m_LoggedUnsupported)
            {
                Debug.LogError("VoxelGI is disabled: this graphics device does not support Compute Shaders, 3D textures, or R16G16B16A16_SFloat UAV access.", this);
                m_LoggedUnsupported = true;
            }
            return supported;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseResources();
            base.Dispose(disposing);
        }

        void ReleaseResources()
        {
            m_RuntimeResources?.Dispose();
            m_RuntimeResources = null;
            m_RenderPass = null;
        }
    }
}
