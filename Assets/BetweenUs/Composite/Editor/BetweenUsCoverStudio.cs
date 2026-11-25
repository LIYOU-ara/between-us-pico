using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BetweenUsCoverStudio
{
 const string AssetRoot="Assets/BetweenUs/Composite/Lounge/Cover";
 const string RootName="COVER - first person product cameras";
 static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge/Cover");
 [Serializable]class Pose{public Transform t;public Vector3 p,s;public Quaternion r;}
 [Serializable]class Report{public string scene,originalCamera;public int changedExistingTransforms;public bool restoredOriginalCamera,restoredVisibility,shaderErrors;public CameraReport[] cameras;}
 [Serializable]class CameraReport{public string name;public Vector3 eye,euler;public float baseFov,fittedScale;public Vector2 manualShift;public Rect targetFrame;public string plate;}
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
 static GameObject Node(string name,Transform parent,Scene scene){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);if(parent!=null)go.transform.SetParent(parent,false);go.layer=29;return go;}
 static BetweenUsCoverCamera CameraFor(string name,Transform parent,BetweenUsCompositeRig rig,Vector3 eye,Quaternion direction,Rect frame,Renderer[] panels,Renderer[] hide)
 {
  var child=parent.Find(name);GameObject go=child!=null?child.gameObject:Node(name,parent,rig.gameObject.scene);var camera=go.GetComponent<Camera>();if(camera==null)camera=go.AddComponent<Camera>();camera.CopyFrom(rig.captureCamera);camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.nearClipPlane=.06f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.aspect=16f/9;camera.transform.position=eye;camera.transform.rotation=direction;camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();
  var view=go.GetComponent<BetweenUsCoverCamera>();if(view==null)view=go.AddComponent<BetweenUsCoverCamera>();view.glassPanels=panels;view.hideForView=hide;view.targetFrame=frame;view.baseFieldOfView=76;view.Reframe();return view;
 }
 [MenuItem("Prototype/Between Us/Lounge/Render headset front cover")]
 public static void RenderCover()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Render the cover outside Play Mode.");AssetDatabase.Refresh();ShaderUtil.allowAsyncCompilation=false;
  var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded)??BetweenUsCompositeStudio.EnsureScene();var scene=rig.gameObject.scene;Folder(AssetRoot);Directory.CreateDirectory(Output);
  var original=rig.captureCamera;var poses=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>!t.name.StartsWith("01 - Frontal headset")&&!t.name.StartsWith("02 - Cover layout")).Select(t=>new Pose{t=t,p=t.localPosition,s=t.localScale,r=t.localRotation}).ToArray();
  var originalRenders=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).ToArray();var states=originalRenders.Select(r=>r.enabled).ToArray();
  var panels=rig.GetComponentsInChildren<Renderer>(true).Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.shader.name=="Between Us/Composite/Frosted Vortex Glass")).ToArray();
  var wearer=rig.transform.Find("Two seated participants - ghost study/Participant A - leaning seated");var body=wearer.GetComponentsInChildren<Renderer>(true).First(r=>r.name=="Seated body");
  var room=scene.GetRootGameObjects().First(g=>g.name=="LOUNGE - translucent environment");var hide=wearer.GetComponentsInChildren<Renderer>(true).Concat(room.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Sofa A -")||t.name=="Muted glass cushion A"||t.name=="Soft halo under wearer").SelectMany(t=>t.GetComponentsInChildren<Renderer>(true))).Distinct().ToArray();
  var root=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName)??Node(RootName,null,scene);Vector3 eye=wearer.GetComponentsInChildren<Renderer>(true).First(r=>r.name=="VR headset").transform.position;Quaternion rotation=Quaternion.Euler(14,4.6f,0);
  var frontal=CameraFor("01 - Frontal headset eye view",root.transform,rig,eye,rotation,new Rect(.045f,.07f,.91f,.86f),panels,hide);
  var cover=CameraFor("02 - Cover layout - title space left",root.transform,rig,eye,rotation,new Rect(.37f,.14f,.59f,.66f),panels,hide);
  frontal.fitToFrame=true;frontal.manualShift=Vector2.zero;
  var wings=rig.transform.Cast<Transform>().Where(t=>t.name.StartsWith("02 - visible")||t.name.StartsWith("03 - interpretation")).SelectMany(t=>t.GetComponentsInChildren<Renderer>(true));cover.hideForView=hide.Concat(wings).Distinct().ToArray();cover.fitToFrame=false;cover.baseFieldOfView=96;cover.manualShift=new Vector2(.20f,0);cover.Reframe();
  var views=new[]{frontal,cover};
  try
  {
   foreach(var stage in scene.GetRootGameObjects().Select(g=>g.GetComponent<BetweenUsPartnerStageRig>()).Where(x=>x!=null))stage.Apply();
   foreach(var view in views)
   {
    bool isCover=view==cover;string stem=isCover?"BetweenUs-Headset-Cover":"BetweenUs-Headset-Frontal";rig.captureCamera=view.GetComponent<Camera>();view.useSourcePlate=false;
    var enabled=rig.GetComponentsInChildren<Renderer>(true).Select(r=>new{renderer=r,value=r.enabled}).ToArray();
    string plate;
    try{foreach(var item in enabled)item.renderer.enabled=false;plate=BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/Cover/"+stem+"-Room-Plate.png");}
    finally{foreach(var item in enabled)item.renderer.enabled=item.value;}
    string path=AssetRoot+"/"+stem+"-Room-Plate.png";File.Copy(plate,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.maxTextureSize=2048;imp.mipmapEnabled=false;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();view.sourcePlate=AssetDatabase.LoadAssetAtPath<Texture2D>(path);view.useSourcePlate=true;rig.RefreshAtlas();
    BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/Cover/"+stem+"-Preview.png");BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,"Lounge/Cover/"+stem+"-4K.png");
    BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,"Lounge/Cover/"+stem+"-Transparent-4K.png");
   }
  }
  finally{foreach(var view in views)view.Restore();rig.captureCamera=original;rig.RefreshAtlas();for(int i=0;i<originalRenders.Length;i++)if(originalRenders[i]!=null)originalRenders[i].enabled=states[i];}
  int changed=poses.Count(p=>p.t!=null&&((p.t.localPosition-p.p).sqrMagnitude>1e-7f||(p.t.localScale-p.s).sqrMagnitude>1e-7f||Quaternion.Angle(p.t.localRotation,p.r)>.001f));bool restored=originalRenders.Select((r,i)=>r==null||r.enabled==states[i]).All(v=>v);bool errors=panels.SelectMany(r=>r.sharedMaterials).Any(m=>m!=null&&ShaderUtil.ShaderHasError(m.shader));
  if(changed!=0||!restored||errors)throw new InvalidOperationException("Cover render validation failed: transforms="+changed+", visibility="+restored+", shaders="+errors);
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
  File.WriteAllText(Path.Combine(Output,"CoverValidation.json"),JsonUtility.ToJson(new Report{scene=scene.path,originalCamera=BetweenUsAuthorCameraInspect.PathOf(original.transform),changedExistingTransforms=changed,restoredOriginalCamera=rig.captureCamera==original,restoredVisibility=restored,shaderErrors=errors,cameras=views.Select(v=>new CameraReport{name=v.name,eye=v.transform.position,euler=v.transform.eulerAngles,baseFov=v.baseFieldOfView,fittedScale=v.fittedScale,manualShift=v.manualShift,targetFrame=v.targetFrame,plate=AssetDatabase.GetAssetPath(v.sourcePlate)}).ToArray()},true));
  if(File.Exists(Path.Combine(Output,"error.txt")))File.Delete(Path.Combine(Output,"error.txt"));Selection.activeGameObject=cover.gameObject;SceneView.RepaintAll();Debug.Log("BETWEEN_US_HEADSET_COVER_OK "+Output);
 }
}
[InitializeOnLoad]static class BetweenUsCoverRequest
{
 static double next;static BetweenUsCoverRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-cover.request");if(!File.Exists(request))return;File.Delete(request);try{BetweenUsCoverStudio.RenderCover();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge/Cover");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}


