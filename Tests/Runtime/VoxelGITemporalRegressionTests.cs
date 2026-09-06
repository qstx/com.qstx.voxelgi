using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace QSTX.VoxelGI.Tests
{
    public sealed class VoxelGITemporalRegressionTests
    {
        const int SyntheticSize = 16;
        const int SceneWidth = 320;
        const int SceneHeight = 180;
        const int SettleFrameCount = 64;
        const float PixelTolerance = 0.01f;
        const string SampleScenePath = "Packages/com.qstx.voxelgi/Samples/SampleScene/SampleScene.unity";

        [UnityTest]
        public IEnumerator TemporalShaderWithZeroRequestedWeightAccumulatesDeterministically()
        {
            RequireGraphicsSupport();
            Shader shader = Shader.Find("Hidden/QSTX/VoxelGI");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);

            var material = new Material(shader);
            int temporalPass = material.FindPass(VoxelGIShaderPassNames.Temporal);
            Assert.That(temporalPass, Is.GreaterThanOrEqualTo(0));

            Texture2D patternA = CreateCheckerboard(false);
            Texture2D patternB = CreateCheckerboard(true);
            Texture2D motion = CreateSolidTexture(Color.clear);
            RenderTexture historyA = CreateHistoryTexture("VoxelGI Test History A");
            RenderTexture historyB = CreateHistoryTexture("VoxelGI Test History B");
            Texture previousCurrent = Shader.GetGlobalTexture(VoxelGIShaderIDs.CurrentIrradiance);
            Texture previousHistory = Shader.GetGlobalTexture(VoxelGIShaderIDs.HistoryIrradiance);
            Texture previousMotion = Shader.GetGlobalTexture("_VoxelGIMotionVectors");
            Vector4 previousScreenSize = Shader.GetGlobalVector(VoxelGIShaderIDs.ScreenSize);
            float previousWeight = Shader.GetGlobalFloat("_VoxelGITemporalCurrentFrameWeight");
            float previousClamp = Shader.GetGlobalFloat("_VoxelGITemporalClampScale");
            int previousHistoryValid = Shader.GetGlobalInt("_VoxelGIHistoryValid");

            try
            {
                RenderTemporal(material, temporalPass, patternA, historyB, motion, historyA, false, 0f, 100f);
                RenderTexture source = historyA;
                RenderTexture destination = historyB;
                int sampleX = SyntheticSize / 2;
                int sampleY = SyntheticSize / 2;
                float expected = CheckerboardValue(sampleX, sampleY, false);

                for (int frame = 0; frame < SettleFrameCount; frame++)
                {
                    bool invert = (frame & 1) == 0;
                    Texture2D current = invert ? patternB : patternA;
                    float currentValue = CheckerboardValue(sampleX, sampleY, invert);
                    expected = Mathf.Lerp(expected, currentValue, VoxelGISettings.MinTemporalCurrentFrameWeight);
                    RenderTemporal(material, temporalPass, current, source, motion, destination,
                        true, 0f, 100f);
                    (source, destination) = (destination, source);
                }

                Color[] pixels = Readback(source);
                Color sample = pixels[sampleY * SyntheticSize + sampleX];
                Assert.That(sample.r, Is.EqualTo(expected).Within(PixelTolerance));
                Assert.That(sample.g, Is.EqualTo(expected).Within(PixelTolerance));
                Assert.That(sample.b, Is.EqualTo(expected).Within(PixelTolerance));
                AssertFinite(pixels);
            }
            finally
            {
                Shader.SetGlobalTexture(VoxelGIShaderIDs.CurrentIrradiance, previousCurrent);
                Shader.SetGlobalTexture(VoxelGIShaderIDs.HistoryIrradiance, previousHistory);
                Shader.SetGlobalTexture("_VoxelGIMotionVectors", previousMotion);
                Shader.SetGlobalVector(VoxelGIShaderIDs.ScreenSize, previousScreenSize);
                Shader.SetGlobalFloat("_VoxelGITemporalCurrentFrameWeight", previousWeight);
                Shader.SetGlobalFloat("_VoxelGITemporalClampScale", previousClamp);
                Shader.SetGlobalInt("_VoxelGIHistoryValid", previousHistoryValid);
                Object.DestroyImmediate(patternA);
                Object.DestroyImmediate(patternB);
                Object.DestroyImmediate(motion);
                Object.DestroyImmediate(material);
                Release(historyA);
                Release(historyB);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SampleSceneTemporalConvergesInEveryFrameMode()
        {
            RequireGraphicsSupport();
            AsyncOperation load = SceneManager.LoadSceneAsync(SampleScenePath, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            VoxelGIVolume[] volumes = Object.FindObjectsByType<VoxelGIVolume>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Assert.That(cameras, Is.Not.Empty);
            Assert.That(volumes, Is.Not.Empty);

            Camera camera = cameras[0];
            VoxelGIVolume volume = volumes[0];
            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(cameraData, Is.Not.Null);
            VolumeProfile profile = volume.profile;
            Assert.That(profile.TryGet(out VoxelGISettings settings), Is.True);

            RenderTexture previousTarget = camera.targetTexture;
            bool previousPostProcessing = cameraData.renderPostProcessing;
            AntialiasingMode previousAntialiasing = cameraData.antialiasing;
            bool previousAllowMsaa = camera.allowMSAA;
            bool previousEnable = settings.enable.value;
            bool previousTemporal = settings.temporalFilter.value;
            float previousWeight = settings.temporalCurrentFrameWeight.value;
            VoxelGIUpdateMode previousUpdateMode = settings.updateMode.value;
            VoxelGIDebugMode previousDebugMode = settings.debugMode.value;
            Behaviour cameraController = FindCameraController(camera);
            bool previousControllerEnabled = cameraController != null && cameraController.enabled;
            var target = new RenderTexture(SceneWidth, SceneHeight, 24, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = "VoxelGI Temporal Regression Target",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            target.Create();

            try
            {
                if (cameraController != null)
                    cameraController.enabled = false;
                cameraData.renderPostProcessing = false;
                cameraData.antialiasing = AntialiasingMode.None;
                camera.allowMSAA = false;
                camera.targetTexture = target;
                settings.enable.value = true;
                settings.temporalFilter.value = true;
                settings.temporalCurrentFrameWeight.value = 0f;
                settings.updateMode.value = VoxelGIUpdateMode.EveryFrame;
                settings.debugMode.value = VoxelGIDebugMode.Temporal;

                yield return new WaitForEndOfFrame();
                Color[] earlyA = Readback(target);
                yield return new WaitForEndOfFrame();
                Color[] earlyB = Readback(target);
                VoxelGICameraContext context = GetCameraContext(camera);
                int initialJitter = context.JitterIndex;

                for (int frame = 0; frame < SettleFrameCount; frame++)
                    yield return new WaitForEndOfFrame();

                Color[] lateA = Readback(target);
                yield return new WaitForEndOfFrame();
                Color[] lateB = Readback(target);
                float earlyDelta = MeanAbsoluteLuminanceDelta(earlyA, earlyB, out int earlyPixels);
                float lateDelta = MeanAbsoluteLuminanceDelta(lateA, lateB, out int latePixels);

                Assert.That(earlyPixels, Is.GreaterThan(100));
                Assert.That(latePixels, Is.GreaterThan(100));
                Assert.That(earlyDelta, Is.GreaterThan(1e-6f));
                Assert.That(lateDelta, Is.LessThan(earlyDelta * 0.75f),
                    $"Temporal did not converge: early delta={earlyDelta}, late delta={lateDelta}");
                Assert.That(context.HistoryNeedsClear, Is.False);
                Assert.That(context.JitterIndex - initialJitter, Is.GreaterThanOrEqualTo(SettleFrameCount));
                AssertFinite(lateB);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.allowMSAA = previousAllowMsaa;
                cameraData.renderPostProcessing = previousPostProcessing;
                cameraData.antialiasing = previousAntialiasing;
                settings.enable.value = previousEnable;
                settings.temporalFilter.value = previousTemporal;
                settings.temporalCurrentFrameWeight.value = previousWeight;
                settings.updateMode.value = previousUpdateMode;
                settings.debugMode.value = previousDebugMode;
                if (cameraController != null)
                    cameraController.enabled = previousControllerEnabled;
                Release(target);
            }
        }

        static void RequireGraphicsSupport()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DTextures ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat,
                    GraphicsFormatUsage.LoadStore))
            {
                Assert.Ignore("VoxelGI Temporal regression tests require Compute Shaders, 3D textures, and RGBA16F Load/Store support.");
            }
        }

        static Texture2D CreateCheckerboard(bool invert)
        {
            var texture = new Texture2D(SyntheticSize, SyntheticSize, TextureFormat.RGBAFloat, false, true)
            {
                name = invert ? "VoxelGI Inverted Checkerboard" : "VoxelGI Checkerboard",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[SyntheticSize * SyntheticSize];
            for (int y = 0; y < SyntheticSize; y++)
            for (int x = 0; x < SyntheticSize; x++)
            {
                float value = CheckerboardValue(x, y, invert);
                pixels[y * SyntheticSize + x] = new Color(value, value, value, 1f);
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(SyntheticSize, SyntheticSize, TextureFormat.RGBAFloat, false, true)
            {
                name = "VoxelGI Zero Motion",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[SyntheticSize * SyntheticSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static float CheckerboardValue(int x, int y, bool invert)
        {
            bool high = ((x + y) & 1) != 0;
            if (invert)
                high = !high;
            return high ? 0.75f : 0.25f;
        }

        static RenderTexture CreateHistoryTexture(string name)
        {
            var texture = new RenderTexture(SyntheticSize, SyntheticSize, 0, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.Create();
            return texture;
        }

        static void RenderTemporal(Material material, int pass, Texture current, Texture history,
            Texture motion, RenderTexture destination, bool historyValid, float currentWeight, float clampScale)
        {
            CommandBuffer command = CommandBufferPool.Get("VoxelGI Temporal Regression");
            try
            {
                command.SetRenderTarget(destination);
                command.ClearRenderTarget(false, true, Color.black);
                command.SetGlobalTexture(VoxelGIShaderIDs.CurrentIrradiance, current);
                command.SetGlobalTexture(VoxelGIShaderIDs.HistoryIrradiance, history);
                command.SetGlobalTexture("_VoxelGIMotionVectors", motion);
                command.SetGlobalVector(VoxelGIShaderIDs.ScreenSize,
                    new Vector4(SyntheticSize, SyntheticSize, 1f / SyntheticSize, 1f / SyntheticSize));
                command.SetGlobalInt("_VoxelGIHistoryValid", historyValid ? 1 : 0);
                command.SetGlobalFloat("_VoxelGITemporalCurrentFrameWeight", currentWeight);
                command.SetGlobalFloat("_VoxelGITemporalClampScale", clampScale);
                command.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(command);
            }
            finally
            {
                CommandBufferPool.Release(command);
            }
        }

        static Color[] Readback(RenderTexture source)
        {
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = source;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                texture.Apply(false, false);
                return texture.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
            }
        }

        static float MeanAbsoluteLuminanceDelta(Color[] first, Color[] second, out int validPixelCount)
        {
            Assert.That(second.Length, Is.EqualTo(first.Length));
            float total = 0f;
            validPixelCount = 0;
            for (int i = 0; i < first.Length; i++)
            {
                float firstLuminance = Luminance(first[i]);
                float secondLuminance = Luminance(second[i]);
                if (Mathf.Max(firstLuminance, secondLuminance) <= 0.01f)
                    continue;
                total += Mathf.Abs(firstLuminance - secondLuminance);
                validPixelCount++;
            }
            return validPixelCount > 0 ? total / validPixelCount : 0f;
        }

        static float Luminance(Color value) =>
            value.r * 0.2126f + value.g * 0.7152f + value.b * 0.0722f;

        static void AssertFinite(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color value = pixels[i];
                Assert.That(float.IsNaN(value.r) || float.IsInfinity(value.r) ||
                            float.IsNaN(value.g) || float.IsInfinity(value.g) ||
                            float.IsNaN(value.b) || float.IsInfinity(value.b) ||
                            float.IsNaN(value.a) || float.IsInfinity(value.a), Is.False,
                    $"Non-finite Temporal output at pixel {i}: {value}");
            }
        }

        static Behaviour FindCameraController(Camera camera)
        {
            Behaviour[] behaviours = camera.GetComponents<Behaviour>();
            foreach (Behaviour behaviour in behaviours)
            {
                if (behaviour != null && behaviour.GetType().Name == "FreeFlyCameraController")
                    return behaviour;
            }
            return null;
        }

        static VoxelGICameraContext GetCameraContext(Camera camera)
        {
            VoxelGIRendererFeature[] features =
                Resources.FindObjectsOfTypeAll<VoxelGIRendererFeature>();
            foreach (VoxelGIRendererFeature feature in features)
            {
                if (feature != null && feature.RuntimeResources != null)
                    return feature.RuntimeResources.GetContext(camera);
            }
            Assert.Fail("Active VoxelGI renderer feature was not found.");
            return null;
        }

        static void Release(RenderTexture texture)
        {
            if (texture == null)
                return;
            if (texture.IsCreated())
                texture.Release();
            Object.DestroyImmediate(texture);
        }
    }
}
