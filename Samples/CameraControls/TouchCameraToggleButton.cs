using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QSTX.VoxelGI.Samples
{
    [DisallowMultipleComponent]
    public sealed class TouchCameraToggleButton : MonoBehaviour
    {
        [SerializeField] BezierCameraController m_Controller;

        GameObject m_ButtonObject;
        Button m_Button;
        Text m_Label;

        void Awake()
        {
            if (m_Controller == null)
                m_Controller = FindFirstObjectByType<BezierCameraController>();
            CreateButton();
        }

        void Update()
        {
            if (m_ButtonObject == null || m_Controller == null)
                return;
            m_ButtonObject.SetActive(m_Controller.GetComponent<FreeFlyCameraController>() == null ||
                                     m_Controller.GetComponent<FreeFlyCameraController>().UsingTouchControls);
            if (m_Label != null)
                m_Label.text = m_Controller.IsPathMode ? "自由相机" : "路径相机";
        }

        void CreateButton()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
                canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                return;

            m_ButtonObject = new GameObject("Touch Camera Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            m_ButtonObject.transform.SetParent(canvas.transform, false);
            RectTransform rect = m_ButtonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(180f, 64f);

            Image image = m_ButtonObject.GetComponent<Image>();
            image.color = new Color(0.05f, 0.08f, 0.12f, 0.82f);
            m_Button = m_ButtonObject.GetComponent<Button>();
            m_Button.onClick.AddListener(OnClicked);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(m_ButtonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            m_Label = labelObject.GetComponent<Text>();
            m_Label.alignment = TextAnchor.MiddleCenter;
            m_Label.color = Color.white;
            m_Label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            m_Label.fontSize = 24;
            m_Label.text = "路径相机";
        }

        void OnClicked()
        {
            if (m_Controller != null)
                m_Controller.ToggleCamera();
        }

        void OnDestroy()
        {
            if (m_Button != null)
                m_Button.onClick.RemoveListener(OnClicked);
        }
    }
}
