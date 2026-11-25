using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using Unity.XR.PXR;

public static class PicoSdkCameraSetup {
    const string ScenePath="Assets/Prototype/PicoSdkCamera.unity";
    [MenuItem("Prototype/Create Ultra SDK camera scene")]
    public static void Create() {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
        if(!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,out XRGeneralSettingsPerBuildTarget xr))
            throw new InvalidOperationException("Create the Hand/API scene once to initialize XR settings");
        if(!xr.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) xr.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var manager=xr.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        if(!XRPackageMetadataStore.AssignLoader(manager,"Unity.XR.PXR.PXR_Loader",BuildTargetGroup.Android))
            throw new InvalidOperationException("Could not enable PICO Android loader");
        EditorUtility.SetDirty(manager);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.sdkcamera");
        PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.Activity;
        if(!EditorBuildSettings.TryGetConfigObject("Unity.XR.PXR.Settings",out PXR_Settings picoSettings)) {
            picoSettings=ScriptableObject.CreateInstance<PXR_Settings>();
            picoSettings.stereoRenderingModeAndroid=PXR_Settings.StereoRenderingModeAndroid.Multiview;
            AssetDatabase.CreateAsset(picoSettings,"Assets/XR/PicoAndroidSettings.asset");
            EditorBuildSettings.AddConfigObject("Unity.XR.PXR.Settings",picoSettings,true);
        }
        EditorUtility.SetDirty(picoSettings);
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3});
        PlayerSettings.runInBackground=true;
        var origin=new GameObject("Ultra native camera diagnostic"); origin.AddComponent<PXR_Manager>();
        var camera=Camera.main; camera.transform.SetParent(origin.transform,false);
        camera.transform.localPosition=new Vector3(0,1.6f,0); camera.backgroundColor=Color.black;
        var panel=new GameObject("Real camera status"); panel.transform.SetParent(camera.transform,false);
        panel.transform.localPosition=new Vector3(-.7f,.5f,1.5f);
        var text=panel.AddComponent<TextMesh>(); text.characterSize=.012f; text.fontSize=36;
        text.anchor=TextAnchor.UpperLeft; text.color=Color.white;
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name="Live SDK camera pixels";
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(camera.transform,false); quad.transform.localPosition=new Vector3(0,-.1f,2);
        quad.transform.localScale=new Vector3(1.6f,1.2f,1);
        string materialPath="Assets/Prototype/NativeCameraPreview.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null) { material=new Material(Shader.Find("Unlit/Texture")); AssetDatabase.CreateAsset(material,materialPath); }
        quad.GetComponent<Renderer>().sharedMaterial=material;
        var frame=new GameObject("Native marker camera frame"); frame.transform.SetParent(origin.transform,false);
        var marker=new GameObject("Native AprilTag 22 pose"); marker.transform.SetParent(frame.transform,false);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name="Native Tag 22 cyan cube";
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(marker.transform,false); cube.transform.localPosition=new Vector3(0,0,-.03f); cube.transform.localScale=Vector3.one*.06f;
        string cubePath="Assets/Prototype/NativeMarkerCube.mat";
        var cyan=AssetDatabase.LoadAssetAtPath<Material>(cubePath);
        if(cyan==null) { cyan=new Material(Shader.Find("Unlit/Color")); cyan.color=Color.cyan; AssetDatabase.CreateAsset(cyan,cubePath); }
        cube.GetComponent<Renderer>().sharedMaterial=cyan; marker.SetActive(false);
        var capture=origin.AddComponent<PicoSdkCameraCapture>(); capture.display=text; capture.previewRenderer=quad.GetComponent<Renderer>();
        capture.flipRowsForTexture=false; capture.headCamera=camera.transform; capture.markerFrame=frame.transform; capture.marker=marker.transform;
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        Debug.Log("ULTRA_CAMERA_SCENE_READY: Android SDK capture diagnostic, not yet device verified");
    }
    [MenuItem("Prototype/Build Ultra SDK camera APK")]
    public static void Build() {
        if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
            throw new InvalidOperationException("Switch the project to Android first, or use Build-Ultra-Camera.cmd");
        Create();
        string directory=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds"); Directory.CreateDirectory(directory);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},
            locationPathName=Path.Combine(directory,"PicoSdkCamera.apk"),target=BuildTarget.Android,options=BuildOptions.Development });
        if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Ultra camera build failed: "+report.summary.result);
        Debug.Log("ULTRA_CAMERA_APK_READY: "+report.summary.outputPath);
    }
}
