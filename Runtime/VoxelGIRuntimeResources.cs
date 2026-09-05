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
            // 缓存 Kernel 索引和线程组尺寸，后续按输入规模计算 Dispatch 所需的组数。
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
        internal const GraphicsFormat VoxelFormat = GraphicsFormat.R16G16B16A16_SFloat;

        public RTHandle AlbedoOpacity { get; private set; }
        public RTHandle Normal { get; private set; }
        public RTHandle Emissive { get; private set; }
        public RTHandle DirectRadiance { get; private set; }
        public RTHandle FinalRadiance { get; private set; }
        public RTHandle ShadowDepth { get; private set; }
        public RTHandle HistoryA { get; private set; }
        public RTHandle HistoryB { get; private set; }

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
            // 体素分辨率或阴影分辨率变化时重建每相机的持久 3D 纹理和 ShadowDepth。
            // 原子累积 Buffer 与 Mip Scratch 属于对应 Render Graph Pass 的帧内资源，不在此分配。
            if (Resolution == resolution && ShadowResolution == shadowResolution &&
                AlbedoOpacity != null && Emissive != null)
                return false;

            ReleaseVoxelResources();
            Resolution = resolution;
            ShadowResolution = shadowResolution;
            MipCount = Mathf.FloorToInt(Mathf.Log(resolution, 2f)) + 1;

            AlbedoOpacity = AllocateVoxelTexture(resolution, false, "VoxelGI Albedo Opacity");
            Normal = AllocateVoxelTexture(resolution, false, "VoxelGI Normal");
            Emissive = AllocateVoxelTexture(resolution, false, "VoxelGI Emissive");
            DirectRadiance = AllocateVoxelTexture(resolution, true, "VoxelGI Direct Radiance");
            FinalRadiance = AllocateVoxelTexture(resolution, true, "VoxelGI Final Radiance");
            ShadowDepth = RTHandles.Alloc(
                shadowResolution, shadowResolution, 1, DepthBits.Depth32, GraphicsFormat.None,
                FilterMode.Bilinear, TextureWrapMode.Clamp, TextureDimension.Tex2D,
                false, false, false, true, name: "VoxelGI Shadow Depth");
            m_VoxelDataValid = false;
            InvalidateHistory();
            return true;
        }

        public bool EnsureHistory(int width, int height, bool required)
        {
            // Temporal 使用两张屏幕尺寸的 Ping-Pong History；分辨率变化时重新分配并清空历史状态。
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
            // EveryFrame 强制更新；OnChange/Manual 则通过设置、范围、注册表、Renderer 或手动版本号判断。
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
            // 体素数据不变时，仅当直接/间接光照参数或方向光状态发生变化才重新光照。
            return !m_VoxelDataValid || ComputeLightingHash(settings, light) != m_LastLightingHash;
        }

        public bool ShouldUpdateShadow(Light light)
        {
            return ComputeLightHash(light) != m_LastLightHash;
        }

        public void MarkVoxelized(VoxelGISettingsSnapshot settings, Bounds bounds, VoxelGIVolume volume,
            Light light, IReadOnlyList<VoxelGIRendererEntry> renderers, int registryVersion)
        {
            // 记录本次体素化使用的输入快照，供下一帧的 OnChange/Manual 判断复用结果。
            m_VoxelDataValid = true;
            m_LastSettingsHash = ComputeVoxelSettingsHash(settings);
            m_LastBoundsHash = bounds.GetHashCode();
            m_LastRegistryVersion = registryVersion;
            m_LastVolumeUpdateVersion = volume.UpdateVersion;
            m_LastLightHash = ComputeLightHash(light);
            // 只有 OnChange 会比较 Renderer 内容；EveryFrame 和 Manual 无需承担材质 CRC 的扫描开销。
            m_LastRendererHash = settings.Voxelization.UpdateMode == VoxelGIUpdateMode.OnChange
                ? ComputeRendererHash(renderers, out _)
                : 0;
        }

        public void MarkRelit(VoxelGISettingsSnapshot settings, Light light)
        {
            // 记录光照参数快照，使仅光照变化时不会误触发体素重建。
            m_LastLightingHash = ComputeLightingHash(settings, light);
            m_LastLightHash = ComputeLightHash(light);
        }

        public void InvalidateHistory()
        {
            // 体素范围、体素数据或光照变化后，旧屏幕结果不再对应当前数据，必须重新建立 Temporal History。
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
            hasActiveSkinnedRenderer = false;
            var materials = ListPool<Material>.Get();
            try
            {
                unchecked
                {
                    int hash = 17;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        VoxelGIRendererEntry entry = entries[i];
                        Renderer renderer = entry.Renderer;
                        if (renderer == null)
                            continue;
                        hash = hash * 31 + renderer.GetInstanceID();
                        hash = hash * 31 + entry.ContributeSurface.GetHashCode();
                        hash = hash * 31 + entry.OccludeRadiance.GetHashCode();
                        hash = hash * 31 + entry.CastVoxelShadow.GetHashCode();
                        hash = hash * 31 + renderer.enabled.GetHashCode();
                        hash = hash * 31 + renderer.gameObject.activeInHierarchy.GetHashCode();
                        hash = hash * 31 + renderer.localToWorldMatrix.GetHashCode();

                        // 遍历全部子材质，并使用内容 CRC 检测 Inspector 或脚本对属性、纹理引用和关键字的原地修改。
                        materials.Clear();
                        renderer.GetSharedMaterials(materials);
                        hash = hash * 31 + materials.Count;
                        for (int materialIndex = 0; materialIndex < materials.Count; materialIndex++)
                        {
                            Material material = materials[materialIndex];
                            if (material == null)
                            {
                                hash *= 31;
                                continue;
                            }
                            hash = hash * 31 + material.GetInstanceID();
                            hash = hash * 31 + material.ComputeCRC();
                            // 显式纳入 VoxelGI 依赖的关键字，避免不同 Unity 版本的 CRC 细节造成漏检。
                            hash = hash * 31 + material.IsKeywordEnabled(VoxelGIShaderKeywords.Emission).GetHashCode();
                            hash = hash * 31 + material.IsKeywordEnabled(VoxelGIShaderKeywords.AlphaTest).GetHashCode();
                        }

                        hasActiveSkinnedRenderer |= renderer is SkinnedMeshRenderer && renderer.enabled &&
                                                    renderer.gameObject.activeInHierarchy;
                    }
                    return hash;
                }
            }
            finally
            {
                ListPool<Material>.Release(materials);
            }
        }

        void ReleaseVoxelResources()
        {
            // 释放每相机持久体素纹理；下一次使用时由 EnsureVoxelResources 按新分辨率重新创建。
            AlbedoOpacity?.Release();
            Normal?.Release();
            Emissive?.Release();
            DirectRadiance?.Release();
            FinalRadiance?.Release();
            ShadowDepth?.Release();
            AlbedoOpacity = null;
            Normal = null;
            Emissive = null;
            DirectRadiance = null;
            FinalRadiance = null;
            ShadowDepth = null;
            m_VoxelDataValid = false;
        }

        void ReleaseHistory()
        {
            // 释放 Temporal 的双缓冲屏幕纹理并重置尺寸状态。
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
            // 创建全屏材质，缓存 Compute Kernel 和全屏 Shader Pass 索引，避免每帧查找。
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
            // 按 Camera InstanceID 隔离体素/History 资源，并更新最后使用帧用于过期回收。
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
            // 将外部蓝噪声等 Texture 缓存为 RTHandle，以便在 Render Graph 中作为 Imported 资源使用。
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
            // 回收一段时间未渲染的相机上下文，防止相机销毁或切换后持续占用 GPU 内存。
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
            // Renderer Feature 销毁时释放所有相机资源、外部纹理句柄和全屏材质。
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
