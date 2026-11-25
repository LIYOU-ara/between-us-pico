using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Offline lounge scenery. Existing people, UI and authored camera poses stay intact.
public static class BetweenUsLoungeStudio
{
 const string RootName="LOUNGE - translucent environment";
 const string AssetRoot="Assets/BetweenUs/Composite/Lounge";
 const int Layer=29;
 [Serializable]class Pose{public Transform t;public string name;public Vector3 p,s;public Quaternion r;}
 [Serializable]class Report{public string scene,camera,source,license;public int furnitureInstances,environmentRenderers,changedExistingTransforms;public bool redBackdropDisabled,redPlateRemoved,allEnvironmentTranslucent,shaderErrors;public Vector3 cameraPosition,cameraEuler;public string[] objects;}
 static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge");
 static BetweenUsCompositeRig Rig()=>Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded);
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
 static GameObject Node(string name,Transform parent,Scene scene){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);if(parent!=null)go.transform.SetParent(parent,false);go.layer=Layer;return go;}
 static void SetLayer(Transform root){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;}
 static Material Mat(string name,Color tint,float alpha,float rim,int queue=2970)
 {
  string path=AssetRoot+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(material==null){var shader=Shader.Find("Between Us/Composite/Lounge Translucent");if(shader==null)throw new InvalidOperationException("Lounge shader missing.");material=new Material(shader){name=name};AssetDatabase.CreateAsset(material,path);}
  material.SetColor("_Color",tint);material.SetFloat("_Opacity",alpha);material.SetFloat("_RimStrength",rim);material.SetFloat("_RimPower",3.0f);material.SetFloat("_Roughness",.86f);material.SetFloat("_AccentStrength",.012f);material.SetColor("_RimColor",new Color(.87f,.91f,.98f,1));material.renderQueue=queue;EditorUtility.SetDirty(material);return material;
 }
 static GameObject Box(string name,Transform parent,Scene scene,Vector3 p,Vector3 size,Material material)
 {
  var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(parent,false);go.layer=Layer;go.transform.localPosition=p;go.transform.localScale=size;var collider=go.GetComponent<Collider>();if(collider!=null)UnityEngine.Object.DestroyImmediate(collider);var renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return go;
 }
 static Bounds BoundsOf(GameObject go){var renderers=go.GetComponentsInChildren<Renderer>();if(renderers.Length==0)throw new InvalidOperationException("No mesh in "+go.name);var bounds=renderers[0].bounds;foreach(var r in renderers.Skip(1))bounds.Encapsulate(r.bounds);return bounds;}
 static GameObject Model(string file,string name,Transform parent,Scene scene,Vector3 p,Vector3 size,float yaw,Material material)
 {
  string path=AssetRoot+"/Models/Kenney/"+file+".obj";var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset==null)throw new InvalidOperationException("Imported furniture model unavailable: "+path);
  var wrapper=Node(name,parent,scene);var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);model.name=file+" - Kenney CC0";model.transform.SetParent(wrapper.transform,false);model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
  var b=BoundsOf(model);model.transform.localScale=new Vector3(size.x/Mathf.Max(b.size.x,.001f),size.y/Mathf.Max(b.size.y,.001f),size.z/Mathf.Max(b.size.z,.001f));b=BoundsOf(model);model.transform.position-=new Vector3(b.center.x,b.min.y,b.center.z);
  wrapper.transform.localPosition=p;wrapper.transform.localRotation=Quaternion.Euler(0,yaw,0);SetLayer(wrapper.transform);
  foreach(var renderer in model.GetComponentsInChildren<Renderer>()){renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}return wrapper;
 }
 static void RemoveRedPlate(BetweenUsCompositeRig rig)
 {
  var red=rig.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Frame 10 - author camera backdrop");if(red!=null){Undo.RecordObject(red,"Hide red halftone backdrop");red.SetActive(false);}
  foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{BetweenUsCompositeStudio.AssetRoot}))
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));if(m==null||!m.HasProperty("_BackdropTex"))continue;var tex=m.GetTexture("_BackdropTex");if(tex!=null&&tex.name.Contains("Frame10")){m.SetTexture("_BackdropTex",null);m.SetFloat("_BackdropStrength",0);EditorUtility.SetDirty(m);}
  }
  foreach(var m in rig.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct()){m.SetTexture("_BackdropTex",null);m.SetFloat("_BackdropStrength",0);EditorUtility.SetDirty(m);}
 }
 static void AddFloorRing(string name,Transform parent,Scene scene,Vector3 p,Material original)
 {
  var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=name;SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(parent,false);go.layer=Layer;go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(90,0,0);go.transform.localScale=new Vector3(5.4f,4.4f,1);UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=original;
 }
 [MenuItem("Prototype/Between Us/Lounge/Build translucent lounge and export")]
 public static void Build()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build the offline lounge outside Play Mode.");
  AssetDatabase.Refresh();var rig=Rig()??BetweenUsCompositeStudio.EnsureScene();ShaderUtil.allowAsyncCompilation=false;Folder(AssetRoot+"/Materials");Folder(AssetRoot+"/Textures");Folder(AssetRoot+"/Backups");Directory.CreateDirectory(Output);
  string backup=AssetRoot+"/Backups/Before-Translucent-Lounge.unity";if(!File.Exists(backup))EditorSceneManager.SaveScene(rig.gameObject.scene,backup,true);
  var poses=rig.GetComponentsInChildren<Transform>(true).Select(t=>new Pose{t=t,name=BetweenUsAuthorCameraInspect.PathOf(t),p=t.localPosition,s=t.localScale,r=t.localRotation}).ToArray();
  RemoveRedPlate(rig);
  var room=rig.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
  if(room==null)
  {
   room=Node(RootName,null,rig.gameObject.scene);Undo.RegisterCreatedObjectUndo(room,"Create translucent lounge");
   var architecture=Node("01 - Architecture - translucent",room.transform,rig.gameObject.scene).transform;var seating=Node("02 - Seating - Kenney CC0",room.transform,rig.gameObject.scene).transform;var tables=Node("03 - Tables and rug",room.transform,rig.gameObject.scene).transform;var lighting=Node("04 - Lamps and shelves",room.transform,rig.gameObject.scene).transform;var plants=Node("05 - Plants and details",room.transform,rig.gameObject.scene).transform;var fx=Node("06 - Soft floor halos",room.transform,rig.gameObject.scene).transform;
   var floor=Mat("01 Floor - subtle smoked glass",new Color(.52f,.57f,.64f,1),.10f,.12f,2950);var wall=Mat("02 Walls - pale architectural glass",new Color(.57f,.63f,.69f,1),.09f,.14f,2955);var edges=Mat("03 Frames - soft silver",new Color(.77f,.82f,.89f,1),.26f,.16f,2960);var sofa=Mat("04 Seating - frosted pearl",new Color(.68f,.72f,.79f,1),.33f,.30f,2970);var table=Mat("05 Tables - clear cool glass",new Color(.56f,.65f,.73f,1),.24f,.34f,2980);var leaf=Mat("06 Foliage - mist sage glass",new Color(.46f,.62f,.58f,1),.24f,.24f,2970);var warm=Mat("07 Lighting - warm opal",new Color(.81f,.75f,.63f,1),.27f,.32f,2970);var rug=Mat("08 Rug - transparent slate",new Color(.44f,.49f,.58f,1),.085f,.10f,2952);var decor=Mat("09 Details - muted rose glass",new Color(.70f,.50f,.54f,1),.24f,.22f,2974);
   var people=rig.transform.Find("Two seated participants - ghost study");var a=people.Find("Participant A - leaning seated").GetComponentsInChildren<Renderer>(true).First(r=>r.name=="Seated body");var b=people.Find("Participant B - relaxed seated").GetComponentsInChildren<Renderer>(true).First(r=>r.name=="Seated body");
   float y=Mathf.Min(a.bounds.min.y,b.bounds.min.y)-.10f;float za=a.bounds.center.z-.35f,zb=b.bounds.center.z+.35f;
   Box("Floor slab",architecture,rig.gameObject.scene,new Vector3(0,y-.07f,.1f),new Vector3(15,.14f,15.6f),floor);
   Box("Rear glass wall",architecture,rig.gameObject.scene,new Vector3(0,y+3.5f,7.35f),new Vector3(15,7,.10f),wall);
   Box("Left glass wall",architecture,rig.gameObject.scene,new Vector3(-7.5f,y+3.5f,.1f),new Vector3(.10f,7,14.6f),wall);
   Box("Right rear glass screen",architecture,rig.gameObject.scene,new Vector3(7.5f,y+3.5f,3.65f),new Vector3(.10f,7,7.4f),wall);
   Box("Rear baseboard",architecture,rig.gameObject.scene,new Vector3(0,y+.09f,7.23f),new Vector3(15,.075f,.07f),edges);
   Box("Left baseboard",architecture,rig.gameObject.scene,new Vector3(-7.4f,y+.09f,.1f),new Vector3(.07f,.075f,14.6f),edges);
   Box("Rear upper frame",architecture,rig.gameObject.scene,new Vector3(0,y+6.7f,7.22f),new Vector3(15,.055f,.055f),edges);
   foreach(float x in new[]{-6.4f,-2.15f,2.15f,6.4f})Box("Rear window mullion",architecture,rig.gameObject.scene,new Vector3(x,y+4.05f,7.21f),new Vector3(.055f,5.25f,.055f),edges);
   Box("Rear window lower frame",architecture,rig.gameObject.scene,new Vector3(0,y+1.43f,7.21f),new Vector3(12.8f,.055f,.055f),edges);
   Model("loungeDesignSofa","Sofa A - facing conversation partner",seating,rig.gameObject.scene,new Vector3(a.bounds.center.x,y,za),new Vector3(5.2f,2.15f,2.35f),180,sofa);
   Model("loungeDesignSofa","Sofa B - facing headset wearer",seating,rig.gameObject.scene,new Vector3(b.bounds.center.x,y,zb),new Vector3(5.0f,2.15f,2.35f),0,sofa);
   Model("loungeChair","Side lounge chair",seating,rig.gameObject.scene,new Vector3(-5.0f,y,.65f),new Vector3(2.0f,2.35f,1.95f),-65,sofa);
   Model("tableCoffeeGlass","Central coffee table",tables,rig.gameObject.scene,new Vector3(.10f,y,-.45f),new Vector3(3.3f,.92f,1.9f),0,table);
   Model("rugRounded","Soft transparent rug",tables,rig.gameObject.scene,new Vector3(0,y+.025f,-.45f),new Vector3(6.5f,.025f,7.4f),0,rug);
   Model("sideTable","Side table beside sofa B",tables,rig.gameObject.scene,new Vector3(4.15f,y,3.55f),new Vector3(1.4f,1.35f,1.05f),0,table);
   Model("lampRoundFloor","Opal floor lamp",lighting,rig.gameObject.scene,new Vector3(5.3f,y,5.0f),new Vector3(.8f,4.9f,.9f),0,warm);
   Model("bookcaseOpenLow","Low display bookshelf",lighting,rig.gameObject.scene,new Vector3(-4.75f,y,6.25f),new Vector3(3.2f,2.3f,1.1f),0,sofa);
   Model("books","Books on display shelf",lighting,rig.gameObject.scene,new Vector3(-4.75f,y+2.3f,6.25f),new Vector3(.85f,.55f,.50f),0,warm);
   Model("pottedPlant","Tall translucent plant",plants,rig.gameObject.scene,new Vector3(-6.0f,y,4.9f),new Vector3(1.15f,3.6f,1.25f),20,leaf);
   Model("pottedPlant","Corner translucent plant",plants,rig.gameObject.scene,new Vector3(5.8f,y,-.1f),new Vector3(.95f,2.8f,1.05f),-15,leaf);
   Model("plantSmall1","Small table plant",plants,rig.gameObject.scene,new Vector3(4.15f,y+1.35f,3.55f),new Vector3(.48f,.68f,.48f),0,leaf);
   Model("pillow","Muted glass cushion A",plants,rig.gameObject.scene,new Vector3(a.bounds.center.x-1.70f,y+1.54f,za-.62f),new Vector3(.90f,.88f,.38f),180,decor);
   Model("pillowLong","Muted glass cushion B",plants,rig.gameObject.scene,new Vector3(b.bounds.center.x+1.65f,y+1.54f,zb+.62f),new Vector3(1.10f,.80f,.38f),0,warm);
   var oldRings=people.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Luminous floor ring").ToArray();var ringMaterial=oldRings.First().sharedMaterial;
   AddFloorRing("Soft halo under wearer",fx,rig.gameObject.scene,new Vector3(a.bounds.center.x,y+.018f,a.bounds.center.z),ringMaterial);AddFloorRing("Soft halo under partner",fx,rig.gameObject.scene,new Vector3(b.bounds.center.x,y+.018f,b.bounds.center.z),ringMaterial);
   foreach(var t in people.GetComponentsInChildren<Transform>(true))if(t.name=="Quiet translucent chair"||t.name=="Luminous floor ring")t.gameObject.SetActive(false);
  }
  Export(rig,room);
  int changed=poses.Count(p=>(p.t.localPosition-p.p).sqrMagnitude>.0000001f||(p.t.localScale-p.s).sqrMagnitude>.0000001f||Quaternion.Angle(p.t.localRotation,p.r)>.001f);if(changed!=0)throw new InvalidOperationException("Existing person/UI/camera poses changed: "+changed);
  var env=room.GetComponentsInChildren<Renderer>(true);bool translucent=env.All(r=>r.sharedMaterials.All(m=>m!=null&&m.HasProperty("_Opacity")&&m.GetFloat("_Opacity")>0&&m.GetFloat("_Opacity")<1));bool shaderErrors=env.Any(r=>r.sharedMaterials.Any(m=>ShaderUtil.ShaderHasError(m.shader)));if(!translucent||shaderErrors)throw new InvalidOperationException("Environment transparency/shader validation failed.");
  var activeGlass=rig.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct();bool noRed=activeGlass.All(m=>m.GetTexture("_BackdropTex")==null||!m.GetTexture("_BackdropTex").name.Contains("Frame10"));
  PrefabUtility.SaveAsPrefabAsset(room,AssetRoot+"/BetweenUs_TranslucentLounge.prefab");AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
  var camera=rig.captureCamera;File.WriteAllText(Path.Combine(Output,"LoungeValidation.json"),JsonUtility.ToJson(new Report{scene=rig.gameObject.scene.path,camera=BetweenUsAuthorCameraInspect.PathOf(camera.transform),cameraPosition=camera.transform.position,cameraEuler=camera.transform.eulerAngles,source="Kenney Furniture Kit - https://kenney.nl/assets/furniture-kit",license="CC0 - original License.txt included",furnitureInstances=room.GetComponentsInChildren<Transform>().Count(t=>t.name.EndsWith("Kenney CC0")),environmentRenderers=env.Length,changedExistingTransforms=changed,redBackdropDisabled=!rig.gameObject.scene.GetRootGameObjects().Any(g=>g.name=="Frame 10 - author camera backdrop"&&g.activeSelf),redPlateRemoved=noRed,allEnvironmentTranslucent=translucent,shaderErrors=shaderErrors,objects=env.Select(r=>r.name).ToArray()},true));
  if(File.Exists(Path.Combine(Output,"error.txt")))File.Delete(Path.Combine(Output,"error.txt"));Selection.activeGameObject=room;if(SceneView.lastActiveSceneView!=null){SceneView.lastActiveSceneView.FrameSelected();SceneView.lastActiveSceneView.Repaint();}Debug.Log("BETWEEN_US_TRANSLUCENT_LOUNGE_OK "+Output);
 }
 static void Export(BetweenUsCompositeRig rig,GameObject room)
 {
  var authored=rig.captureCamera;var oldFlags=authored.clearFlags;authored.clearFlags=CameraClearFlags.SolidColor;var renders=rig.GetComponentsInChildren<Renderer>(true);var enabled=renders.Select(r=>r.enabled).ToArray();
  try
  {
   // Capture the room alone from the same authored viewpoint, then use this new
   // local plate for the existing panel blur. No old red artwork survives.
   for(int i=0;i<renders.Length;i++)renders[i].enabled=false;
   string source=BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/Room-Backdrop-Source.png");
   string texturePath=AssetRoot+"/Textures/Room_Backdrop.png";File.Copy(source,texturePath,true);AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);importer.maxTextureSize=2048;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.sRGBTexture=true;importer.SaveAndReimport();var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
   for(int i=0;i<renders.Length;i++)renders[i].enabled=enabled[i];rig.RefreshAtlas();
   foreach(var material in renders.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct()){material.SetTexture("_BackdropTex",texture);material.SetFloat("_BackdropScreenSpace",1);material.SetFloat("_BackdropStrength",.42f);material.SetFloat("_BlurRadius",18);EditorUtility.SetDirty(material);}
   BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/BetweenUs-Lounge-Preview.png");BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,"Lounge/BetweenUs-Lounge-4K.png");
   foreach(var m in renders.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct())m.SetFloat("_BackdropStrength",0);
   BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,"Lounge/BetweenUs-Lounge-Transparent-4K.png");
   foreach(var m in renders.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct())m.SetFloat("_BackdropStrength",.42f);
   var hidden=rig.transform.Cast<Transform>().Where(t=>t.name.StartsWith("01 - response")||t.name.StartsWith("02 - visible")||t.name.StartsWith("03 - interpretation")||t.name.StartsWith("04 - partner")||t.name.StartsWith("05 - session")).ToArray();var active=hidden.Select(t=>t.gameObject.activeSelf).ToArray();var go=Node("Lounge overview camera - temporary",null,rig.gameObject.scene);var overview=go.AddComponent<Camera>();overview.CopyFrom(authored);overview.enabled=false;overview.fieldOfView=52;overview.transform.position=new Vector3(11.5f,9.5f,-14.8f);overview.transform.LookAt(new Vector3(0,2.4f,.1f));overview.ResetWorldToCameraMatrix();overview.ResetProjectionMatrix();
   try{foreach(var t in hidden)t.gameObject.SetActive(false);rig.captureCamera=overview;BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"Lounge/BetweenUs-Lounge-Room-Overview.png");}finally{for(int i=0;i<hidden.Length;i++)hidden[i].gameObject.SetActive(active[i]);rig.captureCamera=authored;UnityEngine.Object.DestroyImmediate(go);}
  }
  finally{for(int i=0;i<renders.Length;i++)if(renders[i]!=null)renders[i].enabled=enabled[i];rig.captureCamera=authored;authored.clearFlags=oldFlags;rig.RefreshAtlas();}
 }
}
[InitializeOnLoad]static class BetweenUsLoungeRequest
{
 static double next;static BetweenUsLoungeRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-lounge.request");if(!File.Exists(request))return;File.Delete(request);try{BetweenUsLoungeStudio.Build();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"Lounge");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}
