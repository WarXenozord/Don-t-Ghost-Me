using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class BookClick : MonoBehaviour
{
    public MenuSequenceController controller;
    public Animator bookAnimator;
public Transform bookPivot;
    public void OnClick()
    {
        bookAnimator.SetTrigger("OpenBook");
        controller.StartSequence();
        
        gameObject.SetActive(false);
    }


}