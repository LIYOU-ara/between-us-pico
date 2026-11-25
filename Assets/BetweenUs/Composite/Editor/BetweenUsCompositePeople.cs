using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BetweenUsCompositePeople
{
    const string Root=BetweenUsCompositeStudio.AssetRoot;
    const string PeopleRoot="Two seated participants - ghost study";
    const int Layer=29;
    [Serializable] sealed class LayoutEntry { public string path; public Vector3 position,scale; public Quaternion rotation; }
    [Serializable] sealed class LayoutSnapshot { public string note="Current editor layout before people/material changes"; public LayoutEntry[] nodes; }
    static string Output=>Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"SeatedPeople");

    static Material MaterialAsset(string name,string shader)
    {
        var s=Shader.Find(shader);if(s==null||!s.isSupported||ShaderUtil.ShaderHasError(s))throw new InvalidOperationException("Invalid shader: "+shader);
        string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(s){name=name};AssetDatabase.CreateAsset(m,path);}return m;
    }
    static float F(string value)=>float.Parse(value,CultureInfo.InvariantCulture);
    static int Index(string text,int count){int i=int.Parse(text,CultureInfo.InvariantCulture);return i<0?count+i:i-1;}
    static Mesh ReadMesh(string path,string group,Vector3? anchor,float factor,string assetName)
    {
        string assetPath=Root+"/Meshes/"+assetName+".asset";var found=AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);if(found!=null)return found;
        var all=new List<Vector3>();var normals=new List<Vector3>();var vertices=new List<Vector3>();var outputNormals=new List<Vector3>();var indices=new List<int>();string current="";
        foreach(var raw in File.ReadLines(path))
        {
            string line=raw.Trim();if(line.Length==0||line[0]=='#')continue;
            var p=line.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
            if(p[0]=="v")all.Add(new Vector3(F(p[1]),F(p[2]),F(p[3])));
            else if(p[0]=="vn")normals.Add(new Vector3(F(p[1]),F(p[2]),F(p[3])));
            else if(p[0]=="g")current=line.Substring(2);
            else if(p[0]=="f"&&(group==null||current.Contains(group)))
            {
                for(int k=2;k<p.Length-1;k++)foreach(int entry in new[]{1,k,k+1})
                {
                    var token=p[entry].Split('/');vertices.Add(all[Index(token[0],all.Count)]);indices.Add(indices.Count);
                    outputNormals.Add(token.Length>2&&token[2].Length>0?normals[Index(token[2],normals.Count)]:Vector3.up);
                }
            }
        }
        if(vertices.Count<3)throw new InvalidOperationException("No faces in "+group);
        Bounds bounds=new Bounds(vertices[0],Vector3.zero);foreach(var v in vertices)bounds.Encapsulate(v);
        Vector3 centre=anchor??new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        for(int i=0;i<vertices.Count;i++)vertices[i]=(vertices[i]-centre)*factor;
        var mesh=new Mesh{name=assetName};if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(vertices);mesh.SetNormals(outputNormals);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,assetPath);return mesh;
    }
    static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material)
    {
        var go=new GameObject(name);go.layer=Layer;go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return go;
    }
    static void Box(Transform parent,string name,Vector3 centre,Vector3 size,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.layer=Layer;go.transform.SetParent(parent,false);go.transform.localPosition=centre;go.transform.localScale=size;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    static void Chair(Transform parent,Material material)
    {
        var chair=new GameObject("Quiet translucent chair");chair.layer=Layer;chair.transform.SetParent(parent,false);
        Box(chair.transform,"Seat",new Vector3(0,.49f,-.14f),new Vector3(.48f,.045f,.50f),material);
        Box(chair.transform,"Backrest",new Vector3(0,.76f,-.37f),new Vector3(.46f,.50f,.035f),material);
        foreach(float x in new[]{-.20f,.20f})foreach(float z in new[]{-.34f,.06f})Box(chair.transform,"Chair leg",new Vector3(x,.24f,z),new Vector3(.028f,.48f,.028f),material);
    }
    static void Actor(Transform parent,string name,Mesh body,Mesh helmet,Vector3 head,Vector3 position,Vector3 partner,Material person,Material visor,Material chair,Material ring)
    {
        var actor=new GameObject(name);actor.layer=Layer;actor.transform.SetParent(parent,false);actor.transform.localPosition=position;
        var forward=partner-position;forward.y=0;actor.transform.localRotation=Quaternion.LookRotation(forward);actor.transform.localScale=Vector3.one*1.28f;
        MeshObject(actor.transform,"Seated body",body,person);
        var headset=MeshObject(actor.transform,"VR headset",helmet,visor);headset.transform.localPosition=head;headset.transform.localRotation=Quaternion.Euler(14,0,0);
        Chair(actor.transform,chair);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Quad);floor.name="Luminous floor ring";floor.layer=Layer;floor.transform.SetParent(actor.transform,false);
        floor.transform.localPosition=new Vector3(0,.008f,.12f);floor.transform.localRotation=Quaternion.Euler(90,0,0);floor.transform.localScale=new Vector3(1.9f,1.9f,1);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());var renderer=floor.GetComponent<MeshRenderer>();renderer.sharedMaterial=ring;renderer.shadowCastingMode=ShadowCastingMode.Off;
    }
    static LayoutSnapshot Snapshot(Transform root)
    {
        var nodes=root.GetComponentsInChildren<Transform>(true).Where(t=>t!=root&&t.name!=PeopleRoot&&t.name!="Full composition export camera - author camera preserved"&&!InsidePeople(t,root)).Select(t=>new LayoutEntry{path=PathOf(t,root),position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray();return new LayoutSnapshot{nodes=nodes};
    }
    static bool InsidePeople(Transform t,Transform root){while(t!=null&&t!=root){if(t.name==PeopleRoot)return true;t=t.parent;}return false;}
    static string PathOf(Transform t,Transform root){return t.parent==root?t.name:PathOf(t.parent,root)+"/"+t.name;}
    static int Changed(LayoutSnapshot before,Transform root)
    {
        int count=0;foreach(var item in before.nodes){var t=root.Find(item.path);if(t==null||(t.localPosition-item.position).sqrMagnitude>.0000001f||(t.localScale-item.scale).sqrMagnitude>.0000001f||Quaternion.Angle(t.localRotation,item.rotation)>.001f)count++;}return count;
    }
    static void EnsureFullFrameCamera(BetweenUsCompositeRig rig)
    {
        var authorTransform=rig.transform.Find("Desktop perspective camera - pure black - NO XR");
        var author=authorTransform!=null?authorTransform.GetComponent<Camera>():rig.captureCamera;
        var existing=rig.transform.Find("Full composition export camera - author camera preserved");
        Camera full;
        if(existing==null)
        {
            var go=new GameObject("Full composition export camera - author camera preserved");go.transform.SetParent(rig.transform,false);full=go.AddComponent<Camera>();
        }
        else
        {
            // The user has finished the layout: retain the existing export-camera framing.
            rig.captureCamera=existing.GetComponent<Camera>();return;
        }
        full.CopyFrom(author);full.enabled=false;full.stereoTargetEye=StereoTargetEyeMask.None;
        var points=new List<Vector3>();
        foreach(var renderer in rig.GetComponentsInChildren<Renderer>())
        {
            if(renderer.GetComponent<TextMesh>()!=null)continue;
            Bounds bounds=renderer.bounds;
            foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y})foreach(float z in new[]{bounds.min.z,bounds.max.z})points.Add(new Vector3(x,y,z));
        }
        if(points.Count==0)return;
        Quaternion rotation=author.transform.rotation, inverse=Quaternion.Inverse(rotation);
        Bounds view=new Bounds(inverse*points[0],Vector3.zero);foreach(var point in points)view.Encapsulate(inverse*point);
        Vector3 centre=rotation*view.center;
        float tangent=Mathf.Tan(author.fieldOfView*.5f*Mathf.Deg2Rad),distance=0;
        foreach(var point in points)
        {
            Vector3 v=inverse*(point-centre);
            distance=Mathf.Max(distance,Mathf.Abs(v.y)/tangent-v.z,Mathf.Abs(v.x)/(tangent*(16f/9f))-v.z);
        }
        distance=distance*1.08f+.15f;
        full.transform.rotation=rotation;full.transform.position=centre-author.transform.forward*distance;full.aspect=16f/9f;
        rig.captureCamera=full;
    }
    [MenuItem("Prototype/Between Us/Add two seated participants and pearl palettes")]
    public static void BuildAndCapture()
    {
        ShaderUtil.allowAsyncCompilation=false;Directory.CreateDirectory(Output);Directory.CreateDirectory(Root+"/Meshes");
        var rig=BetweenUsCompositeStudio.EnsureScene();var before=Snapshot(rig.transform);
        string sourceScene=BetweenUsCompositeStudio.ScenePath;
        if(!File.Exists(Path.Combine(Output,"BeforePeople.scene-backup")))File.Copy(sourceScene,Path.Combine(Output,"BeforePeople.scene-backup"));
        if(!File.Exists(Path.Combine(Output,"WindowLayoutBefore.json")))File.WriteAllText(Path.Combine(Output,"WindowLayoutBefore.json"),JsonUtility.ToJson(before,true));
        var person=MaterialAsset("Seated person - pearl ghost","Between Us/Composite/Ghost Person");person.SetFloat("_Opacity",.34f);person.SetFloat("_RimStrength",.80f);person.SetFloat("_Cull",0);person.SetFloat("_DepthWrite",1);
        var visor=MaterialAsset("VR headset - pearl","Between Us/Composite/Ghost Person");visor.SetFloat("_Opacity",.68f);visor.SetFloat("_RimStrength",.46f);visor.SetFloat("_AccentStrength",.02f);visor.SetFloat("_Cull",0);visor.SetFloat("_DepthWrite",1);visor.SetColor("_Color",new Color(.82f,.85f,.91f,1));
        var chair=MaterialAsset("Seated chairs - quiet ghost","Between Us/Composite/Ghost Person");chair.SetFloat("_Opacity",.095f);chair.SetFloat("_RimStrength",.20f);chair.SetFloat("_AccentStrength",.01f);chair.SetFloat("_Cull",0);chair.SetFloat("_DepthWrite",0);
        var ring=MaterialAsset("Seated floor rings - silver","Between Us/Composite/Ground Ring");ring.SetFloat("_ShowcaseTime",3.2f);ring.SetFloat("_DiscStrength",.012f);
        var people=rig.transform.Find(PeopleRoot);
        if(people==null)
        {
            var p=new GameObject(PeopleRoot);p.layer=Layer;p.transform.SetParent(rig.transform,false);people=p.transform;
            string peoplePath=Root+"/Models/LowpolyPeople_Source.obj";
            var a=ReadMesh(peoplePath,"_01",null,.06f,"Seated person 01");var b=ReadMesh(peoplePath,"_02",null,.06f,"Seated person 02");
            var helm=ReadMesh(Root+"/Models/VRHeadset_Source.obj",null,new Vector3(.115f,.05f,-.30f),.118f,"Fitted VR headset");
            Vector3 pa=new Vector3(-1.8f,-1.45f,-1.25f),pb=new Vector3(2,-1.45f,-.40f);
            Actor(people,"Participant A - leaning seated",a,helm,new Vector3(-.0397f,1.1876f,.0537f),pa,pb,person,visor,chair,ring);
            Actor(people,"Participant B - relaxed seated",b,helm,new Vector3(.0932f,1.1971f,-.0183f),pb,pa,person,visor,chair,ring);
        }
        // Keep the visor fitted when the supplied body mesh is independently resized.
        for(int index=0;index<2;index++)
        {
            var actor=people.GetChild(index);var body=actor.Find("Seated body");var fitted=actor.Find("VR headset");
            if(fitted==null && body!=null)fitted=body.Find("VR headset");
            if(body!=null && fitted!=null && fitted.parent!=body)
            {
                fitted.SetParent(body,false);fitted.localPosition=index==0?new Vector3(-.0397f,1.1876f,.0537f):new Vector3(.0932f,1.1971f,-.0183f);
                fitted.localRotation=Quaternion.Euler(14,0,0);fitted.localScale=Vector3.one;
            }
        }
        var palette=rig.GetComponent<BetweenUsCompositePalette>();if(palette==null)palette=rig.gameObject.AddComponent<BetweenUsCompositePalette>();
        palette.panels=rig.glassMaterials.Where(m=>m!=null&&m.shader.name=="Between Us/Composite/Frosted Vortex Glass").ToArray();palette.rings=new[]{ring};palette.person=person;palette.headset=visor;
        rig.glassMaterials=palette.panels.Concat(new[]{ring}).ToArray();palette.Apply(BetweenUsCompositePalette.Style.SilverNeutral);
        // This is a compositing HUD: preserve text readability through ghost silhouettes.
        if(rig.captions!=null)foreach(var caption in rig.captions)
        {
            if(caption==null)continue;var textRenderer=caption.GetComponent<Renderer>();
            if(textRenderer!=null && textRenderer.sharedMaterial!=null)textRenderer.sharedMaterial.SetFloat("_ZTest",8);
        }
        EnsureFullFrameCamera(rig);
        string variantPath="SeatedPeople/";
        BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,variantPath+"TwoSeated-Silver-Preview.png");
        BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,false,variantPath+"TwoSeated-Silver-4K.png");
        BetweenUsCompositeStudio.Capture(rig,3840,2160,3.2f,true,variantPath+"TwoSeated-Silver-Transparent-4K.png");
        foreach(BetweenUsCompositePalette.Style mood in Enum.GetValues(typeof(BetweenUsCompositePalette.Style)))
        {
            palette.Apply(mood);BetweenUsCompositeStudio.Capture(rig,1920,1080,3.2f,false,variantPath+"Palette-"+mood+".png");
        }
        palette.Apply(BetweenUsCompositePalette.Style.SilverNeutral);
        int changed=Changed(before,rig.transform);if(changed!=0)throw new InvalidOperationException("An existing layout transform changed: "+changed);
        EditorSceneManager.SaveScene(rig.gameObject.scene,BetweenUsCompositeStudio.ScenePath);AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(Output,"validation.txt"),"Unity GPU captures completed. Two existing seated meshes, two headsets, two luminous rings.\nExisting window/camera transforms changed: "+changed+" of "+before.nodes.Length+".\nDefault silver glass; five manually selected art-direction palettes, not an emotion classifier.\n");
        File.WriteAllText(Path.Combine(Output,"WindowLayoutAfter.json"),JsonUtility.ToJson(Snapshot(rig.transform),true));
        string error=Path.Combine(Output,"error.txt");if(File.Exists(error))File.Delete(error);
        Selection.activeGameObject=people.gameObject;Debug.Log("BETWEEN_US_SEATED_PEOPLE_OK "+Output);
        BetweenUsCompositeStudio.Open();
    }
    [MenuItem("Prototype/Between Us/Palette/Silver neutral")]static void Neutral(){Set(BetweenUsCompositePalette.Style.SilverNeutral);}
    [MenuItem("Prototype/Between Us/Palette/Calm blue")]static void Blue(){Set(BetweenUsCompositePalette.Style.CalmBlue);}
    [MenuItem("Prototype/Between Us/Palette/Warm champagne")]static void Warm(){Set(BetweenUsCompositePalette.Style.WarmChampagne);}
    [MenuItem("Prototype/Between Us/Palette/Reflective lilac")]static void Lilac(){Set(BetweenUsCompositePalette.Style.ReflectiveLilac);}
    [MenuItem("Prototype/Between Us/Palette/Soft rose")]static void Rose(){Set(BetweenUsCompositePalette.Style.TenseRose);}
    static void Set(BetweenUsCompositePalette.Style style){var rig=BetweenUsCompositeStudio.EnsureScene();var p=rig.GetComponent<BetweenUsCompositePalette>();if(p!=null)p.Apply(style);}
}
[InitializeOnLoad]static class BetweenUsSeatedRequest
{
    static double next;static BetweenUsSeatedRequest(){EditorApplication.update+=Tick;}
    static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<next)return;
        next=EditorApplication.timeSinceStartup+1;string path=Path.Combine(Path.GetDirectoryName(Application.dataPath),"between-us-people.request");if(!File.Exists(path))return;File.Delete(path);
        try{BetweenUsCompositePeople.BuildAndCapture();}catch(Exception e){string output=Path.Combine(BetweenUsCompositeStudio.CaptureRoot,"SeatedPeople");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);}
    }
}
