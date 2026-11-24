using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MeshDestructionBuild
{
    private const string ScenePath = "Assets/Demo/MeshDestruction.unity";

    private const string PrefabScenePath = "Assets/Demo/PrefabWall.unity";

    [MenuItem("Demo/Build Windows player")]
    public static void Windows()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-outputPath");
        string path = index >= 0 && index + 1 < args.Length
            ? args[index + 1] : "Build/Fest-MeshDestruction.exe";
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (pipeline == null)
            throw new BuildFailedException("The original Universal Render Pipeline asset is required.");
        var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
        if (renderers.arraySize == 0 || renderers.GetArrayElementAtIndex(0).objectReferenceValue == null)
            throw new BuildFailedException("The original PC renderer could not be resolved.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        CheckScripts(scene);
        var wall = UnityEngine.Object.FindFirstObjectByType<DestructibleWall2DRectFramework>();
        var tester = UnityEngine.Object.FindFirstObjectByType<DestructibleWallTester>();
        if (wall == null || tester == null || Camera.main == null)
            throw new BuildFailedException("Demo scene requires the original wall, camera and tester.");
        var refs = new SerializedObject(tester);
        if (refs.FindProperty("cam").objectReferenceValue != Camera.main ||
            refs.FindProperty("wall").objectReferenceValue != wall)
            throw new BuildFailedException("Check the input target and camera in the demo scene.");
        var renderer = wall.GetComponent<MeshRenderer>();
        if (wall.GetComponent<MeshFilter>().sharedMesh == null ||
            wall.GetComponent<MeshCollider>().sharedMesh == null ||
            renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ||
            renderer.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
            throw new BuildFailedException("The source mesh, collider and wall material must resolve.");

        var prefabScene = EditorSceneManager.OpenScene(PrefabScenePath);
        CheckScripts(prefabScene);
        var prefabWall = UnityEngine.Object.FindFirstObjectByType<BreakableChunkWall>();
        var prefabTester = UnityEngine.Object.FindFirstObjectByType<PrefabWallTester>();
        if (prefabWall == null || prefabTester == null || Camera.main == null)
            throw new BuildFailedException("Missing prefab wall or input controller.");
        var prefabRefs = new SerializedObject(prefabTester);
        if (prefabRefs.FindProperty("cam").objectReferenceValue != Camera.main ||
            prefabRefs.FindProperty("wall").objectReferenceValue != prefabWall)
            throw new BuildFailedException("Check the prefab wall and camera assigned to the input controller.");
        var pieces = prefabWall.GetComponentsInChildren<BreakableChunk>(true);
        if (pieces.Length != 18 || PrefabUtility.GetPrefabAssetType(prefabWall.gameObject) != PrefabAssetType.Variant)
            throw new BuildFailedException("Expected the original eighteen-piece prefab variant.");
        foreach (var piece in pieces)
        {
            var filter = piece.GetComponent<MeshFilter>();
            var collider = piece.GetComponent<MeshCollider>();
            var material = piece.GetComponent<MeshRenderer>().sharedMaterial;
            if (piece.GetComponent<Rigidbody>() == null || filter == null || collider == null ||
                filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.sharedMesh != collider.sharedMesh ||
                material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                throw new BuildFailedException("Missing original geometry, physics or material on " + piece.name);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var settings = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath, PrefabScenePath },
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(settings);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException(report.summary.result.ToString());
        Debug.Log("Mesh Destruction built: " + report.summary.totalSize + " bytes");
    }
    private static void CheckScripts(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var component in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(component.gameObject) != 0)
                    throw new BuildFailedException("Missing script on " + component.name);
            }
        }
    }
}
