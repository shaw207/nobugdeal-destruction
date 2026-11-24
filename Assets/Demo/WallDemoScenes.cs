using UnityEngine.SceneManagement;

public static class WallDemoScenes
{
    public const string Mesh = "MeshDestruction";
    public const string Prefab = "PrefabWall";

    public static void OpenMesh()
    {
        SceneManager.LoadScene(Mesh);
    }

    public static void OpenPrefab()
    {
        SceneManager.LoadScene(Prefab);
    }

    public static void RestartPrefab()
    {
        OpenPrefab();
    }
}
