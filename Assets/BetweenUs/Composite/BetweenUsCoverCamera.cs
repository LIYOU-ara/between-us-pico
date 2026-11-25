using System.Collections.Generic;
using UnityEngine;

// A separate offline first-person product camera. Visibility and plate overrides
// exist only for this camera's render; the approved external view is untouched.
[ExecuteAlways, RequireComponent(typeof(Camera))]
public sealed class BetweenUsCoverCamera : MonoBehaviour
{
 public Renderer[] hideForView, glassPanels;
 public Texture2D sourcePlate;
 public bool useSourcePlate=true, fitToFrame=true;
 public Rect targetFrame=new Rect(.045f,.07f,.91f,.86f);
 public float baseFieldOfView=76;
 public Vector2 manualShift;
 public float fittedScale=1;
 readonly Dictionary<Renderer,bool> enabledStates=new Dictionary<Renderer,bool>();
 readonly Dictionary<Renderer,MaterialPropertyBlock> savedBlocks=new Dictionary<Renderer,MaterialPropertyBlock>();
 public void Reframe()
 {
  var c=GetComponent<Camera>();c.ResetProjectionMatrix();c.fieldOfView=baseFieldOfView;
  if(!fitToFrame){var shifted=Matrix4x4.Perspective(baseFieldOfView,c.aspect,c.nearClipPlane,c.farClipPlane);shifted.m02=-manualShift.x*2;shifted.m12=-manualShift.y*2;c.projectionMatrix=shifted;return;}if(glassPanels==null||glassPanels.Length==0)return;
  var matrix=Matrix4x4.Perspective(baseFieldOfView,c.aspect,c.nearClipPlane,c.farClipPlane);c.projectionMatrix=matrix;
  Vector2 lo=new Vector2(float.PositiveInfinity,float.PositiveInfinity),hi=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
  foreach(var r in glassPanels)
  {
   if(r==null)continue;var b=r.bounds;
   for(int k=0;k<8;k++){Vector3 p=b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1));var v=c.WorldToViewportPoint(p);if(v.z<=c.nearClipPlane)continue;lo=Vector2.Min(lo,new Vector2(v.x,v.y));hi=Vector2.Max(hi,new Vector2(v.x,v.y));}
  }
  if(float.IsInfinity(lo.x))return;Vector2 size=hi-lo,centre=(lo+hi)*.5f;
  fittedScale=Mathf.Min(targetFrame.width/Mathf.Max(size.x,.001f),targetFrame.height/Mathf.Max(size.y,.001f));
  matrix.m00*=fittedScale;matrix.m11*=fittedScale;matrix.m02=fittedScale*(centre.x*2-1)-(targetFrame.center.x*2-1);matrix.m12=fittedScale*(centre.y*2-1)-(targetFrame.center.y*2-1);c.projectionMatrix=matrix;
 }
 void OnPreCull()
 {
  Restore();Reframe();
  if(hideForView!=null)foreach(var r in hideForView){if(r==null||enabledStates.ContainsKey(r))continue;enabledStates.Add(r,r.enabled);r.enabled=false;}
  if(useSourcePlate&&sourcePlate!=null&&glassPanels!=null)foreach(var r in glassPanels)
  {
   if(r==null||savedBlocks.ContainsKey(r))continue;var saved=new MaterialPropertyBlock();r.GetPropertyBlock(saved);savedBlocks.Add(r,saved);var current=new MaterialPropertyBlock();r.GetPropertyBlock(current);current.SetTexture("_BackdropTex",sourcePlate);current.SetFloat("_BackdropScreenSpace",1);current.SetFloat("_BlurRadius",22);r.SetPropertyBlock(current);
  }
 }
 void OnPostRender(){Restore();}
 void OnDisable(){Restore();}
 public void Restore(){foreach(var p in enabledStates)if(p.Key!=null)p.Key.enabled=p.Value;enabledStates.Clear();foreach(var p in savedBlocks)if(p.Key!=null)p.Key.SetPropertyBlock(p.Value);savedBlocks.Clear();}
}

