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

public static class PicoSpeechSetup {
    const string ScenePath="Assets/Prototype/PicoSpeech.unity";
    [MenuItem("Prototype/Create PICO microphone subtitles scene")]
    public static void Create() {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
        if(!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,out XRGeneralSettingsPerBuildTarget xr))
            throw new InvalidOperationException("Create the Hand/API scene once to initialize XR settings");
        if(!xr.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) xr.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var manager=xr.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        if(!XRPackageMetadataStore.AssignLoader(manager,"Unity.XR.PXR.PXR_Loader",BuildTargetGroup.Android))
            throw new InvalidOperationException("Could not enable PICO Android loader");
        EditorUtility.SetDirty(manager);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.speech");
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
        PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
        var origin=new GameObject("Ultra native camera diagnostic"); origin.AddComponent<PXR_Manager>();
        var camera=Camera.main; camera.transform.SetParent(origin.transform,false);
        camera.transform.localPosition=new Vector3(0,1.6f,0); camera.backgroundColor=Color.black;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.025f,.035f);
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/SpeechChinese.ttf");
        if(font==null) throw new InvalidOperationException("Chinese font missing");
        TextMesh MakeText(string name,Vector3 position,int size,float characterSize) {
            var go=new GameObject(name); go.transform.SetParent(camera.transform,false); go.transform.localPosition=position;
            var text=go.AddComponent<TextMesh>(); text.font=font; text.fontSize=size; text.characterSize=characterSize;
            text.anchor=TextAnchor.UpperLeft; text.color=Color.white;
            go.GetComponent<Renderer>().sharedMaterial=font.material; return text;
        }
        var status=MakeText("Microphone and ASR status",new Vector3(-.65f,.38f,1.5f),40,.012f);
        var subtitle=MakeText("Head locked Chinese captions",new Vector3(-.65f,.04f,1.5f),48,.016f);
        var speech=origin.AddComponent<PicoSpeechCapture>(); speech.statusText=status; speech.subtitleText=subtitle;
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        Debug.Log("PICO_SPEECH_SCENE_READY: Native microphone subtitles, device verification pending");
    }
    [MenuItem("Prototype/Build PICO microphone subtitles APK")]
    public static void Build() {
        if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
            throw new InvalidOperationException("Switch the project to Android first, or use Build-Ultra-Camera.cmd");
        Create();
        string directory=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds"); Directory.CreateDirectory(directory);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},
            locationPathName=Path.Combine(directory,"PicoSpeech.apk"),target=BuildTarget.Android,options=BuildOptions.Development });
        if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Ultra camera build failed: "+report.summary.result);
        Debug.Log("PICO_SPEECH_APK_READY: "+report.summary.outputPath);
    }
}
