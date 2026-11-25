using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit one-shot build request for the already-open editor. Never installs
// an APK and never treats an older build-ready marker as a successful build.
[InitializeOnLoad]
public static class PicoConversationBuildRequest
{
    [Serializable] public sealed class Status
    {
        public string phase, started_utc, finished_utc, apk, sha256, error;
        public long bytes;
    }
    static double nextCheck;
    static bool running;
    static string Root => Path.GetDirectoryName(Application.dataPath);
    static string StatusPath => Path.Combine(Root, "Builds", "SpeakerBubble-build.json");
    static PicoConversationBuildRequest() { EditorApplication.update += Update; }

    static void Save(Status status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
        File.WriteAllText(StatusPath, JsonUtility.ToJson(status, true));
    }

    static void Update()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        string request = Path.Combine(Root, "conversation-build.request");
        if (!File.Exists(request)) return;
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Save(new Status { phase = "switching_android" });
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                File.Delete(request);
                Save(new Status { phase = "failed", error = "Could not switch the editor to Android." });
            }
            return;
        }
        File.Delete(request);
        BuildNow();
    }

    [MenuItem("Prototype/Between Us/Build conversation with speaker bubble")]
    public static void BuildNow()
    {
        var status = new Status { phase = "building", started_utc = DateTime.UtcNow.ToString("O") };
        running = true;
        Save(status);
        try
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the modified Unity scene before building.");
            string path = Path.Combine(Root, "Builds", "PicoConversation.apk");
            if (File.Exists(path))
            {
                string backup = Path.Combine(Root, "Builds", "PicoConversation-before-speaker-bubble.apk");
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            ConversationSpeechBubbleSetup.Install();
            PicoConversationSetup.Build();
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.LastWriteTimeUtc < DateTime.Parse(status.started_utc).ToUniversalTime())
                throw new InvalidOperationException("Build did not produce a fresh APK.");
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                status.sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            status.phase = "succeeded";
            status.apk = path;
            status.bytes = file.Length;
            Debug.Log("PICO_SPEAKER_BUBBLE_APK_READY " + path);
        }
        catch (Exception error)
        {
            status.phase = "failed";
            status.error = error.GetType().Name + ": " + error.Message;
            Debug.LogException(error);
        }
        finally
        {
            status.finished_utc = DateTime.UtcNow.ToString("O");
            Save(status);
            running = false;
        }
    }
}
