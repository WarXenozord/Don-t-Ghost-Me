using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class BookClick : MonoBehaviour
{
    public MenuSequenceController controller;
    public Animator bookAnimator;
    public Transform bookPivot;
    [Header("Optional Destroy")]
    public GameObject destroyWhenBookOpened;

    public void OnClick()
    {
        if (bookAnimator != null)
        {
            bookAnimator.SetTrigger("OpenBook");
        }
        if (controller != null)
        {
            controller.StartSequence();
        }
        if (destroyWhenBookOpened != null)
        {
            Destroy(destroyWhenBookOpened);
            destroyWhenBookOpened = null;
        }
        
        gameObject.SetActive(false);
    }

    // Optional hook for close/back button animation event.
    public void OnBookClosed()
    {
        // Intentionally no-op: target object is destroyed on open.
    }

}
