using UnityEngine;
using System.Collections;

public class MenuCameraController : MonoBehaviour
{
    public float moveDuration = 1.5f;

    public IEnumerator MoveToRoutine(Transform target)
    {
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        Vector3 endPos = target.position;
        Quaternion endRot = target.rotation;

        float t = 0f;

        while (t < moveDuration)
        {
            t += Time.deltaTime;
            float smooth = Mathf.SmoothStep(0f, 1f, t / moveDuration);

            transform.position = Vector3.Lerp(startPos, endPos, smooth);
            transform.rotation = Quaternion.Slerp(startRot, endRot, smooth);

            yield return null;
        }

        transform.position = endPos;
        transform.rotation = endRot;
    }
}