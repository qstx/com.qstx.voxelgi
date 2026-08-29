using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QSTX.VoxelGI.Tests
{
    public sealed class VoxelGIRuntimePlayModeTests
    {
        [Test]
        public void UnityVersionAndRuntimeTypesAreAvailable()
        {
            Assert.That(Application.unityVersion, Does.StartWith("6000.3"));
            Assert.That(typeof(VoxelGIRendererFeature), Is.Not.Null);
            Assert.That(typeof(VoxelGISettings), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ContributorSurvivesOneRuntimeFrame()
        {
            var gameObject = new GameObject("VoxelGI Runtime Contributor Test");
            try
            {
                Assert.That(gameObject.AddComponent<VoxelGIContributor>(), Is.Not.Null);
                yield return null;
                Assert.That(gameObject.activeInHierarchy, Is.True);
            }
            finally
            {
                Object.Destroy(gameObject);
            }
        }
    }
}
