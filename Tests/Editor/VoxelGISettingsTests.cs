using NUnit.Framework;
using UnityEngine;

namespace QSTX.VoxelGI.Tests
{
    public sealed class VoxelGISettingsTests
    {
        VoxelGISettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<VoxelGISettings>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Settings);
        }

        [Test]
        public void SettingsAreInactiveByDefault()
        {
            Assert.That(m_Settings.IsActive(), Is.False);
        }

        [Test]
        public void ResolutionIsNormalizedToPowerOfTwo()
        {
            m_Settings.voxelResolution.value = 100;
            m_Settings.shadowResolution.value = 1000;
            VoxelGISettingsSnapshot snapshot = m_Settings.Resolve();
            Assert.That(snapshot.Voxelization.Resolution, Is.EqualTo(128));
            Assert.That(snapshot.Voxelization.ShadowResolution, Is.EqualTo(1024));
        }

        [Test]
        public void BilateralThresholdsNeverInvert()
        {
            m_Settings.depthThresholdLower.value = 0.8f;
            m_Settings.depthThresholdUpper.value = 0.1f;
            m_Settings.normalThresholdLower.value = 0.9f;
            m_Settings.normalThresholdUpper.value = 0.2f;
            VoxelGISettingsSnapshot snapshot = m_Settings.Resolve();
            Assert.That(snapshot.Bilateral.DepthThreshold.y, Is.GreaterThan(snapshot.Bilateral.DepthThreshold.x));
            Assert.That(snapshot.Bilateral.NormalThreshold.y, Is.GreaterThan(snapshot.Bilateral.NormalThreshold.x));
        }
    }
}
