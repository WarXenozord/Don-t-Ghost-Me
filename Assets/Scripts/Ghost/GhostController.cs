using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GhostController : MonoBehaviour
{
    private Vector3 movementInput;
    private Vector2 mouseInput;
    private float xRotation;

    [Header("Components")]
    [SerializeField] private Transform playerCamera;
    public Transform PlayerCamera => playerCamera; // ? Added getter for GhostInteraction
    
    private Rigidbody rb;
    [SerializeField] private float energy = 100f;
    public Vector3 NetworkVelocity { get; private set; }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float verticalSpeed = 6f;
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float acceleration = 8f; // smoothness

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = GetComponentInParent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

        rb.useGravity = false;
        rb.drag = 2f;              // slight float resistance
        rb.angularDrag = 5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        Cursor.lockState = CursorLockMode.Locked;

        var gc = GameObject.FindGameObjectWithTag("GameController");
        if (gc != null)
        {
            var fp = gc.GetComponent<FloorplanRenderer>();
            if (fp != null) fp.RevealAllRooms();

            var mp = gc.GetComponent<MinimapController>();
            if (mp != null) mp.SetPlayer(transform);
        }

        if (playerCamera == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) playerCamera = cam.transform;
        }
    }

    private void Update()
    {
        if (FullMapViewer.IsOpen || ChatUI.IsChatFocused)
        {
            movementInput = Vector3.zero;
            mouseInput = Vector2.zero;
            return;
        }
        
        movementInput = new Vector3(
            Input.GetAxis("Horizontal"),
            0f,
            Input.GetAxis("Vertical")
        );

        mouseInput = new Vector2(
            Input.GetAxis("Mouse X"),
            Input.GetAxis("Mouse Y")
        );

        HandleCamera();
    }

    private void FixedUpdate()
    {
        if (FullMapViewer.IsOpen || ChatUI.IsChatFocused)
        {
            movementInput = Vector3.zero;
            if (rb != null)
            {
                rb.velocity = Vector3.Lerp(rb.velocity, Vector3.zero, acceleration * Time.fixedDeltaTime);
            }
            NetworkVelocity = rb != null ? rb.velocity : Vector3.zero;
            return;
        }
        
        MoveGhost();
        if (rb != null) NetworkVelocity = rb.velocity;
        else NetworkVelocity = Vector3.zero;
    }

    private void MoveGhost()
    {
        if (rb == null) return;
        var chatFocused = ChatUI.IsChatFocused;

        // Horizontal movement relative to where we look
        Vector3 horizontalMove = transform.TransformDirection(movementInput) * moveSpeed;

        // Vertical movement
        float vertical = 0f;
        if (!chatFocused && Input.GetKey(KeyCode.Space))
            vertical = verticalSpeed;
        if (!chatFocused && Input.GetKey(KeyCode.LeftControl))
            vertical = -verticalSpeed;

        Vector3 desiredVelocity = new Vector3(
            horizontalMove.x,
            vertical,
            horizontalMove.z
        );

        // Smooth drifting movement
        rb.velocity = Vector3.Lerp(
            rb.velocity,
            desiredVelocity,
            acceleration * Time.fixedDeltaTime
        );
    }

    private void HandleCamera()
    {
        if (playerCamera == null) return;

        xRotation -= mouseInput.y * sensitivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.Rotate(0f, mouseInput.x * sensitivity, 0f);
        playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}
