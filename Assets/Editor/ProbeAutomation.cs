using System.IO;
using UnityEditor;
using UnityEngine;

// Limited project-local commands for repeatable diagnostics in an already open editor.
[InitializeOnLoad]
public static class ProbeAutomation
{
    static string Root => Path.GetDirectoryName(Application.dataPath);
    static double lastCheck;
    static ProbeAutomation() { EditorApplication.update += Update; }
    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup - lastCheck < 1) return;
        lastCheck = EditorApplication.timeSinceStartup;
        string ultraBuild=Path.Combine(Root,"probe-ultra-build.request");
        if(File.Exists(ultraBuild) && !EditorApplication.isPlayingOrWillChangePlaymode) {
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android)) {
                File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"Unity has not loaded Android Build Support; restart Editor after module installation");
                return;
            }
            File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"Switching to Android");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android) {
                if(!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android)) {
                    File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"Android target switch failed");
                    File.Delete(ultraBuild);
                }
                return;
            }
            File.Delete(ultraBuild);
            try {
                File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"Building native camera APK");
                PicoSdkCameraSetup.Build();
                File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"APK build succeeded");
            } catch(System.Exception exception) {
                File.WriteAllText(Path.Combine(Root,"AprilTag","build-status.txt"),"Build failed: "+exception);
                Debug.LogException(exception);
            }
            return;
        }
        string tagSetup = Path.Combine(Root,"probe-apriltag.request");
        if (File.Exists(tagSetup) && !EditorApplication.isPlayingOrWillChangePlaymode) {
            File.Delete(tagSetup); AprilTagSetup.Create();
        }
        string setup = Path.Combine(Root,"probe-setup.request");
        if (File.Exists(setup) && !EditorApplication.isPlayingOrWillChangePlaymode) {
            File.Delete(setup); HandApiSetup.Create();
        }
        string play = Path.Combine(Root,"probe-play.request");
        if (File.Exists(play) && !EditorApplication.isPlayingOrWillChangePlaymode) {
            File.Delete(play); EditorApplication.isPlaying = true;
        }
        string api = Path.Combine(Root,"probe-api.request");
        if (File.Exists(api) && EditorApplication.isPlaying) {
            File.Delete(api);
            var probe = Object.FindFirstObjectByType<HandApiProbe>();
            if (probe != null) probe.Trigger();
        }
        string stop = Path.Combine(Root,"probe-stop.request");
        if (File.Exists(stop)) { File.Delete(stop); EditorApplication.isPlaying = false; }
    }
}
