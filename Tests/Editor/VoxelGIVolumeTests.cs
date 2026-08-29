using NUnit.Framework;
using UnityEngine;

namespace QSTX.VoxelGI.Tests
{
    public sealed class VoxelGIVolumeTests
    {
        GameObject m_VolumeObject;
        GameObject m_BoundsObject;

        [SetUp]
        public void SetUp()
        {
            m_VolumeObject = new GameObject("Test Voxel GI Volume");
            m_VolumeObject.AddComponent<BoxCollider>().isTrigger = true;
            m_BoundsObject = new GameObject("Test Voxelization Bounds");
            var boundsCollider = m_BoundsObject.AddComponent<BoxCollider>();
            boundsCollider.size = new Vector3(2f, 4f, 6f);
            var volume = m_VolumeObject.AddComponent<VoxelGIVolume>();
            typeof(VoxelGIVolume).GetField("m_VoxelizationBounds",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(volume, boundsCollider);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_VolumeObject);
            Object.DestroyImmediate(m_BoundsObject);
        }

        [Test]
        public void VoxelGridBoundsAreWorldAlignedAndCubic()
        {
            var volume = m_VolumeObject.GetComponent<VoxelGIVolume>();
            Assert.That(volume.TryGetVoxelGridBounds(out Bounds bounds), Is.True);
            Assert.That(bounds.size.x, Is.EqualTo(6f).Within(0.001f));
            Assert.That(bounds.size.y, Is.EqualTo(6f).Within(0.001f));
            Assert.That(bounds.size.z, Is.EqualTo(6f).Within(0.001f));
        }
    }
}
