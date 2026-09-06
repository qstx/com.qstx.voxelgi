using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QSTX.VoxelGI.Samples
{
    [DisallowMultipleComponent]
    public sealed class VoxelGIRuntimePanel : MonoBehaviour
    {
        [SerializeField] bool m_StartExpanded = true;

        Rect m_WindowRect = new Rect(0f, 16f, 380f, 720f);
        Vector2 m_ScrollPosition;
        bool m_Expanded;
        float m_SmoothedFps;
        VoxelGIVolume m_Volume;
        VoxelGISettings m_Settings;
        GUIStyle m_SectionStyle;

        void Awake()
        {
            m_Expanded = m_StartExpanded;
            m_Volume = GetComponent<VoxelGIVolume>();
            if (m_Volume == null)
                m_Volume = FindFirstObjectByType<VoxelGIVolume>();
        }

        void OnGUI()
        {
            if (!TryGetSettings())
                return;

            float height = m_Expanded ? Mathf.Min(Screen.height - 32f, 760f) : 58f;
            m_WindowRect.width = Mathf.Min(390f, Screen.width - 32f);
            m_WindowRect.height = Mathf.Max(58f, height);
            m_WindowRect.x = Screen.width - m_WindowRect.width - 16f;
            m_WindowRect.y = Mathf.Clamp(m_WindowRect.y, 16f, Mathf.Max(16f, Screen.height - m_WindowRect.height - 16f));
            m_WindowRect = GUI.Window(GetInstanceID(), m_WindowRect, DrawWindow, "VoxelGI 参数");
        }

        void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f)
                return;
            float currentFps = 1f / deltaTime;
            float blend = 1f - Mathf.Exp(-8f * deltaTime);
            m_SmoothedFps = m_SmoothedFps <= 0f
                ? currentFps
                : Mathf.Lerp(m_SmoothedFps, currentFps, blend);
        }

        bool TryGetSettings()
        {
            if (m_Volume == null || m_Volume.sharedProfile == null)
                return false;
            return m_Volume.sharedProfile.TryGet(out m_Settings);
        }

        void DrawWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"FPS {m_SmoothedFps:0.0}", GUILayout.Width(76f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(m_Expanded ? "收起 ▲" : "展开 ▼", GUILayout.Width(90f)))
                m_Expanded = !m_Expanded;
            GUILayout.EndHorizontal();

            if (m_Expanded)
            {
                m_ScrollPosition = GUILayout.BeginScrollView(m_ScrollPosition, false, true);
                DrawVoxelization();
                DrawDirectLighting();
                DrawIndirectLighting();
                DrawScreenTracing();
                DrawTemporal();
                DrawBilateral();
                DrawDebug();
                GUILayout.EndScrollView();
            }

            GUI.DragWindow(new Rect(0f, 0f, 260f, 24f));
        }

        void DrawVoxelization()
        {
            Section("常规与体素化");
            DrawToggle("启用 VoxelGI", () => m_Settings.enable.value,
                value => Set(m_Settings.enable, value));
            DrawIntSlider("体素分辨率", 16, 256, () => m_Settings.voxelResolution.value,
                value => { Set(m_Settings.voxelResolution, value); RequestVoxelization(); });
            DrawIntSlider("阴影分辨率", 64, 4096, () => m_Settings.shadowResolution.value,
                value => { Set(m_Settings.shadowResolution, value); RequestVoxelization(); });
            DrawToggle("保守体素化", () => m_Settings.conservativeRasterization.value,
                value => { Set(m_Settings.conservativeRasterization, value); RequestVoxelization(); });
            DrawSlider("保守扩张尺度", 0f, 3f, () => m_Settings.conservativeScale.value,
                value => { Set(m_Settings.conservativeScale, value); RequestVoxelization(); });
            DrawEnum("体素更新模式", () => m_Settings.updateMode.value,
                value => { Set(m_Settings.updateMode, value); RequestVoxelization(); });
        }

        void DrawDirectLighting()
        {
            Section("直接光照");
            DrawSlider("光照强度", 0f, 10f, () => m_Settings.lightIntensity.value,
                value => Set(m_Settings.lightIntensity, value));
            DrawSlider("自发光强度", 0f, 10f, () => m_Settings.emissiveIntensity.value,
                value => Set(m_Settings.emissiveIntensity, value));
            DrawSlider("光照方向偏移", 0f, 5f, () => m_Settings.shadowSunBias.value,
                value => Set(m_Settings.shadowSunBias, value));
            DrawSlider("法线偏移", 0f, 5f, () => m_Settings.shadowNormalBias.value,
                value => Set(m_Settings.shadowNormalBias, value));
        }

        void DrawIndirectLighting()
        {
            Section("间接光照");
            DrawToggle("二次反弹", () => m_Settings.secondBounce.value,
                value => Set(m_Settings.secondBounce, value));
            DrawEnum("间接光质量", () => m_Settings.indirectQuality.value,
                value => Set(m_Settings.indirectQuality, value));
            DrawIntSlider("间接追踪步数", 1, 32, () => m_Settings.indirectMaxSteps.value,
                value => Set(m_Settings.indirectMaxSteps, value));
            DrawSlider("间接透明度衰减", 1f, 10f, () => m_Settings.indirectAlphaAttenuation.value,
                value => Set(m_Settings.indirectAlphaAttenuation, value));
            DrawSlider("间接光强度", 0f, 10f, () => m_Settings.indirectIntensity.value,
                value => Set(m_Settings.indirectIntensity, value));
            DrawSlider("间接首步距离", 0.5f, 3f, () => m_Settings.indirectFirstStep.value,
                value => Set(m_Settings.indirectFirstStep, value));
            DrawSlider("间接步长倍率", 1f, 3f, () => m_Settings.indirectStepScale.value,
                value => Set(m_Settings.indirectStepScale, value));
            DrawSlider("间接 Cone 角度", 20f, 150f, () => m_Settings.indirectConeAngle.value,
                value => Set(m_Settings.indirectConeAngle, value));
            DrawIntSlider("间接最小 Mip", 0, 5, () => m_Settings.indirectMinMipLevel.value,
                value => Set(m_Settings.indirectMinMipLevel, value));
        }

        void DrawScreenTracing()
        {
            Section("屏幕 Cone Tracing");
            DrawEnum("屏幕采样质量", () => m_Settings.screenQuality.value,
                value => Set(m_Settings.screenQuality, value));
            DrawIntSlider("屏幕追踪步数", 1, 32, () => m_Settings.screenMaxSteps.value,
                value => Set(m_Settings.screenMaxSteps, value));
            DrawSlider("屏幕透明度衰减", 1f, 10f, () => m_Settings.screenAlphaAttenuation.value,
                value => Set(m_Settings.screenAlphaAttenuation, value));
            DrawSlider("屏幕间接光强度", 0f, 10f, () => m_Settings.screenIntensity.value,
                value => Set(m_Settings.screenIntensity, value));
            DrawSlider("屏幕首步距离", 0.5f, 3f, () => m_Settings.screenFirstStep.value,
                value => Set(m_Settings.screenFirstStep, value));
            DrawSlider("屏幕步长倍率", 1f, 3f, () => m_Settings.screenStepScale.value,
                value => Set(m_Settings.screenStepScale, value));
            DrawSlider("屏幕 Cone 角度", 20f, 150f, () => m_Settings.screenConeAngle.value,
                value => Set(m_Settings.screenConeAngle, value));
        }

        void DrawTemporal()
        {
            Section("Temporal Filter");
            DrawToggle("启用时域滤波", () => m_Settings.temporalFilter.value,
                value => Set(m_Settings.temporalFilter, value));
            DrawSlider("当前帧权重", 0.02f, 1f, () => m_Settings.temporalCurrentFrameWeight.value,
                value => Set(m_Settings.temporalCurrentFrameWeight, value));
            DrawSlider("History 裁剪倍率", 0f, 4f, () => m_Settings.temporalClampScale.value,
                value => Set(m_Settings.temporalClampScale, value));
            DrawEnum("抖动序列", () => m_Settings.jitterSequence.value,
                value => Set(m_Settings.jitterSequence, value));
            DrawIntSlider("Halton 序列长度", 2, 64, () => m_Settings.haltonLength.value,
                value => Set(m_Settings.haltonLength, value));
        }

        void DrawBilateral()
        {
            Section("Bilateral Filter");
            DrawToggle("启用边缘保持滤波", () => m_Settings.bilateralFilter.value,
                value => Set(m_Settings.bilateralFilter, value));
            DrawSlider("滤波半径", 0f, 10f, () => m_Settings.bilateralRadius.value,
                value => Set(m_Settings.bilateralRadius, value));
            DrawSlider("深度阈值下限", 0f, 2f, () => m_Settings.depthThresholdLower.value,
                value => Set(m_Settings.depthThresholdLower, value));
            DrawSlider("深度阈值上限", 0f, 2f, () => m_Settings.depthThresholdUpper.value,
                value => Set(m_Settings.depthThresholdUpper, value));
            DrawSlider("法线阈值下限", 0f, 1f, () => m_Settings.normalThresholdLower.value,
                value => Set(m_Settings.normalThresholdLower, value));
            DrawSlider("法线阈值上限", 0f, 1f, () => m_Settings.normalThresholdUpper.value,
                value => Set(m_Settings.normalThresholdUpper, value));
        }

        void DrawDebug()
        {
            Section("调试");
            DrawEnum("调试模式", () => m_Settings.debugMode.value,
                value => Set(m_Settings.debugMode, value));
            DrawIntSlider("调试 Mip", 0, 8, () => m_Settings.debugMipLevel.value,
                value => Set(m_Settings.debugMipLevel, value));
            DrawSlider("调试步长", 0.001f, 2f, () => m_Settings.debugRayStep.value,
                value => Set(m_Settings.debugRayStep, value));
        }

        void Section(string title)
        {
            GUILayout.Space(8f);
            if (m_SectionStyle == null)
            {
                m_SectionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold
                };
            }
            GUILayout.Label(title, m_SectionStyle);
        }

        void DrawToggle(string title, Func<bool> getter, Action<bool> setter)
        {
            bool oldValue = getter();
            bool newValue = GUILayout.Toggle(oldValue, title);
            if (newValue != oldValue)
                setter(newValue);
        }

        void DrawSlider(string title, float min, float max, Func<float> getter, Action<float> setter)
        {
            float oldValue = getter();
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(132f));
            float newValue = GUILayout.HorizontalSlider(oldValue, min, max);
            GUILayout.Label(newValue.ToString("0.##"), GUILayout.Width(48f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newValue, oldValue))
                setter(newValue);
        }

        void DrawIntSlider(string title, int min, int max, Func<int> getter, Action<int> setter)
        {
            int oldValue = getter();
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(132f));
            int newValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(oldValue, min, max));
            GUILayout.Label(newValue.ToString(), GUILayout.Width(48f));
            GUILayout.EndHorizontal();
            if (newValue != oldValue)
                setter(newValue);
        }

        void DrawEnum<T>(string title, Func<T> getter, Action<T> setter) where T : struct, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            string[] names = Enum.GetNames(typeof(T));
            int oldIndex = Array.IndexOf(values, getter());
            int newIndex = GUILayout.SelectionGrid(oldIndex, names, 1);
            GUILayout.Label(title);
            if (newIndex >= 0 && newIndex < values.Length && newIndex != oldIndex)
                setter(values[newIndex]);
        }

        void RequestVoxelization()
        {
            if (m_Volume != null)
                m_Volume.RequestVoxelizationUpdate();
        }

        static void Set(BoolParameter parameter, bool value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(ClampedFloatParameter parameter, float value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(MinFloatParameter parameter, float value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(ClampedIntParameter parameter, int value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(NoInterpClampedIntParameter parameter, int value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(VoxelGIConeQualityParameter parameter, VoxelGIConeQuality value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(VoxelGIUpdateModeParameter parameter, VoxelGIUpdateMode value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(VoxelGIJitterSequenceParameter parameter, VoxelGIJitterSequence value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        static void Set(VoxelGIDebugModeParameter parameter, VoxelGIDebugMode value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }
    }
}
