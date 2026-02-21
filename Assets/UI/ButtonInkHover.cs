using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonInkHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    ISelectHandler,
    IDeselectHandler
{
    public GameObject inkPenImage;

    public void OnPointerEnter(PointerEventData eventData)
    {
        inkPenImage.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        inkPenImage.SetActive(false);
    }

    public void OnSelect(BaseEventData eventData)
    {
        inkPenImage.SetActive(true);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        inkPenImage.SetActive(false);
    }
}