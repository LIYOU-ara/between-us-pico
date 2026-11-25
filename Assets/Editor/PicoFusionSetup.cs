using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class PicoFusionSetup {
 const string ScenePath="Assets/Prototype/PicoFusion.unity";
 [MenuItem("Prototype/Create PICO language role-play")]
 public static void Create() {
  PicoSpeechSetup.Create();
  PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.fusion");PlayerSettings.productName="PicoFusion";
  var old=UnityEngine.Object.FindFirstObjectByType<PicoSpeechCapture>();var origin=old.gameObject;
  var status=old.statusText;var subtitle=old.subtitleText;
  UnityEngine.Object.DestroyImmediate(old);UnityEngine.Object.DestroyImmediate(subtitle.gameObject);
  var camera=Camera.main;camera.allowHDR=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
  Unity.XR.PXR.PXR_ProjectSetting.GetProjectConfig().videoSeeThrough=true;
  EditorUtility.SetDirty(Unity.XR.PXR.PXR_ProjectSetting.GetProjectConfig());
  var native=origin.AddComponent<PicoSdkCameraCapture>();native.headCamera=camera.transform;native.flipRowsForTexture=false;
  foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(light.gameObject);
  status.transform.localPosition=new Vector3(-1.08f,-.95f,1.8f);status.fontSize=32;status.characterSize=.0065f;
  var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/SpeechChinese.ttf");
  TextMesh Make(string name,Vector3 pos,Color color) {
   var go=new GameObject(name);go.transform.SetParent(camera.transform,false);go.transform.localPosition=pos;
   var text=go.AddComponent<TextMesh>();text.font=font;text.fontSize=40;text.characterSize=.012f;text.anchor=TextAnchor.UpperLeft;text.color=color;
   go.GetComponent<Renderer>().sharedMaterial=font.material;return text;
  }
  var capture=origin.AddComponent<PicoFusionCapture>();capture.statusText=status;capture.subtitleText=null;
  var overlay=origin.AddComponent<PicoFusionOverlay>();overlay.capture=capture;
  overlay.quoteText=Make("Other speaker transcript",new Vector3(-.8f,.46f,1.7f),Color.white);
  overlay.quoteText.anchor=TextAnchor.LowerCenter;overlay.quoteText.characterSize=.009f;
  overlay.quoteText.transform.SetParent(origin.transform,true);
  overlay.faceStatusText=Make("Visible facial actions HUD",new Vector3(-1.08f,-.42f,1.8f),new Color(.75f,.9f,.95f));
  overlay.analysisText=Make("Grounded utterance analysis",new Vector3(-1.08f,-.54f,1.8f),new Color(.8f,.8f,.8f));
  overlay.replyText=Make("Clarify and firm replies",new Vector3(.08f,-.54f,1.8f),new Color(1f,.68f,.32f));
  overlay.faceStatusText.characterSize=.008f;overlay.analysisText.characterSize=.011f;overlay.replyText.characterSize=.011f;
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();Debug.Log("PICO_FUSION_SCENE_READY");
 }
 [MenuItem("Prototype/Build PICO language role-play APK")]
 public static void Build() {
  if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Android build target required");
  Create();string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds","PicoFusion.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.Android,options=BuildOptions.Development});
  if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("PICO Dialogue build failed: "+report.summary.result);
  Debug.Log("PICO_FUSION_APK_READY: "+output);
 }
}
