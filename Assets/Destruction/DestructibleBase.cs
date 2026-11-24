using UnityEngine;

/*
 * Clase base para objetos destructibles que necesitan reconstruccion.
 * Centraliza dirty flags, control de frecuencia de rebuild y los puntos de
 * entrada comunes para las reconstrucciones.
 */
public abstract class DestructibleBase : MonoBehaviour
{
    [Header("Rebuild")]
    [SerializeField] protected bool rebuildImmediatelyOnDamage = true;
    [SerializeField] protected float minRebuildInterval = 0.02f;

    protected bool VisualDirty { get; set; }
    protected bool CollisionDirty { get; set; }
    protected bool AnalysisDirty { get; set; }

    private float _lastRebuildTime = -999f;

    /*
     * Procesa reconstrucciones pendientes cuando el objeto esta marcado como dirty.
     * Evita reconstruir cada frame si existe un intervalo minimo configurado.
     */
    protected virtual void Update()
    {
        if (!HasDirtyFlags())
            return;

        if (rebuildImmediatelyOnDamage && Time.time - _lastRebuildTime >= minRebuildInterval)
            RebuildNow();
    }

    protected bool HasDirtyFlags()
    {
        return VisualDirty || CollisionDirty || AnalysisDirty;
    }

    protected void MarkVisualDirty() => VisualDirty = true;
    protected void MarkCollisionDirty() => CollisionDirty = true;
    protected void MarkAnalysisDirty() => AnalysisDirty = true;

    /*
     * Marca todos los subsistemas que deben reconstruirse despues de un cambio.
     * Permite incluir o no el analisis para diferenciar cambios visuales simples
     * de dano que tambien afecta soporte estructural.
     */
    protected void MarkAllDirty(bool includeAnalysis = true)
    {
        VisualDirty = true;
        CollisionDirty = true;
        AnalysisDirty = includeAnalysis;
    }

    protected void ClearDirtyFlags()
    {
        VisualDirty = false;
        CollisionDirty = false;
        AnalysisDirty = false;
    }

    protected bool CanRebuildNow()
    {
        return Time.time - _lastRebuildTime >= minRebuildInterval;
    }

    protected void RegisterRebuildTime()
    {
        _lastRebuildTime = Time.time;
    }

    /*
     * Punto obligatorio donde cada destructible aplica sus cambios pendientes.
     * Las clases hijas deciden como reconstruir malla, colision, debris o datos
     * propios del tipo de objeto.
     */
    public abstract void RebuildNow();
}
