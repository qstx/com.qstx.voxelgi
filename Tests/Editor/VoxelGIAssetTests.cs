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
                     { "Assets/Materials/Blue.mat", "Assets/Materials/Emissive.mat", "Assets/Materials/Gray.mat",
                       "Assets/Materials/Green.mat", "Assets/Materials/Red.mat", "Assets/Materials/White.mat",
                       "Assets/Materials/Yellow.mat" })
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
        public void SampleVolumeProfileContainsVoxelGISettings()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(
                "Assets/Scenes/SampleScene/Voxel GI Volume Profile.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TryGet<QSTX.VoxelGI.VoxelGISettings>(out _), Is.True);
        }
    }
}
