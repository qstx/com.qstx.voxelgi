using UnityEngine;

namespace QSTX.VoxelGI.Samples
{
[DisallowMultipleComponent]
public sealed class FreeFlyCameraController : MonoBehaviour
{
    public enum ControlMode
    {
        Auto,
        KeyboardMouse,
        TouchUI
    }

    [Header("Control Scheme")]
    [SerializeField] ControlMode m_ControlMode = ControlMode.Auto;

    [Header("Movement")]
    [SerializeField, Min(0f)] float m_MoveSpeed = 5f;
    [SerializeField, Min(1f)] float m_SprintMultiplier = 3f;
    [SerializeField, Min(0f)] float m_MoveAcceleration = 14f;
    [SerializeField, Min(0f)] float m_MouseWheelSpeed = 8f;

    [Header("Look")]
    [SerializeField, Min(0f)] float m_MouseLookSensitivity = 2f;
    [SerializeField, Min(0f)] float m_TouchLookSensitivity = 0.12f;
    [SerializeField, Range(1f, 89f)] float m_MaxPitch = 88f;
    [SerializeField] int m_MouseLookButton = 1;
    [SerializeField] bool m_LockCursorWhileLooking = true;

    [Header("Touch UI")]
    [SerializeField] GameObject m_TouchUiRoot;
    [SerializeField] TouchVirtualJoystick m_MoveJoystick;
    [SerializeField] TouchDragArea m_LookArea;
    [SerializeField] TouchHoldButton m_MoveUpButton;
    [SerializeField] TouchHoldButton m_MoveDownButton;
    [SerializeField] TouchHoldButton m_SprintButton;

    Vector3 m_CurrentVelocity;
    float m_Yaw;
    float m_Pitch;
    bool m_UsingTouchControls;
    bool m_RequireMouseRelease;
    bool m_IgnoreMouseDelta;

    public ControlMode Mode
    {
        get => m_ControlMode;
        set
        {
            m_ControlMode = value;
            RefreshControlMode(true);
        }
    }

    public bool UsingTouchControls => m_UsingTouchControls;

    public GameObject TouchUiRoot
    {
        get => m_TouchUiRoot;
        set => m_TouchUiRoot = value;
    }

    public TouchVirtualJoystick MoveJoystick
    {
        get => m_MoveJoystick;
        set => m_MoveJoystick = value;
    }

    public TouchDragArea LookArea
    {
        get => m_LookArea;
        set => m_LookArea = value;
    }

    public TouchHoldButton MoveUpButton
    {
        get => m_MoveUpButton;
        set => m_MoveUpButton = value;
    }

    public TouchHoldButton MoveDownButton
    {
        get => m_MoveDownButton;
        set => m_MoveDownButton = value;
    }

    public TouchHoldButton SprintButton
    {
        get => m_SprintButton;
        set => m_SprintButton = value;
    }

    void OnEnable()
    {
        SyncRotationFromTransform();
        RefreshControlMode(true);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
        m_RequireMouseRelease = false;
        m_IgnoreMouseDelta = false;
        m_CurrentVelocity = Vector3.zero;
        if (m_TouchUiRoot != null && Application.isPlaying)
            m_TouchUiRoot.SetActive(false);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            // The OS may report a large mouse delta when a locked cursor is
            // restored. Re-sync the accumulator and discard that first delta.
            SyncRotationFromTransform();
            m_IgnoreMouseDelta = true;
            return;
        }

        // Never keep a locked cursor or movement velocity while the window is
        // inactive. Require a complete release/re-press before looking again.
        SetCursorLocked(false);
        m_CurrentVelocity = Vector3.zero;
        m_RequireMouseRelease = true;
        m_IgnoreMouseDelta = true;
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
            OnApplicationFocus(false);
    }

    void Update()
    {
        RefreshControlMode(false);
        float deltaTime = Time.unscaledDeltaTime;
        HandleLook();
        HandleMovement(deltaTime);

        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLocked(false);
    }

    void HandleMovement(float deltaTime)
    {
        Vector3 input = m_UsingTouchControls ? Vector3.zero : GetKeyboardMoveInput();

        if (m_UsingTouchControls && m_MoveJoystick != null)
        {
            Vector2 joystick = m_MoveJoystick.Value;
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 planarRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            input += planarRight * joystick.x + planarForward * joystick.y;
        }

        float verticalInput = 0f;
        if (m_UsingTouchControls)
        {
            if (m_MoveUpButton != null && m_MoveUpButton.IsPressed)
                verticalInput += 1f;
            if (m_MoveDownButton != null && m_MoveDownButton.IsPressed)
                verticalInput -= 1f;
        }
        else
        {
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space))
                verticalInput += 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C))
                verticalInput -= 1f;
        }
        input += Vector3.up * verticalInput;

        input = Vector3.ClampMagnitude(input, 1f);
        bool sprint = m_UsingTouchControls
            ? m_SprintButton != null && m_SprintButton.IsPressed
            : Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speed = m_MoveSpeed * (sprint ? m_SprintMultiplier : 1f);
        Vector3 targetVelocity = input * speed;
        float blend = 1f - Mathf.Exp(-m_MoveAcceleration * deltaTime);
        m_CurrentVelocity = Vector3.Lerp(m_CurrentVelocity, targetVelocity, blend);

        float wheel = m_UsingTouchControls ? 0f : Input.mouseScrollDelta.y;
        Vector3 wheelMovement = transform.forward * (wheel * m_MouseWheelSpeed);
        transform.position += m_CurrentVelocity * deltaTime + wheelMovement;
    }

    Vector3 GetKeyboardMoveInput()
    {
        float horizontal = 0f;
        float forward = 0f;
        if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D)) horizontal += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.W)) forward += 1f;
        return transform.right * horizontal + transform.forward * forward;
    }

    void HandleLook()
    {
        Vector2 lookDelta = Vector2.zero;

        if (m_UsingTouchControls)
        {
            if (m_LookArea != null)
            {
                Vector2 touchDelta = m_LookArea.ConsumeDelta();
                lookDelta.x += touchDelta.x * m_TouchLookSensitivity;
                lookDelta.y += touchDelta.y * m_TouchLookSensitivity;
            }
        }
        else if (Input.GetMouseButton(m_MouseLookButton))
        {
            if (m_RequireMouseRelease)
                return;

            if (Input.GetMouseButtonDown(m_MouseLookButton) && m_LockCursorWhileLooking)
            {
                SetCursorLocked(true);
                m_IgnoreMouseDelta = true;
            }

            if (m_IgnoreMouseDelta)
            {
                m_IgnoreMouseDelta = false;
                return;
            }

            lookDelta.x += Input.GetAxisRaw("Mouse X") * m_MouseLookSensitivity;
            lookDelta.y += Input.GetAxisRaw("Mouse Y") * m_MouseLookSensitivity;
        }
        else if (m_RequireMouseRelease)
        {
            m_RequireMouseRelease = false;
            m_IgnoreMouseDelta = true;
        }
        else if (Input.GetMouseButtonUp(m_MouseLookButton) && m_LockCursorWhileLooking)
        {
            SetCursorLocked(false);
            m_IgnoreMouseDelta = true;
        }

        if (lookDelta.sqrMagnitude <= Mathf.Epsilon)
            return;

        m_Yaw += lookDelta.x;
        m_Pitch = Mathf.Clamp(m_Pitch - lookDelta.y, -m_MaxPitch, m_MaxPitch);
        transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
    }

    void SyncRotationFromTransform()
    {
        Vector3 euler = transform.eulerAngles;
        m_Yaw = euler.y;
        m_Pitch = NormalizeAngle(euler.x);
    }

    void RefreshControlMode(bool force)
    {
        bool useTouch = m_ControlMode == ControlMode.TouchUI ||
                        (m_ControlMode == ControlMode.Auto && Application.isMobilePlatform);
        if (!force && useTouch == m_UsingTouchControls)
            return;

        m_UsingTouchControls = useTouch;
        if (m_TouchUiRoot != null && Application.isPlaying)
            m_TouchUiRoot.SetActive(m_UsingTouchControls);

        if (m_UsingTouchControls)
            SetCursorLocked(false);
        else if (m_LookArea != null)
            m_LookArea.ConsumeDelta();
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
}
