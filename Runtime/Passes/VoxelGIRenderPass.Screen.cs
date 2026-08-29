using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace QSTX.VoxelGI
{
    internal sealed partial class VoxelGIRenderPass
    {
        static readonly ProfilingSampler ScreenTraceSampler = new ProfilingSampler("VoxelGI.ScreenTrace");
        static readonly ProfilingSampler TemporalSampler = new ProfilingSampler("VoxelGI.Temporal");
        static readonly ProfilingSampler BilateralSampler = new ProfilingSampler("VoxelGI.Bilateral");
        static readonly ProfilingSampler CompositeSampler = new ProfilingSampler("VoxelGI.Composite");
        static readonly ProfilingSampler DebugSampler = new ProfilingSampler("VoxelGI.Debug");

        sealed class ScreenTracePassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle Depth;
            public TextureHandle Normals;
            public TextureHandle Radiance;
            public TextureHandle BlueNoise;
            public int Width;
            public int Height;
        }

        TextureHandle RecordScreenTrace(RenderGraph renderGraph, UniversalResourceData resourceData, VoxelGIFrame frame)
        {
            TextureHandle output = CreateScreenTexture(renderGraph, resourceData.activeColorTexture,
                "VoxelGI Screen Trace", false);
            TextureHandle radiance = renderGraph.ImportTexture(frame.Settings.IndirectLighting.SecondBounce
                ? frame.CameraContext.FinalRadiance
                : frame.CameraContext.DirectRadiance);
            TextureHandle blueNoise = renderGraph.ImportTexture(
                frame.Resources.GetExternalTexture(frame.Settings.Temporal.BlueNoise));
            Vector2Int screenSize = GetScreenTargetSize(renderGraph, resourceData.activeColorTexture,
                frame.CameraData);

            using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                "VoxelGI/ScreenTrace", out ScreenTracePassData data, ScreenTraceSampler);
            data.Frame = frame;
            data.Depth = resourceData.cameraDepthTexture;
            data.Normals = resourceData.cameraNormalsTexture;
            data.Radiance = radiance;
            data.BlueNoise = blueNoise;
            data.Width = screenSize.x;
            data.Height = screenSize.y;
            builder.UseTexture(data.Depth, AccessFlags.Read);
            builder.UseTexture(data.Normals, AccessFlags.Read);
            builder.UseTexture(data.Radiance, AccessFlags.Read);
            builder.UseTexture(data.BlueNoise, AccessFlags.Read);
            builder.SetRenderAttachment(output, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (ScreenTracePassData passData, RasterGraphContext context) =>
                ExecuteScreenTrace(passData, context.cmd));
            return output;
        }

        static void ExecuteScreenTrace(ScreenTracePassData data, RasterCommandBuffer cmd)
        {
            VoxelGISettingsSnapshot.ScreenTracingSettings settings = data.Frame.Settings.ScreenTracing;
            VoxelGISettingsSnapshot.TemporalSettings temporal = data.Frame.Settings.Temporal;
            VoxelGICameraContext cameraContext = data.Frame.CameraContext;
            Vector2 jitter = GetJitter(cameraContext.JitterIndex++, temporal);

            cmd.SetGlobalTexture("_VoxelGIScreenDepth", data.Depth);
            cmd.SetGlobalTexture("_VoxelGIScreenNormals", data.Normals);
            cmd.SetGlobalTexture("_VoxelGITraceRadiance", data.Radiance);
            cmd.SetGlobalTexture("_VoxelGIBlueNoise", data.BlueNoise);
            cmd.SetGlobalMatrix(VoxelGIShaderIDs.WorldToVoxel, data.Frame.Matrices.WorldToVoxel);
            cmd.SetGlobalInt(VoxelGIShaderIDs.Resolution, data.Frame.Settings.Voxelization.Resolution);
            cmd.SetGlobalFloat("_VoxelGIScreenMaxMip", cameraContext.MipCount - 1);
            cmd.SetGlobalInt("_VoxelGIScreenMaxSteps", settings.MaxSteps);
            cmd.SetGlobalFloat("_VoxelGIScreenAlphaAttenuation", settings.AlphaAttenuation);
            cmd.SetGlobalFloat("_VoxelGIScreenIntensity", settings.Intensity);
            cmd.SetGlobalFloat("_VoxelGIScreenFirstStep", settings.FirstStep);
            cmd.SetGlobalFloat("_VoxelGIScreenStepScale", settings.StepScale);
            cmd.SetGlobalFloat("_VoxelGIScreenConeAngle", settings.ConeAngle);
            cmd.SetGlobalInt("_VoxelGIScreenQuality", (int)settings.Quality);
            cmd.SetGlobalInt("_VoxelGITemporalEnabled", temporal.Enabled ? 1 : 0);
            cmd.SetGlobalInt("_VoxelGIHasBlueNoise", temporal.BlueNoise != null ? 1 : 0);
            cmd.SetGlobalVector(VoxelGIShaderIDs.ScreenSize,
                new Vector4(data.Width, data.Height, 1f / data.Width, 1f / data.Height));
            Texture noiseTexture = temporal.BlueNoise != null ? temporal.BlueNoise : Texture2D.grayTexture;
            cmd.SetGlobalVector("_VoxelGIBlueNoiseSize",
                new Vector4(noiseTexture.width, noiseTexture.height, 1f / noiseTexture.width, 1f / noiseTexture.height));
            Vector2 noiseScale = temporal.BlueNoiseScale;
            cmd.SetGlobalVector("_VoxelGIBlueNoiseScale",
                new Vector4(noiseScale.x, noiseScale.y, 1f / Mathf.Max(noiseScale.x, 1e-5f),
                    1f / Mathf.Max(noiseScale.y, 1e-5f)));
            cmd.SetGlobalVector("_VoxelGIJitter", jitter);
            cmd.DrawProcedural(Matrix4x4.identity, data.Frame.Resources.FullscreenMaterial,
                data.Frame.Resources.ScreenTracePass, MeshTopology.Triangles, 3);
        }

        sealed class TemporalPassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle Current;
            public TextureHandle History;
            public TextureHandle Motion;
            public bool HistoryValid;
        }

        TextureHandle RecordTemporal(RenderGraph renderGraph, UniversalResourceData resourceData,
            VoxelGIFrame frame, TextureHandle current)
        {
            Vector2Int screenSize = GetScreenTargetSize(renderGraph, resourceData.activeColorTexture,
                frame.CameraData);
            frame.CameraContext.EnsureHistory(screenSize.x, screenSize.y, true);
            RTHandle sourceHandle = frame.CameraContext.HistoryWriteA
                ? frame.CameraContext.HistoryB
                : frame.CameraContext.HistoryA;
            RTHandle destinationHandle = frame.CameraContext.HistoryWriteA
                ? frame.CameraContext.HistoryA
                : frame.CameraContext.HistoryB;
            TextureHandle source = renderGraph.ImportTexture(sourceHandle);
            TextureHandle destination = renderGraph.ImportTexture(destinationHandle);

            using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                "VoxelGI/Temporal", out TemporalPassData data, TemporalSampler);
            data.Frame = frame;
            data.Current = current;
            data.History = source;
            data.Motion = resourceData.motionVectorColor;
            data.HistoryValid = !frame.CameraContext.HistoryNeedsClear;
            builder.UseTexture(data.Current, AccessFlags.Read);
            builder.UseTexture(data.History, AccessFlags.Read);
            builder.UseTexture(data.Motion, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (TemporalPassData passData, RasterGraphContext context) =>
                ExecuteTemporal(passData, context.cmd));

            frame.CameraContext.HistoryWriteA = !frame.CameraContext.HistoryWriteA;
            frame.CameraContext.HistoryNeedsClear = false;
            return destination;
        }

        static void ExecuteTemporal(TemporalPassData data, RasterCommandBuffer cmd)
        {
            VoxelGISettingsSnapshot.TemporalSettings settings = data.Frame.Settings.Temporal;
            cmd.SetGlobalTexture(VoxelGIShaderIDs.CurrentIrradiance, data.Current);
            cmd.SetGlobalTexture(VoxelGIShaderIDs.HistoryIrradiance, data.History);
            cmd.SetGlobalTexture("_VoxelGIMotionVectors", data.Motion);
            cmd.SetGlobalInt("_VoxelGIHistoryValid", data.HistoryValid ? 1 : 0);
            cmd.SetGlobalFloat("_VoxelGITemporalCurrentFrameWeight", settings.CurrentFrameWeight);
            cmd.SetGlobalFloat("_VoxelGITemporalClampScale", settings.ClampScale);
            cmd.DrawProcedural(Matrix4x4.identity, data.Frame.Resources.FullscreenMaterial,
                data.Frame.Resources.TemporalPass, MeshTopology.Triangles, 3);
        }

        sealed class BilateralPassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle Input;
            public TextureHandle Depth;
            public TextureHandle Normals;
            public TextureHandle Output;
            // The active color texture can be render-scale/dynamic-resolution sized.
            // Do not derive the compute domain from Camera.pixelWidth/Height: those
            // values describe the camera target, not necessarily this RenderGraph RT.
            public int Width;
            public int Height;
        }

        TextureHandle RecordBilateral(RenderGraph renderGraph, UniversalResourceData resourceData,
            VoxelGIFrame frame, TextureHandle input)
        {
            TextureHandle output = CreateScreenTexture(renderGraph, resourceData.activeColorTexture,
                "VoxelGI Bilateral", true);
            Vector2Int screenSize = GetScreenTargetSize(renderGraph, resourceData.activeColorTexture,
                frame.CameraData);
            using IComputeRenderGraphBuilder builder = renderGraph.AddComputePass(
                "VoxelGI/Bilateral", out BilateralPassData data, BilateralSampler);
            data.Frame = frame;
            data.Input = input;
            data.Depth = resourceData.cameraDepthTexture;
            data.Normals = resourceData.cameraNormalsTexture;
            data.Output = output;
            data.Width = screenSize.x;
            data.Height = screenSize.y;
            builder.UseTexture(data.Input, AccessFlags.Read);
            builder.UseTexture(data.Depth, AccessFlags.Read);
            builder.UseTexture(data.Normals, AccessFlags.Read);
            builder.UseTexture(data.Output, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (BilateralPassData passData, ComputeGraphContext context) =>
                ExecuteBilateral(passData, context.cmd));
            return output;
        }

        static void ExecuteBilateral(BilateralPassData data, ComputeCommandBuffer cmd)
        {
            VoxelGIRuntimeResources resources = data.Frame.Resources;
            ComputeShader compute = resources.ComputeShader;
            int kernel = resources.Kernels.Bilateral.Index;
            Camera camera = data.Frame.CameraData.camera;
            VoxelGISettingsSnapshot.BilateralSettings settings = data.Frame.Settings.Bilateral;
            int width = data.Width > 0 ? data.Width : camera.pixelWidth;
            int height = data.Height > 0 ? data.Height : camera.pixelHeight;
            cmd.SetComputeTextureParam(compute, kernel, "_VoxelGIFilterInput", data.Input);
            cmd.SetComputeTextureParam(compute, kernel, "_VoxelGIDepthTexture", data.Depth);
            cmd.SetComputeTextureParam(compute, kernel, "_VoxelGIScreenNormalTexture", data.Normals);
            cmd.SetComputeTextureParam(compute, kernel, "_VoxelGIFilterOutput", data.Output);
            cmd.SetComputeVectorParam(compute, VoxelGIShaderIDs.ScreenSize,
                new Vector4(width, height, 1f / width, 1f / height));
            cmd.SetComputeVectorParam(compute, "_VoxelGIBilateralThresholds",
                new Vector4(settings.DepthThreshold.x, settings.DepthThreshold.y,
                    settings.NormalThreshold.x, settings.NormalThreshold.y));
            cmd.SetComputeFloatParam(compute, "_VoxelGIBilateralRadius", settings.Radius);
            cmd.DispatchCompute(compute, kernel,
                resources.Kernels.Bilateral.GroupsX(width),
                resources.Kernels.Bilateral.GroupsY(height), 1);
        }

        sealed class CompositePassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle SceneColor;
            public TextureHandle Indirect;
        }

        void RecordComposite(RenderGraph renderGraph, UniversalResourceData resourceData,
            VoxelGIFrame frame, TextureHandle indirect)
        {
            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle sceneCopy = CreateScreenTexture(renderGraph, source, "VoxelGI Scene Copy", false);
            RenderGraphUtils.AddCopyPass(renderGraph, source, sceneCopy, "VoxelGI Copy Scene Color");
            TextureHandle combined = CreateScreenTexture(renderGraph, source, "VoxelGI Composite", false);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                "VoxelGI/Composite", out CompositePassData data, CompositeSampler))
            {
                data.Frame = frame;
                data.SceneColor = sceneCopy;
                data.Indirect = indirect;
                builder.UseTexture(data.SceneColor, AccessFlags.Read);
                builder.UseTexture(data.Indirect, AccessFlags.Read);
                builder.SetRenderAttachment(combined, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (CompositePassData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(VoxelGIShaderIDs.SceneColor, passData.SceneColor);
                    context.cmd.SetGlobalTexture(VoxelGIShaderIDs.IndirectIrradiance, passData.Indirect);
                    context.cmd.DrawProcedural(Matrix4x4.identity, passData.Frame.Resources.FullscreenMaterial,
                        passData.Frame.Resources.CompositePass, MeshTopology.Triangles, 3);
                });
            }
            RenderGraphUtils.AddCopyPass(renderGraph, combined, source, "VoxelGI Copy Composite To Camera");
        }

        sealed class DebugPassData
        {
            public VoxelGIFrame Frame;
            public TextureHandle Albedo;
            public TextureHandle Normal;
            public TextureHandle Direct;
            public TextureHandle Final;
            public TextureHandle Shadow;
            public TextureHandle DebugTexture;
        }

        void RecordDebug(RenderGraph renderGraph, UniversalResourceData resourceData,
            VoxelGIFrame frame, TextureHandle debugTexture)
        {
            TextureHandle source = resourceData.activeColorTexture;
            TextureHandle output = CreateScreenTexture(renderGraph, source, "VoxelGI Debug", false);
            if (!debugTexture.IsValid())
                debugTexture = renderGraph.ImportTexture(frame.Resources.GetExternalTexture(Texture2D.blackTexture));
            BufferHandle emissive = renderGraph.ImportBuffer(frame.CameraContext.EmissiveAccumulation);

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                "VoxelGI/Debug", out DebugPassData data, DebugSampler))
            {
                data.Frame = frame;
                data.Albedo = renderGraph.ImportTexture(frame.CameraContext.AlbedoOpacity);
                data.Normal = renderGraph.ImportTexture(frame.CameraContext.Normal);
                data.Direct = renderGraph.ImportTexture(frame.CameraContext.DirectRadiance);
                data.Final = renderGraph.ImportTexture(frame.Settings.IndirectLighting.SecondBounce
                    ? frame.CameraContext.FinalRadiance
                    : frame.CameraContext.DirectRadiance);
                data.Shadow = renderGraph.ImportTexture(frame.CameraContext.ShadowDepth);
                data.DebugTexture = debugTexture;
                builder.UseTexture(data.Albedo, AccessFlags.Read);
                builder.UseTexture(data.Normal, AccessFlags.Read);
                builder.UseTexture(data.Direct, AccessFlags.Read);
                builder.UseTexture(data.Final, AccessFlags.Read);
                builder.UseTexture(data.Shadow, AccessFlags.Read);
                builder.UseTexture(data.DebugTexture, AccessFlags.Read);
                builder.UseBuffer(emissive, AccessFlags.Read);
                builder.SetRenderAttachment(output, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (DebugPassData passData, RasterGraphContext context) =>
                    ExecuteDebug(passData, context.cmd));
            }
            RenderGraphUtils.AddCopyPass(renderGraph, output, source, "VoxelGI Copy Debug To Camera");
        }

        static void ExecuteDebug(DebugPassData data, RasterCommandBuffer cmd)
        {
            Camera camera = data.Frame.CameraData.camera;
            Matrix4x4 gpuViewProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) *
                                               camera.worldToCameraMatrix;
            cmd.SetGlobalTexture(VoxelGIShaderIDs.AlbedoOpacity, data.Albedo);
            cmd.SetGlobalTexture(VoxelGIShaderIDs.Normal, data.Normal);
            cmd.SetGlobalTexture(VoxelGIShaderIDs.DirectRadiance, data.Direct);
            cmd.SetGlobalTexture(VoxelGIShaderIDs.FinalRadiance, data.Final);
            cmd.SetGlobalTexture(VoxelGIShaderIDs.ShadowMap, data.Shadow, RenderTextureSubElement.Depth);
            cmd.SetGlobalTexture("_VoxelGIDebugTexture", data.DebugTexture);
            cmd.SetGlobalBuffer("_VoxelGIEmissiveAccumulation", data.Frame.CameraContext.EmissiveAccumulation);
            cmd.SetGlobalMatrix(VoxelGIShaderIDs.WorldToVoxel, data.Frame.Matrices.WorldToVoxel);
            cmd.SetGlobalMatrix("_VoxelGIInverseViewProjection", gpuViewProjection.inverse);
            cmd.SetGlobalVector("_VoxelGICameraPosition", camera.transform.position);
            cmd.SetGlobalFloat("_VoxelGIGridWorldSize", data.Frame.Bounds.size.x);
            cmd.SetGlobalInt(VoxelGIShaderIDs.Resolution, data.Frame.Settings.Voxelization.Resolution);
            cmd.SetGlobalInt(VoxelGIShaderIDs.DebugMode, (int)data.Frame.Settings.Debug.Mode);
            cmd.SetGlobalInt(VoxelGIShaderIDs.DebugMipLevel, data.Frame.Settings.Debug.MipLevel);
            cmd.SetGlobalFloat(VoxelGIShaderIDs.DebugRayStep, data.Frame.Settings.Debug.RayStep);
            cmd.DrawProcedural(Matrix4x4.identity, data.Frame.Resources.FullscreenMaterial,
                data.Frame.Resources.DebugPass, MeshTopology.Triangles, 3);
        }

        static TextureHandle CreateScreenTexture(RenderGraph renderGraph, TextureHandle reference,
            string name, bool randomWrite)
        {
            TextureDesc descriptor = renderGraph.GetTextureDesc(reference);
            descriptor.name = name;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.black;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.enableRandomWrite = randomWrite;
            return renderGraph.CreateTexture(descriptor);
        }

        static Vector2Int GetScreenTargetSize(RenderGraph renderGraph, TextureHandle reference,
            UniversalCameraData cameraData)
        {
            TextureDesc descriptor = renderGraph.GetTextureDesc(reference);
            int width = descriptor.width > 0 ? descriptor.width : cameraData.scaledWidth;
            int height = descriptor.height > 0 ? descriptor.height : cameraData.scaledHeight;
            if (width <= 0)
                width = cameraData.camera.pixelWidth;
            if (height <= 0)
                height = cameraData.camera.pixelHeight;
            return new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
        }

        static Vector2 GetJitter(int index, VoxelGISettingsSnapshot.TemporalSettings settings)
        {
            if (settings.JitterSequence == VoxelGIJitterSequence.Halton)
                return new Vector2(Halton(index, 2), Halton(index, 3));
            const float conjugate = 0.618033988749895f;
            return new Vector2(Mathf.Repeat(index * conjugate, 1f),
                Mathf.Repeat(index * conjugate * conjugate, 1f));
        }

        static float Halton(int index, int radix)
        {
            int value = index % 1024;
            float result = 0f;
            float fraction = 1f / radix;
            while (value > 0)
            {
                result += value % radix * fraction;
                value /= radix;
                fraction /= radix;
            }
            return result;
        }
    }
}
