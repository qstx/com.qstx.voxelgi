using UnityEngine;
using UnityEngine.EventSystems;

namespace QSTX.VoxelGI.Samples
{
[DisallowMultipleComponent]
public sealed class TouchHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsPressed { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsPressed = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        IsPressed = false;
    }

    void OnDisable()
    {
        IsPressed = false;
    }
}
}
