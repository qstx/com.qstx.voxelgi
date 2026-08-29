using UnityEngine;
using UnityEngine.EventSystems;

namespace QSTX.VoxelGI.Samples
{
[DisallowMultipleComponent]
public sealed class TouchDragArea : MonoBehaviour, IDragHandler, IEndDragHandler
{
    Vector2 m_AccumulatedDelta;

    public void OnDrag(PointerEventData eventData)
    {
        m_AccumulatedDelta += eventData.delta;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        m_AccumulatedDelta = Vector2.zero;
    }

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = m_AccumulatedDelta;
        m_AccumulatedDelta = Vector2.zero;
        return delta;
    }

    void OnDisable()
    {
        m_AccumulatedDelta = Vector2.zero;
    }
}
}
