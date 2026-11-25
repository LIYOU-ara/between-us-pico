using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PicoVisionSetup {
    const string ScenePath="Assets/Prototype/PicoVision.unity";
    [MenuItem("Prototype/Create PICO Vision scene")]
    public static void Create() {
        PicoSdkCameraSetup.Create();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.vision");
        PlayerSettings.productName="PicoVision";
        var capture=UnityEngine.Object.FindFirstObjectByType<PicoSdkCameraCapture>();
        if(capture.display!=null) UnityEngine.Object.DestroyImmediate(capture.display.gameObject);
        capture.display=null;
        var camera=Camera.main;
        camera.allowHDR=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var panel=new GameObject("Face observation text");panel.transform.SetParent(camera.transform,false);
        panel.transform.localPosition=new Vector3(-.72f,.67f,1.5f);
        var text=panel.AddComponent<TextMesh>();text.characterSize=.012f;text.fontSize=36;text.anchor=TextAnchor.UpperLeft;text.color=Color.white;
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/SpeechChinese.ttf");
        if(font==null)throw new InvalidOperationException("Chinese font missing");
        text.font=font; text.GetComponent<MeshRenderer>().sharedMaterial=font.material;
        var overlay=panel.AddComponent<PicoVisionOverlay>();overlay.display=text;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("PICO_VISION_SCENE_READY");
    }
    [MenuItem("Prototype/Build PICO Vision APK")]
    public static void Build() {
        if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Android build target required");
        Create();string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds","PicoVision.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.Android,options=BuildOptions.Development});
        if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("PICO Vision build failed: "+report.summary.result);
        Debug.Log("PICO_VISION_APK_READY: "+output);
    }
}
