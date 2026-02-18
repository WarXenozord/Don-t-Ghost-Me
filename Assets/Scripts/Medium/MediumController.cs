using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MediumController : MonoBehaviour
{ 
    private Vector3 Velocity;
    private Vector3 PlayerMovementInput;
    private Vector2 PlayerMouseInput;
    private bool Sneaking = false;
    private float xRotation;
    private Vector3 _lastWorldPos;
    public Vector3 NetworkVelocity { get; private set; }

    [Header("Components Needed")]
    [SerializeField] private Transform PlayerCamera;
    [SerializeField] private CharacterController Controller;
    [SerializeField] private Transform Player;
    [Space]
    [Header("Movement")]
    [SerializeField] private float Speed;
    [SerializeField] private float JumpForce;
    [SerializeField] private float Sensetivity;
    [SerializeField] private float Gravity = 9.81f;
    [Space]
    [Header("Sneaking")]
    [SerializeField] private bool Sneak = false;
    [SerializeField] private float SneakSpeed;
    [Header("Interaction")]
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private LayerMask interactableLayer; // set to "Interactable" layer
    private Candle _currentAimedCandle;
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        _lastWorldPos = transform.position;
    }

    // Update is called once per frame
    void Update()
    {

        PlayerMovementInput = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        PlayerMouseInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        MovePlayer();
        MoveCamera();

        if (Input.GetKey(KeyCode.RightShift) && Sneak)
        {
            Player.localScale = new Vector3(1f, 0.5f, 1f);
            Sneaking = true;
        }
        if (Input.GetKeyUp(KeyCode.RightShift))
        {
            Player.localScale = new Vector3(1f, 1f, 1f);
            Sneaking = false;
        }

        var dt = Time.deltaTime;
        if (dt > 0f)
        {
            NetworkVelocity = (transform.position - _lastWorldPos) / dt;
        }
        else
        {
            NetworkVelocity = Vector3.zero;
        }
        _lastWorldPos = transform.position;
        CheckAimHighlight();  
        HandleInteraction();


    }
    private void MovePlayer()
    {
        Vector3 MoveVector = transform.TransformDirection(PlayerMovementInput);


        if (Controller.isGrounded)
        {
            Velocity.y = -1f;

            if (Input.GetKeyDown(KeyCode.Space) && Sneaking == false)
            {
                Velocity.y = JumpForce;
            }
        }
        else
        {
            Velocity.y += Gravity * -2f * Time.deltaTime;
        }
        if (Sneaking)
        {
            Controller.Move(MoveVector * SneakSpeed * Time.deltaTime);
        }
        else
        {
            Controller.Move(MoveVector * Speed * Time.deltaTime);
        }
        Controller.Move(Velocity * Time.deltaTime);

    }
    private void MoveCamera()
    {
        if (FullMapViewer.IsOpen) return; // ? add this line, done

        xRotation -= PlayerMouseInput.y * Sensetivity;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.Rotate(0f, PlayerMouseInput.x * Sensetivity, 0f);
        PlayerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
    private void CheckAimHighlight()
    {
        Ray ray = new Ray(PlayerCamera.position, PlayerCamera.forward);
        bool hit = interactableLayer.value != 0
            ? Physics.Raycast(ray, out RaycastHit hitInfo, interactionRange, interactableLayer)
            : Physics.Raycast(ray, out hitInfo, interactionRange);

        if (hit)
        {
            Candle candle = hitInfo.collider.GetComponent<Candle>();

            if (candle != null)
            {
                if (_currentAimedCandle != candle)
                {
                    if (_currentAimedCandle != null){
                     Debug.Log("Bye candle, because now..");
                        _currentAimedCandle.SetAimed(false);
                        _currentAimedCandle.SetHighlight(false);
                    }

                    _currentAimedCandle = candle;
                    _currentAimedCandle.SetAimed(true);
                    _currentAimedCandle.SetHighlight(true);
                    Debug.Log("New candle!");
                }

                return;
            }
        }

        if (_currentAimedCandle != null)
        {
            _currentAimedCandle.SetAimed(false);
            _currentAimedCandle.SetHighlight(false);
            Debug.Log("Bye candle!");
            _currentAimedCandle = null;
        }
}
    private void HandleInteraction()
    {
        if (Input.GetKeyDown(interactKey))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        Ray ray = new Ray(PlayerCamera.position, PlayerCamera.forward);
        
        bool hit = interactableLayer.value != 0
            ? Physics.Raycast(ray, out RaycastHit hitInfo, interactionRange, interactableLayer)
            : Physics.Raycast(ray, out hitInfo, interactionRange);

        if (!hit) return;

        // Check for Medium-specific interactables
        var candle = hitInfo.collider.GetComponent<Candle>();
        if (candle != null)
        {
            Debug.Log("col with can");
            candle.CollectByMedium(this);
            return;
        }

        // Can add more Medium-specific interactions here
        // (keys, artifacts, switches, etc.)
    }

    private void OnDrawGizmosSelected()
    {
        if (PlayerCamera != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawRay(PlayerCamera.position, PlayerCamera.forward * interactionRange);
        }
    }
}
