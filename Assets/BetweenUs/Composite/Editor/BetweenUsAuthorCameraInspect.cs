using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class BetweenUsAuthorCameraInspect
{
 [Serializable] public class Entry { public string name,path,scene; public Vector3 position,euler; public float fov,aspect,near,far,depth; public bool enabled,orthographic; public int mask; }
 [Serializable] public class Inventory { public string selected; public Entry[] cameras; }
 static double next;
 static BetweenUsAuthorCameraInspect(){EditorApplication.update+=Tick;}
 public static string PathOf(Transform t)=>t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
 static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+1;
 string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-camera-inspect.request");if(!File.Exists(request))return;File.Delete(request);
 var cameras=Resources.FindObjectsOfTypeAll<Camera>().Where(c=>c.gameObject.scene.IsValid()&&c.gameObject.scene.isLoaded&&c.hideFlags==HideFlags.None).Select(c=>new Entry{name=c.name,path=PathOf(c.transform),scene=c.gameObject.scene.path,position=c.transform.position,euler=c.transform.eulerAngles,fov=c.fieldOfView,aspect=c.aspect,near=c.nearClipPlane,far=c.farClipPlane,depth=c.depth,enabled=c.enabled,orthographic=c.orthographic,mask=c.cullingMask}).ToArray();
 string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"BackgroundFinal");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"CameraInventory.json"),JsonUtility.ToJson(new Inventory{selected=Selection.activeGameObject!=null?PathOf(Selection.activeGameObject.transform):"none",cameras=cameras},true));
 }
}
