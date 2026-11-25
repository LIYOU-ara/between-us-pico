using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Resources assets enter every player build, even when no scene references them.
// Reject the old credential asset rather than modifying or deleting private files.
public sealed class ConversationCredentialBuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => int.MinValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        EnsureSafeBuild(report.summary.platform);
    }

    public static void EnsureSafeBuild(BuildTarget target)
    {
        if (target != BuildTarget.Android) return;
        string legacyCredential = Path.Combine(Application.dataPath, "Resources", "Conversation", "LocalApiKey.txt");
        if (File.Exists(legacyCredential))
            throw new BuildFailedException("Android build blocked: Assets/Resources/Conversation/LocalApiKey.txt contains a legacy credential resource. Move it outside Assets before building and configure credentials at runtime. No files were deleted.");
    }
}
