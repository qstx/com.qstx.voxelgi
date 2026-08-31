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
        }

        void RecordVoxelization(RenderGraph renderGraph, VoxelGIFrame frame)
        {
            VoxelGICameraContext cameraContext = frame.CameraContext;
            TextureHandle albedo = renderGraph.ImportTexture(cameraContext.AlbedoOpacity);
            TextureHandle normal = renderGraph.ImportTexture(cameraContext.Normal);
            TextureHandle direct = renderGraph.ImportTexture(cameraContext.DirectRadiance);
            BufferHandle albedoBuffer = renderGraph.ImportBuffer(cameraContext.AlbedoAccumulation);
            BufferHandle normalBuffer = renderGraph.ImportBuffer(cameraContext.NormalAccumulation);
            BufferHandle emissiveBuffer = renderGraph.ImportBuffer(cameraContext.EmissiveAccumulation);
            BufferHandle opacityBuffer = renderGraph.ImportBuffer(cameraContext.OpacityAccumulation);

            using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass(
                "VoxelGI/Voxelization", out VoxelizationPassData data, VoxelizationSampler);
            data.Frame = frame;
            builder.UseTexture(albedo, AccessFlags.Write);
            builder.UseTexture(normal, AccessFlags.Write);
            builder.UseTexture(direct, AccessFlags.Write);
            builder.UseBuffer(albedoBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(normalBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(emissiveBuffer, AccessFlags.ReadWrite);
            builder.UseBuffer(opacityBuffer, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (VoxelizationPassData passData, UnsafeGraphContext context) =>
            {
                CommandBuffer commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                ComputeVoxelizer.Dispatch(commandBuffer, passData.Frame.Resources, passData.Frame.CameraContext,
                    passData.Frame.Settings, passData.Frame.Matrices.WorldToVoxel, passData.Frame.Bounds,
                    passData.Frame.Renderers);
            });
        }
    }
}
