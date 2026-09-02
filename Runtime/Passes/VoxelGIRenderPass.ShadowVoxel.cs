using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace QSTX.VoxelGI
{
    internal sealed partial class VoxelGIRenderPass
    {
        static readonly ProfilingSampler ShadowSampler = new ProfilingSampler("VoxelGI.Shadow");
        static readonly ProfilingSampler VoxelizationSampler = new ProfilingSampler("VoxelGI.Voxelization");
        static readonly int LightDirection = Shader.PropertyToID("_LightDirection");
        static readonly int ShadowBias = Shader.PropertyToID("_ShadowBias");

        sealed class ShadowPassData
        {
            public VoxelGIFrame Frame;
        }

        void RecordShadow(RenderGraph renderGraph, VoxelGIFrame frame)
        {
            TextureHandle shadow = renderGraph.ImportTexture(frame.CameraContext.ShadowDepth);
            using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                "VoxelGI/Shadow", out ShadowPassData data, ShadowSampler);
            data.Frame = frame;
            builder.SetRenderAttachmentDepth(shadow, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (ShadowPassData passData, RasterGraphContext context) =>
                ExecuteShadow(passData.Frame, context.cmd));
        }

        static void ExecuteShadow(VoxelGIFrame frame, RasterCommandBuffer cmd)
        {
            int resolution = frame.Settings.Voxelization.ShadowResolution;
            cmd.ClearRenderTarget(true, false, Color.black, 1f);
            cmd.SetViewport(new Rect(0f, 0f, resolution, resolution));
            cmd.SetViewProjectionMatrices(frame.Matrices.ShadowView, frame.Matrices.ShadowProjection);
            cmd.SetGlobalVector(LightDirection, -frame.DirectionalLight.transform.forward);
            cmd.SetGlobalVector(ShadowBias, Vector4.zero);
            cmd.SetGlobalDepthBias(1f, 2.5f);

            for (int i = 0; i < frame.Renderers.Count; i++)
            {
                VoxelGIRendererEntry entry = frame.Renderers[i];
                Renderer renderer = entry.Renderer;
                if (!entry.CastVoxelShadow || renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                int subMeshCount = GetSubMeshCount(renderer);
                for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
                {
                    Material material = subMesh < materials.Length ? materials[subMesh] : null;
                    if (material == null || material.renderQueue > (int)RenderQueue.GeometryLast)
                        continue;
                    int pass = material.FindPass("ShadowCaster");
                    if (pass < 0)
                        pass = material.FindPass("VoxelGIShadow");
                    if (pass >= 0)
                        cmd.DrawRenderer(renderer, material, subMesh, pass);
                }
            }

            cmd.SetGlobalDepthBias(0f, 0f);
            cmd.SetViewProjectionMatrices(frame.CameraData.GetViewMatrix(), frame.CameraData.GetProjectionMatrix());
        }

        static int GetSubMeshCount(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                return skinned.sharedMesh.subMeshCount;
            if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out MeshFilter filter) &&
                filter.sharedMesh != null)
                return filter.sharedMesh.subMeshCount;
            return 0;
        }

        sealed class VoxelizationPassData
        {
            public VoxelGIFrame Frame;
            public BufferHandle AlbedoAccumulation;
            public BufferHandle NormalAccumulation;
            public BufferHandle EmissiveAccumulation;
            public BufferHandle OpacityAccumulation;
        }

        void RecordVoxelization(RenderGraph renderGraph, VoxelGIFrame frame)
        {
            VoxelGICameraContext cameraContext = frame.CameraContext;
            TextureHandle albedo = renderGraph.ImportTexture(cameraContext.AlbedoOpacity);
            TextureHandle normal = renderGraph.ImportTexture(cameraContext.Normal);
            TextureHandle emissive = renderGraph.ImportTexture(cameraContext.Emissive);
            int voxelCount = checked(frame.Settings.Voxelization.Resolution *
                                     frame.Settings.Voxelization.Resolution *
                                     frame.Settings.Voxelization.Resolution);
            BufferHandle albedoBuffer = renderGraph.CreateBuffer(new BufferDesc(voxelCount, sizeof(uint))
                { name = "VoxelGI Albedo Accumulation" });
            BufferHandle normalBuffer = renderGraph.CreateBuffer(new BufferDesc(voxelCount, sizeof(uint))
                { name = "VoxelGI Normal Accumulation" });
            BufferHandle emissiveBuffer = renderGraph.CreateBuffer(new BufferDesc(voxelCount, sizeof(uint) * 4)
                { name = "VoxelGI Emissive Accumulation" });
            BufferHandle opacityBuffer = renderGraph.CreateBuffer(new BufferDesc(voxelCount, sizeof(uint))
                { name = "VoxelGI Opacity Accumulation" });

            // 体素化会在执行阶段遍历 Renderer，直接绑定 Mesh/SkinnedMeshRenderer 的原始顶点、索引
            // GraphicsBuffer 以及材质纹理。这些动态外部资源没有对应的 Render Graph Handle，无法由
            // Render Graph 完整跟踪和验证；同时现有 ComputeVoxelizer 使用传统 CommandBuffer 记录命令，
            // 因此这里必须使用 UnsafePass。由 Render Graph 创建的纹理和累积 Buffer 仍需在下方显式声明访问方式。
            using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass(
                "VoxelGI/Voxelization", out VoxelizationPassData data, VoxelizationSampler);
            data.Frame = frame;
            data.AlbedoAccumulation = albedoBuffer;
            data.NormalAccumulation = normalBuffer;
            data.EmissiveAccumulation = emissiveBuffer;
            data.OpacityAccumulation = opacityBuffer;
            builder.UseTexture(albedo, AccessFlags.Write);
            builder.UseTexture(normal, AccessFlags.Write);
            builder.UseTexture(emissive, AccessFlags.Write);
            builder.UseBuffer(albedoBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(normalBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(emissiveBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(opacityBuffer, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (VoxelizationPassData passData, UnsafeGraphContext context) =>
            {
                CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                var accumulation = new VoxelGIAccumulationBuffers(
                    passData.AlbedoAccumulation,
                    passData.NormalAccumulation,
                    passData.EmissiveAccumulation,
                    passData.OpacityAccumulation);
                ComputeVoxelizer.Dispatch(commandBuffer, passData.Frame.Resources, passData.Frame.CameraContext,
                    accumulation, passData.Frame.Settings, passData.Frame.Matrices.WorldToVoxel,
                    passData.Frame.Bounds, passData.Frame.Renderers);
            });
        }
    }
}
