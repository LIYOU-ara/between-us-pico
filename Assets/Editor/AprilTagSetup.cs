using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.PXR;

public static class AprilTagSetup
{
    [MenuItem("Prototype/Create AprilTag 22 scene")]
    public static void Create() {
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath),"AprilTag"));
        var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
        PlayerSettings.runInBackground=true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
        var origin=new GameObject("AprilTag tracking origin");
        origin.AddComponent<PXR_Manager>();
        var camera=Camera.main;
        camera.transform.SetParent(origin.transform,false);
        camera.transform.localPosition=new Vector3(0,1.6f,0);
        camera.backgroundColor=new Color(.025f,.025f,.04f);
        var panel=new GameObject("AprilTag status");
        panel.transform.SetParent(camera.transform,false);
        panel.transform.localPosition=new Vector3(-.65f,.5f,1.5f);
        var text=panel.AddComponent<TextMesh>(); text.characterSize=.012f; text.fontSize=36;
        text.anchor=TextAnchor.UpperLeft; text.color=Color.white;
        var frame=new GameObject("Capture camera frame - estimated until calibrated");
        var marker=new GameObject("AprilTag 22 pose"); marker.transform.SetParent(frame.transform,false);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name="Tag 22 cyan cube";
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(marker.transform,false); cube.transform.localPosition=new Vector3(0,0,-.03f);
        cube.transform.localScale=Vector3.one*.06f;
        var cyan=new Material(Shader.Find("Unlit/Color")); cyan.color=Color.cyan;
        cyan=SaveMaterial(cyan,"AprilTagCube.mat"); cube.GetComponent<Renderer>().sharedMaterial=cyan;
        var preview=GameObject.CreatePrimitive(PrimitiveType.Quad); preview.name="Actual received camera image";
        Object.DestroyImmediate(preview.GetComponent<Collider>());
        preview.transform.SetParent(frame.transform,false);
        var previewMat=new Material(Shader.Find("Unlit/Texture"));
        previewMat=SaveMaterial(previewMat,"AprilTagPreview.mat"); preview.GetComponent<Renderer>().sharedMaterial=previewMat;
        marker.SetActive(false); preview.SetActive(false);
        var probe=origin.AddComponent<AprilTagProbe>();
        probe.marker=marker.transform; probe.captureFrame=frame.transform; probe.preview=preview.transform; probe.display=text;
        EditorSceneManager.SaveScene(scene,"Assets/Prototype/AprilTag22.unity"); AssetDatabase.SaveAssets();
        Debug.Log("APRILTAG_SCENE_READY: tag36h11 ID 22; no camera frames are assumed.");
    }
    static Material SaveMaterial(Material material,string name) {
        string path="Assets/Prototype/"+name;
        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(existing==null) AssetDatabase.CreateAsset(material,path);
        else { EditorUtility.CopySerialized(material,existing); EditorUtility.SetDirty(existing); Object.DestroyImmediate(material); return existing; }
        return material;
    }
}
