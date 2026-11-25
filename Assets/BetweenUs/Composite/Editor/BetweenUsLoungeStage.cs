using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class BetweenUsLoungeStage
{
 const string AssetRoot="Assets/BetweenUs/Composite/Lounge/StageLighting";
 const string RootName="STAGE - partner spotlight";
 static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge/StageLight");
 [Serializable]class Pose{public Transform t;public Vector3 p,s;public Quaternion r;}
 [Serializable]class Report{public string scene,camera,partner,light;public int changedExistingTransforms;public bool redBackdropDisabled,allRoomTranslucent,shaderErrors;public Vector3 lightPosition,lightDirection;public float strength,spotAngle,subjectOpacity,heroOpacity;}
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
 static GameObject Node(string name,Transform parent,Scene scene){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);if(parent!=null)go.transform.SetParent(parent,false);go.layer=29;return go;}
 static Material MaterialAsset(string name,string shaderName){string path=AssetRoot+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){var shader=Shader.Find(shaderName);if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Invalid stage shader: "+shaderName);m=new Material(shader){name=name};AssetDatabase.CreateAsset(m,path);}return m;}
 static Mesh Cone()
 {
  const int segments=96,rings=20;var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
  for(int y=0;y<=rings;y++){float t=Mathf.Max(.0001f,(float)y/rings);for(int x=0;x<=segments;x++){float a=(float)x/segments*Mathf.PI*2;var radial=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);v.Add(new Vector3(radial.x*t,radial.y*t,t));n.Add(new Vector3(radial.x,radial.y,-1).normalized);uv.Add(new Vector2((float)x/segments,(float)y/rings));}}
  for(int y=0;y<rings;y++)for(int x=0;x<segments;x++){int a=y*(segments+1)+x,b=a+1,c=a+segments+1,d=c+1;tri.Add(a);tri.Add(b);tri.Add(c);tri.Add(b);tri.Add(d);tri.Add(c);}
  var mesh=new Mesh{name="Soft spotlight cone - unit length"};mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateBounds();return mesh;
 }
 [MenuItem("Prototype/Between Us/Lounge/Partner stage light and export")]
 public static void Build()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use stage setup outside Play Mode.");
  AssetDatabase.Refresh();ShaderUtil.allowAsyncCompilation=false;var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded)??BetweenUsCompositeStudio.EnsureScene();var scene=rig.gameObject.scene;
  var room=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="LOUNGE - translucent environment");if(room==null)throw new InvalidOperationException("The existing translucent lounge must be loaded.");
  Folder(AssetRoot);Directory.CreateDirectory(Output);string backup=AssetRoot+"/Before-Partner-Stage.unity";if(!File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
  var poses=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>new Pose{t=t,p=t.localPosition,r=t.localRotation,s=t.localScale}).ToArray();
  var b=rig.transform.Find("Two seated participants - ghost study/Participant B - relaxed seated").GetComponentsInChildren<Renderer>(true).First(r=>r.name=="Seated body");
  var stage=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
  if(stage==null)
  {
   stage=Node(RootName,null,scene);Undo.RegisterCreatedObjectUndo(stage,"Partner dramatic stage lighting");
   var bridge=stage.AddComponent<BetweenUsPartnerStageRig>();bridge.partner=b;
   var lightGo=Node("Key - warm white spot - adjust this light",stage.transform,scene);var key=lightGo.AddComponent<Light>();key.type=LightType.Spot;key.color=new Color(1,.91f,.80f);key.intensity=1.65f;key.range=15;key.spotAngle=38;key.innerSpotAngle=23;key.shadows=LightShadows.None;key.cullingMask=1<<29;key.renderMode=LightRenderMode.ForcePixel;
   lightGo.transform.position=new Vector3(-2.15f,7.65f,-.20f);lightGo.transform.LookAt(new Vector3(b.bounds.center.x,3.0f,b.bounds.center.z));bridge.keyLight=key;bridge.ambient=.20f;bridge.floorY=room.GetComponentsInChildren<Renderer>().First(r=>r.name=="Floor slab").bounds.max.y+.02f;
   var subject=MaterialAsset("Partner B - theatrical pearl","Between Us/Composite/Ghost Person");subject.CopyPropertiesFromMaterial(b.sharedMaterial);subject.name="Partner B - theatrical pearl";subject.SetFloat("_Opacity",.50f);subject.SetFloat("_RimStrength",.65f);subject.SetFloat("_AccentStrength",.01f);subject.SetFloat("_StageStrength",key.intensity);subject.SetFloat("_StageAmbient",bridge.ambient);subject.SetVector("_StageLightPosition",key.transform.position);subject.SetVector("_StageLightDirection",key.transform.forward);subject.SetFloat("_StageSpotCos",Mathf.Cos(key.spotAngle*.5f*Mathf.Deg2Rad));subject.SetFloat("_StageSpotSoftness",.025f);subject.SetFloat("_StageLightRange",key.range);subject.SetColor("_StageColor",key.color);subject.SetColor("_StageRimColor",bridge.rimColour);subject.SetVector("_StageRimDirection",bridge.rimDirection.normalized);subject.SetFloat("_StageRimStrength",.40f);EditorUtility.SetDirty(subject);Undo.RecordObject(b,"Isolate partner spotlight material");b.sharedMaterial=subject;
   var beamMat=MaterialAsset("Soft warm stage beam","Between Us/Composite/Stage Beam");beamMat.SetColor("_Color",key.color);beamMat.SetFloat("_Opacity",.075f);beamMat.SetFloat("_EdgeSoftness",.70f);beamMat.SetFloat("_BackfaceContribution",.08f);beamMat.SetFloat("_EndFade",.4f);beamMat.renderQueue=2945;EditorUtility.SetDirty(beamMat);
   var beam=Node("Soft visible beam - translucent",lightGo.transform,scene);var mesh=Cone();AssetDatabase.CreateAsset(mesh,AssetRoot+"/StageBeam.mesh.asset");beam.AddComponent<MeshFilter>().sharedMesh=mesh;beam.AddComponent<MeshRenderer>().sharedMaterial=beamMat;bridge.beam=beam.transform;
   var poolMat=MaterialAsset("Soft warm floor pool","Between Us/Composite/Stage Beam");poolMat.SetColor("_Color",key.color);poolMat.SetFloat("_GroundMode",1);poolMat.SetFloat("_Opacity",.16f);poolMat.SetFloat("_PoolRadius",.88f);poolMat.SetFloat("_PoolFalloff",3.4f);poolMat.renderQueue=2965;EditorUtility.SetDirty(poolMat);
   var pool=GameObject.CreatePrimitive(PrimitiveType.Quad);pool.name="Soft light pool - no hard circle";SceneManager.MoveGameObjectToScene(pool,scene);pool.transform.SetParent(stage.transform,false);pool.layer=29;pool.transform.rotation=Quaternion.Euler(90,0,0);UnityEngine.Object.DestroyImmediate(pool.GetComponent<Collider>());pool.GetComponent<Renderer>().sharedMaterial=poolMat;bridge.floorPool=pool.transform;
   // Reduce competing scenery once. Re-exporting preserves later material edits.
   foreach(var m in room.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct())
   {
    if(m==null||m.shader.name!="Between Us/Composite/Lounge Translucent")continue;Undo.RecordObject(m,"Lower lounge visual weight");Color c=m.GetColor("_Color");m.SetColor("_Color",new Color(c.r*.60f,c.g*.60f,c.b*.60f,c.a));m.SetFloat("_RimStrength",m.GetFloat("_RimStrength")*.65f);if(m.name.StartsWith("04 Seating"))m.SetFloat("_Opacity",.25f);EditorUtility.SetDirty(m);
   }
   foreach(var m in rig.glassMaterials)
   {
    if(m==null)continue;Undo.RecordObject(m,"Strengthen response hierarchy");bool hero=m.name.StartsWith("01 - response"),quote=m.name.StartsWith("04 - partner");m.SetFloat("_Opacity",hero?.75f:quote?.70f:.60f);m.SetFloat("_RimStrength",hero?.96f:quote?.90f:.77f);m.SetFloat("_FlowIntensity",hero?1.24f:1.05f);m.SetFloat("_EdgeWidth",hero?.0027f:.0018f);EditorUtility.SetDirty(m);
   }
  }
  stage.GetComponent<BetweenUsPartnerStageRig>().Apply();rig.RefreshAtlas();Export(rig,room,stage);
  int changed=poses.Count(p=>p.t!=null&&((p.t.localPosition-p.p).sqrMagnitude>1e-7f||(p.t.localScale-p.s).sqrMagnitude>1e-7f||Quaternion.Angle(p.t.localRotation,p.r)>.001f));if(changed!=0)throw new InvalidOperationException("Unexpected changes to existing poses: "+changed);
  var all=room.GetComponentsInChildren<Renderer>(true).Concat(stage.GetComponentsInChildren<Renderer>(true)).Append(b).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();bool errors=all.Any(m=>ShaderUtil.ShaderHasError(m.shader));if(errors)throw new InvalidOperationException("Stage shader compilation failed.");
  var sr=stage.GetComponent<BetweenUsPartnerStageRig>();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  File.WriteAllText(Path.Combine(Output,"StageValidation.json"),JsonUtility.ToJson(new Report{scene=scene.path,camera=BetweenUsAuthorCameraInspect.PathOf(rig.captureCamera.transform),partner=BetweenUsAuthorCameraInspect.PathOf(b.transform),light=BetweenUsAuthorCameraInspect.PathOf(sr.keyLight.transform),changedExistingTransforms=changed,redBackdropDisabled=!scene.GetRootGameObjects().Any(g=>g.name=="Frame 10 - author camera backdrop"&&g.activeSelf),allRoomTranslucent=room.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.HasProperty("_Opacity")&&m.GetFloat("_Opacity")>0&&m.GetFloat("_Opacity")<1)),shaderErrors=errors,lightPosition=sr.keyLight.transform.position,lightDirection=sr.keyLight.transform.forward,strength=sr.keyLight.intensity,spotAngle=sr.keyLight.spotAngle,subjectOpacity=b.sharedMaterial.GetFloat("_Opacity"),heroOpacity=rig.glassMaterials[0].GetFloat("_Opacity")},true));
  if(File.Exists(Path.Combine(Output,"error.txt")))File.Delete(Path.Combine(Output,"error.txt"));Selection.activeGameObject=sr.keyLight.gameObject;SceneView.RepaintAll();Debug.Log("BETWEEN_US_PARTNER_STAGE_OK "+Output);
 }
 [MenuItem("Prototype/Between Us/Lounge/More transparent partner and visible cone")]
 public static void Tune()
 {
  var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().First(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded);
  var stage=rig.gameObject.scene.GetRootGameObjects().First(g=>g.name==RootName).GetComponent<BetweenUsPartnerStageRig>();
  var subject=stage.partner.sharedMaterial;Undo.RecordObject(subject,"More translucent stage partner");subject.SetFloat("_Opacity",.29f);subject.SetFloat("_RimStrength",.45f);EditorUtility.SetDirty(subject);
  Undo.RecordObject(stage,"Softer partner highlights");stage.ambient=.24f;stage.rimStrength=.32f;Undo.RecordObject(stage.keyLight,"Balance visible stage cone");stage.keyLight.intensity=1.45f;subject.SetFloat("_StageStrength",1.45f);subject.SetFloat("_StageAmbient",.24f);subject.SetFloat("_StageRimStrength",.32f);
  var beam=stage.beam.GetComponent<Renderer>().sharedMaterial;Undo.RecordObject(beam,"Visible soft stage cone");beam.SetFloat("_Opacity",.26f);beam.SetFloat("_Intensity",1.05f);beam.SetFloat("_EdgeSoftness",.55f);beam.SetFloat("_BackfaceContribution",.15f);beam.SetFloat("_LengthFalloff",.40f);beam.SetFloat("_EndFade",.30f);beam.SetFloat("_TipFade",.08f);EditorUtility.SetDirty(beam);stage.Apply();Build();
 }
 static void Export(BetweenUsCompositeRig rig,GameObject room,GameObject stage)
 {
  var camera=rig.captureCamera;var flags=camera.clearFlags;camera.clearFlags=CameraClearFlags.SolidColor;var renders=rig.GetComponentsInChildren<Renderer>(true);var enabled=renders.Select(r=>r.enabled).ToArray();var mats=renders.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct().ToArray();
  try
  {
   foreach(var r in renders)r.enabled=false;string source=BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/StageLight/Room-Backdrop-Source.png");string path=AssetRoot+"/Room_Backdrop_Stage.png";File.Copy(source,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.maxTextureSize=2048;imp.mipmapEnabled=false;imp.textureCompression=TextureImporterCompression.Uncompressed;imp.SaveAndReimport();var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   for(int i=0;i<renders.Length;i++)renders[i].enabled=enabled[i];rig.RefreshAtlas();
   foreach(var m in mats){m.SetTexture("_BackdropTex",texture);m.SetFloat("_BackdropScreenSpace",1);m.SetFloat("_BackdropStrength",m.name.StartsWith("01 - response")?.30f:.38f);m.SetFloat("_BlurRadius",22);EditorUtility.SetDirty(m);}
   BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/StageLight/BetweenUs-Lounge-Stage-Preview.png");BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,"Lounge/StageLight/BetweenUs-Lounge-Stage-4K.png");
   foreach(var m in mats)m.SetFloat("_BackdropStrength",0);BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,"Lounge/StageLight/BetweenUs-Lounge-Stage-Transparent-4K.png");foreach(var m in mats)m.SetFloat("_BackdropStrength",m.name.StartsWith("01 - response")?.30f:.38f);
  }
  finally{for(int i=0;i<renders.Length;i++)if(renders[i]!=null)renders[i].enabled=enabled[i];camera.clearFlags=flags;rig.RefreshAtlas();}
 }
}
[InitializeOnLoad]static class BetweenUsLoungeStageRequest
{
 static double next;static BetweenUsLoungeStageRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-lounge-stage.request");if(!File.Exists(request))return;string action=File.ReadAllText(request).Trim();File.Delete(request);try{if(action=="tune")BetweenUsLoungeStage.Tune();else BetweenUsLoungeStage.Build();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge/StageLight");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}

