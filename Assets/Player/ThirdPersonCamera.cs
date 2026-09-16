using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Distance")]
    public float distance = 5f;
    public float minDistance = 2f;
    public float maxDistance = 8f;
    public float collisionRadius = 0.2f;
    public float collisionPadding = 0.1f;
    public float collisionSmoothTime = 0.08f;
    public LayerMask collisionLayers = ~0;

    [Header("Look")]
    public float mouseSensitivity = 3f;
    public float minPitch = -30f;
    public float maxPitch = 60f;

    [Header("Cursor")]
    public bool lockCursor = true;
    public bool rightClickToAim = true;

    [Header("Impact Shake")]
    public bool useImpactShake = true;
    public float impactShakeDuration = 0.12f;
    public float impactShakeStrength = 0.08f;

    private float yaw;
    private float pitch = 15f;
    private float cameraDistance;
    private float cameraDistanceVelocity;
    private bool isAiming;
    private float shakeTimer;
    private float shakeStrength;

    public bool IsAiming => isAiming;

    void OnEnable()
    {
        CombatEntity.CombatImpact += ShakeOnImpact;
    }

    void OnDisable()
    {
        CombatEntity.CombatImpact -= ShakeOnImpact;
    }

    void Start()
    {
        if (target == null)
            target = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (target != null)
        {
            yaw = target.eulerAngles.y;
            cameraDistance = distance;
        }

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        isAiming = rightClickToAim && Input.GetMouseButton(1);

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (Input.GetKeyDown(KeyCode.Escape) && lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * 5f, minDistance, maxDistance);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position + targetOffset;
        Vector3 cameraDirection = -(rotation * Vector3.forward);
        float safeDistance = GetSafeCameraDistance(focusPoint, cameraDirection, distance);
        cameraDistance = Mathf.SmoothDamp(cameraDistance, safeDistance, ref cameraDistanceVelocity, collisionSmoothTime);
        Vector3 desiredPosition = focusPoint + cameraDirection * cameraDistance;

        Vector3 shakeOffset = Vector3.zero;
        if (useImpactShake && shakeTimer > 0f)
        {
            float fade = shakeTimer / Mathf.Max(0.01f, impactShakeDuration);
            shakeOffset = Random.insideUnitSphere * (shakeStrength * fade);
            shakeTimer -= Time.deltaTime;
        }

        transform.position = desiredPosition + shakeOffset;
        transform.LookAt(focusPoint);
    }

    void ShakeOnImpact(float intensity)
    {
        if (!useImpactShake)
            return;

        shakeTimer = Mathf.Max(shakeTimer, impactShakeDuration);
        shakeStrength = Mathf.Max(shakeStrength, impactShakeStrength * intensity);
    }

    float GetSafeCameraDistance(Vector3 focusPoint, Vector3 cameraDirection, float wantedDistance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            focusPoint,
            collisionRadius,
            cameraDirection,
            wantedDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore);

        float safeDistance = wantedDistance;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform == target || hit.collider.transform.IsChildOf(target))
                continue;

            CombatEntity hitEntity = hit.collider.GetComponentInParent<CombatEntity>();
            if (hitEntity != null && hitEntity.IsDead)
                continue;

            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.1f, hit.distance - collisionPadding));
        }

        return safeDistance;
    }
}