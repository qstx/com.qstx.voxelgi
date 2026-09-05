using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QSTX.VoxelGI.Tests
{
    public sealed class VoxelGIAssetTests
    {
        // ComputeShader has no Shader.Find equivalent, so the package test
        // resolves it by its stable .meta GUID instead of a filesystem path.
        const string ComputeShaderGuid = "f9e2183f8d86eb8489c2352bf547e871";

        static T LoadPackageAsset<T>(string guid) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Assert.That(path, Is.Not.Empty, $"Package asset GUID {guid} is not registered.");
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        [Test]
        public void ComputeShaderContainsExpectedKernels()
        {
            var compute = LoadPackageAsset<ComputeShader>(ComputeShaderGuid);
            Assert.That(compute, Is.Not.Null);
            foreach (string kernel in new[]
                     { "ClearVoxelAccumulation", "VoxelizeMesh", "ResolveVoxelAccumulation", "VoxelDirectLighting",
                       "VoxelIndirectLighting", "MipmapGeneration", "CopyTexture3D", "BilateralFiltering" })
                Assert.DoesNotThrow(() => compute.FindKernel(kernel), kernel);
        }

        [Test]
        public void FullscreenShaderContainsOnlyNamedVoxelPasses()
        {
            var shader = Shader.Find("Hidden/QSTX/VoxelGI");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                Assert.That(material.FindPass("ScreenTrace"), Is.GreaterThanOrEqualTo(0));
                Assert.That(material.FindPass("TemporalFilter"), Is.GreaterThanOrEqualTo(0));
                Assert.That(material.FindPass("Composite"), Is.GreaterThanOrEqualTo(0));
                Assert.That(material.FindPass("DebugVisualization"), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void SampleMaterialsUseUrpLit()
        {
            foreach (string path in new[]
                     { "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Blue.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Emissive.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Gray.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Green.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Red.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/White.mat",
                       "Packages/com.qstx.voxelgi/Samples/SampleScene/Materials/Yellow.mat" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material, Is.Not.Null, path);
                Assert.That(material.shader, Is.Not.Null, path);
                Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"), path);
            }
        }

        [Test]
        public void BlockerContainsOnlyVoxelShadowPass()
        {
            var shader = Shader.Find("QSTX/VoxelGI/Blocker");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                Assert.That(material.FindPass("VoxelGIShadow"), Is.GreaterThanOrEqualTo(0));
                Assert.That(material.FindPass("UniversalForward"), Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void BlockerOccludesRadianceWithoutContributingSurface()
        {
            var shader = Shader.Find("QSTX/VoxelGI/Blocker");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var renderer = gameObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            try
            {
                VoxelGIRendererRegistry.MarkDirty();
                var entries = VoxelGIRendererRegistry.GetEntries(0f);
                bool found = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    VoxelGIRendererEntry entry = entries[i];
                    if (entry.Renderer != renderer)
                        continue;
                    found = true;
                    Assert.That(entry.ContributeSurface, Is.False);
                    Assert.That(entry.OccludeRadiance, Is.True);
                    Assert.That(entry.CastVoxelShadow, Is.True);
                    break;
                }
                Assert.That(found, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(material);
                VoxelGIRendererRegistry.MarkDirty();
            }
        }

        [Test]
        public void SampleVolumeProfileContainsVoxelGISettings()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(
                "Packages/com.qstx.voxelgi/Samples/SampleScene/Voxel GI Volume Profile.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryGet<QSTX.VoxelGI.VoxelGISettings>(out _), Is.True);
        }

        [Test]
        public void EmissionKeywordOverridesResidualLitProperties()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            try
            {
                material.SetColor("_EmissionColor", Color.white);
                material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                material.DisableKeyword(VoxelGIShaderKeywords.Emission);
                Assert.That(ComputeVoxelizer.IsEmissionEnabled(material), Is.False);

                material.EnableKeyword(VoxelGIShaderKeywords.Emission);
                Assert.That(ComputeVoxelizer.IsEmissionEnabled(material), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void OnChangeDetectsKeywordAndContentChangesOnAnySharedMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            var firstMaterial = new Material(shader);
            var secondMaterial = new Material(shader);
            var settings = ScriptableObject.CreateInstance<VoxelGISettings>();
            var rendererObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var volumeObject = new GameObject("VoxelGI Material Change Test Volume");
            var context = new VoxelGICameraContext();
            try
            {
                var renderer = rendererObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { firstMaterial, secondMaterial };
                var volume = volumeObject.AddComponent<VoxelGIVolume>();
                settings.updateMode.value = VoxelGIUpdateMode.OnChange;
                VoxelGISettingsSnapshot snapshot = settings.Resolve();
                var entries = new[] { new VoxelGIRendererEntry(renderer, true, true, true) };
                var bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
                const int registryVersion = 1;

                secondMaterial.SetColor("_EmissionColor", Color.white);
                secondMaterial.DisableKeyword(VoxelGIShaderKeywords.Emission);
                context.MarkVoxelized(snapshot, bounds, volume, null, entries, registryVersion);
                Assert.That(context.ShouldVoxelize(snapshot, bounds, volume, null, entries, registryVersion),
                    Is.False);

                secondMaterial.EnableKeyword(VoxelGIShaderKeywords.Emission);
                Assert.That(context.ShouldVoxelize(snapshot, bounds, volume, null, entries, registryVersion),
                    Is.True);

                context.MarkVoxelized(snapshot, bounds, volume, null, entries, registryVersion);
                Assert.That(context.ShouldVoxelize(snapshot, bounds, volume, null, entries, registryVersion),
                    Is.False);

                secondMaterial.SetColor("_BaseColor", Color.red);
                Assert.That(context.ShouldVoxelize(snapshot, bounds, volume, null, entries, registryVersion),
                    Is.True);
            }
            finally
            {
                context.Dispose();
                Object.DestroyImmediate(volumeObject);
                Object.DestroyImmediate(rendererObject);
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(firstMaterial);
                Object.DestroyImmediate(secondMaterial);
            }
        }
    }
}
