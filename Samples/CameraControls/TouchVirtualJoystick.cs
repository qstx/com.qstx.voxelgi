using UnityEngine;
using UnityEngine.EventSystems;

namespace QSTX.VoxelGI.Samples
{
[DisallowMultipleComponent]
public sealed class TouchVirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] RectTransform m_Handle;
    [SerializeField, Range(0f, 1f)] float m_DeadZone = 0.08f;

    RectTransform m_Base;
    Vector2 m_Value;

    public Vector2 Value => m_Value.sqrMagnitude >= m_DeadZone * m_DeadZone ? m_Value : Vector2.zero;

    public RectTransform Handle
    {
        get => m_Handle;
        set => m_Handle = value;
    }

    void Awake()
    {
        m_Base = (RectTransform)transform;
    }

    void OnDisable()
    {
        ResetJoystick();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        UpdateValue(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateValue(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetJoystick();
    }

    void UpdateValue(PointerEventData eventData)
    {
        if (m_Base == null)
            m_Base = (RectTransform)transform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                m_Base, eventData.position, eventData.pressEventCamera, out var localPoint))
            return;

        Vector2 radius = m_Base.rect.size * 0.5f;
        Vector2 normalized = new Vector2(
            radius.x > Mathf.Epsilon ? localPoint.x / radius.x : 0f,
            radius.y > Mathf.Epsilon ? localPoint.y / radius.y : 0f);
        m_Value = Vector2.ClampMagnitude(normalized, 1f);

        if (m_Handle != null)
            m_Handle.anchoredPosition = new Vector2(m_Value.x * radius.x, m_Value.y * radius.y) * 0.55f;
    }

    void ResetJoystick()
    {
        m_Value = Vector2.zero;
        if (m_Handle != null)
            m_Handle.anchoredPosition = Vector2.zero;
    }
}
}
