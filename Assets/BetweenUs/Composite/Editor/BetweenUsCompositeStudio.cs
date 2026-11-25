using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Independent desktop compositing scene. Never changes headset or build settings.
public sealed class BetweenUsCompositeStudio : EditorWindow
{
    public const string AssetRoot = "Assets/BetweenUs/Composite";
    public const string ScenePath = AssetRoot + "/BetweenUs_BlackRed_CurvedComposite.unity";
    const int Layer = 29;
    public static string CaptureRoot => Path.Combine(Path.GetDirectoryName(Application.dataPath), "CompositeCaptures");
    BetweenUsCompositeRig rig;
    RenderTexture preview;
    bool animate = true;
    float time = 3.2f;
    double lastRepaint;
    string status = "Pure black / perspective / curved geometry / no XR";

    [MenuItem("Prototype/Between Us/Black Red Curved Composite Studio")]
    public static void Open()
    {
        var w = GetWindow<BetweenUsCompositeStudio>("Between Us | Composite");
        w.minSize = new Vector2(880,600); w.rig = EnsureScene(); w.Show();
    }
    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; if (preview != null) { preview.Release(); DestroyImmediate(preview); } }
    void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup-lastRepaint < 1.0/24) return;
        lastRepaint = EditorApplication.timeSinceStartup; Repaint();
    }
    void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("BETWEEN US / PEARL GLASS COMPOSITE", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Desktop scene for post-production / no headset connection", EditorStyles.miniLabel);
        if (rig == null) { if(GUILayout.Button("Load compositing scene")) rig = EnsureScene(); return; }
        float width = Mathf.Max(320,position.width-20);
        var rect = GUILayoutUtility.GetRect(width, Mathf.Min(width*9f/16, Mathf.Max(260,position.height-190)));
        if (Event.current.type == EventType.Repaint)
        {
            if(preview==null) { preview = new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32); preview.antiAliasing=4; preview.Create(); }
            Render(rig,preview,animate ? (float)EditorApplication.timeSinceStartup : time,false);
            EditorGUI.DrawPreviewTexture(rect,preview,null,ScaleMode.ScaleToFit);
        }
        var palette=rig.GetComponent<BetweenUsCompositePalette>();
        if(palette!=null)
        {
            var chosen=(BetweenUsCompositePalette.Style)EditorGUILayout.EnumPopup("Glass palette",palette.style);
            if(chosen!=palette.style)palette.Apply(chosen);
        }
        using(new EditorGUILayout.HorizontalScope()) { animate=GUILayout.Toggle(animate,"Animate glass flow","Button",GUILayout.Width(145)); time=EditorGUILayout.Slider("Frame time",time,0,21); }
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("Export 4K black + transparent PNG")) { ExportStills(rig,time); status=CaptureRoot; }
            if(GUILayout.Button("Export 8s / 30fps PNG sequence")) { ExportSequence(); status=Path.Combine(CaptureRoot,"SequenceBlack"); }
            if(GUILayout.Button("Select rig",GUILayout.Width(90))) Selection.activeGameObject=rig.gameObject;
        }
        EditorGUILayout.LabelField(status,EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Straight-alpha PNG. The source-plate material slot can blur a supplied background plate.",EditorStyles.miniLabel);
    }
    static Material Mat(string name,string shaderName)
    {
        var shader=Shader.Find(shaderName);
        if(shader==null || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Invalid shader: "+shaderName);
        string path=AssetRoot+"/Materials/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(shader){name=name}; AssetDatabase.CreateAsset(material,path); }
        return material;
    }
    public static BetweenUsCompositeRig EnsureScene()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Open outside Play Mode.");
        Directory.CreateDirectory(AssetRoot+"/Materials"); Directory.CreateDirectory(AssetRoot+"/Meshes"); AssetDatabase.Refresh();
        // Roll back only our own unsaved scene left by an interrupted generator.
        for(int index=SceneManager.sceneCount-1;index>=0;index--)
        {
            var pending=SceneManager.GetSceneAt(index);
            if(!pending.isLoaded || !string.IsNullOrEmpty(pending.path)) continue;
            bool ours=false; foreach(var item in pending.GetRootGameObjects()) if(item.name=="BETWEEN US - desktop compositing rig" && item.GetComponent<BetweenUsCompositeRig>()!=null) ours=true;
            if(ours) EditorSceneManager.CloseScene(pending,true);
        }
        Scene scene=SceneManager.GetSceneByPath(ScenePath);
        if(scene.IsValid() && scene.isLoaded) foreach(var root in scene.GetRootGameObjects()) { var found=root.GetComponentInChildren<BetweenUsCompositeRig>(); if(found!=null) return found; }
        if(File.Exists(ScenePath))
        {
            scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            foreach(var root in scene.GetRootGameObjects()) { var found=root.GetComponentInChildren<BetweenUsCompositeRig>(); if(found!=null) return found; }
            throw new InvalidOperationException("Saved composite scene is missing its rig.");
        }
        Scene previous=SceneManager.GetActiveScene();
        scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
        try
        {
            var root=new GameObject("BETWEEN US - desktop compositing rig"); root.layer=Layer;
            var r=root.AddComponent<BetweenUsCompositeRig>();
            r.regularFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Regular.otf");
            r.mediumFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
            if(r.regularFont==null) r.regularFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if(r.mediumFont==null)r.mediumFont=r.regularFont;
            var regular=Mat("Composite text - Regular","Between Us/Composite/Curved Text"); var medium=Mat("Composite text - Medium","Between Us/Composite/Curved Text");
            var co=new GameObject("Desktop perspective camera - pure black - NO XR"); co.transform.SetParent(root.transform,false);
            co.transform.position=new Vector3(7.2f,4,-13.7f); co.transform.LookAt(new Vector3(0,1.7f,.2f));
            var camera=co.AddComponent<Camera>(); camera.orthographic=false; camera.fieldOfView=30; camera.aspect=16f/9;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; camera.cullingMask=1<<Layer;
            camera.nearClipPlane=.1f; camera.farClipPlane=100; camera.stereoTargetEye=StereoTargetEyeMask.None; camera.allowHDR=false; camera.allowMSAA=true; camera.enabled=false; r.captureCamera=camera;
            var materials=new List<Material>(); var labels=new List<TextMesh>();
            var hero=Panel(root.transform,"01 - response - curved hero",6.4f,3.25f,7.5f,new Vector3(0,1.55f,0),0,materials);
            var left=Panel(root.transform,"02 - visible cues - left wing",3,3.05f,5.8f,new Vector3(-4.48f,1.55f,.72f),-32,materials);
            var right=Panel(root.transform,"03 - interpretation - right wing",3,3.05f,5.8f,new Vector3(4.48f,1.55f,.72f),32,materials);
            var quote=Panel(root.transform,"04 - partner quote - upper arc",6.7f,.82f,7.5f,new Vector3(0,3.7f,-.05f),0,materials);
            var controls=Panel(root.transform,"05 - session controls - lower arc",6,.48f,7.5f,new Vector3(0,-.55f,-.05f),0,materials);
            Color white=new Color(.97f,.965f,.96f,1), muted=new Color(.65f,.65f,.69f,1), red=new Color(1,.30f,.34f,1);
            Text(r,hero,labels,medium,"YOUR TURN","YOUR TURN",-2.8f,1.30f,.28f,5.6f,white,7.5f,true);
            Rule(hero,"Header divider",5.6f,.93f,7.5f);
            Text(r,hero,labels,medium,"Try asking label","TRY ASKING",-2.8f,.70f,.16f,5.5f,red,7.5f,true);
            Text(r,hero,labels,regular,"Main suggested question","Which part of planning feels\ndisconnected from aesthetics?",-2.8f,.30f,.64f,5.5f,white,7.5f);
            Text(r,hero,labels,medium,"Needs label","EXPRESS YOUR NEEDS",-2.8f,-.66f,.16f,5.5f,red,7.5f,true);
            Text(r,hero,labels,regular,"Own needs response","My practice includes planning\nand concept development.",-2.8f,-1.02f,.39f,5.5f,white,7.5f);
            Text(r,left,labels,medium,"Visible cues title","VISIBLE CUES",-1.18f,1.18f,.22f,2.35f,white,5.8f,true);
            Rule(left,"Cue divider",2.35f,.82f,5.8f);
            Text(r,left,labels,regular,"Visible cues","Slight smile\nSteady eye contact",-1.18f,.52f,.48f,2.35f,white,5.8f);
            Text(r,left,labels,medium,"Observation marker","OBSERVE FIRST",-1.18f,-.38f,.14f,2.35f,red,5.8f,true);
            Text(r,left,labels,regular,"Cue uncertainty","A visible cue is not\na confirmed feeling.",-1.18f,-.73f,.34f,2.35f,muted,5.8f);
            Text(r,right,labels,medium,"Reading title","POSSIBLE READING",-1.18f,1.18f,.22f,2.35f,white,5.8f,true);
            Rule(right,"Reading divider",2.35f,.82f,5.8f);
            Text(r,right,labels,regular,"Reading content","They may be questioning\nwhat counts as art.",-1.18f,.52f,.43f,2.35f,white,5.8f);
            Text(r,right,labels,medium,"Hypothesis marker","A HYPOTHESIS TO CHECK",-1.18f,-.38f,.14f,2.35f,red,5.8f,true);
            Text(r,right,labels,regular,"Reading uncertainty","Personal context\nis still incomplete.",-1.18f,-.73f,.34f,2.35f,muted,5.8f);
            Text(r,quote,labels,regular,"Partner words","\"Are you really doing what an art student should do?\"",-3,.12f,.18f,6,white,7.5f);
            Text(r,controls,labels,regular,"Session controls","LIVE CONVERSATION        /        PRIVATE GUIDANCE        /        PAUSE",-2.65f,.09f,.11f,5.3f,white,7.5f);
            r.glassMaterials=materials.ToArray(); r.captions=labels.ToArray(); r.RefreshAtlas(); r.SetPreviewTime(3.2f);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene,ScenePath); return r;
        }
        finally { if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }
    static Transform Panel(Transform parent,string name,float width,float height,float radius,Vector3 position,float yaw,List<Material> materials)
    {
        var go=new GameObject(name); go.layer=Layer; go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localRotation=Quaternion.Euler(0,yaw,0);
        string meshPath=AssetRoot+"/Meshes/"+name.Substring(0,2)+" curved panel.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(mesh==null){mesh=BetweenUsCompositeRig.MakeCurvedMesh(width,height,radius,128);AssetDatabase.CreateAsset(mesh,meshPath);}
        go.AddComponent<MeshFilter>().sharedMesh=mesh; var renderer=go.AddComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        var material=Mat(name,"Between Us/Composite/Frosted Vortex Glass"); material.SetFloat("_Aspect",width/height); material.SetFloat("_Radius",height<1?.19f:.07f); material.SetFloat("_BackdropStrength",0);
        renderer.sharedMaterial=material; materials.Add(material); return go.transform;
    }
    static void Text(BetweenUsCompositeRig r,Transform panel,List<TextMesh> labels,Material mat,string name,string content,float x,float y,float height,float width,Color color,float radius,bool medium=false)
    {
        var go=new GameObject(name); go.layer=Layer; go.transform.SetParent(panel,false); go.transform.localPosition=new Vector3(x,y,-.025f);
        var t=go.AddComponent<TextMesh>(); t.font=medium?r.mediumFont:r.regularFont; t.fontSize=96; t.anchor=TextAnchor.UpperLeft; t.alignment=TextAlignment.Left; t.characterSize=1; t.lineSpacing=1.18f; t.color=color; t.text=content;
        t.font.RequestCharactersInTexture(content,96,FontStyle.Normal); var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=mat; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        var size=renderer.localBounds.size; go.transform.localScale=Vector3.one*Mathf.Min(height/Mathf.Max(size.y,.001f),width/Mathf.Max(size.x,.001f));
        r.BindCurvedText(t,panel,radius); labels.Add(t);
    }
    static void Rule(Transform panel,string name,float width,float y,float radius)
    {
        var go=new GameObject(name); go.layer=Layer; go.transform.SetParent(panel,false); var line=go.AddComponent<LineRenderer>();
        var mat=Mat("Composite separator","Unlit/Color"); mat.color=new Color(.48f,.18f,.20f,1); line.sharedMaterial=mat; line.useWorldSpace=false; line.positionCount=97; line.widthMultiplier=.009f; line.shadowCastingMode=ShadowCastingMode.Off;
        for(int i=0;i<97;i++){float x=Mathf.Lerp(-width/2,width/2,i/96f);line.SetPosition(i,new Vector3(radius*Mathf.Sin(x/radius),y,-radius*(1-Mathf.Cos(x/radius))-.022f));}
    }
    static void Render(BetweenUsCompositeRig r,RenderTexture target,float seconds,bool alpha)
    {
        r.SetPreviewTime(seconds); r.RefreshAtlas(); var c=r.captureCamera; var oldTarget=c.targetTexture; var oldColor=c.backgroundColor;
        try { c.targetTexture=target; c.aspect=target.width/(float)target.height; c.backgroundColor=new Color(0,0,0,alpha?0:1); c.Render(); }
        finally { c.targetTexture=oldTarget; c.backgroundColor=oldColor; }
    }
    public static string Capture(BetweenUsCompositeRig r,int width,int height,float seconds,bool alpha,string filename)
    {
        Directory.CreateDirectory(CaptureRoot); var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){antiAliasing=4}; var old=RenderTexture.active; Texture2D pixels=null;
        try
        {
            target.Create(); Render(r,target,seconds,alpha); RenderTexture.active=target;
            pixels=new Texture2D(width,height,alpha?TextureFormat.RGBA32:TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
            if(alpha)
            {
                var data=pixels.GetPixels32();
                for(int i=0;i<data.Length;i++)
                {
                    if(data[i].a==0){data[i].r=data[i].g=data[i].b=0;continue;}
                    float divide=255f/data[i].a; data[i].r=(byte)Mathf.Clamp(Mathf.RoundToInt(data[i].r*divide),0,255); data[i].g=(byte)Mathf.Clamp(Mathf.RoundToInt(data[i].g*divide),0,255); data[i].b=(byte)Mathf.Clamp(Mathf.RoundToInt(data[i].b*divide),0,255);
                }
                pixels.SetPixels32(data); pixels.Apply();
            }
            string path=Path.Combine(CaptureRoot,filename); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,pixels.EncodeToPNG()); return path;
        }
        finally { RenderTexture.active=old; if(pixels!=null)DestroyImmediate(pixels); target.Release(); DestroyImmediate(target); }
    }
    static void ExportStills(BetweenUsCompositeRig r,float seconds)
    {
        Capture(r,3840,2160,seconds,false,"BetweenUs-Curved-BlackRed-4K.png"); Capture(r,3840,2160,seconds,true,"BetweenUs-Curved-Transparent-4K.png");
    }
    public static void BuildAndCapture()
    {
        ShaderUtil.allowAsyncCompilation=false; var r=EnsureScene(); r.RefreshAtlas();
        Capture(r,1920,1080,3.2f,false,"BetweenUs-Curved-BlackRed-Preview.png"); Capture(r,1920,1080,8.2f,false,"BetweenUs-Curved-BlackRed-MotionCheck.png"); ExportStills(r,3.2f);
        foreach(var m in r.glassMaterials)if(ShaderUtil.ShaderHasError(m.shader))throw new InvalidOperationException("Glass shader compile error");
        EditorSceneManager.SaveScene(r.gameObject.scene,ScenePath); AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(CaptureRoot,"validation.txt"),"Unity desktop Camera.Render completed. Pure black; perspective; no stereo.\n5 cylindrical panels with curved typography.\n1080p motion comparison + 4K black and straight-alpha PNG.\nNo headset deployment or changes to existing headset scenes/build settings.\n");
        string staleError=Path.Combine(CaptureRoot,"error.txt");if(File.Exists(staleError))File.Delete(staleError);
        Debug.Log("BETWEEN_US_COMPOSITE_CAPTURE_OK "+CaptureRoot);
    }
    [MenuItem("Prototype/Between Us/Export Composite 8s PNG Sequence")]
    public static void ExportSequence()
    {
        var r=EnsureScene(); for(int i=0;i<240;i++)Capture(r,1920,1080,i/30f,false,"SequenceBlack/frame-"+i.ToString("D4")+".png"); Debug.Log("BETWEEN_US_COMPOSITE_SEQUENCE_OK "+CaptureRoot);
    }
}
[InitializeOnLoad]
static class BetweenUsCompositeRequest
{
    static double next;
    static BetweenUsCompositeRequest(){EditorApplication.update+=Update;}
    static void Update()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+1;string request=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-composite.request");if(!File.Exists(request))return;
        string action=File.ReadAllText(request).Trim();File.Delete(request);
        try{BetweenUsCompositeStudio.BuildAndCapture();if(action=="sequence")BetweenUsCompositeStudio.ExportSequence();if(!Application.isBatchMode)BetweenUsCompositeStudio.Open();}
        catch(Exception e){Directory.CreateDirectory(BetweenUsCompositeStudio.CaptureRoot);File.WriteAllText(Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"error.txt"),e.ToString());Debug.LogException(e);}
    }
}
