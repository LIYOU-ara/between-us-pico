using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BetweenUsCompositeReadability
{
 public static void Apply(BetweenUsCompositeRig rig)
 {
  var quote=rig.transform.Find("04 - partner quote - upper arc");var controls=rig.transform.Find("05 - session controls - lower arc");
  if(quote==null||controls==null)throw new InvalidOperationException("Quote/controls panels missing.");
  Vector3 quotePosition=quote.localPosition,quoteScale=quote.localScale;Quaternion quoteRotation=quote.localRotation;
  var filter=quote.GetComponent<MeshFilter>();var original=AssetDatabase.LoadAssetAtPath<Mesh>(BetweenUsCompositeStudio.AssetRoot+"/Meshes/04 curved panel.asset");
  if(original==null)throw new InvalidOperationException("Original curved quote mesh missing.");
  var material=quote.GetComponent<Renderer>().sharedMaterial;if(!material.HasProperty("_TailEnabled"))throw new InvalidOperationException("Tail shader is not imported.");
  float height=original.bounds.size.y,width=material.GetFloat("_Aspect")*height,radius=7.5f;
  const int segments=128;float[] rows={-.42f,0f,1f};var vertices=new Vector3[(segments+1)*3];var uv=new Vector2[vertices.Length];var triangles=new int[segments*12];
  for(int row=0;row<3;row++)for(int col=0;col<=segments;col++)
  {
   float u=col/(float)segments,x=(u-.5f)*width;int index=row*(segments+1)+col;
   vertices[index]=new Vector3(radius*Mathf.Sin(x/radius),(rows[row]-.5f)*height,-radius*(1-Mathf.Cos(x/radius)));uv[index]=new Vector2(u,rows[row]);
  }
  for(int row=0;row<2;row++)for(int col=0;col<segments;col++)
  {
   int a=row*(segments+1)+col,b=a+segments+1,t=(row*segments+col)*6;triangles[t]=a;triangles[t+1]=b;triangles[t+2]=a+1;triangles[t+3]=a+1;triangles[t+4]=b;triangles[t+5]=b+1;
  }
  string meshPath=BetweenUsCompositeStudio.AssetRoot+"/Meshes/04 curved speech bubble with tail.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
  if(mesh==null){mesh=new Mesh{name="Curved quote - seamless speech bubble tail"};AssetDatabase.CreateAsset(mesh,meshPath);}else mesh.Clear();
  mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();filter.sharedMesh=mesh;EditorUtility.SetDirty(mesh);
  material.SetFloat("_TailEnabled",1);material.SetFloat("_TailAnchor",.66f);material.SetFloat("_TailLength",.34f);material.SetFloat("_TailWidth",.50f);material.SetFloat("_TailLean",-.16f);EditorUtility.SetDirty(material);
  // Three readable, full-word labels replace the cramped sentence in the lower arc.
  var previous=controls.Find("Session controls");if(previous!=null)previous.gameObject.SetActive(false);
  string[] names={"01 - Live conversation","02 - Private guidance","03 - Pause"};string[] words={"LIVE\nCONVERSATION","PRIVATE\nGUIDANCE","PAUSE"};float[] xs={-1.95f,0f,1.95f};var texts=new List<TextMesh>();float commonScale=float.MaxValue;
  for(int i=0;i<3;i++)
  {
   var child=controls.Find(names[i]);if(child==null){var go=new GameObject(names[i]);go.layer=controls.gameObject.layer;go.transform.SetParent(controls,false);child=go.transform;go.AddComponent<TextMesh>();}
   child.localPosition=new Vector3(xs[i],0,-.028f);child.localRotation=Quaternion.identity;child.localScale=Vector3.one;
   var text=child.GetComponent<TextMesh>();text.font=rig.mediumFont!=null?rig.mediumFont:rig.regularFont;text.fontSize=96;text.characterSize=1;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.lineSpacing=1.02f;text.color=Color.white;text.text=words[i];
   text.font.RequestCharactersInTexture(text.text,96,FontStyle.Normal);var renderer=text.GetComponent<Renderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
   rig.BindCurvedText(text,controls,7.5f);var size=renderer.localBounds.size;if(i<2)commonScale=Mathf.Min(commonScale,Mathf.Min(.38f/Mathf.Max(size.y,.001f),1.78f/Mathf.Max(size.x,.001f)));texts.Add(text);
  }
  foreach(var text in texts)text.transform.localScale=Vector3.one*commonScale;
  rig.captions=rig.captions.Where(t=>t!=null).Concat(texts).Distinct().ToArray();rig.RefreshAtlas();
  foreach(var text in texts){var m=text.GetComponent<Renderer>().sharedMaterial;if(m.HasProperty("_ZTest"))m.SetFloat("_ZTest",8);}
  if(quote.localPosition!=quotePosition||quote.localScale!=quoteScale||Quaternion.Angle(quote.localRotation,quoteRotation)>.001f)throw new InvalidOperationException("The user's new quote pose changed.");
  EditorUtility.SetDirty(rig);EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(rig.gameObject.scene);
 }
}
