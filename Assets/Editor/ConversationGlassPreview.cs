using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// An explicit desktop GPU check. It does not acquire a camera frame, enter
// Play Mode, or modify the supplied PNGs or their materials.
public static class ConversationGlassPreview
{
    public static string Verify(Camera camera, ConversationSpeechBubble quote, ConversationSpeechBubble analysis,
        GameObject backdrop, GameObject person, string output)
    {
        if (camera == null || quote == null || analysis == null || backdrop == null || person == null)
            throw new ArgumentException("Camera glass preview references are missing.");
        if (camera.orthographic) throw new InvalidOperationException("Camera glass preview needs a perspective camera.");
        if (!SystemInfo.supportsRenderTextures) throw new InvalidOperationException("GPU render textures are unavailable.");
        var blurShader = Shader.Find("Hidden/Between Us/Camera Glass Blur");
        if (blurShader == null || !blurShader.isSupported || ShaderUtil.ShaderHasError(blurShader))
            throw new InvalidOperationException("The camera glass blur shader is missing, unsupported or has compile errors.");
        var panels = new[] { quote.background, analysis.background };
        foreach (var panel in panels)
            if (panel == null || panel.sharedMaterial == null || ShaderUtil.ShaderHasError(panel.sharedMaterial.shader))
                throw new InvalidOperationException("A projected glass panel shader has compile errors or is missing.");

        Directory.CreateDirectory(output);
        var originalBlocks = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        for (int i = 0; i < panels.Length; i++) panels[i].GetPropertyBlock(originalBlocks[i]);
        bool quoteActive = quote.gameObject.activeSelf, analysisActive = analysis.gameObject.activeSelf;
        bool backdropActive = backdrop.activeSelf, personActive = person.activeSelf;
        bool previousHdr = camera.allowHDR, previousSrgbWrite = GL.sRGBWrite;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        RenderTexture source = null, baseline = null, intermediate = null, blurred = null, full = null;
        Material blurMaterial = null;
        Texture2D sourcePixels = null, baselinePixels = null, blurredPixels = null, unblurredView = null, blurredView = null;
        try
        {
            source = MakeTarget(640, 360, 24, "Generated camera-glass source");
            baseline = MakeTarget(320, 180, 0, "Unblurred camera-glass control");
            intermediate = MakeTarget(320, 180, 0, "Camera-glass horizontal intermediate");
            blurred = MakeTarget(320, 180, 0, "Camera-glass Gaussian result");
            full = MakeTarget(1920, 1080, 24, "Camera-glass full preview");
            blurMaterial = new Material(blurShader) { hideFlags = HideFlags.DontSave };
            blurMaterial.SetFloat("_BlurRadius", 2f);
            camera.allowHDR = false;
            backdrop.SetActive(true); person.SetActive(true);
            quote.gameObject.SetActive(false); analysis.gameObject.SetActive(false);
            camera.targetTexture = source;
            camera.Render();
            sourcePixels = Read(source);
            var sourceData = sourcePixels.GetPixels32();
            if (LumaVariance(sourceData) < .001)
                throw new InvalidOperationException("The generated camera source is empty or lacks the required colour-block background.");
            Save(sourcePixels, output, "CameraGlass-GeneratedBackground.png");

            // Control and blurred images share the same size. Downsampling alone
            // must not be counted as the Gaussian's high-frequency reduction.
            Graphics.Blit(source, baseline);
            Graphics.Blit(source, intermediate, blurMaterial, 0);
            Graphics.Blit(intermediate, blurred, blurMaterial, 1);
            Graphics.Blit(blurred, intermediate, blurMaterial, 0);
            Graphics.Blit(intermediate, blurred, blurMaterial, 1);
            baselinePixels = Read(baseline); blurredPixels = Read(blurred);
            double sharpVariance = StripeVariance(baselinePixels.GetPixels32(), baseline.width, baseline.height);
            double blurVariance = StripeVariance(blurredPixels.GetPixels32(), blurred.width, blurred.height);
            if (sharpVariance < .000001 || blurVariance >= sharpVariance * .8)
                throw new InvalidOperationException("The four-pass GPU blur did not reduce stripe high-frequency variance: control=" + F(sharpVariance) + ", blurred=" + F(blurVariance));
            Save(baselinePixels, output, "CameraGlass-UnblurredSample.png");
            Save(blurredPixels, output, "CameraGlass-BlurredSample.png");

            quote.gameObject.SetActive(quoteActive); analysis.gameObject.SetActive(analysisActive);
            float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            var intrinsics = new Vector4(1f / (2f * camera.aspect * tangent), 1f / (2f * tangent), .5f, .5f);
            var worldToOptical = Matrix4x4.Scale(new Vector3(1, 1, -1)) * camera.worldToCameraMatrix;
            void Project(Texture texture)
            {
                for (int i = 0; i < panels.Length; i++)
                {
                    var block = new MaterialPropertyBlock(); panels[i].GetPropertyBlock(block);
                    block.SetTexture("_BlurTex", texture);
                    block.SetMatrix("_GlassWorldToCamera", worldToOptical);
                    block.SetVector("_GlassIntrinsics", intrinsics);
                    block.SetFloat("_GlassFlipRows", 0);
                    block.SetFloat("_CameraGlassReady", 1);
                    block.SetFloat("_GlassStrength", 1);
                    panels[i].SetPropertyBlock(block);
                }
            }
            camera.targetTexture = full;
            Project(baseline); camera.Render(); unblurredView = Read(full);
            Save(unblurredView, output, "CameraGlass-Projected-Unblurred.png");
            Project(blurred); camera.Render(); blurredView = Read(full);
            Save(blurredView, output, "CameraGlass-Projected.png");
            double projectedDifference = PanelDifference(camera, analysis.background, unblurredView.GetPixels32(), blurredView.GetPixels32(), full.width, full.height);
            if (projectedDifference <= .0003)
                throw new InvalidOperationException("Changing the projected camera texture did not visibly affect the lower glass panel: delta=" + F(projectedDifference));

            string result = "Desktop GPU camera-glass verification passed: generated source 640x360; shared 320x180 four-pass H/V Gaussian; " +
                "stripe high-frequency variance control=" + F(sharpVariance) + ", blurred=" + F(blurVariance) +
                ", ratio=" + F(blurVariance / sharpVariance) + "; projected-panel mean RGB change=" + F(projectedDifference) +
                ". Both full views use the same camera, projection, text and generated scene. This does not validate live PICO camera calibration, stereo, latency or system passthrough compositing.";
            File.WriteAllText(Path.Combine(output, "CameraGlass-validation.txt"), result + "\n");
            Debug.Log("CONVERSATION_CAMERA_GLASS_PREVIEW_VERIFIED " + result);
            return result;
        }
        finally
        {
            for (int i = 0; i < panels.Length; i++) if (panels[i] != null) panels[i].SetPropertyBlock(originalBlocks[i]);
            quote.gameObject.SetActive(quoteActive); analysis.gameObject.SetActive(analysisActive);
            backdrop.SetActive(backdropActive); person.SetActive(personActive);
            camera.targetTexture = previousTarget; camera.allowHDR = previousHdr;
            RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgbWrite;
            Dispose(sourcePixels); Dispose(baselinePixels); Dispose(blurredPixels); Dispose(unblurredView); Dispose(blurredView);
            Dispose(blurMaterial);
            Release(source); Release(baseline); Release(intermediate); Release(blurred); Release(full);
        }
    }

    static RenderTexture MakeTarget(int width, int height, int depth, string name)
    {
        var target = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            useMipMap = false, autoGenerateMips = false, antiAliasing = 1, hideFlags = HideFlags.DontSave
        };
        target.Create();
        if (!target.IsCreated()) { Dispose(target); throw new InvalidOperationException("Could not create GPU render target: " + name); }
        return target;
    }

    static Texture2D Read(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(false);
            return pixels;
        }
        catch { Dispose(pixels); throw; }
        finally { RenderTexture.active = previous; }
    }

    static double Luma(Color32 pixel) => (.2126 * pixel.r + .7152 * pixel.g + .0722 * pixel.b) / 255.0;

    static double LumaVariance(Color32[] pixels)
    {
        double sum = 0, squares = 0; int count = 0;
        for (int i = 0; i < pixels.Length; i += 7) { double value = Luma(pixels[i]); sum += value; squares += value * value; count++; }
        return count == 0 ? 0 : Math.Max(0, squares / count - Math.Pow(sum / count, 2));
    }

    static double StripeVariance(Color32[] pixels, int width, int height)
    {
        double sum = 0, squares = 0; int count = 0;
        for (int y = 12; y < height - 12; y++) for (int x = 12; x < width - 12; x++)
        {
            // Side regions contain the generated fine stripes and avoid most
            // of the geometric figure. Horizontal second differences isolate
            // fine detail instead of measuring the large colour blocks.
            if (x > width * .34f && x < width * .66f) continue;
            int index = y * width + x;
            double value = Luma(pixels[index - 1]) - 2 * Luma(pixels[index]) + Luma(pixels[index + 1]);
            sum += value; squares += value * value; count++;
        }
        return count == 0 ? 0 : Math.Max(0, squares / count - Math.Pow(sum / count, 2));
    }

    static double PanelDifference(Camera camera, Renderer panel, Color32[] first, Color32[] second, int width, int height)
    {
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int x = 0; x <= 1; x++) for (int y = 0; y <= 1; y++)
        {
            var point = camera.WorldToScreenPoint(panel.transform.TransformPoint(new Vector3(x - .5f, y - .5f, 0)));
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        int left = Mathf.Clamp(Mathf.CeilToInt(min.x), 0, width - 1), right = Mathf.Clamp(Mathf.FloorToInt(max.x), 0, width - 1);
        int bottom = Mathf.Clamp(Mathf.CeilToInt(min.y), 0, height - 1), top = Mathf.Clamp(Mathf.FloorToInt(max.y), 0, height - 1);
        double sum = 0; int count = 0;
        for (int y = bottom; y <= top; y += 2) for (int x = left; x <= right; x += 2)
        {
            int index = y * width + x;
            sum += Math.Abs(first[index].r - second[index].r) + Math.Abs(first[index].g - second[index].g) + Math.Abs(first[index].b - second[index].b);
            count++;
        }
        return count == 0 ? 0 : sum / (count * 3.0 * 255);
    }

    static void Save(Texture2D texture, string output, string name) => File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
    static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
    static void Dispose(UnityEngine.Object value) { if (value != null) UnityEngine.Object.DestroyImmediate(value); }
    static void Release(RenderTexture texture) { if (texture != null) { texture.Release(); Dispose(texture); } }
}
