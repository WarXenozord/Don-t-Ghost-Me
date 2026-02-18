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

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float verticalSpeed = 6f;
    [SerializeField] private float sensitivity = 2f;
    [SerializeField] private float acceleration = 8f; // smoothness

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        FloorplanRenderer fp = GameObject.FindGameObjectWithTag("GameController").GetComponent<FloorplanRenderer>();
        fp.RevealAllRooms();
        MinimapController mp = GameObject.FindGameObjectWithTag("GameController").GetComponent<MinimapController>();
        mp.SetPlayer(this.gameObject.GetComponent<Transform>());
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.drag = 2f;              // slight float resistance
        rb.angularDrag = 5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Update()
    {
        if (FullMapViewer.IsOpen) return; // ? Don't move when map is open
        
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
        if (FullMapViewer.IsOpen) return; // ? Don't move when map is open
        
        MoveGhost();
    }

    private void MoveGhost()
    {
        // Horizontal movement relative to where we look
        Vector3 horizontalMove = transform.TransformDirection(movementInput) * moveSpeed;

        // Vertical movement
        float vertical = 0f;
        if (Input.GetKey(KeyCode.Space))
            vertical = verticalSpeed;
        if (Input.GetKey(KeyCode.LeftControl))
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
        xRotation -= mouseInput.y * sensitivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.Rotate(0f, mouseInput.x * sensitivity, 0f);
        playerCamera.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}