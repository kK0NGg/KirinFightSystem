using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float aimWalkSpeed = 2.5f;
    public float punchMoveSpeed = 1.25f;
    public float punchCooldown = 0.45f;
    public float comboResetTime = 3f;
    public float punchAnimationSpeed = 1.25f;
    public float landingAnimationSpeed = 0.7f;
    public float animationSmoothTime = 0.12f;
    public float animationCrossFadeTime = 0.18f;
    public float airControl = 0.65f;
    public float landingRecoveryTime = 0.35f;
    public bool useLandingSystem = true;
    public bool useLandingAnimation = true;
    public float turnSpeed = 10f;
    public float aimTurnSpeed = 16f;
    public float jumpHeight = 1.5f;
    public float gravity = -9.81f;
    public KeyCode guardKey = KeyCode.G;
    public KeyCode attackKey = KeyCode.Mouse0;
    public bool enableAim = true;
    public CombatHitbox punchHitbox;
    public float punchHitStart = 0.18f;
    public float punchHitEnd = 0.42f;

    [Header("References (drag in Inspector)")]
    public Transform cameraTransform;
    public Animator animator;
    public RuntimeAnimatorController animationController;

    [Header("Collision Debug")]
    public bool debugPlayerCollisions;
    public bool drawPlayerCollisionGizmos = true;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isAiming;
    private bool isGuarding;
    private bool attackPlaying;
    private bool attackQueued;
    private bool landingPlaying;
    private bool wasGrounded;
    private float landingRecoveryTimer;
    private int nextPunch = 1;
    private float lastPunchTime;
    private float punchTimer;
    private Collider lastBlockingCollider;
    private float lastCollisionLogTime = -1f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        ClampStepOffsetToScale();
        wasGrounded = controller.isGrounded;

        if (cameraTransform == null)
        {
            if (Camera.main != null)
                cameraTransform = Camera.main.transform;
            else
                cameraTransform = FindAnyObjectByType<ThirdPersonCamera>()?.transform;
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (punchHitbox == null)
            punchHitbox = GetComponentInChildren<CombatHitbox>();

        if (animator != null && animationController != null)
            animator.runtimeAnimatorController = animationController;
    }

    void ClampStepOffsetToScale()
    {
        if (controller == null)
            return;

        float verticalScale = Mathf.Abs(transform.lossyScale.y);
        float horizontalScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float scaledHeight = controller.height * verticalScale;
        float scaledRadius = controller.radius * horizontalScale;
        float maximumStepOffset = scaledHeight + scaledRadius * 2f;
        controller.stepOffset = Mathf.Min(controller.stepOffset, maximumStepOffset);
    }

    void Update()
    {
        if (controller == null || !controller.enabled || !gameObject.activeInHierarchy)
            return;

        isAiming = enableAim && Input.GetMouseButton(1);
        isGuarding = isAiming && Input.GetKey(guardKey);

        HandleGravityAndGround();
        HandleMovement();
        HandleJump();
        HandleAttack();
        UpdatePunchHitbox();
        RestoreNormalAnimatorSpeedWhenNeeded();
    }

    void HandleGravityAndGround()
    {
        bool groundedBeforeMove = controller.isGrounded;
        if (groundedBeforeMove && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        bool groundedAfterMove = controller.isGrounded;
        if (!wasGrounded && groundedAfterMove)
        {
            landingRecoveryTimer = useLandingSystem ? landingRecoveryTime : 0f;
            if (useLandingSystem && useLandingAnimation && animator != null && HasAnimatorParameter("Landing", AnimatorControllerParameterType.Trigger))
            {
                landingPlaying = true;
                animator.speed = landingAnimationSpeed;
                animator.SetTrigger("Landing");
            }
        }

        if (landingRecoveryTimer > 0f)
            landingRecoveryTimer -= Time.deltaTime;

        isGrounded = groundedAfterMove;
        wasGrounded = groundedAfterMove;

        animator?.SetBool("IsGrounded", isGrounded);
        animator?.SetBool("Falling", !isGrounded && velocity.y < 0f);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!debugPlayerCollisions || hit.collider == null)
            return;

        if (lastBlockingCollider == hit.collider && Time.time - lastCollisionLogTime < 0.5f)
            return;

        lastBlockingCollider = hit.collider;
        lastCollisionLogTime = Time.time;
        Bounds colliderBounds = hit.collider.bounds;
        Debug.Log(
            $"Player CharacterController touched '{hit.collider.name}' "
            + $"type: {hit.collider.GetType().Name}, root: {hit.collider.transform.root.name}, "
            + $"layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}, tag: {hit.collider.tag}, "
            + $"trigger: {hit.collider.isTrigger}, size: {colliderBounds.size}, point: {hit.point})",
            hit.collider);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawPlayerCollisionGizmos)
            return;

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            Gizmos.color = Color.yellow;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 center = characterController.center;
            Vector3 size = new Vector3(characterController.radius * 2f, characterController.height, characterController.radius * 2f);
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previousMatrix;
        }

        Collider[] childColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider childCollider in childColliders)
        {
            if (childCollider == null || childCollider is CharacterController)
                continue;

            Gizmos.color = childCollider.isTrigger ? Color.magenta : Color.red;
            Gizmos.DrawWireCube(childCollider.bounds.center, childCollider.bounds.size);
        }
    }

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(h, 0f, v);

        if (isGuarding)
        {
            inputDir = Vector3.zero;
            h = 0f;
            v = 0f;
        }

        bool canSprint = isGrounded && landingRecoveryTimer <= 0f;
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && canSprint && !isAiming && !isGuarding && !attackPlaying;
        float targetSpeed = sprinting ? sprintSpeed : moveSpeed;
        if (isAiming)
            targetSpeed = aimWalkSpeed;
        if (attackPlaying)
            targetSpeed = punchMoveSpeed;
        if (!isGrounded && useLandingSystem)
            targetSpeed *= airControl;
        if (landingRecoveryTimer > 0f)
            targetSpeed = Mathf.Min(targetSpeed, aimWalkSpeed);
        float currentSpeed = 0f;

        if (cameraTransform == null)
        {
            if (animator != null && HasAnimatorParameter("Speed", AnimatorControllerParameterType.Float))
                animator.SetFloat("Speed", 0f, animationSmoothTime, Time.deltaTime);
            return;
        }

        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        Vector3 cameraRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);

        if (cameraForward.sqrMagnitude < 0.01f)
            cameraForward = Vector3.forward;
        if (cameraRight.sqrMagnitude < 0.01f)
            cameraRight = Vector3.right;

        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDir = cameraForward * inputDir.z + cameraRight * inputDir.x;

        if (inputDir.sqrMagnitude > 0.01f)
        {
            if (isAiming)
            {
                Vector3 aimForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                if (aimForward.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(aimForward.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, aimTurnSpeed * Time.deltaTime);
                }
            }
            else
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDir.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            controller.Move(moveDir.normalized * targetSpeed * Time.deltaTime);
            currentSpeed = targetSpeed;
        }
        else if (isAiming)
        {
            Vector3 aimForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (aimForward.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(aimForward.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, aimTurnSpeed * Time.deltaTime);
            }
        }

        if (animator != null && HasAnimatorParameter("Speed", AnimatorControllerParameterType.Float))
            animator.SetFloat("Speed", currentSpeed, animationSmoothTime, Time.deltaTime);
        UpdateCombatAnimation(h, v, currentSpeed);
        if (animator != null && HasAnimatorParameter("MoveX", AnimatorControllerParameterType.Float))
            animator.SetFloat("MoveX", isAiming ? h : 0f);
        if (animator != null && HasAnimatorParameter("MoveY", AnimatorControllerParameterType.Float))
            animator.SetFloat("MoveY", isAiming ? v : 0f);
        if (animator != null && HasAnimatorParameter("Combat", AnimatorControllerParameterType.Bool))
            animator.SetBool("Combat", isAiming);
        if (animator != null && HasAnimatorParameter("Aiming", AnimatorControllerParameterType.Bool))
            animator.SetBool("Aiming", isAiming);
        if (animator != null && HasAnimatorParameter("Guard", AnimatorControllerParameterType.Bool))
            animator.SetBool("Guard", isGuarding);
    }

    bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == parameterType)
                return true;
        }

        return false;
    }

    void UpdateCombatAnimation(float horizontal, float vertical, float currentSpeed)
    {
        if (animator == null || !isGrounded)
            return;

        if (landingPlaying)
        {
            AnimatorStateInfo currentLanding = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextLanding = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : default;

            if (IsLandingState(currentLanding) || IsLandingState(nextLanding))
            {
                if (IsLandingState(currentLanding) && currentLanding.normalizedTime >= 0.95f)
                {
                    landingPlaying = false;
                    animator.speed = 1f;
                }
                else
                    return;
            }
            else
            {
                landingPlaying = false;
                animator.speed = 1f;
            }
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (IsLandingState(currentState))
        {
            if (currentState.normalizedTime < 0.95f)
                return;

            animator.speed = 1f;
        }

        if (attackPlaying)
        {
            AnimatorStateInfo currentAttack = animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextAttack = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : default;

            if (IsPunchState(currentAttack) || IsPunchState(nextAttack))
            {
                if (IsPunchState(currentAttack) && currentAttack.normalizedTime >= 0.95f)
                {
                    attackPlaying = false;
                    animator.speed = 1f;
                    if (attackQueued && isAiming && !isGuarding)
                    {
                        attackQueued = false;
                        StartPunch();
                        return;
                    }
                }
                else
                    return;
            }
            else
            {
                attackPlaying = false;
                animator.speed = 1f;
            }
        }

        if (isGuarding)
        {
            PlayStateIfAvailable("Guard");
            return;
        }

        if (!isAiming)
        {
            PlayStateIfAvailable("Movement");
            return;
        }

        if (currentSpeed < 0.01f)
        {
            PlayStateIfAvailable("Fight Idle");
            return;
        }

        string combatState;
        if (Mathf.Abs(vertical) >= Mathf.Abs(horizontal))
            combatState = vertical >= 0f ? "Fight Walk Front" : "Fight Walk Back";
        else
            combatState = horizontal >= 0f ? "Fight Walk Right" : "Fight Walk Left";

        PlayStateIfAvailable(combatState);
    }

    void PlayStateIfAvailable(string stateName)
    {
        int stateHash = Animator.StringToHash("Base Layer." + stateName);
        if (!animator.HasState(0, stateHash))
            return;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : default;

        string fullStateName = "Base Layer." + stateName;
        if (!currentState.IsName(fullStateName) && !nextState.IsName(fullStateName))
            animator.CrossFadeInFixedTime(stateHash, animationCrossFadeTime);
    }

    void HandleJump()
    {
        if (Input.GetButtonDown("Jump") && isGrounded && (!useLandingSystem || landingRecoveryTimer <= 0f) && !isAiming && !isGuarding)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator?.SetTrigger("Jump");
        }
    }

    void HandleAttack()
    {
        if (!Input.GetKeyDown(attackKey) || !isAiming || !isGrounded || isGuarding || animator == null)
            return;

        if (attackPlaying)
        {
            attackQueued = true;
            return;
        }

        if (Time.time - lastPunchTime < punchCooldown)
            return;

        StartPunch();
    }

    void UpdatePunchHitbox()
    {
        if (punchHitbox == null)
            return;

        if (!attackPlaying)
        {
            punchHitbox.SetHitboxActive(false);
            return;
        }

        punchTimer += Time.deltaTime * punchAnimationSpeed;
        bool active = punchTimer >= punchHitStart && punchTimer <= punchHitEnd;
        punchHitbox.SetHitboxActive(active);
    }

    void StartPunch()
    {
        if (Time.time - lastPunchTime > comboResetTime)
            nextPunch = 1;

        string punchState = "Punch" + nextPunch;
        if (!HasAnimatorState(punchState))
            return;

        PlayStateIfAvailable(punchState);
        attackPlaying = true;
        punchTimer = 0f;
        if (punchHitbox != null)
            punchHitbox.attackIndex = nextPunch;
        punchHitbox?.SetHitboxActive(false);
        animator.speed = punchAnimationSpeed;
        lastPunchTime = Time.time;
        nextPunch = nextPunch == 3 ? 1 : nextPunch + 1;

        if (HasAnimatorParameter("Attack", AnimatorControllerParameterType.Trigger))
            animator.SetTrigger("Attack");
    }

    void RestoreNormalAnimatorSpeedWhenNeeded()
    {
        if (animator == null || attackPlaying || landingPlaying)
            return;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : default;

        if (!IsLandingState(currentState) && !IsLandingState(nextState))
            animator.speed = 1f;
    }

    bool HasAnimatorState(string stateName)
    {
        return animator != null && animator.HasState(0, Animator.StringToHash("Base Layer." + stateName));
    }

    bool IsPunchState(AnimatorStateInfo state)
    {
        return state.IsName("Base Layer.Punch1")
            || state.IsName("Base Layer.Punch2")
            || state.IsName("Base Layer.Punch3");
    }

    bool IsLandingState(AnimatorStateInfo state)
    {
        return state.IsName("Base Layer.Landing");
    }
}