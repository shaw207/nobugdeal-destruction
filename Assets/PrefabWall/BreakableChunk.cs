using System.Collections;
using UnityEngine;

/*
 * Representa una pieza individual que puede desprenderse.
 * Empieza como parte estatica de un objeto y, al romperse, activa fisica,
 * recibe impulso y se destruye despues de una vida corta.
 */
public class BreakableChunk : MonoBehaviour
{
    public Vector3 LocalCenter => localCenter;
    public Vector3 WorldCenter => GetWorldCenter();
    public bool IsBroken => isBroken;

    [SerializeField] private Vector3 localCenter;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider chunkCollider;
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Lifetime")]
    [SerializeField] private float minLifetime = 1.5f;
    [SerializeField] private float maxLifetime = 3.0f;
    [SerializeField] private bool disableColliderAfterDelay = true;
    [SerializeField] private float colliderDisableDelay = 0.6f;

    private bool isBroken;
    private Coroutine routine;

    private void Reset()
    {
        CacheComponents();
    }

    private void Awake()
    {
        CacheComponents();
        SetupInitialState();
    }

    /*
     * Devuelve el chunk a su estado inicial unido al objeto.
     * Mantiene la pieza unida y permite apuntar a su forma original hasta que
     * el sistema principal decida romperla.
     */
    public void SetupInitialState()
    {
        CacheComponents();

        isBroken = false;

        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (chunkCollider != null)
            chunkCollider.enabled = true;
    }

    /*
     * Activa la pieza como fragmento fisico.
     * La separa de su padre, habilita collider y rigidbody, aplica fuerzas y
     * arranca la rutina que limpia el objeto despues de unos segundos.
     */
    public void Break(Vector3 hitPoint, Vector3 impulse, Vector3 torque)
    {
        if (isBroken)
            return;

        isBroken = true;
        transform.SetParent(null, true);

        if (chunkCollider is MeshCollider meshCollider)
            meshCollider.convex = true;
        if (chunkCollider != null)
            chunkCollider.enabled = true;

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);
            rb.AddTorque(torque, ForceMode.Impulse);
        }

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(LifeRoutine());
    }

    private void CacheComponents()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (chunkCollider == null)
            chunkCollider = GetComponent<Collider>();

        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
    }

    private Vector3 GetWorldCenter()
    {
        if (meshRenderer != null)
            return meshRenderer.bounds.center;

        if (chunkCollider != null)
            return chunkCollider.bounds.center;

        if (transform.parent != null)
            return transform.parent.TransformPoint(localCenter);

        return transform.position;
    }

    /*
     * Controla la vida posterior a la rotura.
     * Puede desactivar el collider primero para reducir interacciones fisicas y
     * luego destruye el fragmento para mantener limpia la escena.
     */
    private IEnumerator LifeRoutine()
    {
        if (disableColliderAfterDelay && chunkCollider != null)
        {
            yield return new WaitForSeconds(colliderDisableDelay);
            if (chunkCollider != null)
                chunkCollider.enabled = false;
        }

        float delay = Random.Range(minLifetime, maxLifetime);
        if (disableColliderAfterDelay && chunkCollider != null)
            delay -= colliderDisableDelay;
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheComponents();

        if (transform.parent != null && meshRenderer != null)
            localCenter = transform.parent.InverseTransformPoint(meshRenderer.bounds.center);
    }
#endif
}
