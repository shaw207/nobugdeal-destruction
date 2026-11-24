using System.Collections.Generic;
using UnityEngine;

/*
 * Gestiona una pared compuesta por varios chunks fisicos.
 * Cuando recibe dano de armas, selecciona los chunks cercanos al impacto, los
 * rompe con impulso y limita el numero de piezas que se sueltan por impacto.
 */
public class BreakableChunkWall : MonoBehaviour
{
    private const float LocalHitRadius = 1f;

    [Header("Main")]
    [SerializeField] private Collider mainCollider;
    [SerializeField] private BreakableChunk[] chunks;

    [Header("Break")]
    [SerializeField] private float baseBreakRadius = 0.6f;
    [SerializeField] private float breakForce = 6f;
    [SerializeField] private float upwardForce = 1.5f;
    [SerializeField] private float randomTorque = 12f;
    [SerializeField] private int maxBreakPerHit = 6;
    [SerializeField] private float explosionRadiusMultiplier = 1.35f;
    [SerializeField] private float explosionForceMultiplier = 1.25f;

    private static readonly List<ChunkCandidate> candidateBuffer = new List<ChunkCandidate>(64);

    private struct ChunkCandidate
    {
        public BreakableChunk chunk;
        public float sqrDist;
        public bool directHit;
        public float falloff;
    }

    private void Reset()
    {
        mainCollider = GetComponentInChildren<Collider>();
        chunks = GetComponentsInChildren<BreakableChunk>(true);
    }

    private void Awake()
    {
        // Cada pieza conserva su propio collider; las aberturas deben dejar pasar el cursor.
        if (mainCollider != null)
            mainCollider.enabled = false;
    }

    // Un impacto local prioriza la pieza bajo el cursor y solo alcanza a sus vecinas cercanas.
    // La seleccion y la caida del impulso usan radios independientes.
    public bool ApplyImpact(Vector3 point, Vector3 direction, Vector3 normal, bool area, BreakableChunk hitPiece = null)
    {
        if (chunks == null || chunks.Length == 0)
            return false;

        Vector3 localHit = transform.InverseTransformPoint(point);
        float forceRadius = area ? Mathf.Max(baseBreakRadius, baseBreakRadius * explosionRadiusMultiplier) : baseBreakRadius;
        float localForceRadius = WorldRadiusToLocal(forceRadius);
        float selectionRadius = area ? localForceRadius : LocalHitRadius;
        float extraForce = area ? Mathf.Max(breakForce, breakForce * explosionForceMultiplier) : breakForce;
        Vector3 worldForceDir = direction.sqrMagnitude > 0.0001f ? direction.normalized : -normal.normalized;

        candidateBuffer.Clear();

        for (int i = 0; i < chunks.Length; i++)
        {
            BreakableChunk chunk = chunks[i];
            if (chunk == null || chunk.IsBroken)
                continue;

            bool directHit = !area && chunk == hitPiece;
            float localSqrDist = (chunk.LocalCenter - localHit).sqrMagnitude;
            float selectionSqrDist = area ? localSqrDist : (transform.TransformPoint(chunk.LocalCenter) - point).sqrMagnitude;
            if (!directHit && selectionSqrDist > selectionRadius * selectionRadius)
                continue;

            float dist = Mathf.Sqrt(localSqrDist);
            float falloff = 1f - Mathf.Clamp01(dist / Mathf.Max(0.0001f, localForceRadius));

            candidateBuffer.Add(new ChunkCandidate
            {
                chunk = chunk,
                sqrDist = selectionSqrDist,
                directHit = directHit,
                falloff = falloff
            });
        }

        if (candidateBuffer.Count == 0)
            return false;

        candidateBuffer.Sort((a, b) =>
        {
            int priority = b.directHit.CompareTo(a.directHit);
            return priority != 0 ? priority : a.sqrDist.CompareTo(b.sqrDist);
        });

        int breakCount = Mathf.Min(maxBreakPerHit, candidateBuffer.Count);

        for (int i = 0; i < breakCount; i++)
        {
            BreakableChunk chunk = candidateBuffer[i].chunk;
            float falloff = candidateBuffer[i].falloff;

            Vector3 impulse =
                worldForceDir * (breakForce * falloff + extraForce) +
                Vector3.up * (upwardForce * falloff);

            Vector3 torque = Random.insideUnitSphere * (randomTorque * Mathf.Max(0.25f, falloff));
            chunk.Break(point, impulse, torque);
        }

        return true;
    }

    private float WorldRadiusToLocal(float worldRadius)
    {
        Vector3 lossy = transform.lossyScale;
        float sx = Mathf.Abs(lossy.x);
        float sy = Mathf.Abs(lossy.y);
        float sz = Mathf.Abs(lossy.z);
        float avg = Mathf.Max(0.0001f, (sx + sy + sz) / 3f);
        return worldRadius / avg;
    }
}
