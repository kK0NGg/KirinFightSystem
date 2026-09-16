using UnityEngine;

public class CombatEntity : MonoBehaviour
{
    public static event System.Action<float> CombatImpact;

    [Header("Identity")]
    public bool isBoss;
    public float maxHealth = 100f;
    public bool showHitDebug = true;

    [Header("Block")]
    public bool useBlockSystem = true;
    public float maxBlockHealth = 50f;
    public float blockDamageMultiplier = 1f;
    public float blockRegenDelay = 5f;
    public float[] blockRegenRates = { 1f, 2f, 3f, 5f, 7f, 10f, 15f };

    [Header("Death / Ragdoll")]
    public bool useRagdollOnDeath = true;
    public bool disableObjectOnDeath = false;
    public bool canWalkThroughAfterDeath = false;
    public bool fadeAfterDeath = true;
    public float deathFadeDelay = 5f;
    public float deathFadeDuration = 1f;
    public float deathImpulse = 2f;

    [Header("Recovery")]
    public bool useHitRecovery = true;
    public float hitRecoveryTime = 0.25f;
    public float bossRecoveryMultiplier = 0.5f;

    [Header("Animation")]
    public Animator animator;
    public bool useHitReaction = true;
    public string hitTriggerPrefix = "Hit";
    public string guardReactionTrigger = "GuardReaction";
    public string guardBool = "Guard";
    public string hitStatePrefix = "Hit";
    public string guardReactionState = "GuardReaction";
    public float hitAnimationSpeed = 1.15f;
    public float guardReactionSpeed = 1.1f;
    public float guardReactionDuration = 0.25f;
    public bool useHitAnimation = true;

    [Header("Impact Feel")]
    public float knockbackStrength = 2.5f;
    public float knockbackDuration = 0.12f;
    public float maxKnockbackDistance = 0.8f;
    public float hitStopDuration = 0.04f;
    public bool useHitStop = true;

    private float currentHealth;
    private float currentBlockHealth;
    private float blockRegenTimer;
    private float blockRegenElapsed;
    private float recoveryTimer;
    private float reactionSpeedTimer;
    private float animatorSpeedBeforeHit = 1f;
    private bool deathHandled;
    private Rigidbody[] ragdollBodies;
    private Collider[] ragdollColliders;
    private Vector3 knockbackVelocity;
    private float knockbackTimer;
    private static bool hitStopActive;
    private static float hitStopPreviousTimeScale = 1f;

    public bool IsRecovering => recoveryTimer > 0f;
    public bool IsDead => currentHealth <= 0f;
    public float CurrentHealth => currentHealth;
    public float CurrentBlockHealth => currentBlockHealth;

    void Awake()
    {
        currentHealth = maxHealth;
        currentBlockHealth = maxBlockHealth;
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        ragdollBodies = GetComponentsInChildren<Rigidbody>(true);
        ragdollColliders = GetComponentsInChildren<Collider>(true);
        SetRagdollState(false);
    }

    void OnEnable()
    {
        if (animator != null)
            animator.speed = 1f;
    }

    void OnDisable()
    {
        if (animator != null)
            animator.speed = 1f;

        if (hitStopActive && !isActiveAndEnabled)
        {
            Time.timeScale = hitStopPreviousTimeScale;
            hitStopActive = false;
        }
    }

    void Update()
    {
        if (recoveryTimer > 0f)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f && animator != null)
                animator.speed = animatorSpeedBeforeHit;
        }

        if (reactionSpeedTimer > 0f)
        {
            reactionSpeedTimer -= Time.deltaTime;
            if (reactionSpeedTimer <= 0f && animator != null)
                animator.speed = 1f;
        }

        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.deltaTime;
            CharacterController characterController = GetComponent<CharacterController>();
            if (characterController != null && characterController.enabled)
                characterController.Move(knockbackVelocity * Time.deltaTime);
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 12f * Time.deltaTime);
        }

            UpdateBlockRegeneration();
    }

    public void ReceiveHit(float damage, Vector3 hitDirection, string hitSource = "Unknown", int attackIndex = 1)
    {
        if (IsDead || IsRecovering)
            return;

        bool isBlocking = animator != null && HasParameter(guardBool, AnimatorControllerParameterType.Bool)
            && animator.GetBool(guardBool);

        if (isBlocking && useBlockSystem && currentBlockHealth > 0f)
        {
            float blockedDamage = damage * blockDamageMultiplier;
            currentBlockHealth = Mathf.Max(0f, currentBlockHealth - blockedDamage);
            blockRegenTimer = 0f;
            blockRegenElapsed = 0f;

            if (showHitDebug)
                Debug.Log($"{name} blocked {blockedDamage} damage from {hitSource}. Block HP: {currentBlockHealth}/{maxBlockHealth}", this);

            CombatImpact?.Invoke(0.65f);
            StartHitStop(hitStopDuration * 0.6f);
            PlayReaction(guardReactionState, guardReactionTrigger, guardReactionSpeed);
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damage);
        CombatImpact?.Invoke(1f);
        ApplyKnockback(hitDirection);
        StartHitStop(hitStopDuration);

        EnemyMovement enemyMovement = GetComponent<EnemyMovement>();
        if (enemyMovement != null)
            enemyMovement.ReactToHitAndRetreat();

        if (showHitDebug)
            Debug.Log($"{name} was hit by {hitSource} for {damage} damage. HP: {currentHealth}/{maxHealth}", this);

        if (currentHealth <= 0f)
        {
            Die(hitDirection);
            return;
        }

        if (!useHitRecovery)
            return;

        recoveryTimer = hitRecoveryTime * (isBoss ? bossRecoveryMultiplier : 1f);
        if (!useHitAnimation || !useHitReaction || animator == null)
            return;

        animatorSpeedBeforeHit = animator.speed;
        animator.speed = hitAnimationSpeed;

        string reactionName = hitStatePrefix + Mathf.Clamp(attackIndex, 1, 3);
        string reactionTrigger = hitTriggerPrefix + Mathf.Clamp(attackIndex, 1, 3);
        PlayReaction(reactionName, reactionTrigger, hitAnimationSpeed);
    }

    void ApplyKnockback(Vector3 hitDirection)
    {
        if (knockbackStrength <= 0f || knockbackDuration <= 0f)
            return;

        hitDirection.y = 0f;
        if (hitDirection.sqrMagnitude < 0.001f)
            return;

        float knockbackDistance = Mathf.Min(knockbackStrength, maxKnockbackDistance);
        knockbackVelocity = hitDirection.normalized * (knockbackDistance / knockbackDuration);
        knockbackTimer = knockbackDuration;
    }

    void StartHitStop(float duration)
    {
        if (!useHitStop || duration <= 0f || hitStopActive)
            return;

        StartCoroutine(HitStopRoutine(duration));
    }

    System.Collections.IEnumerator HitStopRoutine(float duration)
    {
        hitStopActive = true;
        hitStopPreviousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = hitStopPreviousTimeScale;
        hitStopActive = false;
    }

    void PlayReaction(string stateName, string triggerName, float playbackSpeed)
    {
        if (!useHitReaction || animator == null)
            return;

        animatorSpeedBeforeHit = animator.speed;
        animator.speed = playbackSpeed;
        reactionSpeedTimer = stateName == guardReactionState ? guardReactionDuration : hitRecoveryTime;

        if (HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
            animator.SetTrigger(triggerName);

        if (!string.IsNullOrWhiteSpace(stateName))
        {
            int stateHash = Animator.StringToHash("Base Layer." + stateName);
            if (animator.HasState(0, stateHash))
                animator.CrossFadeInFixedTime(stateHash, 0.08f);
        }
    }

    void UpdateBlockRegeneration()
    {
        if (!useBlockSystem || currentBlockHealth >= maxBlockHealth)
            return;

        blockRegenTimer += Time.deltaTime;
        if (blockRegenTimer < blockRegenDelay)
            return;

        blockRegenElapsed += Time.deltaTime;
        blockRegenTimer = blockRegenDelay;

        if (blockRegenRates == null || blockRegenRates.Length == 0)
            return;

        int rateIndex = Mathf.Clamp(Mathf.FloorToInt(blockRegenElapsed), 0, blockRegenRates.Length - 1);
        float regenRate = blockRegenRates[rateIndex];
        currentBlockHealth = Mathf.Min(maxBlockHealth, currentBlockHealth + regenRate * Time.deltaTime);
    }

    void Die(Vector3 hitDirection)
    {
        if (deathHandled)
            return;

        deathHandled = true;
        recoveryTimer = 0f;

        ThirdPersonController playerController = GetComponent<ThirdPersonController>();
        if (playerController != null)
            playerController.enabled = false;

        EnemyMovement enemyMovement = GetComponent<EnemyMovement>();
        if (enemyMovement != null)
            enemyMovement.enabled = false;

        CombatHitbox[] hitboxes = GetComponentsInChildren<CombatHitbox>(true);
        foreach (CombatHitbox hitbox in hitboxes)
            hitbox.SetHitboxActive(false);

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
            characterController.enabled = false;

        if (animator != null)
            animator.enabled = false;

        if (useRagdollOnDeath)
        {
            SetRagdollState(true);
            AddDeathImpulse(hitDirection);
        }

        if (canWalkThroughAfterDeath)
        {
            if (useRagdollOnDeath)
                IgnorePlayerCollisionsAfterDeath();
            else
                DisableDeathCollisions();
        }

        if (disableObjectOnDeath)
            gameObject.SetActive(false);
        else if (fadeAfterDeath)
            StartCoroutine(FadeAndRemoveAfterDeath());
    }

    void SetRagdollState(bool ragdollEnabled)
    {
        if (ragdollBodies != null)
        {
            foreach (Rigidbody body in ragdollBodies)
            {
                if (body == null || body.gameObject == gameObject)
                    continue;

                body.isKinematic = !ragdollEnabled;
                body.useGravity = ragdollEnabled;
            }
        }

        if (ragdollColliders != null)
        {
            foreach (Collider collider in ragdollColliders)
            {
                if (collider == null || collider.gameObject == gameObject || collider is CharacterController)
                    continue;

                collider.enabled = ragdollEnabled;
            }
        }
    }

    void AddDeathImpulse(Vector3 hitDirection)
    {
        if (hitDirection.sqrMagnitude < 0.01f)
            return;

        foreach (Rigidbody body in ragdollBodies)
        {
            if (body != null && body.gameObject != gameObject)
                body.AddForce(hitDirection.normalized * deathImpulse, ForceMode.Impulse);
        }
    }

    void DisableDeathCollisions()
    {
        if (ragdollColliders == null)
            return;

        foreach (Collider collider in ragdollColliders)
        {
            if (collider != null)
                collider.enabled = false;
        }
    }

    void IgnorePlayerCollisionsAfterDeath()
    {
        if (ragdollColliders == null)
            return;

        ThirdPersonController playerController = FindAnyObjectByType<ThirdPersonController>();
        if (playerController == null)
            return;

        Collider[] playerColliders = playerController.GetComponentsInChildren<Collider>(true);
        foreach (Collider corpseCollider in ragdollColliders)
        {
            if (corpseCollider == null || !corpseCollider.enabled)
                continue;

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null && playerCollider != corpseCollider)
                    Physics.IgnoreCollision(corpseCollider, playerCollider, true);
            }
        }
    }

    System.Collections.IEnumerator FadeAndRemoveAfterDeath()
    {
        yield return new WaitForSeconds(deathFadeDelay);

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Material[][] materials = new Material[renderers.Length][];
        Color[][] originalColors = new Color[renderers.Length][];

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            materials[rendererIndex] = renderers[rendererIndex].materials;
            originalColors[rendererIndex] = new Color[materials[rendererIndex].Length];

            for (int materialIndex = 0; materialIndex < materials[rendererIndex].Length; materialIndex++)
                originalColors[rendererIndex][materialIndex] = GetMaterialColor(materials[rendererIndex][materialIndex]);
        }

        float elapsed = 0f;
        while (elapsed < deathFadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, deathFadeDuration));

            for (int rendererIndex = 0; rendererIndex < materials.Length; rendererIndex++)
            {
                for (int materialIndex = 0; materialIndex < materials[rendererIndex].Length; materialIndex++)
                {
                    Color color = originalColors[rendererIndex][materialIndex];
                    color.a *= alpha;
                    SetMaterialColor(materials[rendererIndex][materialIndex], color);
                }
            }

            yield return null;
        }

        gameObject.SetActive(false);
    }

    Color GetMaterialColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");
        return Color.white;
    }

    void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
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
}
