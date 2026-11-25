using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class PicoConversationSetup {
 const string ScenePath="Assets/Prototype/PicoConversation.unity";
 const string CredentialMode="runtime-session-only";
 [Serializable] sealed class CredentialBuild {public string sha256,credential_mode,built_utc;public long bytes;}
 [MenuItem("Prototype/Create PICO language role-play")]
 public static void Create() {
  PicoSpeechSetup.Create();
  if(EditorBuildSettings.TryGetConfigObject("Unity.XR.PXR.Settings",out Unity.XR.PXR.PXR_Settings stereoSettings)){stereoSettings.stereoRenderingModeAndroid=Unity.XR.PXR.PXR_Settings.StereoRenderingModeAndroid.MultiPass;EditorUtility.SetDirty(stereoSettings);}
  PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.prototype.pico.conversation");PlayerSettings.productName="PicoConversation";
  var old=UnityEngine.Object.FindFirstObjectByType<PicoSpeechCapture>();var origin=old.gameObject;
  // Unity's transparent eye buffer contains premultiplied RGB after source-over drawing.
  var picoManager=origin.GetComponent<Unity.XR.PXR.PXR_Manager>();
  picoManager.usePremultipliedAlpha=true;EditorUtility.SetDirty(picoManager);
  var status=old.statusText;var subtitle=old.subtitleText;
  UnityEngine.Object.DestroyImmediate(old);UnityEngine.Object.DestroyImmediate(subtitle.gameObject);
  var camera=Camera.main;camera.allowHDR=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
  Unity.XR.PXR.PXR_ProjectSetting.GetProjectConfig().videoSeeThrough=true;
  EditorUtility.SetDirty(Unity.XR.PXR.PXR_ProjectSetting.GetProjectConfig());
  var native=origin.AddComponent<PicoSdkCameraCapture>();native.headCamera=camera.transform;native.flipRowsForTexture=false;
  foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(light.gameObject);
  status.transform.localPosition=new Vector3(-1.08f,-.95f,1.8f);status.fontSize=32;status.characterSize=.0065f;
  var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Regular.otf");
  var quoteFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
  if(font==null||quoteFont==null)throw new InvalidOperationException("Conversation Regular/Medium fonts missing");
  const string captionPath="Assets/Prototype/ConversationCaption.mat";var captionMat=AssetDatabase.LoadAssetAtPath<Material>(captionPath);
  var captionShader=Shader.Find("Prototype/StereoCaption");if(captionShader==null)throw new InvalidOperationException("Caption shader missing");
  if(captionMat==null){captionMat=new Material(captionShader);AssetDatabase.CreateAsset(captionMat,captionPath);}else captionMat.shader=captionShader;
  captionMat.mainTexture=font.material.mainTexture;EditorUtility.SetDirty(captionMat);
  TextMesh Make(string name,Vector3 pos,Color color) {
   var go=new GameObject(name);go.transform.SetParent(camera.transform,false);go.transform.localPosition=pos;
   var text=go.AddComponent<TextMesh>();text.font=font;text.fontSize=96;text.fontStyle=FontStyle.Normal;text.characterSize=.010f;text.anchor=TextAnchor.MiddleCenter;text.color=color;
   go.GetComponent<Renderer>().sharedMaterial=captionMat;return text;
  }
  var localFace=origin.AddComponent<PicoLocalFace>();var service=origin.AddComponent<PicoConversationOnDevice>();service.localFace=localFace;
  var capture=origin.AddComponent<PicoConversationCapture>();capture.statusText=status;capture.subtitleText=null;
  var overlay=origin.AddComponent<PicoConversationOverlay>();overlay.capture=capture;overlay.captionMaterial=captionMat;
  overlay.quoteText=Make("Other speaker transcript",new Vector3(-.8f,.46f,1.7f),Color.white);
  overlay.quoteText.font=quoteFont;
  overlay.quoteText.anchor=TextAnchor.MiddleCenter;overlay.quoteText.characterSize=.010f;
  overlay.quoteText.transform.SetParent(origin.transform,true);
  overlay.speechBubble=ConversationSpeechBubble.Attach(overlay.quoteText);
  overlay.faceStatusText=Make("Visible facial actions HUD",new Vector3(-1.08f,-.42f,1.8f),new Color(.75f,.9f,.95f));
  overlay.analysisText=Make("Grounded utterance analysis",new Vector3(0,-.11f,1.8f),Color.white);
  overlay.analysisPanel=ConversationSpeechBubble.AttachAnalysis(overlay.analysisText);
  overlay.analysisPanel.transform.localPosition=new Vector3(0,-.43f,1.8f);
  overlay.speechBubble.SetTranscript("");overlay.analysisPanel.SetTranscript("");
  // Scene files must reference persistent assets, never the preview/runtime font clones.
  var panelTextMat=Resources.Load<Material>("Conversation/SpeakerBubbleText");
  if(panelTextMat==null)throw new InvalidOperationException("Install the conversation frame materials before creating the scene");
  overlay.quoteText.GetComponent<Renderer>().sharedMaterial=panelTextMat;
  overlay.analysisText.GetComponent<Renderer>().sharedMaterial=panelTextMat;
  overlay.replyText=Make("Clarify and firm replies",new Vector3(.08f,-.54f,1.8f),new Color(1f,.68f,.32f));
  overlay.faceStatusText.characterSize=.008f;overlay.analysisText.characterSize=.011f;overlay.replyText.characterSize=.011f;
  var photo=GameObject.CreatePrimitive(PrimitiveType.Quad);photo.name="B current face snapshot";UnityEngine.Object.DestroyImmediate(photo.GetComponent<Collider>());
  photo.transform.SetParent(camera.transform,false);photo.transform.localPosition=new Vector3(-.78f,-.11f,1.8f);photo.transform.localScale=new Vector3(.20f,.20f,1);
  const string photoMatPath="Assets/Prototype/ConversationFacePhoto.mat";var photoMat=AssetDatabase.LoadAssetAtPath<Material>(photoMatPath);
  if(photoMat==null){photoMat=new Material(Shader.Find("Unlit/Texture"));AssetDatabase.CreateAsset(photoMat,photoMatPath);}photo.GetComponent<Renderer>().sharedMaterial=photoMat;
  overlay.photoRenderer=photo.GetComponent<Renderer>();photo.SetActive(false);
  ConversationSpeechBubbleSetup.InstallCameraGlass(overlay);
  ConversationSpeechBubbleSetup.InstallTemplateHud(overlay);
  EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);AssetDatabase.SaveAssets();Debug.Log("PICO_CONVERSATION_SCENE_READY");
 }
 [MenuItem("Prototype/Build PICO language role-play APK")]
 public static void Build() {
   string output=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Builds","PicoConversation.apk");
   string readyMarker=Path.Combine(Path.GetDirectoryName(output),"PicoConversation.build-ready.txt");
   string credentialMarker=Path.Combine(Path.GetDirectoryName(output),"PicoConversation.credential-build.json");
   // Revoke previous build evidence before any operation that can fail.
   if(File.Exists(credentialMarker))File.Delete(credentialMarker);
   if(File.Exists(readyMarker))File.Delete(readyMarker);
  if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)throw new InvalidOperationException("Android build target required");
   ConversationCredentialBuildGuard.EnsureSafeBuild(BuildTarget.Android);
   Create();Directory.CreateDirectory(Path.GetDirectoryName(output));
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.Android,options=BuildOptions.Development});
  if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("PICO Conversation build failed: "+report.summary.result);
   var apk=new FileInfo(output);if(!apk.Exists||apk.Length==0)throw new InvalidOperationException("Build reported success but the APK is missing or empty.");
   var credentialBuild=new CredentialBuild{bytes=apk.Length,credential_mode=CredentialMode,built_utc=DateTime.UtcNow.ToString("O")};
   using(var hash=SHA256.Create())using(var stream=File.OpenRead(output))credentialBuild.sha256=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
   File.WriteAllText(credentialMarker,JsonUtility.ToJson(credentialBuild,true));
   File.WriteAllText(readyMarker,"PICO_CONVERSATION_APK_READY\nCREDENTIAL_MODE="+CredentialMode+"\n"+credentialBuild.built_utc);
   Debug.Log("PICO_CONVERSATION_APK_READY: "+output);
 }
}
