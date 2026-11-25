using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.PXR;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

public static class HandApiSetup
{
    [MenuItem("Prototype/Create hand and API scene")]
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
        var config = PXR_ProjectSetting.GetProjectConfig();
        config.handTracking = true;
        config.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
        PXR_ProjectSetting.SaveAssets();
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new [] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        var debuggerConfig = Unity.XR.PXR.Debugger.PXR_PicoDebuggerSO.Instance;
        debuggerConfig.isOpen = false;
        EditorUtility.SetDirty(debuggerConfig);
        if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets","XR");
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey,out XRGeneralSettingsPerBuildTarget xr)) {
            xr = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(xr,"Assets/XR/GeneralSettings.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey,xr,true);
        }
        if (!xr.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) xr.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        var manager = xr.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!XRPackageMetadataStore.AssignLoader(manager,"Unity.XR.PICO.LivePreview.PXR_PTLoader",BuildTargetGroup.Standalone))
            throw new System.InvalidOperationException("Could not enable PICO Live Preview loader");
        EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssets();
        var origin = new GameObject("Tracking origin");
        origin.AddComponent<PXR_Manager>();
        Camera.main.transform.SetParent(origin.transform, false);
        Camera.main.transform.localPosition = new Vector3(0,1.6f,0);
        Camera.main.transform.localRotation = Quaternion.identity;
        var panel = new GameObject("World status");
        panel.transform.position = new Vector3(-.65f,1.9f,1.7f);
        var text = panel.AddComponent<TextMesh>(); text.characterSize = .018f; text.fontSize = 36;
        text.anchor = TextAnchor.UpperLeft; text.color = Color.white;
        var signal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        signal.name = "API status beacon"; signal.transform.position = new Vector3(0,1.2f,1.5f);
        signal.transform.localScale = Vector3.one * .18f;
        var probe = origin.AddComponent<HandApiProbe>();
        probe.trackingOrigin = origin.transform; probe.display = text; probe.target = signal.GetComponent<Renderer>();
        EditorSceneManager.SaveScene(scene,"Assets/Prototype/HandApi.unity");
        Debug.Log("HAND_API_SCENE_READY: PICO Live Preview configured for Standalone.");
    }
}
