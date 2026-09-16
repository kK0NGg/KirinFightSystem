using UnityEngine;

[RequireComponent(typeof(CombatEntity))]
public class TrainingDummy : MonoBehaviour
{
    [Header("Test Controls")]
    public KeyCode attackKey = KeyCode.T;
    public KeyCode guardKey = KeyCode.Y;
    public CombatHitbox attackHitbox;
    public float attackDuration = 0.45f;
    public float attackHitStart = 0.12f;
    public float attackHitEnd = 0.3f;
    [Range(1, 3)] public int attackIndex = 1;

    private CombatEntity combatEntity;
    private float attackTimer;
    private bool attacking;

    void Awake()
    {
        combatEntity = GetComponent<CombatEntity>();
        if (attackHitbox == null)
            attackHitbox = GetComponentInChildren<CombatHitbox>();
    }

    void Update()
    {
        if (Input.GetKeyDown(attackKey) && !attacking && !combatEntity.IsRecovering)
        {
            attacking = true;
            attackTimer = 0f;
            if (attackHitbox != null)
                attackHitbox.attackIndex = attackIndex;
            if (combatEntity.animator != null)
            {
                if (HasParameter("Attack", AnimatorControllerParameterType.Trigger))
                    combatEntity.animator.SetTrigger("Attack");

                int punchState = Animator.StringToHash("Base Layer.Punch1");
                if (combatEntity.animator.HasState(0, punchState))
                    combatEntity.animator.CrossFadeInFixedTime(punchState, 0.1f);
            }
        }

        bool guarding = Input.GetKey(guardKey);
        if (combatEntity.animator != null && HasParameter("Guard", AnimatorControllerParameterType.Bool))
            combatEntity.animator.SetBool("Guard", guarding);

        if (!attacking)
        {
            attackHitbox?.SetHitboxActive(false);
            return;
        }

        attackTimer += Time.deltaTime;
        attackHitbox?.SetHitboxActive(attackTimer >= attackHitStart && attackTimer <= attackHitEnd);

        if (attackTimer >= attackDuration)
        {
            attacking = false;
            attackHitbox?.SetHitboxActive(false);
        }
    }

    bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (combatEntity.animator == null || combatEntity.animator.runtimeAnimatorController == null)
            return false;

        foreach (AnimatorControllerParameter parameter in combatEntity.animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == parameterType)
                return true;
        }

        return false;
    }
}
