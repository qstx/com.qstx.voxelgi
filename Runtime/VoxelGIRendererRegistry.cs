using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QSTX.VoxelGI
{
    internal readonly struct VoxelGIRendererEntry
    {
        public readonly Renderer Renderer;
        public readonly bool ContributeSurface;
        public readonly bool OccludeRadiance;
        public readonly bool CastVoxelShadow;

        public VoxelGIRendererEntry(Renderer renderer, bool contributeSurface, bool occludeRadiance,
            bool castVoxelShadow)
        {
            Renderer = renderer;
            ContributeSurface = contributeSurface;
            OccludeRadiance = occludeRadiance;
            CastVoxelShadow = castVoxelShadow;
        }
    }

    internal static class VoxelGIRendererRegistry
    {
        static readonly List<VoxelGIRendererEntry> Entries = new List<VoxelGIRendererEntry>(256);
        static readonly Dictionary<Renderer, VoxelGIRendererEntry> ExplicitEntries =
            new Dictionary<Renderer, VoxelGIRendererEntry>();

        static bool s_NeedsScan = true;
        static float s_LastScanTime = float.NegativeInfinity;
        static int s_Version;

        public static int Version => s_Version;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            Entries.Clear();
            ExplicitEntries.Clear();
            s_NeedsScan = true;
            s_LastScanTime = float.NegativeInfinity;
            s_Version = 0;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => MarkDirty();
        static void OnSceneUnloaded(Scene scene) => MarkDirty();

        public static IReadOnlyList<VoxelGIRendererEntry> GetEntries(float fallbackRescanInterval)
        {
            float now = Time.realtimeSinceStartup;
            if (s_NeedsScan || (fallbackRescanInterval > 0f && now - s_LastScanTime >= fallbackRescanInterval))
                Rescan(now);
            return Entries;
        }

        public static void Register(Renderer renderer, bool contributeSurface, bool castVoxelShadow)
        {
            if (renderer == null)
                return;
            ExplicitEntries[renderer] = new VoxelGIRendererEntry(renderer, contributeSurface,
                contributeSurface || castVoxelShadow, castVoxelShadow);
            MarkDirty();
        }

        public static void Unregister(Renderer renderer)
        {
            if (renderer != null && ExplicitEntries.Remove(renderer))
                MarkDirty();
        }

        public static void MarkDirty()
        {
            s_NeedsScan = true;
            unchecked { s_Version++; }
        }

        static void Rescan(float now)
        {
            Entries.Clear();
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Renderer renderer in renderers)
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                    continue;
                if (ExplicitEntries.TryGetValue(renderer, out VoxelGIRendererEntry explicitEntry))
                    Entries.Add(explicitEntry);
                else
                {
                    Material material = renderer.sharedMaterial;
                    bool blockerOnly = material != null && material.FindPass("ShadowCaster") < 0 &&
                                       material.FindPass("VoxelGIShadow") >= 0;
                    Entries.Add(new VoxelGIRendererEntry(renderer, !blockerOnly, true, true));
                }
            }
            s_LastScanTime = now;
            s_NeedsScan = false;
        }
    }
}
