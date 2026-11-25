using System;
using UnityEngine;

// Visual presentation of an existing, coarse face hypothesis. No audio analysis or inference here.
[DefaultExecutionOrder(180)]
[DisallowMultipleComponent]
public sealed class ConversationEmotionGlass : MonoBehaviour
{
    public Renderer[] panels = Array.Empty<Renderer>();
    [Min(.05f)] public float transitionSeconds = 3f;
    [Min(.05f)] public float standbyTransitionSeconds = .9f;
    public const float DefaultVortexStrength = .85f;
    [Range(0, 1)] public float vortexStrength = DefaultVortexStrength;

    static readonly int EmotionColorId = Shader.PropertyToID("_EmotionColor");
    static readonly int EmotionStrengthId = Shader.PropertyToID("_EmotionStrength");
    static readonly int VortexStrengthId = Shader.PropertyToID("_VortexStrength");
    static readonly int IdleIridescenceId = Shader.PropertyToID("_IdleIridescence");
    static readonly string[] UnreadableClues = {
        "画面不清", "看不清", "人脸不清", "面部不可见", "脸部不可见", "未见人脸", "没有人脸", "未检测到人脸",
        "无法辨认", "无法识别人脸", "照片不可判读", "图像不可判读", "无法判读面部", "无法判读人脸",
        "遮挡严重", "严重遮挡", "多人脸", "多个人脸", "多张人脸", "过于模糊", "严重模糊", "人脸过小", "脸部过小",
        "no face", "face not visible", "face is not visible", "unreadable image", "unreadable photo", "image is unreadable",
        "multiple faces", "too blurry", "face obscured", "face occluded"
    };

    MaterialPropertyBlock properties;
    Color currentColor = Color.white, fromColor = Color.white, targetColor = Color.white;
    float currentStrength, fromStrength, targetStrength, elapsed, duration;
    float standbyStrength, standbyFrom, standbyTarget, standbyElapsed, standbyDuration;
    int knownSession = -1, knownPhoto = -1;
    bool hasContext;
    string hypothesis = "难判断", targetHypothesis = "难判断";

    public string CurrentHypothesis => hypothesis;
    public Color CurrentColor => currentColor;
    public float CurrentStrength => currentStrength;
    public float CurrentStandbyStrength => standbyStrength;

    public void Configure(Renderer[] renderers)
    {
        EnsureProperties();
        ClearOwnProperties();
        panels = renderers ?? Array.Empty<Renderer>();
        ResetStandby();
        ResetContext(true);
    }

    public void SetHypothesis(int sessionId, int photoId, bool hasPhoto, bool visualObserved, string hypothesis, string uncertainty)
    {
        if (!enabled)
        {
            hasContext = false; knownSession = knownPhoto = -1;
            ResetAnimation(); ResetStandby(); ClearOwnProperties(); return;
        }
        EnsureProperties();
        bool changedSnapshot = !hasContext || sessionId != knownSession || photoId != knownPhoto;
        if (changedSnapshot)
        {
            // A newly identified image must never inherit the preceding image's hue.
            ResetAnimation();
            knownSession = sessionId; knownPhoto = photoId; hasContext = true;
        }
        string normalized = ConversationModelFields.EmotionHypothesis(hypothesis);
        bool valid = sessionId >= 0 && photoId > 0 && hasPhoto && visualObserved && normalized != "难判断" && !ExplicitlyUnreadable(uncertainty);
        Color color; float strength;
        if (valid) Palette(normalized, out color, out strength);
        else { normalized = "难判断"; color = Color.white; strength = 0; }
        // Polls for one immutable snapshot keep the same running transition.
        if (normalized != targetHypothesis || (changedSnapshot && valid))
            Retarget(normalized, color, strength, valid ? Mathf.Max(.05f, transitionSeconds) : Mathf.Min(1f, Mathf.Max(.05f, transitionSeconds)));
        ApplyProperties();
    }

    public void ResetContext(bool immediate = true)
    {
        EnsureProperties();
        hasContext = false; knownSession = knownPhoto = -1;
        if (!enabled)
        {
            ResetAnimation(); ResetStandby(); ClearOwnProperties(); return;
        }
        // Status views clear emotion every frame. Idle appearance has its own
        // transition and must not restart when that context reset is repeated.
        if (immediate) ResetAnimation();
        else if (targetHypothesis != "难判断") Retarget("难判断", Color.white, 0, Mathf.Min(1f, Mathf.Max(.05f, transitionSeconds)));
        ApplyProperties();
    }

    public void SetStandby(bool standby, bool immediate = false)
    {
        EnsureProperties();
        if (!enabled)
        {
            ResetAnimation(); ResetStandby(); ClearOwnProperties(); return;
        }
        float target = standby ? 1f : 0f;
        if (immediate)
        {
            standbyStrength = standbyFrom = standbyTarget = target;
            standbyElapsed = standbyDuration = 0;
        }
        else if (standbyTarget != target)
        {
            standbyFrom = standbyStrength; standbyTarget = target;
            standbyElapsed = 0; standbyDuration = Mathf.Max(.05f, standbyTransitionSeconds);
        }
        ApplyProperties();
    }

    // Delta-time API for deterministic GPU previews as well as normal runtime animation.
    public void StepForValidation(float delta)
    {
        if (!enabled)
        {
            ResetAnimation(); ResetStandby(); ClearOwnProperties(); return;
        }
        if (float.IsNaN(delta) || float.IsInfinity(delta)) delta = 0;
        if (duration > 0)
        {
            elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0, delta));
            float fraction = Mathf.SmoothStep(0, 1, elapsed / duration);
            currentColor = Color.LerpUnclamped(fromColor, targetColor, fraction);
            currentStrength = Mathf.Clamp(Mathf.LerpUnclamped(fromStrength, targetStrength, fraction), 0, .85f);
            if (elapsed >= duration) { currentColor = targetColor; currentStrength = targetStrength; duration = 0; }
        }
        if (standbyDuration > 0)
        {
            standbyElapsed = Mathf.Min(standbyDuration, standbyElapsed + Mathf.Max(0, delta));
            float fraction = Mathf.SmoothStep(0, 1, standbyElapsed / standbyDuration);
            standbyStrength = Mathf.Clamp01(Mathf.LerpUnclamped(standbyFrom, standbyTarget, fraction));
            if (standbyElapsed >= standbyDuration) { standbyStrength = standbyTarget; standbyDuration = 0; }
        }
        ApplyProperties();
    }

    static bool ExplicitlyUnreadable(string uncertainty)
    {
        if (string.IsNullOrWhiteSpace(uncertainty)) return false;
        // Generic single-frame or possible-emotion caveats are deliberately not failure tests.
        foreach (string clue in UnreadableClues)
            if (uncertainty.IndexOf(clue, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static void Palette(string value, out Color color, out float strength)
    {
        switch (value)
        {
            case "平静": color = new Color(.40f, .62f, .59f, 1); strength = .58f; return;
            case "愉快": color = new Color(.79f, .68f, .47f, 1); strength = .68f; return;
            case "不悦": color = new Color(.72f, .41f, .49f, 1); strength = .76f; return;
            case "惊讶": color = new Color(.62f, .53f, .76f, 1); strength = .68f; return;
            case "紧张": color = new Color(.41f, .56f, .75f, 1); strength = .74f; return;
            default: color = Color.white; strength = 0; return;
        }
    }

    void Retarget(string value, Color color, float strength, float seconds)
    {
        hypothesis = targetHypothesis = value;
        fromColor = currentColor; fromStrength = currentStrength;
        targetColor = color; targetStrength = Mathf.Clamp(strength, 0, .85f);
        elapsed = 0; duration = seconds;
    }

    void ResetAnimation()
    {
        hypothesis = targetHypothesis = "难判断";
        currentColor = fromColor = targetColor = Color.white;
        currentStrength = fromStrength = targetStrength = elapsed = duration = 0;
    }

    void ResetStandby()
    {
        standbyStrength = standbyFrom = standbyTarget = standbyElapsed = standbyDuration = 0;
    }

    void EnsureProperties() { if (properties == null) properties = new MaterialPropertyBlock(); }

    void ApplyProperties()
    {
        // Callers may keep driving this public API during rollback. A disabled component stays inert.
        if (!enabled) { ClearOwnProperties(); return; }
        if (panels == null || panels.Length == 0) return;
        EnsureProperties();
        float flow = Mathf.Clamp01(vortexStrength);
        foreach (var renderer in panels)
        {
            if (renderer == null) continue;
            // Read, merge, then write: preserve camera projection, blur textures and all unrelated overrides.
            renderer.GetPropertyBlock(properties);
            properties.SetColor(EmotionColorId, currentColor);
            properties.SetFloat(EmotionStrengthId, Mathf.Clamp(currentStrength, 0, .85f));
            properties.SetFloat(VortexStrengthId, flow);
            properties.SetFloat(IdleIridescenceId, Mathf.Clamp01(standbyStrength));
            renderer.SetPropertyBlock(properties);
        }
    }

    void ClearOwnProperties()
    {
        if (properties == null || panels == null) return;
        foreach (var renderer in panels)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetColor(EmotionColorId, Color.white);
            properties.SetFloat(EmotionStrengthId, 0);
            properties.SetFloat(VortexStrengthId, 0);
            properties.SetFloat(IdleIridescenceId, 0);
            renderer.SetPropertyBlock(properties);
        }
    }

    void OnEnable() { EnsureProperties(); ApplyProperties(); }
    void LateUpdate() { StepForValidation(Time.unscaledDeltaTime); }
    void OnDisable()
    {
        hasContext = false; knownSession = knownPhoto = -1;
        ResetAnimation(); ResetStandby(); ClearOwnProperties();
    }
}

