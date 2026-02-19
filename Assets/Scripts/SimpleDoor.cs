using UnityEngine;

public class SimpleDoor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform pivot;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;

    [Header("Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 5f;

    private bool _isOpen = false;
    private bool _isAnimating = false;

    private Quaternion _closedRotation;
    private Quaternion _targetRotation;

    private void Start()
    {
        _closedRotation = pivot.localRotation;
    }

    public void OpenDoor(Transform player)
{
    if (_isOpen || _isAnimating)
        return;

    _isOpen = true;

    Vector3 playerForward = player.forward;
Vector3 doorForward = pivot.forward;

// If player is looking roughly same direction as door forward
float dot = Vector3.Dot(playerForward, doorForward);

// If dot > 0 ? same direction
// If dot < 0 ? opposite direction

float direction = dot > 0 ? -1f : 1f;

_targetRotation = _closedRotation * Quaternion.Euler(0f, openAngle * direction, 0f);
    float playerSpeed = player.gameObject.GetComponent<CharacterController>().velocity.magnitude;
    float dynamicSpeed = Mathf.Lerp(3f, 10f, playerSpeed / 6f);
    StartCoroutine(AnimateDoor(dynamicSpeed*1.1f));
}
public void CloseDoor()
{
    if (!_isOpen || _isAnimating)
        return;

    _isOpen = false;

    _targetRotation = _closedRotation;

    StartCoroutine(AnimateDoor(1f));
}
    private void OnTriggerEnter(Collider other)
{
    if (other.CompareTag("Medium")){
        Debug.Log("Player collided!");
        OpenDoor(other.transform);
    }
}
private void OnTriggerExit(Collider other)
{
    if (other.CompareTag("Medium"))
    {
        Debug.Log("Byee!");
        CloseDoor();
    }
}
    private System.Collections.IEnumerator AnimateDoor(float speed)
{
    _isAnimating = true;

    Quaternion startRot = pivot.localRotation;
    float t = 0f;

    while (t < 1f)
    {
        t += Time.deltaTime * speed;
        pivot.localRotation = Quaternion.Slerp(startRot, _targetRotation, t);
        yield return null;
    }

    pivot.localRotation = _targetRotation;
    _isAnimating = false;
}
}