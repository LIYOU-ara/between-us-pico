using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class PicoDialogueSetup {
 const string ScenePath="Assets/Prototype/PicoDialogue.unity";
 [MenuItem("Prototype/Create PICO language role-play")]
 public static void Create() {
  PicoSpeechSetup.Create();
  PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.dialogue");PlayerSettings.productName="PicoDialogue";
  var old=UnityEngine.Object.FindFirstObjectByType<PicoSpeechCapture>();var origin=old.gameObject;
  var status=old.statusText;var subtitle=old.subtitleText;
  UnityEngine.Object.DestroyImmediate(old);UnityEngine.Object.DestroyImmediate(subtitle.gameObject);
  var camera=Camera.main;camera.allowHDR=false;
  status.transform.localPosition=new Vector3(-.8f,.74f,1.7f);status.fontSize=36;status.characterSize=.011f;
  var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/SpeechChinese.ttf");
  TextMesh Make(string name,Vector3 pos,Color color) {
   var go=new GameObject(name);go.transform.SetParent(camera.transform,false);go.transform.localPosition=pos;
   var text=go.AddComponent<TextMesh>();text.font=font;text.fontSize=40;text.characterSize=.012f;text.anchor=TextAnchor.UpperLeft;text.color=color;
   go.GetComponent<Renderer>().sharedMaterial=font.material;return text;
  }
  var capture=origin.AddComponent<PicoDialogueCapture>();capture.statusText=status;capture.subtitleText=null;
  var overlay=origin.AddComponent<PicoDialogueOverlay>();overlay.capture=capture;
  overlay.quoteText=Make("Other speaker transcript",new Vector3(-.8f,.46f,1.7f),Color.white);
  overlay.analysisText=Make("Grounded utterance analysis",new Vector3(-.8f,.16f,1.7f),new Color(.8f,.8f,.8f));
  overlay.replyText=Make("Clarify and firm replies",new Vector3(-.8f,-.26f,1.7f),new Color(1f,.68f,.32f));
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();Debug.Log("PICO_DIALOGUE_SCENE_READY");
 }
 [MenuItem("Prototype/Build PICO language role-play APK")]
 public static void Build() {
  if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Android build target required");
  Create();string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds","PicoDialogue.apk");Directory.CreateDirectory(Path.GetDirectoryName(output));
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.Android,options=BuildOptions.Development});
  if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("PICO Dialogue build failed: "+report.summary.result);
  Debug.Log("PICO_DIALOGUE_APK_READY: "+output);
 }
}
