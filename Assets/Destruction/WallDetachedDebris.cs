using UnityEngine;

/*
 * Controla la vida de un fragmento desprendido de una pared destructible.
 * Existe para destruir debris temporal despues de unos segundos y evitar que la
 * escena acumule objetos fisicos innecesarios.
 */
public class WallDetachedDebris : MonoBehaviour
{
    private Mesh _ownedMesh;
    private float _remainingLifetime;
    private bool _initialized;

    public void Initialize(float lifetime, Mesh ownedMesh)
    {
        _ownedMesh = ownedMesh;
        _remainingLifetime = Mathf.Max(0.05f, lifetime);
        _initialized = true;
    }

    private void OnDestroy()
    {
        if (_ownedMesh != null)
            Destroy(_ownedMesh);
    }

    private void Update()
    {
        if (!_initialized)
            return;

        _remainingLifetime -= Time.deltaTime;
        if (_remainingLifetime <= 0f)
            Destroy(gameObject);
    }
}
