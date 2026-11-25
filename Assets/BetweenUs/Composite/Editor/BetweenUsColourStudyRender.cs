using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class BetweenUsColourStudyRender
{
 const string AssetFolder="Assets/BetweenUs/Composite/ColourStudies";
 [Serializable] class Palette {public string id,title;public Color frost,a,b,rim;public float tint;public Palette(string i,string t,Color f,Color x,Color y,float k,Color r){id=i;title=t;frost=f;a=x;b=y;tint=k;rim=r;}}
 [Serializable] class MaterialBinding {public Renderer renderer;public Material[] materials;public bool enabled;}
 [Serializable] class Pose {public Transform node;public Vector3 position,scale;public Quaternion rotation;}
 [Serializable] class Report {public string camera,folder;public int variants,files,existingTransforms,changedTransforms;public bool originalMaterialsRestored,originalBackdropRestored,shaderErrors;public Palette[] palettes;}
 static Color C(float r,float g,float b)=>new Color(r,g,b,1);
 static Palette[] Presets()=>new[]{
  new Palette("01-Mist-Blue","Mist Blue",C(.42f,.50f,.62f),C(.25f,.48f,.76f),C(.68f,.82f,.94f),.44f,C(.88f,.95f,1)),
  new Palette("02-Soft-Teal","Soft Teal",C(.42f,.55f,.55f),C(.20f,.62f,.58f),C(.66f,.86f,.78f),.44f,C(.88f,1,.96f)),
  new Palette("03-Soft-Lilac","Soft Lilac",C(.50f,.46f,.59f),C(.52f,.38f,.72f),C(.82f,.70f,.91f),.46f,C(.96f,.91f,1)),
  new Palette("04-Warm-Champagne","Warm Champagne",C(.58f,.51f,.44f),C(.77f,.52f,.29f),C(.94f,.80f,.62f),.40f,C(1,.96f,.88f)),
  new Palette("05-Dusty-Rose","Dusty Rose",C(.60f,.46f,.49f),C(.76f,.34f,.44f),C(.94f,.70f,.73f),.42f,C(1,.91f,.94f))};
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
 [MenuItem("Prototype/Between Us/Render five glass colour studies")]
 public static void Render()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before offline colour exports.");
  ShaderUtil.allowAsyncCompilation=false;Folder(AssetFolder);
  string output=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"ColourStudies");Directory.CreateDirectory(output);
  var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(x=>x.gameObject.scene.IsValid()&&x.gameObject.scene.isLoaded);
  if(rig==null||rig.captureCamera==null)throw new InvalidOperationException("Open the approved desktop composition.");
  var originalCamera=rig.captureCamera;var oldGlass=rig.glassMaterials;float oldTime=rig.previewTime;
  var poses=rig.GetComponentsInChildren<Transform>(true).Select(t=>new Pose{node=t,position=t.localPosition,scale=t.localScale,rotation=t.localRotation}).ToArray();
  var bindings=rig.GetComponentsInChildren<Renderer>(true).Select(r=>new MaterialBinding{renderer=r,materials=r.sharedMaterials,enabled=r.enabled}).ToArray();
  var originalPanelMaterials=bindings.SelectMany(x=>x.materials).Where(m=>m!=null&&m.shader.name=="Between Us/Composite/Frosted Vortex Glass").Distinct().ToArray();
  if(originalPanelMaterials.Length==0)throw new InvalidOperationException("No dialogue glass found.");
  var backdrop=rig.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Frame 10 - author camera backdrop");bool oldBackdrop=backdrop!=null&&backdrop.activeSelf;
  var hero=rig.transform.Find("01 - response - curved hero");if(hero==null)throw new InvalidOperationException("Main response panel not found.");
  var cameraObject=new GameObject("Temporary colour study export camera");SceneManager.MoveGameObjectToScene(cameraObject,rig.gameObject.scene);var camera=cameraObject.AddComponent<Camera>();
  camera.CopyFrom(originalCamera);camera.transform.SetPositionAndRotation(originalCamera.transform.position,originalCamera.transform.rotation);camera.enabled=false;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<29;camera.allowHDR=false;camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();rig.captureCamera=camera;
  int files=0;bool errors=false;var presets=Presets();
  try
  {
   foreach(var palette in presets)
   {
    string variantFolder=AssetFolder+"/"+palette.id;Folder(variantFolder);var map=new Dictionary<Material,Material>();
    foreach(var original in originalPanelMaterials)
    {
     string path=variantFolder+"/"+original.name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
     if(material==null){material=new Material(original){name=palette.title+" - "+original.name};AssetDatabase.CreateAsset(material,path);}else material.CopyPropertiesFromMaterial(original);
     material.SetColor("_FrostColor",palette.frost);material.SetColor("_TintA",palette.a);material.SetColor("_TintB",palette.b);material.SetColor("_RimColor",palette.rim);material.SetFloat("_TintStrength",palette.tint);material.SetFloat("_Opacity",.64f);material.SetFloat("_FrostGrain",.0025f);material.SetFloat("_RimStrength",.77f);material.SetFloat("_FlowIntensity",1.05f);EditorUtility.SetDirty(material);map.Add(original,material);
    }
    foreach(var binding in bindings)binding.renderer.sharedMaterials=binding.materials.Select(m=>m!=null&&map.ContainsKey(m)?map[m]:m).ToArray();
    rig.glassMaterials=oldGlass.Select(m=>m!=null&&map.ContainsKey(m)?map[m]:m).ToArray();
    errors|=map.Values.Any(m=>ShaderUtil.ShaderHasError(m.shader));if(errors)throw new InvalidOperationException("Colour study shader compilation failed.");
    string prefix="ColourStudies/"+palette.id+"/";Directory.CreateDirectory(Path.Combine(output,palette.id));
    // Identical approved perspective, dialogue and deterministic animation time.
    camera.CopyFrom(originalCamera);camera.transform.SetPositionAndRotation(originalCamera.transform.position,originalCamera.transform.rotation);camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.allowHDR=false;camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();
    if(backdrop!=null)backdrop.SetActive(true);
    BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,prefix+"Scene-Frame10-4K.png");files++;
    if(backdrop!=null)backdrop.SetActive(false);foreach(var material in map.Values)material.SetFloat("_BackdropStrength",0);
    BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,prefix+"Scene-Black-4K.png");files++;
    BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,prefix+"Scene-Transparent-4K.png");files++;
    // A standalone, genuinely curved dialogue panel for a reference-style layout.
    foreach(var binding in bindings)binding.renderer.enabled=binding.enabled&&binding.renderer.transform.IsChildOf(hero);
    var mesh=hero.GetComponent<MeshFilter>().sharedMesh;var size=mesh.bounds.size;var scale=hero.lossyScale;float aspect=1.6f,fov=42f;float tangent=Mathf.Tan(fov*.5f*Mathf.Deg2Rad);
    float distance=Mathf.Max(size.x*Mathf.Abs(scale.x)/(2*tangent*aspect),size.y*Mathf.Abs(scale.y)/(2*tangent))*1.13f+size.z*Mathf.Abs(scale.z)*.5f;
    Vector3 centre=hero.TransformPoint(mesh.bounds.center);camera.fieldOfView=fov;camera.orthographic=false;camera.nearClipPlane=.03f;camera.farClipPlane=2000;camera.transform.SetPositionAndRotation(centre-hero.forward*distance,hero.rotation);camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();
    BetweenUsCompositeStudio.Capture(rig,2400,1500,3.2f,true,prefix+"Dialogue-Transparent.png");files++;
    foreach(var binding in bindings)binding.renderer.enabled=binding.enabled;
    foreach(var pair in map){pair.Value.SetFloat("_BackdropStrength",pair.Key.GetFloat("_BackdropStrength"));EditorUtility.SetDirty(pair.Value);}
   }
  }
  finally
  {
   foreach(var binding in bindings){if(binding.renderer==null)continue;binding.renderer.sharedMaterials=binding.materials;binding.renderer.enabled=binding.enabled;}
   if(backdrop!=null)backdrop.SetActive(oldBackdrop);rig.glassMaterials=oldGlass;rig.captureCamera=originalCamera;rig.SetPreviewTime(oldTime);rig.RefreshAtlas();UnityEngine.Object.DestroyImmediate(cameraObject);
  }
  int changed=poses.Count(p=>(p.node.localPosition-p.position).sqrMagnitude>.0000001f||(p.node.localScale-p.scale).sqrMagnitude>.0000001f||Quaternion.Angle(p.node.localRotation,p.rotation)>.001f);
  bool restored=bindings.All(b=>b.renderer.sharedMaterials.SequenceEqual(b.materials)&&b.renderer.enabled==b.enabled);
  if(changed!=0||!restored)throw new InvalidOperationException("The approved scene was not fully restored.");
  AssetDatabase.SaveAssets();
  File.WriteAllText(Path.Combine(output,"RenderValidation.json"),JsonUtility.ToJson(new Report{camera=BetweenUsAuthorCameraInspect.PathOf(originalCamera.transform),folder=output,variants=presets.Length,files=files,existingTransforms=poses.Length,changedTransforms=changed,originalMaterialsRestored=restored,originalBackdropRestored=backdrop==null||backdrop.activeSelf==oldBackdrop,shaderErrors=errors,palettes=presets},true));
  string stale=Path.Combine(output,"error.txt");if(File.Exists(stale))File.Delete(stale);Debug.Log("BETWEEN_US_FIVE_COLOUR_STUDIES_OK "+output);
 }
}
[InitializeOnLoad]static class BetweenUsColourStudyRequest
{
 static double next;static BetweenUsColourStudyRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-colour-studies.request");if(!File.Exists(request))return;File.Delete(request);try{BetweenUsColourStudyRender.Render();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"ColourStudies");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}
