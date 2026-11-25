using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Dedicated offline export: keep the author's real camera and all existing poses.
public static class BetweenUsBackgroundFinalRender
{
 const string BackgroundAsset="Assets/BetweenUs/Composite/Frame10_Background.png";
 const string BackdropName="Frame 10 - author camera backdrop";
 [Serializable] class Pose {public Transform transform;public Vector3 position,scale;public Quaternion rotation;}
 [Serializable] class Report {public string camera,scene,background,view;public Vector3 cameraPosition,cameraEuler;public float fieldOfView,aspect,ringSoftness,ringGlowWidth;public int width,height,existingTransforms,changedTransforms,rings;public bool shaderErrors;}
 static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"BackgroundFinal");
 [MenuItem("Prototype/Between Us/Render Frame 10 with author's camera")]
 public static void Render()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Render outside Play Mode.");
  Directory.CreateDirectory(Output);ShaderUtil.allowAsyncCompilation=false;
  var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded);
  if(rig==null)throw new InvalidOperationException("The finished desktop scene must be loaded.");
  var sceneView=SceneView.lastActiveSceneView;
  if(sceneView==null)throw new InvalidOperationException("No current Scene View is available.");
  var cameraTransform=rig.transform.Find("User Scene View - Frame10 render camera");
  if(cameraTransform==null){var go=new GameObject("User Scene View - Frame10 render camera");go.transform.SetParent(rig.transform,false);cameraTransform=go.transform;go.AddComponent<Camera>();}
  var source=cameraTransform.GetComponent<Camera>();source.CopyFrom(rig.captureCamera);source.enabled=false;
  float fov=sceneView.cameraSettings.fieldOfView;
  source.orthographic=sceneView.orthographic;source.fieldOfView=fov;source.orthographicSize=sceneView.size;
  float orbitDistance=sceneView.orthographic?sceneView.size*2f:sceneView.size/Mathf.Sin(fov*.5f*Mathf.Deg2Rad);
  source.transform.SetPositionAndRotation(sceneView.pivot-sceneView.rotation*Vector3.forward*orbitDistance,sceneView.rotation);
  source.nearClipPlane=.03f;source.farClipPlane=2000;source.ResetWorldToCameraMatrix();source.ResetProjectionMatrix();source.cullingMask=1<<29;source.stereoTargetEye=StereoTargetEyeMask.None;
  var poses=rig.GetComponentsInChildren<Transform>(true).Select(t=>new Pose{transform=t,position=t.localPosition,scale=t.localScale,rotation=t.localRotation}).ToArray();
  var importer=(TextureImporter)AssetImporter.GetAtPath(BackgroundAsset);
  if(importer==null)throw new InvalidOperationException("Background has not been imported.");
  importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
  var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundAsset);
  source.aspect=16f/9f;
  var backdrop=rig.gameObject.scene.GetRootGameObjects().FirstOrDefault(g=>g.name==BackdropName);
  if(backdrop==null){backdrop=new GameObject(BackdropName);SceneManager.MoveGameObjectToScene(backdrop,rig.gameObject.scene);}
  backdrop.layer=29;
  var filter=backdrop.GetComponent<MeshFilter>();if(filter==null)filter=backdrop.AddComponent<MeshFilter>();var renderer=backdrop.GetComponent<MeshRenderer>();if(renderer==null)renderer=backdrop.AddComponent<MeshRenderer>();
  float maxDepth=rig.GetComponentsInChildren<Renderer>().Select(r=>source.transform.InverseTransformPoint(r.bounds.center).z+r.bounds.extents.magnitude).DefaultIfEmpty(20f).Max();
  float distance=Mathf.Min(source.farClipPlane*.90f,Mathf.Max(30f,maxDepth+10f));
  string meshPath=BetweenUsCompositeStudio.AssetRoot+"/Frame10_ViewAlignedBackdrop.asset";
  var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(mesh==null){mesh=new Mesh{name="Frame 10 viewport-aligned backdrop"};AssetDatabase.CreateAsset(mesh,meshPath);}
  mesh.Clear();mesh.vertices=new[]{source.ViewportToWorldPoint(new Vector3(0,0,distance)),source.ViewportToWorldPoint(new Vector3(0,1,distance)),source.ViewportToWorldPoint(new Vector3(1,1,distance)),source.ViewportToWorldPoint(new Vector3(1,0,distance))};
  mesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();mesh.RecalculateNormals();filter.sharedMesh=mesh;
  string materialPath=BetweenUsCompositeStudio.AssetRoot+"/Materials/Frame10 - exact background.mat";
  var backdropMaterial=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(backdropMaterial==null){backdropMaterial=new Material(Shader.Find("Unlit/Texture")){name="Frame10 - exact background"};AssetDatabase.CreateAsset(backdropMaterial,materialPath);}backdropMaterial.mainTexture=texture;renderer.sharedMaterial=backdropMaterial;
  renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
  foreach(var material in rig.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.HasProperty("_BackdropTex")).Distinct())
  {material.SetTexture("_BackdropTex",texture);material.SetFloat("_BackdropScreenSpace",1);material.SetFloat("_BackdropStrength",.50f);material.SetFloat("_BlurRadius",22);EditorUtility.SetDirty(material);}
  var rings=rig.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Luminous floor ring").ToArray();
  foreach(var material in rings.Select(r=>r.sharedMaterial).Distinct())
  {material.SetFloat("_Softness",1);material.SetFloat("_CoreStrength",.18f);material.SetFloat("_GlowStrength",.26f);material.SetFloat("_GlowWidth",.16f);material.SetFloat("_Opacity",.42f);material.SetFloat("_DiscStrength",.003f);material.SetColor("_Color",new Color(1f,.18f,.25f,1));material.SetColor("_RimColor",new Color(1f,.40f,.43f,1));EditorUtility.SetDirty(material);}
  rig.SetPreviewTime(3.2f);rig.RefreshAtlas();
  var materials=rig.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().ToArray();
  bool shaderErrors=materials.Any(m=>ShaderUtil.ShaderHasError(m.shader));if(shaderErrors)throw new InvalidOperationException("A composition shader has a compile error.");
  Capture(rig,source,1920,1080,"BetweenUs-Frame10-Preview.png");
  Capture(rig,source,3840,2160,"BetweenUs-Frame10-4K.png");
  int changed=poses.Count(p=>(p.transform.localPosition-p.position).sqrMagnitude>.0000001f||(p.transform.localScale-p.scale).sqrMagnitude>.0000001f||Quaternion.Angle(p.transform.localRotation,p.rotation)>.001f);
  if(changed!=0)throw new InvalidOperationException("An existing transform changed: "+changed);
  rig.captureCamera=source;EditorUtility.SetDirty(rig);EditorUtility.SetDirty(mesh);EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(rig.gameObject.scene);
  File.WriteAllText(Path.Combine(Output,"RenderValidation.json"),JsonUtility.ToJson(new Report{view="Exact current Scene View orbit; no auto framing or backoff",camera=BetweenUsAuthorCameraInspect.PathOf(source.transform),scene=rig.gameObject.scene.path,background=BackgroundAsset,cameraPosition=source.transform.position,cameraEuler=source.transform.eulerAngles,fieldOfView=source.fieldOfView,aspect=source.aspect,width=3840,height=2160,existingTransforms=poses.Length,changedTransforms=changed,rings=rings.Length,ringSoftness=1,ringGlowWidth=.16f,shaderErrors=shaderErrors},true));
  string error=Path.Combine(Output,"error.txt");if(File.Exists(error))File.Delete(error);Debug.Log("BETWEEN_US_FRAME10_RENDER_OK "+Output);
 }
 static void Capture(BetweenUsCompositeRig rig,Camera camera,int width,int height,string filename)
 {
  var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=4};var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;var oldFlags=camera.clearFlags;var oldColor=camera.backgroundColor;var oldStereo=camera.stereoTargetEye;bool oldHDR=camera.allowHDR;Texture2D pixels=null;
  try{target.Create();rig.SetPreviewTime(3.2f);rig.RefreshAtlas();camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.stereoTargetEye=StereoTargetEyeMask.None;camera.allowHDR=false;camera.Render();RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(Output,filename),pixels.EncodeToPNG());}
  finally{camera.targetTexture=oldTarget;camera.clearFlags=oldFlags;camera.backgroundColor=oldColor;camera.stereoTargetEye=oldStereo;camera.allowHDR=oldHDR;RenderTexture.active=oldActive;if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);target.Release();UnityEngine.Object.DestroyImmediate(target);}
 }
}
[InitializeOnLoad] static class BetweenUsBackgroundRenderRequest
{
 static double next;static BetweenUsBackgroundRenderRequest(){EditorApplication.update+=Tick;}
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-background-render.request");if(!File.Exists(request))return;File.Delete(request);try{BetweenUsBackgroundFinalRender.Render();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"BackgroundFinal");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}}
}
