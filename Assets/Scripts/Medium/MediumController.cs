using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MediumController : MonoBehaviour
{ 
    private Vector3 Velocity;
    private Vector3 PlayerMovementInput;
    private Vector2 PlayerMouseInput;
    private bool Sprinting = false;
    private float xRotation;
    private Vector3 _lastWorldPos;
    public Vector3 NetworkVelocity { get; private set; }
    [Header("Camera Effects")]
[SerializeField] private float normalFOV = 60f;
[SerializeField] private float sprintFOV = 75f;
[SerializeField] private float fovSmoothSpeed = 8f;
private Camera playerCam;
    [Header("Components Needed")]
    [SerializeField] private CharacterController Controller;
    [SerializeField] private Transform Player;
    [SerializeField] private GameObject cameraObject;
    private Transform PlayerCamera;
    [Space]
    [Header("Movement")]
    [SerializeField] private float Speed;
    [SerializeField] private float JumpForce;
    [SerializeField] private float Sensetivity;
    [SerializeField] private float Gravity = 9.81f;
    [Space]
    [Header("Sprint")]
    [SerializeField] private float SprintSpeed;
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 5f;
    [SerializeField] private float staminaDrainRate = 1f;
    [SerializeField] private float staminaRegenRate = 0.8f;
    [SerializeField] private float staminaRegenDelay = 1f;
    private Image staminaBarFill;
    public GameObject staminaBar;

    private float _currentStamina;
    private float _regenTimer;
    [Header("Interaction")]
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private LayerMask interactableLayer; // set to "Interactable" layer
    private Candle _currentAimedCandle;
    [Header("Audio")]
[SerializeField] private AudioSource breathingSource;
[SerializeField] private float lowStaminaThreshold = 1.5f;
[SerializeField] private float heavyBreathVolume = 1f;
[SerializeField] private float normalBreathVolume = 0f;
[SerializeField] private float breathFadeSpeed = 2f;
[Header("Exhaustion")]
[SerializeField] private float exhaustionDuration = 2f;
[SerializeField] private float exhaustionSpeedMultiplier = 0.5f;
private HalfLifeEffect _halfLifeEffect;
private bool _hasBeenHit = false;

[Header("Footsteps")]
[SerializeField] private AudioSource footstepSource;
[SerializeField] private AudioClip footstepClip;
[SerializeField] private float walkStepInterval = 0.5f;
[SerializeField] private float runStepInterval = 0.3f;

private float _stepTimer;


[Header("Animation State (Network Sync)")]
[SerializeField] private bool _isWalking = false;
[SerializeField] private bool _isRunning = false;
[SerializeField] private bool _isJumping = false;

// Animation state constants
private const int ANIM_IDLE = 0;
private const int ANIM_WALK = 1;
private const int ANIM_RUN = 2;
private const int ANIM_JUMP = 3;

/// <summary>
/// Gets current animation state for network sync
/// </summary>
public int GetAnimState()
{
    if (_isJumping) return ANIM_JUMP;
    if (_isRunning) return ANIM_RUN;
    if (_isWalking) return ANIM_WALK;
    return ANIM_IDLE;
}
private float _exhaustionTimer;
    private bool _isExhausted;

    public float CurrentStamina => _currentStamina;
    void Start()
    {
        PlayerCamera= cameraObject.GetComponent<Transform>();
        _halfLifeEffect = PlayerCamera.GetComponent<HalfLifeEffect>();
        playerCam = cameraObject.GetComponent<Camera>();
        playerCam.fieldOfView = normalFOV;

        Cursor.lockState = CursorLockMode.Locked;
        _lastWorldPos = transform.position;
        MinimapController mp = GameObject.FindGameObjectWithTag("GameController").GetComponent<MinimapController>();
        mp.SetPlayer(this.gameObject.GetComponent<Transform>());
        Transform canvas = GameObject.FindGameObjectWithTag("Canvas").GetComponent<Transform>();
        Transform child = Instantiate(staminaBar).GetComponent<Transform>();
        child.transform.SetParent(canvas);
        staminaBarFill= child.GetChild(0).GetComponent<Image>();
        _currentStamina = maxStamina;
        UpdateStaminaBar();
        if (breathingSource != null)
{
    breathingSource.loop = true;
    breathingSource.volume = 0f;
    breathingSource.Play();
}
    }

    // Update is called once per frame
    void Update()
    {
        var chatFocused = ChatUI.IsChatFocused;

        if (chatFocused)
        {
            PlayerMovementInput = Vector3.zero;
            PlayerMouseInput = Vector2.zero;
        }
        else
        {
            PlayerMovementInput = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            PlayerMouseInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        }
        // Update animation states based on input
        float inputMagnitude = PlayerMovementInput.magnitude;
        _isWalking = inputMagnitude > 0.1f && !Sprinting;
        _isRunning = inputMagnitude > 0.1f && Sprinting;
        _isJumping = !Controller.isGrounded; // In air = jumping
        var animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.SetBool("IsWalking", _isWalking);
            animator.SetBool("IsRunning", _isRunning);
            if (Input.GetButtonDown("Jump"))
                animator.SetTrigger("Jump");
        }
        UpdateStaminaBar();
        MovePlayer();
        MoveCamera();

        HandleSprint();

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
        if (!chatFocused)
        {
            CheckAimHighlight();
            HandleInteraction();
        }
        UpdateFOV();
        UpdateBreathing();

        HandleFootsteps();
    }
    
    private void HandleFootsteps()
{
    bool isMoving = PlayerMovementInput.magnitude > 0.1f;
    bool grounded = Controller.isGrounded;

    if (isMoving && grounded)
    {
        if (!footstepSource.isPlaying)
            footstepSource.Play();

        // Running = faster steps
        footstepSource.pitch = Sprinting ? 1.5f : 1f;
    }
    else
    {
        if (footstepSource.isPlaying)
            footstepSource.Stop();
    }
}
    
        private void UpdateFOV()
{
    float targetFOV = Sprinting ? sprintFOV : normalFOV;
    playerCam.fieldOfView = Mathf.Lerp(
        playerCam.fieldOfView,
        targetFOV,
        fovSmoothSpeed * Time.deltaTime
    );
}
private void UpdateBreathing()
{
    if (breathingSource == null) return;

    float targetVolume = _currentStamina <= lowStaminaThreshold
        ? heavyBreathVolume
        : normalBreathVolume;

    breathingSource.volume = Mathf.Lerp(
        breathingSource.volume,
        targetVolume,
        breathFadeSpeed * Time.deltaTime
    );
}
private void HandleSprint()
    {
        if (_isExhausted)
        {
        _exhaustionTimer -= Time.deltaTime;

        if (_exhaustionTimer <= 0f)
            _isExhausted = false;
        }
     bool sprintInput = Input.GetKey(KeyCode.LeftShift);
        if (_currentStamina <= 0f && !_isExhausted)
        {
        _isExhausted = true;
        _exhaustionTimer = exhaustionDuration;
        }

        if (sprintInput && _currentStamina > 0f && PlayerMovementInput.magnitude > 0.1f)
        {
            Sprinting = true;
            _currentStamina -= staminaDrainRate * Time.deltaTime;
            _regenTimer = 0f;
        }
        else
        {
        Sprinting = false;

            if (_currentStamina < maxStamina)
            {
                _regenTimer += Time.deltaTime;

                if (_regenTimer >= staminaRegenDelay)
                {
                    _currentStamina += staminaRegenRate * Time.deltaTime;
                }
            }
    }

    _currentStamina = Mathf.Clamp(_currentStamina, 0f, maxStamina);
}
    private void MovePlayer()
    {
        var chatFocused = ChatUI.IsChatFocused;
        Vector3 MoveVector = transform.TransformDirection(PlayerMovementInput);


        if (Controller.isGrounded)
        {
            Velocity.y = -1f;

            if (!chatFocused && Input.GetKeyDown(KeyCode.Space))
            {
                Velocity.y = JumpForce;
            }
        }
        else
        {
            Velocity.y += Gravity * -2f * Time.deltaTime;
        }
        if (Sprinting)
        {
            Controller.Move(MoveVector * SprintSpeed * Time.deltaTime);
        }
        else
        {   if (_isExhausted)
                Controller.Move(MoveVector * Speed*exhaustionSpeedMultiplier * Time.deltaTime);
            else
                Controller.Move(MoveVector * Speed * Time.deltaTime);
        }
        Controller.Move(Velocity * Time.deltaTime);

    }
    private void MoveCamera()
    {
        if (FullMapViewer.IsOpen || ChatUI.IsChatFocused) return; // ? add this line, done

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


    void UpdateStaminaBar()
    {
        if (staminaBarFill == null) return;
        // Update the fill amount based on the health ratio
        staminaBarFill.fillAmount = _currentStamina / maxStamina;
        // If using a Slider: healthSlider.value = currentHealth / maxHealth;
    }

    public bool TryConsumeStamina(float amount)
    {
        if (amount <= 0f) return true;
        if (_currentStamina < amount) return false;

        _currentStamina -= amount;
        _currentStamina = Mathf.Clamp(_currentStamina, 0f, maxStamina);
        _regenTimer = 0f;
        UpdateStaminaBar();
        return true;
    }
    public void EnterHalfLife()
{
    if (_hasBeenHit) return;

    _hasBeenHit = true;

    if (_halfLifeEffect != null)
        _halfLifeEffect.isHalfLife = true;

    Debug.Log("Half Life mode activated!");
}
}
