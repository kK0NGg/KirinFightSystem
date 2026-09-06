using System.Collections.Generic;
using UnityEngine;

public class CombatHitbox : MonoBehaviour
{
    [Header("Hit")]
    public CombatEntity owner;
    public float damage = 10f;
    [Range(1, 3)] public int attackIndex = 1;
    public LayerMask targetLayers = ~0;
    public bool showHitboxDebug = true;

    private readonly HashSet<CombatEntity> hitEntities = new HashSet<CombatEntity>();
    private Collider hitCollider;
    private bool isActive;

    void Awake()
    {
        hitCollider = GetComponent<Collider>();
        if (hitCollider == null)
            Debug.LogError("CombatHitbox needs a Collider on the same GameObject.", this);
        else
            hitCollider.isTrigger = true;

        if (owner == null)
            owner = GetComponentInParent<CombatEntity>();

        if (owner == null && showHitboxDebug)
            Debug.LogWarning($"{name} has no CombatEntity owner.", this);

        SetHitboxActive(false);
    }

    public void SetHitboxActive(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;
        if (hitCollider != null)
        {
            hitCollider.enabled = active;
            if (!active)
                Physics.SyncTransforms();
        }

        if (active)
        {
            hitEntities.Clear();
            if (showHitboxDebug)
                Debug.Log($"{name} hitbox ACTIVE. Owner: {(owner != null ? owner.name : "NONE")}", this);
        }
        else if (showHitboxDebug)
        {
            Debug.Log($"{name} hitbox OFF.", this);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    void OnTriggerStay(Collider other)
    {
        TryHit(other);
    }

    void Update()
    {
        if (!isActive || hitCollider == null)
            return;

        Bounds bounds = hitCollider.bounds;
        Collider[] overlaps = Physics.OverlapBox(
            bounds.center,
            bounds.extents,
            Quaternion.identity,
            targetLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider overlap in overlaps)
            TryHit(overlap);
    }

    void TryHit(Collider other)
    {
        if (!isActive || owner == null)
            return;

        CombatEntity target = other.GetComponentInParent<CombatEntity>();
        if (target == null || target == owner || hitEntities.Contains(target))
            return;

        bool colliderLayerAllowed = ((1 << other.gameObject.layer) & targetLayers.value) != 0;
        bool targetLayerAllowed = ((1 << target.gameObject.layer) & targetLayers.value) != 0;
        if (!colliderLayerAllowed && !targetLayerAllowed)
            return;

        hitEntities.Add(target);
        Vector3 hitDirection = (target.transform.position - transform.position).normalized;
        if (showHitboxDebug)
            Debug.Log($"{name} HIT {target.name} for {damage} damage.", this);
        target.ReceiveHit(damage, hitDirection, name, attackIndex);
    }
}
