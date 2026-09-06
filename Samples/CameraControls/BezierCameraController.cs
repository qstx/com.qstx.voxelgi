using UnityEngine;

namespace QSTX.VoxelGI.Samples
{
    [DisallowMultipleComponent]
    public sealed class BezierCameraController : MonoBehaviour
    {
        [Header("相机")]
        [SerializeField] Camera m_FreeCamera;
        [SerializeField] Camera m_PathCamera;
        [SerializeField] FreeFlyCameraController m_FreeFlyController;
        [SerializeField] Transform m_LookAtTarget;

        [Header("路径")]
        [SerializeField] BezierCameraPath m_Path;
        [SerializeField, Min(0.01f)] float m_PathDuration = 12f;
        [SerializeField] bool m_Loop = true;
        [SerializeField] bool m_PingPong;
        [SerializeField] bool m_StartOnPath;

        bool m_PathMode;
        float m_PathTime;
        int m_PathDirection = 1;

        public bool IsPathMode => m_PathMode;

        void Awake()
        {
            if (m_FreeCamera == null)
                m_FreeCamera = GetComponent<Camera>();
            if (m_FreeFlyController == null)
                m_FreeFlyController = GetComponent<FreeFlyCameraController>();
            if (m_Path == null)
                m_Path = FindFirstObjectByType<BezierCameraPath>();

            SetPathMode(m_StartOnPath, true);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                ToggleCamera();

            if (m_PathMode && m_Path != null && m_PathCamera != null)
                UpdatePathCamera();
        }

        public void ToggleCamera()
        {
            SetPathMode(!m_PathMode, false);
        }

        void UpdatePathCamera()
        {
            float duration = Mathf.Max(m_PathDuration, 0.01f);
            m_PathTime += Time.unscaledDeltaTime / duration * m_PathDirection;
            if (m_PingPong)
            {
                if (m_PathTime > 1f)
                {
                    m_PathTime = 1f;
                    m_PathDirection = -1;
                }
                else if (m_PathTime < 0f)
                {
                    m_PathTime = 0f;
                    m_PathDirection = 1;
                }
            }
            else if (m_Loop)
            {
                m_PathTime = Mathf.Repeat(m_PathTime, 1f);
            }
            else
            {
                m_PathTime = Mathf.Clamp01(m_PathTime);
            }

            Vector3 position = m_Path.Evaluate(m_PathTime);
            Vector3 tangent = m_Path.EvaluateTangent(m_PathTime);
            Vector3 lookDirection = m_LookAtTarget != null
                ? m_LookAtTarget.position - position
                : tangent;
            if (lookDirection.sqrMagnitude > 1e-6f)
                m_PathCamera.transform.SetPositionAndRotation(position,
                    Quaternion.LookRotation(lookDirection.normalized, Vector3.up));
            else
                m_PathCamera.transform.position = position;
        }

        void SetPathMode(bool enabled, bool immediate)
        {
            m_PathMode = enabled && m_PathCamera != null && m_Path != null;
            if (m_PathMode && immediate)
                m_PathTime = 0f;

            if (m_FreeCamera != null)
                m_FreeCamera.enabled = !m_PathMode;
            if (m_PathCamera != null)
                m_PathCamera.enabled = m_PathMode;
            if (m_FreeFlyController != null)
                m_FreeFlyController.enabled = !m_PathMode;
        }
    }
}
