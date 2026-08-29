using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace QSTX.VoxelGI
{
    internal readonly struct VoxelGIComputeKernel
    {
        public readonly int Index;
        public readonly uint ThreadX;
        public readonly uint ThreadY;
        public readonly uint ThreadZ;

        public VoxelGIComputeKernel(ComputeShader shader, string name)
        {
            Index = shader.FindKernel(name);
            shader.GetKernelThreadGroupSizes(Index, out ThreadX, out ThreadY, out ThreadZ);
        }

        public int GroupsX(int count) => CeilDiv(count, ThreadX);
        public int GroupsY(int count) => CeilDiv(count, ThreadY);
        public int GroupsZ(int count) => CeilDiv(count, ThreadZ);

        static int CeilDiv(int value, uint divisor)
        {
            return Mathf.CeilToInt(value / (float)Mathf.Max(1, (int)divisor));
        }
    }

    internal sealed class VoxelGIKernels
    {
        public readonly VoxelGIComputeKernel Clear;
        public readonly VoxelGIComputeKernel Voxelize;
        public readonly VoxelGIComputeKernel Resolve;
        public readonly VoxelGIComputeKernel DirectLighting;
        public readonly VoxelGIComputeKernel IndirectLighting;
        public readonly VoxelGIComputeKernel GenerateMip;
        public readonly VoxelGIComputeKernel CopyMip;
        public readonly VoxelGIComputeKernel Bilateral;

        public VoxelGIKernels(ComputeShader shader)
        {
            Clear = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.Clear);
            Voxelize = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.Voxelize);
            Resolve = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.Resolve);
            DirectLighting = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.DirectLighting);
            IndirectLighting = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.IndirectLighting);
            GenerateMip = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.GenerateMip);
            CopyMip = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.CopyMip);
            Bilateral = new VoxelGIComputeKernel(shader, VoxelGIKernelNames.Bilateral);
        }
    }

    internal sealed class VoxelGICameraContext : IDisposable
    {
        const GraphicsFormat VoxelFormat = GraphicsFormat.R16G16B16A16_SFloat;

        public RTHandle AlbedoOpacity { get; private set; }
        public RTHandle Normal { get; private set; }
        public RTHandle DirectRadiance { get; private set; }
        public RTHandle FinalRadiance { get; private set; }
        public RTHandle MipScratch { get; private set; }
        public RTHandle ShadowDepth { get; private set; }
        public RTHandle HistoryA { get; private set; }
        public RTHandle HistoryB { get; private set; }

        public GraphicsBuffer AlbedoAccumulation { get; private set; }
        public GraphicsBuffer NormalAccumulation { get; private set; }
        public GraphicsBuffer EmissiveAccumulation { get; private set; }
        public GraphicsBuffer OpacityAccumulation { get; private set; }

        public int Resolution { get; private set; }
        public int ShadowResolution { get; private set; }
        public int MipCount { get; private set; }
        public int LastUsedFrame { get; set; }
        public bool HistoryWriteA { get; set; }
        public bool HistoryNeedsClear { get; set; } = true;
        public int JitterIndex { get; set; }

        bool m_VoxelDataValid;
        int m_LastSettingsHash;
        int m_LastBoundsHash;
        int m_LastRegistryVersion;
        int m_LastVolumeUpdateVersion;
        int m_LastLightHash;
        int m_LastLightingHash;
        int m_LastRendererHash;
        int m_HistoryWidth;
        int m_HistoryHeight;

        public bool EnsureVoxelResources(int resolution, int shadowResolution)
        {
            if (Resolution == resolution && ShadowResolution == shadowResolution && AlbedoOpacity != null)
                return false;

            ReleaseVoxelResources();
            Resolution = resolution;
            ShadowResolution = shadowResolution;
            MipCount = Mathf.FloorToInt(Mathf.Log(resolution, 2f)) + 1;
            int voxelCount = checked(resolution * resolution * resolution);

            AlbedoOpacity = AllocateVoxelTexture(resolution, false, "VoxelGI Albedo Opacity");
            Normal = AllocateVoxelTexture(resolution, false, "VoxelGI Normal");
            DirectRadiance = AllocateVoxelTexture(resolution, true, "VoxelGI Direct Radiance");
            FinalRadiance = AllocateVoxelTexture(resolution, true, "VoxelGI Final Radiance");
            MipScratch = AllocateVoxelTexture(resolution, true, "VoxelGI Mip Scratch");
            ShadowDepth = RTHandles.Alloc(
                shadowResolution, shadowResolution, 1, DepthBits.Depth32, GraphicsFormat.None,
                FilterMode.Bilinear, TextureWrapMode.Clamp, TextureDimension.Tex2D,
                false, false, false, true, name: "VoxelGI Shadow Depth");

            AlbedoAccumulation = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelCount, sizeof(uint));
            NormalAccumulation = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelCount, sizeof(uint));
            EmissiveAccumulation = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelCount, sizeof(uint) * 4);
            OpacityAccumulation = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxelCount, sizeof(uint));
            m_VoxelDataValid = false;
            InvalidateHistory();
            return true;
        }

        public bool EnsureHistory(int width, int height, bool required)
        {
            if (!required)
                return false;
            if (HistoryA != null && m_HistoryWidth == width && m_HistoryHeight == height)
                return false;

            ReleaseHistory();
            m_HistoryWidth = width;
            m_HistoryHeight = height;
            HistoryA = RTHandles.Alloc(width, height, colorFormat: VoxelFormat, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: "VoxelGI History A");
            HistoryB = RTHandles.Alloc(width, height, colorFormat: VoxelFormat, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, name: "VoxelGI History B");
            HistoryWriteA = true;
            HistoryNeedsClear = true;
            JitterIndex = 0;
            return true;
        }

        public bool ShouldVoxelize(VoxelGISettingsSnapshot settings, Bounds bounds, VoxelGIVolume volume,
            Light light, IReadOnlyList<VoxelGIRendererEntry> renderers, int registryVersion)
        {
            if (!m_VoxelDataValid || settings.Voxelization.UpdateMode == VoxelGIUpdateMode.EveryFrame)
                return true;

            int settingsHash = ComputeVoxelSettingsHash(settings);
            int boundsHash = bounds.GetHashCode();
            if (settingsHash != m_LastSettingsHash || boundsHash != m_LastBoundsHash)
                return true;

            if (settings.Voxelization.UpdateMode == VoxelGIUpdateMode.Manual)
                return volume.UpdateVersion != m_LastVolumeUpdateVersion;

            if (registryVersion != m_LastRegistryVersion)
                return true;

            int rendererHash = ComputeRendererHash(renderers, out bool hasActiveSkinnedRenderer);
            return hasActiveSkinnedRenderer || rendererHash != m_LastRendererHash;
        }

        public bool ShouldRelight(VoxelGISettingsSnapshot settings, Light light)
        {
            return !m_VoxelDataValid || ComputeLightingHash(settings, light) != m_LastLightingHash;
        }

        public bool ShouldUpdateShadow(Light light)
        {
            return ComputeLightHash(light) != m_LastLightHash;
        }

        public void MarkVoxelized(VoxelGISettingsSnapshot settings, Bounds bounds, VoxelGIVolume volume,
            Light light, IReadOnlyList<VoxelGIRendererEntry> renderers, int registryVersion)
        {
            m_VoxelDataValid = true;
            m_LastSettingsHash = ComputeVoxelSettingsHash(settings);
            m_LastBoundsHash = bounds.GetHashCode();
            m_LastRegistryVersion = registryVersion;
            m_LastVolumeUpdateVersion = volume.UpdateVersion;
            m_LastLightHash = ComputeLightHash(light);
            m_LastRendererHash = ComputeRendererHash(renderers, out _);
        }

        public void MarkRelit(VoxelGISettingsSnapshot settings, Light light)
        {
            m_LastLightingHash = ComputeLightingHash(settings, light);
            m_LastLightHash = ComputeLightHash(light);
        }

        public void InvalidateHistory()
        {
            HistoryNeedsClear = true;
            JitterIndex = 0;
        }

        static RTHandle AllocateVoxelTexture(int resolution, bool mipmapped, string name)
        {
            return RTHandles.Alloc(
                resolution, resolution, resolution, DepthBits.None, VoxelFormat,
                FilterMode.Bilinear, TextureWrapMode.Clamp, TextureDimension.Tex3D,
                true, mipmapped, false, false, name: name);
        }

        static int ComputeVoxelSettingsHash(VoxelGISettingsSnapshot settings)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + settings.Voxelization.Resolution;
                hash = hash * 31 + settings.Voxelization.ShadowResolution;
                hash = hash * 31 + settings.Voxelization.LayerMask.value;
                hash = hash * 31 + settings.Voxelization.ConservativeRasterization.GetHashCode();
                hash = hash * 31 + settings.Voxelization.ConservativeScale.GetHashCode();
                hash = hash * 31 + settings.Voxelization.UpdateMode.GetHashCode();
                return hash;
            }
        }

        static int ComputeLightingHash(VoxelGISettingsSnapshot settings, Light light)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + settings.DirectLighting.LightIntensity.GetHashCode();
                hash = hash * 31 + settings.DirectLighting.EmissiveIntensity.GetHashCode();
                hash = hash * 31 + settings.DirectLighting.ShadowSunBias.GetHashCode();
                hash = hash * 31 + settings.DirectLighting.ShadowNormalBias.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.SecondBounce.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.Quality.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.MaxSteps;
                hash = hash * 31 + settings.IndirectLighting.AlphaAttenuation.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.Intensity.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.FirstStep.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.StepScale.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.ConeAngle.GetHashCode();
                hash = hash * 31 + settings.IndirectLighting.MinMipLevel;
                hash = hash * 31 + ComputeLightHash(light);
                return hash;
            }
        }

        static int ComputeLightHash(Light light)
        {
            if (light == null)
                return 0;
            unchecked
            {
                int hash = light.GetInstanceID();
                hash = hash * 31 + light.transform.localToWorldMatrix.GetHashCode();
                hash = hash * 31 + light.color.GetHashCode();
                hash = hash * 31 + light.intensity.GetHashCode();
                hash = hash * 31 + light.enabled.GetHashCode();
                return hash;
            }
        }

        static int ComputeRendererHash(IReadOnlyList<VoxelGIRendererEntry> entries, out bool hasActiveSkinnedRenderer)
        {
            unchecked
            {
                int hash = 17;
                hasActiveSkinnedRenderer = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    Renderer renderer = entries[i].Renderer;
                    if (renderer == null)
                        continue;
                    hash = hash * 31 + renderer.GetInstanceID();
                    hash = hash * 31 + renderer.enabled.GetHashCode();
                    hash = hash * 31 + renderer.gameObject.activeInHierarchy.GetHashCode();
                    hash = hash * 31 + renderer.localToWorldMatrix.GetHashCode();
                    hash = hash * 31 + (renderer.sharedMaterial != null ? renderer.sharedMaterial.GetInstanceID() : 0);
                    hasActiveSkinnedRenderer |= renderer is SkinnedMeshRenderer && renderer.enabled &&
                                                renderer.gameObject.activeInHierarchy;
                }
                return hash;
            }
        }

        void ReleaseVoxelResources()
        {
            AlbedoOpacity?.Release();
            Normal?.Release();
            DirectRadiance?.Release();
            FinalRadiance?.Release();
            MipScratch?.Release();
            ShadowDepth?.Release();
            AlbedoOpacity = null;
            Normal = null;
            DirectRadiance = null;
            FinalRadiance = null;
            MipScratch = null;
            ShadowDepth = null;
            AlbedoAccumulation?.Dispose();
            NormalAccumulation?.Dispose();
            EmissiveAccumulation?.Dispose();
            OpacityAccumulation?.Dispose();
            AlbedoAccumulation = null;
            NormalAccumulation = null;
            EmissiveAccumulation = null;
            OpacityAccumulation = null;
            m_VoxelDataValid = false;
        }

        void ReleaseHistory()
        {
            HistoryA?.Release();
            HistoryB?.Release();
            HistoryA = null;
            HistoryB = null;
            m_HistoryWidth = 0;
            m_HistoryHeight = 0;
            HistoryNeedsClear = true;
        }

        public void Dispose()
        {
            ReleaseHistory();
            ReleaseVoxelResources();
        }
    }

    internal sealed class VoxelGIRuntimeResources : IDisposable
    {
        readonly Dictionary<int, VoxelGICameraContext> m_CameraContexts =
            new Dictionary<int, VoxelGICameraContext>();
        readonly Dictionary<Texture, RTHandle> m_ExternalTextures = new Dictionary<Texture, RTHandle>();

        public Material FullscreenMaterial { get; }
        public ComputeShader ComputeShader { get; }
        public VoxelGIKernels Kernels { get; }
        public int ScreenTracePass { get; }
        public int TemporalPass { get; }
        public int CompositePass { get; }
        public int DebugPass { get; }

        public VoxelGIRuntimeResources(Shader fullscreenShader, ComputeShader computeShader)
        {
            ComputeShader = computeShader;
            FullscreenMaterial = CoreUtils.CreateEngineMaterial(fullscreenShader);
            Kernels = new VoxelGIKernels(computeShader);
            ScreenTracePass = FindPass(VoxelGIShaderPassNames.ScreenTrace);
            TemporalPass = FindPass(VoxelGIShaderPassNames.Temporal);
            CompositePass = FindPass(VoxelGIShaderPassNames.Composite);
            DebugPass = FindPass(VoxelGIShaderPassNames.Debug);
        }

        public VoxelGICameraContext GetContext(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (!m_CameraContexts.TryGetValue(id, out VoxelGICameraContext context))
            {
                context = new VoxelGICameraContext();
                m_CameraContexts.Add(id, context);
            }
            context.LastUsedFrame = Time.frameCount;
            return context;
        }

        public RTHandle GetExternalTexture(Texture texture)
        {
            texture ??= Texture2D.grayTexture;
            if (!m_ExternalTextures.TryGetValue(texture, out RTHandle handle))
            {
                handle = RTHandles.Alloc(texture);
                m_ExternalTextures.Add(texture, handle);
            }
            return handle;
        }

        public void ReleaseUnusedContexts(int maxUnusedFrames = 8)
        {
            if (m_CameraContexts.Count == 0)
                return;
            var stale = ListPool<int>.Get();
            foreach (var pair in m_CameraContexts)
            {
                if (Time.frameCount - pair.Value.LastUsedFrame > maxUnusedFrames)
                    stale.Add(pair.Key);
            }
            foreach (int id in stale)
            {
                m_CameraContexts[id].Dispose();
                m_CameraContexts.Remove(id);
            }
            ListPool<int>.Release(stale);
        }

        int FindPass(string passName)
        {
            int index = FullscreenMaterial.FindPass(passName);
            if (index < 0)
                throw new InvalidOperationException($"VoxelGI shader pass '{passName}' was not found.");
            return index;
        }

        public void Dispose()
        {
            foreach (VoxelGICameraContext context in m_CameraContexts.Values)
                context.Dispose();
            m_CameraContexts.Clear();
            foreach (RTHandle handle in m_ExternalTextures.Values)
                handle.Release();
            m_ExternalTextures.Clear();
            CoreUtils.Destroy(FullscreenMaterial);
        }
    }
}
