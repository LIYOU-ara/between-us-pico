using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Render the user's finished composition; never rebuild or reposition its contents.
public static class BetweenUsCurrentSceneRender
{
    [Serializable] sealed class Pose { public string path; public Vector3 position,scale; public Quaternion rotation; }
    [Serializable] sealed class Report { public string view; public int existingTransforms,changedTransforms,unexpectedTransformChanges,resizedQuoteCount,seatedPeople,rings; public float wearerOpacity,ringOpacity; public Pose[] poses; }
    static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"CurrentFinal");
    static string NodePath(Transform t,Transform root)=>t==root?".":(t.parent==root?t.name:NodePath(t.parent,root)+"/"+t.name);
    static Material CopyMaterial(Renderer renderer,string name)
    {
        string path=BetweenUsCompositeStudio.AssetRoot+"/Materials/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(renderer.sharedMaterial){name=name};AssetDatabase.CreateAsset(material,path);}
        renderer.sharedMaterial=material;return material;
    }
    [MenuItem("Prototype/Between Us/Render current finished layout on black")]
    public static void Render()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Render outside Play Mode.");
        ShaderUtil.allowAsyncCompilation=false;
        var rig=Resources.FindObjectsOfTypeAll<BetweenUsCompositeRig>().FirstOrDefault(r=>r.gameObject.scene.IsValid()&&r.gameObject.scene.isLoaded);
        if(rig==null)throw new InvalidOperationException("Open the finished compositing scene first.");
        Directory.CreateDirectory(Output);
        var transforms=rig.GetComponentsInChildren<Transform>(true);
        var poses=transforms.Select(t=>new Pose{path=NodePath(t,rig.transform),position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();
        var sceneView=SceneView.lastActiveSceneView;
        var sourceCamera=sceneView!=null&&sceneView.camera!=null?sceneView.camera:rig.captureCamera;
        var people=rig.transform.Find("Two seated participants - ghost study");
        if(people==null)throw new InvalidOperationException("No seated participants found.");
        Transform foreground=null;float nearest=float.MaxValue;
        foreach(Transform actor in people)
        {
            if(!actor.GetComponentsInChildren<Renderer>().Any(x=>x.name=="VR headset"))continue;
            float distance=(actor.position-sourceCamera.transform.position).sqrMagnitude;
            if(distance<nearest){nearest=distance;foreground=actor;}
        }
        if(foreground==null)throw new InvalidOperationException("No participant with a headset was found.");
        var body=foreground.Find("Seated body");
        var bodyMaterial=CopyMaterial(body.GetComponent<Renderer>(),"Foreground wearer - translucent red contour");
        bodyMaterial.SetFloat("_Opacity",.65f);bodyMaterial.SetColor("_RimColor",new Color(1,.08f,.13f,1));bodyMaterial.SetFloat("_RimStrength",1.10f);bodyMaterial.SetFloat("_RimPower",2.8f);bodyMaterial.SetFloat("_AccentStrength",.025f);
        var helmet=foreground.GetComponentsInChildren<Renderer>().FirstOrDefault(x=>x.name=="VR headset");
        if(helmet!=null)
        {
            var material=CopyMaterial(helmet,"Foreground headset - soft red contour");material.SetFloat("_Opacity",.78f);material.SetColor("_RimColor",new Color(1,.12f,.18f,1));material.SetFloat("_RimStrength",.72f);material.SetFloat("_RimPower",3.2f);
        }
        var ringRenderers=people.GetComponentsInChildren<Renderer>().Where(x=>x.name=="Luminous floor ring").ToArray();
        foreach(var renderer in ringRenderers)
        {
            var m=renderer.sharedMaterial;m.SetFloat("_Opacity",.44f);m.SetFloat("_Width",.013f);m.SetFloat("_GlowWidth",.055f);m.SetFloat("_DiscStrength",.004f);m.SetColor("_Color",new Color(.82f,.86f,.92f,1));
        }
        if(rig.captions!=null)foreach(var caption in rig.captions)
        {
            var renderer=caption!=null?caption.GetComponent<Renderer>():null;
            if(renderer!=null&&renderer.sharedMaterial.HasProperty("_ZTest"))renderer.sharedMaterial.SetFloat("_ZTest",8);
        }
        // Resize only the upper spoken-words caption, with a stable size each export.
        var quote=rig.captions!=null?rig.captions.FirstOrDefault(t=>t!=null&&t.name=="Partner words"):null;
        Transform requestedTextTransform=null;
        if(quote!=null)
        {
            quote.font.RequestCharactersInTexture(quote.text,quote.fontSize,FontStyle.Normal);
            var bounds=quote.GetComponent<Renderer>().localBounds.size;
            var panel=quote.transform.parent.GetComponent<MeshFilter>();
            float panelWidth=panel!=null?panel.sharedMesh.bounds.size.x:6.7f;
            float panelHeight=panel!=null?panel.sharedMesh.bounds.size.y:.82f;
            float scale=Mathf.Min(panelHeight*.36f/Mathf.Max(bounds.y,.001f),panelWidth*.90f/Mathf.Max(bounds.x,.001f));
            quote.transform.localScale=Vector3.one*scale;requestedTextTransform=quote.transform;
        }
        var cameraObject=new GameObject("Temporary black-plate render camera");
        SceneManager.MoveGameObjectToScene(cameraObject,rig.gameObject.scene);
        var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(rig.captureCamera);
        if(sceneView!=null)
        {
            // Scene View's render camera can hold a transient picking matrix after reload.
            // Reconstruct the user's orbit pose from its persistent pivot/rotation/size.
            float fov=sceneView.cameraSettings.fieldOfView;
            camera.orthographic=sceneView.orthographic;camera.fieldOfView=fov;camera.orthographicSize=sceneView.size;
            float distance=sceneView.orthographic?sceneView.size*2f:sceneView.size/Mathf.Sin(fov*.5f*Mathf.Deg2Rad);
            camera.transform.SetPositionAndRotation(sceneView.pivot-sceneView.rotation*Vector3.forward*distance,sceneView.rotation);
        }
        else camera.transform.SetPositionAndRotation(sourceCamera.transform.position,sourceCamera.transform.rotation);
        camera.nearClipPlane=.03f;camera.farClipPlane=2000;camera.ResetWorldToCameraMatrix();camera.ResetProjectionMatrix();
        camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.cullingMask=1<<29;
        camera.stereoTargetEye=StereoTargetEyeMask.None;camera.allowHDR=false;camera.allowMSAA=true;camera.aspect=16f/9f;
        // Keep the user's orbit direction while leaving a small clean black margin.
        // Only the temporary export camera moves; no scene transform is touched.
        if(!camera.orthographic)
        {
            float tangent=Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad),backoff=0;
            foreach(var renderer in rig.GetComponentsInChildren<Renderer>())
            {
                if(renderer.GetComponent<TextMesh>()!=null)continue;
                Bounds bounds=renderer.bounds;
                foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y})foreach(float z in new[]{bounds.min.z,bounds.max.z})
                {
                    Vector3 v=camera.transform.InverseTransformPoint(new Vector3(x,y,z));
                    backoff=Mathf.Max(backoff,Mathf.Abs(v.y)/(tangent*.88f)-v.z,Mathf.Abs(v.x)/(tangent*(16f/9f)*.90f)-v.z);
                }
            }
            if(backoff>0)camera.transform.position-=camera.transform.forward*(backoff+.1f);
        }
        var originalCamera=rig.captureCamera;
        try
        {
            rig.captureCamera=camera;
            BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,"CurrentFinal/BetweenUs-Current-Preview.png");
            BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,"CurrentFinal/BetweenUs-Current-Black-4K.png");
            BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,"CurrentFinal/BetweenUs-Current-Transparent-4K.png");
        }
        finally{rig.captureCamera=originalCamera;UnityEngine.Object.DestroyImmediate(cameraObject);}
        int changed=0,unexpected=0;
        for(int i=0;i<transforms.Length;i++)if((transforms[i].localPosition-poses[i].position).sqrMagnitude>.0000001f||(transforms[i].localScale-poses[i].scale).sqrMagnitude>.0000001f||Quaternion.Angle(transforms[i].localRotation,poses[i].rotation)>.001f)
        {
            changed++;
            bool requested=transforms[i]==requestedTextTransform&&(transforms[i].localPosition-poses[i].position).sqrMagnitude<.0000001f&&Quaternion.Angle(transforms[i].localRotation,poses[i].rotation)<.001f;
            if(!requested)unexpected++;
        }
        if(unexpected!=0)throw new InvalidOperationException("A finished-layout transform changed unexpectedly.");
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(rig.gameObject.scene,BetweenUsCompositeStudio.ScenePath);
        var report=new Report{view=sceneView!=null?"User's current Scene View camera":"Existing capture camera",existingTransforms=poses.Length,changedTransforms=changed,unexpectedTransformChanges=unexpected,resizedQuoteCount=requestedTextTransform!=null?1:0,seatedPeople=people.childCount,rings=ringRenderers.Length,wearerOpacity=.65f,ringOpacity=.44f,poses=poses};
        File.WriteAllText(Path.Combine(Output,"RenderValidation.json"),JsonUtility.ToJson(report,true));
        string error=Path.Combine(Output,"error.txt");if(File.Exists(error))File.Delete(error);
        Debug.Log("BETWEEN_US_CURRENT_BLACK_RENDER_OK "+Output);
    }
}
[InitializeOnLoad]static class BetweenUsCurrentRenderRequest
{
    static double next;static BetweenUsCurrentRenderRequest(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-render-current.request");if(!File.Exists(request))return;File.Delete(request);
        try{BetweenUsCurrentSceneRender.Render();}catch(Exception e){string folder=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"CurrentFinal");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),e.ToString());Debug.LogException(e);}
    }
}
