using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class BetweenUsBrighterRingRender
{
 [Serializable]class Pose{public Transform node;public Vector3 position,scale;public Quaternion rotation;}
 [Serializable]class Binding{public Renderer renderer;public Material[] materials;}
 [Serializable]class Report{public string camera;public Vector3 cameraPosition,cameraEuler;public int rings,changedTransforms;public float originalOpacity,variantOpacity,contributionMultiplier;public bool materialsRestored,shaderErrors;}
 [MenuItem("Prototype/Between Us/Render brighter red floor rings")]
 public static void Render()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Render outside Play Mode.");
  ShaderUtil.allowAsyncCompilation=false;
  var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(x=>x.gameObject.scene.IsValid()&&x.gameObject.scene.isLoaded);
  if(rig==null)throw new InvalidOperationException("Open the approved desktop scene.");
  BetweenUsCompositeReadability.Apply(rig);
  var source=rig.transform.Find("User Scene View - Frame10 render camera");if(source==null)throw new InvalidOperationException("The approved camera was not found.");
  var camera=source.GetComponent<Camera>();var oldCamera=rig.captureCamera;var oldGlass=rig.glassMaterials;float oldTime=rig.previewTime;
  var poses=rig.GetComponentsInChildren<Transform>(true).Select(t=>new Pose{node=t,position=t.localPosition,scale=t.localScale,rotation=t.localRotation}).ToArray();
  var rings=rig.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Luminous floor ring").ToArray();if(rings.Length!=2)throw new InvalidOperationException("Expected two approved floor rings.");
  var bindings=rings.Select(r=>new Binding{renderer=r,materials=r.sharedMaterials}).ToArray();var map=new Dictionary<Material,Material>();
  const string folder="Assets/BetweenUs/Composite/Materials";float originalOpacity=0;
  foreach(var original in bindings.SelectMany(b=>b.materials).Distinct())
  {
   originalOpacity=original.GetFloat("_Opacity");string path=folder+"/Floor ring - brighter diffuse red variant.mat";
   var variant=AssetDatabase.LoadAssetAtPath<Material>(path);if(variant==null){variant=new Material(original){name="Floor ring - brighter diffuse red variant"};AssetDatabase.CreateAsset(variant,path);}else variant.CopyPropertiesFromMaterial(original);
   variant.SetFloat("_Opacity",.80f);EditorUtility.SetDirty(variant);map.Add(original,variant);
  }
  var backdrop=rig.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Frame 10 - author camera backdrop");if(backdrop==null)throw new InvalidOperationException("Frame 10 background missing.");bool oldBackdrop=backdrop.activeSelf;
  string output=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"BackgroundFinal","BrighterRing");Directory.CreateDirectory(output);bool shaderErrors=map.Values.Any(m=>ShaderUtil.ShaderHasError(m.shader));if(shaderErrors)throw new InvalidOperationException("Ring shader compilation failed.");
  var oldFlags=camera.clearFlags;
  try
  {
   foreach(var binding in bindings)binding.renderer.sharedMaterials=binding.materials.Select(m=>map[m]).ToArray();
   rig.glassMaterials=oldGlass.Select(m=>m!=null&&map.ContainsKey(m)?map[m]:m).ToArray();rig.captureCamera=camera;camera.clearFlags=CameraClearFlags.SolidColor;backdrop.SetActive(true);
   BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"BackgroundFinal/BrighterRing/BetweenUs-Frame10-BrighterRing-Preview.png");
   BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,"BackgroundFinal/BrighterRing/BetweenUs-Frame10-BrighterRing-4K.png");
  }
  finally
  {
   foreach(var binding in bindings)binding.renderer.sharedMaterials=binding.materials;rig.glassMaterials=oldGlass;rig.captureCamera=oldCamera;camera.clearFlags=oldFlags;backdrop.SetActive(oldBackdrop);rig.SetPreviewTime(oldTime);rig.RefreshAtlas();
  }
  int changed=poses.Count(p=>(p.node.localPosition-p.position).sqrMagnitude>.0000001f||(p.node.localScale-p.scale).sqrMagnitude>.0000001f||Quaternion.Angle(p.node.localRotation,p.rotation)>.001f);
  bool restored=bindings.All(b=>b.renderer.sharedMaterials.SequenceEqual(b.materials));if(changed!=0||!restored)throw new InvalidOperationException("The original layout/materials were not restored.");AssetDatabase.SaveAssets();
  File.WriteAllText(Path.Combine(output,"RenderValidation.json"),JsonUtility.ToJson(new Report{camera=BetweenUsAuthorCameraInspect.PathOf(camera.transform),cameraPosition=camera.transform.position,cameraEuler=camera.transform.eulerAngles,rings=rings.Length,changedTransforms=changed,originalOpacity=originalOpacity,variantOpacity=.80f,contributionMultiplier=.80f/originalOpacity,materialsRestored=restored,shaderErrors=shaderErrors},true));
  string error=Path.Combine(output,"error.txt");if(File.Exists(error))File.Delete(error);Debug.Log("BETWEEN_US_BRIGHTER_RING_RENDER_OK "+output);
 }
}
[InitializeOnLoad]static class BetweenUsBrighterRingRequest
{
 static double next;static BetweenUsBrighterRingRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-brighter-ring.request");if(!File.Exists(request))return;File.Delete(request);try{BetweenUsBrighterRingRender.Render();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"BackgroundFinal","BrighterRing");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}
