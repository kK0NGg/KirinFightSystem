using UnityEngine;

public enum EnemyGroupBehavior
{
    Solo,
    Group,
    Surround,
    Queue
}

public enum EnemyCombatStyle
{
    Attack,
    Feint,
    Block,
    SideStep
}

[RequireComponent(typeof(CharacterController))]
public class EnemyMovement : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public bool findPlayerByTag = true;
    public bool enableFollow = true;
    public float detectionRange = 15f;

    [Header("Movement")]
    public float moveSpeed = 2.5f;
    public float acceleration = 8f;
    public float deceleration = 12f;
    public float rotationSpeed = 10f;
    public float stopDistance = 2f;
    public bool canMove = true;
    public KeyCode guardKey = KeyCode.Y;
    public bool enableTestGuard = true;
    public EnemyGroupBehavior groupBehavior = EnemyGroupBehavior.Group;
    public float groupRadius = 8f;
    public float groupSeparation = 1.5f;
    public float surroundRadius = 2.8f;
    public int maxSimultaneousAttackers = 2;
    public bool retreatWhenWaiting = true;
    public EnemyCombatStyle combatStyle = EnemyCombatStyle.Attack;
    public float styleDecisionInterval = 2.5f;
    [Range(0f, 1f)] public float blockChance = 0.35f;
    public float sideStepDuration = 0.7f;

    [Header("Combat")]
    public float attackRange = 2.2f;
    public float attackCooldown = 1.1f;
    public float attackDuration = 0.6f;
    public float attackWindup = 0.18f;
    public float attackActiveTime = 0.2f;
    public float fightIdleRange = 6f;
    public float backupDistance = 1.4f;
    public float backupWaitTime = 1.2f;
    public int backupStepCount = 3;
    public float backupStepDuration = 0.22f;
    public float backupStepPause = 0.16f;
    public float comboResetTime = 3f;
    public CombatHitbox attackHitbox;
    [Range(1, 3)] public int attackIndex = 1;

    [Header("Animation")]
    public Animator animator;
    public RuntimeAnimatorController enemyAnimationController;
    public string speedParameter = "Speed";
    [Range(0.1f, 2f)] public float punchAnimationSpeed = 0.75f;

    private CharacterController controller;
    private CombatEntity combatEntity;
    private float currentMoveSpeed;
    private float attackCooldownTimer;
    private bool isAttacking;
    private float attackTimer;
    private int comboStep;
    private float comboResetTimer;
    private bool isBackingUp;
    private float backupWaitTimer;
    private float backupStepTimer;
    private int backupStepsRemaining;
    private bool isMovingBackupStep;
    private int currentLocomotionStateHash;
    private float styleDecisionTimer;
    private float sideStepTimer;
    private int sideStepDirection = 1;
    private bool styleGuarding;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        combatEntity = GetComponentInParent<CombatEntity>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (animator != null)
            animator.speed = 1f;

        currentMoveSpeed = 0f;
        attackTimer = 0f;
        isAttacking = false;
    }

    void OnDisable()
    {
        if (attackHitbox != null)
            attackHitbox.SetHitboxActive(false);

        if (animator != null)
            animator.speed = 1f;
    }

    void Start()
    {
        if (target == null && findPlayerByTag)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (target == null)
            target = FindAnyObjectByType<ThirdPersonController>()?.transform;

        if (attackHitbox == null)
            attackHitbox = GetComponentInChildren<CombatHitbox>(true);

        if (attackHitbox != null && combatEntity != null)
            attackHitbox.owner = combatEntity;

        if (attackHitbox != null && target != null)
        {
            attackHitbox.targetLayers |= 1 << target.gameObject.layer;

            Collider[] targetColliders = target.GetComponentsInChildren<Collider>(true);
            foreach (Collider targetCollider in targetColliders)
                attackHitbox.targetLayers |= 1 << targetCollider.gameObject.layer;
        }

        if (animator != null && enemyAnimationController != null)
            animator.runtimeAnimatorController = enemyAnimationController;
    }

    void Update()
    {
        if (animator != null && animator.isActiveAndEnabled && animator.speed <= 0f)
            animator.speed = 1f;

        UpdateGroundedAnimation();

        bool guarding = enableTestGuard && Input.GetKey(guardKey);
        UpdateCombatStyle();
        SetGuarding(guarding || styleGuarding);

        attackCooldownTimer -= Time.deltaTime;
        comboResetTimer -= Time.deltaTime;

        if (comboResetTimer <= 0f)
            ResetCombo();

        if (isAttacking)
        {
            attackTimer += Time.deltaTime * Mathf.Max(0.1f, punchAnimationSpeed);
            if (attackHitbox != null)
            {
                bool hitboxActive = attackTimer >= attackWindup && attackTimer <= attackWindup + attackActiveTime;
                attackHitbox.SetHitboxActive(hitboxActive);
            }

            if (attackTimer >= attackDuration)
            {
                bool finishedCombo = attackIndex == 3;
                isAttacking = false;
                attackTimer = 0f;
                if (attackHitbox != null)
                    attackHitbox.SetHitboxActive(false);

                if (animator != null)
                    animator.speed = 1f;

                if (finishedCombo)
                {
                    StartBackup();
                }
                else
                {
                    attackCooldownTimer = attackCooldown;
                    PlayLocomotionState("Fight Idle");
                }
            }
        }

        if (isAttacking)
        {
            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        if (guarding || !CanFollow())
        {
            SetFightIdle(false);
            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        if (!canMove || target == null || !CanUseController())
        {
            SetFightIdle(false);
            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distanceToTarget = toTarget.magnitude;

        if (distanceToTarget > detectionRange || toTarget.sqrMagnitude < 0.001f)
        {
            SetFightIdle(false);
            PlayLocomotionState("Movement");
            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        if (isBackingUp)
        {
            FaceTarget(toTarget);
            PlayLocomotionState("Fight Walk Back");

            if (backupStepsRemaining > 0)
            {
                if (isMovingBackupStep)
                {
                    MoveAtSpeed(moveSpeed * 0.8f, -toTarget.normalized);
                    backupStepTimer += Time.deltaTime;

                    if (backupStepTimer >= backupStepDuration)
                    {
                        backupStepsRemaining--;
                        backupStepTimer = 0f;
                        isMovingBackupStep = false;
                    }
                }
                else
                {
                    MoveAtSpeed(0f, Vector3.zero);
                    backupStepTimer -= Time.deltaTime;

                    if (backupStepTimer <= -backupStepPause)
                    {
                        backupStepTimer = 0f;
                        isMovingBackupStep = true;
                    }
                }
            }
            else
            {
                backupWaitTimer += Time.deltaTime;
                MoveAtSpeed(0f, Vector3.zero);

                if (backupWaitTimer >= backupWaitTime)
                {
                    isBackingUp = false;
                    backupWaitTimer = 0f;
                    backupStepTimer = 0f;
                    isMovingBackupStep = false;
                    ResetCombo();
                }
            }

            ApplyGravity();
            return;
        }

        bool isCloseEnoughForFightIdle = distanceToTarget <= fightIdleRange;
        SetFightIdle(isCloseEnoughForFightIdle);

        if (distanceToTarget <= attackRange)
        {
            FaceTarget(toTarget);
            PlayLocomotionState("Fight Idle");

            if (!isAttacking && attackCooldownTimer <= 0f)
            {
                if (CanStartGroupAttack())
                    TryAttack();
                else if (retreatWhenWaiting)
                    StartBackup();
            }

            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        if (distanceToTarget <= stopDistance)
        {
            PlayLocomotionState("Fight Idle");
            MoveAtSpeed(0f, Vector3.zero);
            ApplyGravity();
            return;
        }

        Vector3 moveDirection = GetGroupMoveDirection(toTarget, distanceToTarget);
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

        if (distanceToTarget <= fightIdleRange)
        {
            if (combatStyle == EnemyCombatStyle.SideStep && sideStepTimer > 0f)
                PlayLocomotionState(sideStepDirection < 0 ? "Fight Walk Left" : "Fight Walk Right");
            else
                PlayLocomotionState("Fight Walk Front");
        }
        else
            PlayLocomotionState("Movement");

        MoveAtSpeed(moveSpeed, moveDirection);
        ApplyGravity();
    }

    void TryAttack()
    {
        if (isAttacking || attackCooldownTimer > 0f || target == null)
            return;

        int punchNumber = comboStep % 3 + 1;
        TriggerPunch(punchNumber);
        comboStep = punchNumber;
        comboResetTimer = comboResetTime;
        attackCooldownTimer = attackCooldown;
    }

    void UpdateCombatStyle()
    {
        styleDecisionTimer -= Time.deltaTime;

        if (sideStepTimer > 0f)
            sideStepTimer -= Time.deltaTime;

        if (styleDecisionTimer > 0f)
            return;

        styleDecisionTimer = Mathf.Max(0.1f, styleDecisionInterval);

        if (combatStyle == EnemyCombatStyle.Block)
        {
            styleGuarding = Random.value <= blockChance;
        }
        else if (combatStyle == EnemyCombatStyle.Feint && target != null)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance <= fightIdleRange && Random.value > 0.35f)
                StartBackup();
        }
        else if (combatStyle == EnemyCombatStyle.SideStep)
        {
            sideStepDirection = Random.value < 0.5f ? -1 : 1;
            sideStepTimer = sideStepDuration;
        }
        else
        {
            styleGuarding = false;
        }
    }

    Vector3 GetGroupMoveDirection(Vector3 toTarget, float distanceToTarget)
    {
        Vector3 desiredDirection = toTarget.normalized;
        if (groupBehavior == EnemyGroupBehavior.Surround && target != null)
        {
            EnemyMovement[] group = GetGroupMembers();
            int slot = GetGroupSlot(group);
            float angle = (360f / Mathf.Max(1, group.Length)) * slot * Mathf.Deg2Rad;
            Vector3 desiredPosition = target.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * surroundRadius;
            Vector3 toSlot = desiredPosition - transform.position;
            toSlot.y = 0f;
            if (toSlot.sqrMagnitude > 0.001f)
                desiredDirection = toSlot.normalized;
        }

        if (groupBehavior == EnemyGroupBehavior.Group
            || groupBehavior == EnemyGroupBehavior.Surround
            || groupBehavior == EnemyGroupBehavior.Queue)
        {
            Vector3 separation = GetSeparationDirection();
            desiredDirection = (desiredDirection + separation).normalized;
        }

        if (combatStyle == EnemyCombatStyle.SideStep && sideStepTimer > 0f)
        {
            Vector3 sideDirection = Vector3.Cross(Vector3.up, desiredDirection) * sideStepDirection;
            desiredDirection = (desiredDirection * 0.65f + sideDirection * 0.75f).normalized;
        }

        return desiredDirection.sqrMagnitude > 0.001f ? desiredDirection : toTarget.normalized;
    }

    Vector3 GetSeparationDirection()
    {
        Vector3 separation = Vector3.zero;
        EnemyMovement[] group = GetGroupMembers();

        foreach (EnemyMovement other in group)
        {
            if (other == this || other.target != target)
                continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance > 0.001f && distance < groupSeparation)
                separation += away.normalized * (1f - distance / groupSeparation);
        }

        return separation;
    }

    EnemyMovement[] GetGroupMembers()
    {
        EnemyMovement[] allEnemies = FindObjectsByType<EnemyMovement>(FindObjectsSortMode.None);
        return System.Array.FindAll(allEnemies, enemy => enemy != null
            && enemy.isActiveAndEnabled
            && enemy.target == target
            && Vector3.Distance(enemy.transform.position, transform.position) <= groupRadius);
    }

    int GetGroupSlot(EnemyMovement[] group)
    {
        System.Array.Sort(group, (first, second) => first.GetInstanceID().CompareTo(second.GetInstanceID()));
        return System.Array.IndexOf(group, this);
    }

    bool CanStartGroupAttack()
    {
        if (groupBehavior == EnemyGroupBehavior.Solo || maxSimultaneousAttackers <= 0)
            return true;

        int attackingEnemies = 0;
        foreach (EnemyMovement enemy in GetGroupMembers())
        {
            if (enemy != this && enemy.isAttacking)
                attackingEnemies++;
        }

        return attackingEnemies < maxSimultaneousAttackers;
    }

    void TriggerPunch(int punchNumber)
    {
        isAttacking = true;
        attackTimer = 0f;
        attackIndex = Mathf.Clamp(punchNumber, 1, 3);

        if (attackHitbox != null)
        {
            attackHitbox.attackIndex = attackIndex;
            attackHitbox.owner = combatEntity;
            attackHitbox.SetHitboxActive(false);
        }

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.speed = punchAnimationSpeed;
            currentLocomotionStateHash = 0;
            string triggerName = "Punch" + punchNumber;
            int punchStateHash = Animator.StringToHash("Base Layer.Punch" + punchNumber);

            if (animator.HasState(0, punchStateHash))
            {
                animator.CrossFadeInFixedTime(punchStateHash, 0.08f);
                return;
            }

            if (HasAnimatorParameter(triggerName, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(triggerName);
                return;
            }

            if (HasAnimatorParameter("Attack", AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger("Attack");
                return;
            }

            int genericAttack = Animator.StringToHash("Base Layer.Attack");
            if (animator.HasState(0, genericAttack))
                animator.CrossFadeInFixedTime(genericAttack, 0.08f);
        }
    }

    void FaceTarget(Vector3 directionToTarget)
    {
        directionToTarget.y = 0f;
        if (directionToTarget.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(directionToTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    void PlayLocomotionState(string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        int stateHash = Animator.StringToHash("Base Layer." + stateName);
        if (currentLocomotionStateHash == stateHash || !animator.HasState(0, stateHash))
            return;

        animator.CrossFadeInFixedTime(stateHash, 0.12f);
        currentLocomotionStateHash = stateHash;
    }

    void ResetCombo()
    {
        comboStep = 0;
        comboResetTimer = 0f;
    }

    public void ReactToHitAndRetreat()
    {
        if (combatEntity != null && combatEntity.IsDead)
            return;

        isAttacking = false;
        attackTimer = 0f;
        attackCooldownTimer = attackCooldown;
        if (animator != null)
            animator.speed = 1f;
        ResetCombo();
        StartBackup();

        if (attackHitbox != null)
            attackHitbox.SetHitboxActive(false);
    }

    void StartBackup()
    {
        isBackingUp = true;
        backupWaitTimer = 0f;
        backupStepTimer = 0f;
        backupStepsRemaining = Mathf.Max(0, backupStepCount);
        isMovingBackupStep = backupStepsRemaining > 0;
    }

    void SetFightIdle(bool active)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        if (HasAnimatorParameter("FightIdle", AnimatorControllerParameterType.Bool))
            animator.SetBool("FightIdle", active);
        else if (HasAnimatorParameter("Combat", AnimatorControllerParameterType.Bool))
            animator.SetBool("Combat", active);
    }

    bool CanFollow()
    {
        if (!enableFollow || (combatEntity != null && (combatEntity.IsDead || combatEntity.IsRecovering)))
            return false;

        if (animator == null || animator.runtimeAnimatorController == null)
            return true;

        if (HasGuardParameter() && animator.GetBool("Guard"))
            return false;


        return true;
    }

    bool IsAttackState(AnimatorStateInfo state)
    {
        return state.IsName("Base Layer.Attack")
            || state.IsName("Base Layer.Punch1")
            || state.IsName("Base Layer.Punch2")
            || state.IsName("Base Layer.Punch3");
    }

    void MoveAtSpeed(float targetSpeed, Vector3 moveDirection)
    {
        float rate = targetSpeed > currentMoveSpeed ? acceleration : deceleration;
        currentMoveSpeed = Mathf.MoveTowards(currentMoveSpeed, targetSpeed, rate * Time.deltaTime);

        if (currentMoveSpeed > 0.001f && moveDirection.sqrMagnitude > 0.001f && CanUseController())
            controller.Move(moveDirection * currentMoveSpeed * Time.deltaTime);

        SetAnimationSpeed(currentMoveSpeed);
    }

    void ApplyGravity()
    {
        if (!CanUseController())
            return;

        if (controller.isGrounded)
            controller.Move(Vector3.down * 2f * Time.deltaTime);
        else
            controller.Move(Physics.gravity * Time.deltaTime);
    }

    bool CanUseController()
    {
        return isActiveAndEnabled
            && gameObject.activeInHierarchy
            && controller != null
            && controller.enabled;
    }

    void UpdateGroundedAnimation()
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        {
            if (HasAnimatorParameter("IsGrounded", AnimatorControllerParameterType.Bool))
                animator.SetBool("IsGrounded", controller != null && controller.enabled && controller.isGrounded);

            if (HasAnimatorParameter("Falling", AnimatorControllerParameterType.Bool))
                animator.SetBool("Falling", false);
        }
    }

    void SetAnimationSpeed(float speed)
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null && HasSpeedParameter())
            animator.SetFloat(speedParameter, speed, 0.12f, Time.deltaTime);
    }

    void SetGuarding(bool guarding)
    {
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null && HasGuardParameter())
            animator.SetBool("Guard", guarding);
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

    bool HasGuardParameter()
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == "Guard" && parameter.type == AnimatorControllerParameterType.Bool)
                return true;
        }

        return false;
    }

    bool HasSpeedParameter()
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == speedParameter && parameter.type == AnimatorControllerParameterType.Float)
                return true;
        }

        return false;
    }
}
