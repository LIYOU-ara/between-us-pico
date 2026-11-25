using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// A deterministic desktop check of reflection motion, not a headset comfort test.
// All overrides are temporary; the authored PNGs and persistent materials stay untouched.
public static class ConversationRimPreview
{
    const int Width = 1920, Height = 1080;

    public static string Verify(Camera camera, ConversationSpeechBubble quote,
        ConversationSpeechBubble analysis, string output)
    {
        if (camera == null || quote == null || analysis == null)
            throw new ArgumentException("Rim preview camera or conversation panels are missing.");
        var bubbles = new[] { quote, analysis };
        var panels = new[] { quote.background, analysis.background };
        foreach (var panel in panels)
            if (panel == null || panel.sharedMaterial == null ||
                !panel.sharedMaterial.HasProperty("_FlowTime") ||
                ShaderUtil.ShaderHasError(panel.sharedMaterial.shader))
                throw new InvalidOperationException("A glass rim material is missing or has shader errors.");

        Directory.CreateDirectory(output);
        var originalBlocks = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        for (int i = 0; i < panels.Length; i++) panels[i].GetPropertyBlock(originalBlocks[i]);
        var originalEnabled = new Dictionary<Renderer, bool>();
        var permitted = new HashSet<Renderer>(panels);
        foreach (var bubble in bubbles)
            if (bubble.caption != null) permitted.Add(bubble.caption.GetComponent<Renderer>());
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var oldClear = camera.clearFlags;
        var oldColor = camera.backgroundColor;
        var oldEye = camera.stereoTargetEye;
        bool oldHdr = camera.allowHDR, oldSrgb = GL.sRGBWrite;
        RenderTexture target = null;
        Texture2D first = null, second = null;
        var sources = new Texture2D[2];
        try
        {
            // Isolate the two UI panels against a neutral background. A generated
            // scale figure or coloured test backdrop must not bias the coral check.
            foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                if (renderer.gameObject.scene == camera.gameObject.scene && !permitted.Contains(renderer))
                {
                    originalEnabled[renderer] = renderer.enabled;
                    renderer.enabled = false;
                }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .09f, .09f, 1);
            camera.allowHDR = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Temporary neutral glass rim preview", antiAliasing = 1,
                useMipMap = false, autoGenerateMips = false, hideFlags = HideFlags.DontSave
            };
            target.Create();
            if (!target.IsCreated()) throw new InvalidOperationException("Could not create the rim preview render target.");
            camera.targetTexture = target;

            for (int i = 0; i < panels.Length; i++)
            {
                var texture = panels[i].sharedMaterial.mainTexture as Texture2D;
                if (texture == null) throw new InvalidOperationException("The original PNG shape is not assigned to a panel.");
                sources[i] = ReadSource(texture);
            }
            void SetTime(float time)
            {
                foreach (var panel in panels)
                {
                    var block = new MaterialPropertyBlock();
                    panel.GetPropertyBlock(block);
                    block.SetFloat("_CameraGlassReady", 0);
                    block.SetFloat("_GlassStrength", 1);
                    block.SetFloat("_FlowTime", time);
                    panel.SetPropertyBlock(block);
                }
            }
            SetTime(0); camera.Render(); first = Read(target);
            File.WriteAllBytes(Path.Combine(output, "RimFlow-0.png"), first.EncodeToPNG());
            SetTime(4); camera.Render(); second = Read(target);
            File.WriteAllBytes(Path.Combine(output, "RimFlow-4.png"), second.EncodeToPNG());
            var a = first.GetPixels32(); var b = second.GetPixels32();

            int changed = 0, captionMax = 0, coralMax = 0, rimSamples = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i]; var bubble = bubbles[i]; var source = sources[i];
                // Captions use the anchor-relative artwork coordinates. Test a
                // dense grid across their entire allocated reading surface.
                Rect caption = bubble.CaptionBounds;
                var captionPixels = new HashSet<int>();
                for (int y = 0; y <= 80; y++) for (int x = 0; x <= 240; x++)
                {
                    float u = Mathf.Lerp(caption.xMin, caption.xMax, x / 240f);
                    float v = Mathf.Lerp(caption.yMin, caption.yMax, y / 80f);
                    int index = Project(camera, panel, u, v);
                    if (index >= 0) captionPixels.Add(index);
                }
                foreach (int index in captionPixels) captionMax = Math.Max(captionMax, Difference(a[index], b[index]));

                // Project the actual authored outline pixels, including the speech
                // tail. No procedural rectangle stands in for the supplied shape.
                var rimPixels = new HashSet<int>();
                var authored = source.GetPixels32();
                for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++)
                {
                    Color32 pixel = authored[y * source.width + x];
                    if (pixel.a < 64 || pixel.r - Math.Max(pixel.g, pixel.b) < 32) continue;
                    int index = Project(camera, panel, (x + .5f) / source.width, (y + .5f) / source.height);
                    if (index >= 0) rimPixels.Add(index);
                }
                if (rimPixels.Count < 20) throw new InvalidOperationException("Too few authored outline pixels are visible for the rim check.");
                rimSamples += rimPixels.Count;
                foreach (int index in rimPixels)
                {
                    if (Difference(a[index], b[index]) > 1) changed++;
                    coralMax = Math.Max(coralMax, Math.Max(RedChroma(a[index]), RedChroma(b[index])));
                }
            }
            if (changed <= 10)
                throw new InvalidOperationException("The rim reflection did not visibly move between time 0 and 4 seconds: changed=" + changed);
            if (captionMax > 1)
                throw new InvalidOperationException("Rim animation altered the caption reading surface: maximum RGB change=" + captionMax + "/255.");
            if (coralMax > 12)
                throw new InvalidOperationException("The neutral preview still contains coral pigment along the original outline: red chroma=" + coralMax + "/255.");

            string report = string.Format(CultureInfo.InvariantCulture,
                "Desktop GPU glass-rim check passed: 0 s / 4 s; {0} changed rim samples of {1}; caption-surface maximum RGB change={2}/255; maximum red chroma at authored outline={3}/255. Original PNGs, materials and property blocks restored. This verifies neutral rim colour and localized reflection motion in desktop mono rendering; live headset appearance and comfort require a separate device check.",
                changed, rimSamples, captionMax, coralMax);
            File.WriteAllText(Path.Combine(output, "RimFlow-validation.txt"), report + "\n");
            Debug.Log("CONVERSATION_GLASS_RIM_VERIFIED " + report);
            return report;
        }
        finally
        {
            for (int i = 0; i < panels.Length; i++)
                if (panels[i] != null) panels[i].SetPropertyBlock(originalBlocks[i]);
            foreach (var entry in originalEnabled) if (entry.Key != null) entry.Key.enabled = entry.Value;
            camera.targetTexture = oldTarget; camera.clearFlags = oldClear;
            camera.backgroundColor = oldColor; camera.allowHDR = oldHdr; camera.stereoTargetEye = oldEye;
            RenderTexture.active = oldActive; GL.sRGBWrite = oldSrgb;
            Dispose(first); Dispose(second);
            foreach (var source in sources) Dispose(source);
            if (target != null) { target.Release(); Dispose(target); }
        }
    }

    static int Project(Camera camera, Renderer panel, float u, float v)
    {
        Vector3 screen = camera.WorldToScreenPoint(panel.transform.TransformPoint(new Vector3(u - .5f, v - .5f, 0)));
        int x = Mathf.RoundToInt(screen.x - .5f), y = Mathf.RoundToInt(screen.y - .5f);
        return screen.z <= 0 || x < 0 || y < 0 || x >= Width || y >= Height ? -1 : y * Width + x;
    }

    static Texture2D ReadSource(Texture2D source)
    {
        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        try { Graphics.Blit(source, temporary); return Read(temporary); }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
    }

    static Texture2D Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(false);
            return texture;
        }
        catch { Dispose(texture); throw; }
        finally { RenderTexture.active = previous; }
    }

    static int Difference(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
    static int RedChroma(Color32 pixel) => pixel.r - Math.Max(pixel.g, pixel.b);
    static void Dispose(UnityEngine.Object value) { if (value != null) UnityEngine.Object.DestroyImmediate(value); }
}
