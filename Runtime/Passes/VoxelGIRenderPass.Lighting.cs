using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace QSTX.VoxelGI
{
    internal sealed partial class VoxelGIRenderPass
    {
        static readonly ProfilingSampler LightingSampler = new ProfilingSampler("VoxelGI.Lighting");

        sealed class LightingPassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle Albedo;
            public TextureHandle Normal;
            public TextureHandle Direct;
            public TextureHandle Final;
            public TextureHandle Scratch;
            public TextureHandle Shadow;
        }

        void RecordLighting(RenderGraph renderGraph, VoxelGIFrame frame)
        {
            var data = new LightingPassData();
            using IComputeRenderGraphBuilder builder = renderGraph.AddComputePass(
                "VoxelGI/Lighting", out data, LightingSampler);
            data.Frame = frame;
            data.Albedo = renderGraph.ImportTexture(frame.CameraContext.AlbedoOpacity);
            data.Normal = renderGraph.ImportTexture(frame.CameraContext.Normal);
            data.Direct = renderGraph.ImportTexture(frame.CameraContext.DirectRadiance);
            data.Final = renderGraph.ImportTexture(frame.CameraContext.FinalRadiance);
            data.Scratch = renderGraph.ImportTexture(frame.CameraContext.MipScratch);
            data.Shadow = renderGraph.ImportTexture(frame.CameraContext.ShadowDepth);
            builder.UseTexture(data.Albedo, AccessFlags.Read);
            builder.UseTexture(data.Normal, AccessFlags.Read);
            builder.UseTexture(data.Direct, AccessFlags.ReadWrite);
            builder.UseTexture(data.Final, AccessFlags.ReadWrite);
            builder.UseTexture(data.Scratch, AccessFlags.ReadWrite);
            builder.UseTexture(data.Shadow, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (LightingPassData passData, ComputeGraphContext context) =>
                ExecuteLighting(passData, context.cmd));
        }

        static void ExecuteLighting(LightingPassData data, ComputeCommandBuffer cmd)
        {
            VoxelGIFrame frame = data.Frame;
            VoxelGIRuntimeResources resources = frame.Resources;
            ComputeShader compute = resources.ComputeShader;
            VoxelGISettingsSnapshot settings = frame.Settings;
            int resolution = settings.Voxelization.Resolution;

            int directKernel = resources.Kernels.DirectLighting.Index;
            cmd.SetComputeTextureParam(compute, directKernel, "_VoxelGIAlbedoOpacity", data.Albedo);
            cmd.SetComputeTextureParam(compute, directKernel, "_VoxelGINormalTexture", data.Normal);
            cmd.SetComputeTextureParam(compute, directKernel, "_VoxelGIDirectRadiance", data.Direct);
            cmd.SetComputeTextureParam(compute, directKernel, "_VoxelGIShadowMap", data.Shadow, 0,
                RenderTextureSubElement.Depth);
            SetSharedLightingParameters(cmd, compute, frame);
            cmd.DispatchCompute(compute, directKernel,
                resources.Kernels.DirectLighting.GroupsX(resolution),
                resources.Kernels.DirectLighting.GroupsY(resolution),
                resources.Kernels.DirectLighting.GroupsZ(resolution));
            GenerateMipChain(cmd, resources, data.Direct, data.Scratch, resolution, frame.CameraContext.MipCount);

            if (!settings.IndirectLighting.SecondBounce)
                return;

            int indirectKernel = resources.Kernels.IndirectLighting.Index;
            cmd.SetKeyword(compute, new LocalKeyword(compute, "_VOXEL_GI_INDIRECT_LOW"),
                settings.IndirectLighting.Quality == VoxelGIConeQuality.Low);
            cmd.SetKeyword(compute, new LocalKeyword(compute, "_VOXEL_GI_INDIRECT_MEDIUM"),
                settings.IndirectLighting.Quality == VoxelGIConeQuality.Medium);
            cmd.SetKeyword(compute, new LocalKeyword(compute, "_VOXEL_GI_INDIRECT_HIGH"),
                settings.IndirectLighting.Quality == VoxelGIConeQuality.High);
            cmd.SetComputeTextureParam(compute, indirectKernel, "_VoxelGIAlbedoOpacity", data.Albedo);
            cmd.SetComputeTextureParam(compute, indirectKernel, "_VoxelGINormalTexture", data.Normal);
            cmd.SetComputeTextureParam(compute, indirectKernel, "_VoxelGIInputRadiance", data.Direct);
            cmd.SetComputeTextureParam(compute, indirectKernel, "_VoxelGIFinalRadiance", data.Final);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectMaxMip", frame.CameraContext.MipCount - 1);
            cmd.SetComputeIntParam(compute, "_VoxelGIIndirectMaxSteps", settings.IndirectLighting.MaxSteps);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectAlphaAttenuation",
                settings.IndirectLighting.AlphaAttenuation);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectIntensity", settings.IndirectLighting.Intensity);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectFirstStep", settings.IndirectLighting.FirstStep);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectStepScale", settings.IndirectLighting.StepScale);
            cmd.SetComputeFloatParam(compute, "_VoxelGIIndirectConeAngle", settings.IndirectLighting.ConeAngle);
            cmd.SetComputeIntParam(compute, "_VoxelGIIndirectMinMip", settings.IndirectLighting.MinMipLevel);
            cmd.DispatchCompute(compute, indirectKernel,
                resources.Kernels.IndirectLighting.GroupsX(resolution),
                resources.Kernels.IndirectLighting.GroupsY(resolution),
                resources.Kernels.IndirectLighting.GroupsZ(resolution));
            GenerateMipChain(cmd, resources, data.Final, data.Scratch, resolution, frame.CameraContext.MipCount);
        }

        static void SetSharedLightingParameters(ComputeCommandBuffer cmd, ComputeShader compute, VoxelGIFrame frame)
        {
            VoxelGISettingsSnapshot.DirectLightingSettings settings = frame.Settings.DirectLighting;
            cmd.SetComputeMatrixParam(compute, VoxelGIShaderIDs.VoxelToWorld, frame.Matrices.VoxelToWorld);
            cmd.SetComputeMatrixParam(compute, VoxelGIShaderIDs.WorldToVoxel, frame.Matrices.WorldToVoxel);
            cmd.SetComputeMatrixParam(compute, VoxelGIShaderIDs.WorldToShadow, frame.Matrices.WorldToShadow);
            cmd.SetComputeIntParam(compute, VoxelGIShaderIDs.Resolution, frame.Settings.Voxelization.Resolution);
            cmd.SetComputeFloatParam(compute, VoxelGIShaderIDs.VoxelSize, frame.Matrices.VoxelSize);
            cmd.SetComputeFloatParam(compute, "_VoxelGILightIntensity", settings.LightIntensity);
            cmd.SetComputeFloatParam(compute, "_VoxelGIEmissiveIntensity", settings.EmissiveIntensity);
            cmd.SetComputeFloatParam(compute, "_VoxelGIShadowSunBias", settings.ShadowSunBias);
            cmd.SetComputeFloatParam(compute, "_VoxelGIShadowNormalBias", settings.ShadowNormalBias);
            cmd.SetComputeIntParam(compute, "_VoxelGIReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);
            if (frame.DirectionalLight == null)
            {
                cmd.SetComputeIntParam(compute, "_VoxelGIHasDirectionalLight", 0);
                return;
            }
            Light light = frame.DirectionalLight;
            cmd.SetComputeIntParam(compute, "_VoxelGIHasDirectionalLight", 1);
            cmd.SetComputeVectorParam(compute, "_VoxelGISunDirection", light.transform.forward);
            cmd.SetComputeVectorParam(compute, "_VoxelGISunColor", light.color);
            cmd.SetComputeFloatParam(compute, "_VoxelGISunIntensity", light.intensity);
        }

        static void GenerateMipChain(ComputeCommandBuffer cmd, VoxelGIRuntimeResources resources,
            TextureHandle target, TextureHandle scratch, int baseResolution, int mipCount)
        {
            ComputeShader compute = resources.ComputeShader;
            for (int sourceMip = 0; sourceMip < mipCount - 1; sourceMip++)
            {
                int destinationMip = sourceMip + 1;
                int destinationResolution = Mathf.Max(1, baseResolution >> destinationMip);
                int generate = resources.Kernels.GenerateMip.Index;
                cmd.SetComputeIntParam(compute, "_VoxelGISourceMip", sourceMip);
                cmd.SetComputeIntParam(compute, "_VoxelGIDestinationResolution", destinationResolution);
                cmd.SetComputeTextureParam(compute, generate, "_VoxelGIMipSource", target);
                cmd.SetComputeTextureParam(compute, generate, "_VoxelGIMipDestination", scratch, destinationMip);
                cmd.DispatchCompute(compute, generate,
                    resources.Kernels.GenerateMip.GroupsX(destinationResolution),
                    resources.Kernels.GenerateMip.GroupsY(destinationResolution),
                    resources.Kernels.GenerateMip.GroupsZ(destinationResolution));

                int copy = resources.Kernels.CopyMip.Index;
                cmd.SetComputeIntParam(compute, VoxelGIShaderIDs.Resolution, baseResolution);
                cmd.SetComputeIntParam(compute, "_VoxelGICopyMip", destinationMip);
                cmd.SetComputeTextureParam(compute, copy, "_VoxelGICopySource", scratch);
                cmd.SetComputeTextureParam(compute, copy, "_VoxelGICopyDestination", target, destinationMip);
                cmd.DispatchCompute(compute, copy,
                    resources.Kernels.CopyMip.GroupsX(destinationResolution),
                    resources.Kernels.CopyMip.GroupsY(destinationResolution),
                    resources.Kernels.CopyMip.GroupsZ(destinationResolution));
            }
        }
    }
}
