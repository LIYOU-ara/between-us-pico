using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PrototypeSetup
{
    [MenuItem("Prototype/Create diagnostic scene")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        new GameObject("Cooperative prototype").AddComponent<CooperativePrototype>();
        EditorSceneManager.SaveScene(scene, "Assets/Prototype/Diagnostic.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Prototype/Diagnostic.unity", true) };
        Debug.Log("PROTOTYPE_SCENE_READY");
    }
}
