using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// All generated assets live in PortfolioHud. Existing PICO scenes/settings are untouched.
public sealed class PortfolioHudStudio : EditorWindow
{
    const string Root = "Assets/Prototype/PortfolioHud";
    const string ScenePath = Root + "/BetweenUsHudShowcase.unity";
    const int PreviewLayer = 30;
    static readonly Color Accent = new Color(1f, .17f, .24f, 1f);
    PortfolioHudPreviewDriver driver;
    RenderTexture preview;
    bool animate = true;
    float frozenTime = 3.2f;
    float glow = .62f, sweep = .25f, scan = .035f, pulse = .055f, opacity = 1f;
    double lastFrame;
    string captureMessage = "";

    [MenuItem("Prototype/Between Us/HUD Shader Studio")]
    public static void Open()
    {
        var window = GetWindow<PortfolioHudStudio>("Between Us | HUD Shader Studio");
        window.minSize = new Vector2(860, 600);
        window.driver = EnsureScene();
        window.Show();
        window.Focus();
    }

    [MenuItem("Prototype/Between Us/Select new HUD panel material")]
    public static void SelectPanel()
    {
        EnsureScene();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Material>(Root + "/HUD Panel - Coral.mat");
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (preview != null) { preview.Release(); DestroyImmediate(preview); }
    }

    void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup - lastFrame < 1.0 / 24.0) return;
        lastFrame = EditorApplication.timeSinceStartup;
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("BETWEEN US / HUD SHADER STUDIO", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Desktop material study · animated preview · scripted conversation", EditorStyles.miniLabel);
        if (driver == null)
        {
            if (GUILayout.Button("Open standalone showcase")) driver = EnsureScene();
            return;
        }
        EditorGUILayout.Space(8);
        float width = Mathf.Max(300, position.width - 20);
        float height = Mathf.Min(width * 9f / 16f, Mathf.Max(250, position.height - 245));
        Rect rect = GUILayoutUtility.GetRect(width, height);
        if (Event.current.type == EventType.Repaint)
        {
            ApplyControls();
            if (preview == null || preview.width != 1600 || preview.height != 900)
            {
                if (preview != null) { preview.Release(); DestroyImmediate(preview); }
                preview = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                preview.Create();
            }
            Render(driver, preview, animate ? (float)EditorApplication.timeSinceStartup : frozenTime);
            EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
        }
        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            animate = GUILayout.Toggle(animate, "Animate", "Button", GUILayout.Width(90));
            if (!animate) frozenTime = EditorGUILayout.Slider("Time", frozenTime, 0, 20);
            if (GUILayout.Button("Panel material", GUILayout.Width(110))) SelectPanel();
            if (GUILayout.Button("Capture 1920 x 1080", GUILayout.Width(165)))
            {
                float captureTime = animate ? (float)EditorApplication.timeSinceStartup : frozenTime;
                string path = Capture(driver, captureTime, "BetweenUs-HUD-Coral.png");
                captureMessage = path;
                EditorUtility.RevealInFinder(path);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope())
            {
                glow = EditorGUILayout.Slider("Edge glow", glow, 0, 1);
                sweep = EditorGUILayout.Slider("Sweep speed", sweep, 0, 1);
                opacity = EditorGUILayout.Slider("Opacity", opacity, .15f, 1);
            }
            using (new EditorGUILayout.VerticalScope())
            {
                scan = EditorGUILayout.Slider("Scan texture", scan, 0, .15f);
                pulse = EditorGUILayout.Slider("Edge breathing", pulse, 0, .15f);
                if (GUILayout.Button("Reset coral preset"))
                { glow = .62f; sweep = .25f; scan = .035f; pulse = .055f; opacity = 1; }
            }
        }
        EditorGUILayout.LabelField(string.IsNullOrEmpty(captureMessage)
            ? "Exported images are desktop shader previews, not headset captures." : captureMessage,
            EditorStyles.miniLabel);
    }

    void ApplyControls()
    {
        if (driver.panelMaterials == null) return;
        foreach (var material in driver.panelMaterials)
        {
            if (material == null) continue;
            material.SetFloat("_GlowStrength", glow);
            material.SetFloat("_SweepSpeed", sweep);
            material.SetFloat("_ScanStrength", scan);
            material.SetFloat("_PulseStrength", pulse);
            material.SetFloat("_Opacity", opacity);
        }
    }

    static Material MaterialAsset(string name, string shaderName)
    {
        string path = Root + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Shader has not imported: " + shaderName);
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static PortfolioHudPreviewDriver EnsureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the shader studio outside Play Mode.");
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.IsValid() && scene.isLoaded)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<PortfolioHudPreviewDriver>();
                if (found != null) return found;
            }
        }
        if (File.Exists(ScenePath))
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<PortfolioHudPreviewDriver>();
                if (found != null) return found;
            }
            throw new InvalidOperationException("Showcase scene has no preview driver.");
        }
        Scene previous = SceneManager.GetActiveScene();
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var root = new GameObject("Between Us - standalone shader study");
            root.layer = PreviewLayer;
            var controller = root.AddComponent<PortfolioHudPreviewDriver>();
            var cameraObject = new GameObject("Portfolio preview camera - no XR");
            cameraObject.transform.SetParent(root.transform);
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5.625f;
            camera.aspect = 16f / 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.009f, .011f, .017f, 1);
            camera.cullingMask = 1 << PreviewLayer;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            controller.previewCamera = camera;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Bold.otf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            controller.captionFont = font;
            var panel = MaterialAsset("HUD Panel - Coral", "Between Us/Portfolio/Holographic Panel");
            var face = MaterialAsset("HUD Scan Frame - Coral", "Between Us/Portfolio/Holographic Panel");
            var reply = MaterialAsset("HUD Reply - Coral", "Between Us/Portfolio/Holographic Panel");
            var text = MaterialAsset("HUD Text - Coral", "Between Us/Portfolio/Holographic Text");
            var lines = MaterialAsset("HUD Wire - Coral", "Unlit/Color");
            lines.color = new Color(.32f, .055f, .09f, 1);
            text.SetFloat("_GlowStrength", .28f);
            text.SetFloat("_SweepStrength", .08f);
            controller.panelMaterials = new[] { panel, face, reply };
            controller.textMaterial = text;
            foreach (var material in controller.panelMaterials)
            {
                material.SetColor("_AccentColor", Accent);
                material.SetFloat("_GlowStrength", .62f);
                material.SetFloat("_SweepSpeed", .25f);
                material.SetFloat("_PulseStrength", .055f);
            }
            Quad(root.transform, "Primary private HUD", panel, 0, -.05f, 16.9f, 7.2f, 2);
            Quad(root.transform, "Context scan frame", face, -5.4f, -.15f, 4.6f, 5.35f, 1.8f);
            Quad(root.transform, "Suggested reply emphasis", reply, 2.45f, -1.9f, 9f, 1.58f, 1.8f);
            Grid(root.transform, lines);
            FaceWire(root.transform, lines);
            var captions = new List<TextMesh>();
            Color white = new Color(.96f, .96f, .97f, 1);
            Color muted = new Color(.53f, .56f, .63f, 1);
            AddText(root.transform, captions, font, text, "Project title", "BETWEEN US", -8.25f, 4.65f, .70f, 8, white);
            AddText(root.transform, captions, font, text, "Study label", "A PRIVATE LAYER IN A SHARED CONVERSATION", -8.20f, 3.77f, .18f, 10, muted);
            AddText(root.transform, captions, font, text, "Status", "LIVE CONVERSATION  /  03", 4.8f, 4.5f, .18f, 4.0f, Accent);
            AddText(root.transform, captions, font, text, "Context label", "VISIBLE CUES", -7.15f, 2.04f, .19f, 3.5f, Accent);
            AddText(root.transform, captions, font, text, "Face qualifier", "Single-frame observation", -7.15f, 1.67f, .15f, 3.5f, muted);
            AddText(root.transform, captions, font, text, "Face observation", "Slight smile", -7.10f, -1.4f, .28f, 3.5f, white);
            AddText(root.transform, captions, font, text, "Face uncertainty", "A visible cue, not a confirmed feeling.", -7.10f, -1.89f, .13f, 3.5f, muted);
            AddText(root.transform, captions, font, text, "Transcript label", "B  /  SPOKEN WORDS", -2.2f, 2.14f, .18f, 8.4f, muted);
            AddText(root.transform, captions, font, text, "Actual transcript", "\"What do you mean, it's over?\"", -2.20f, 1.62f, .43f, 9.1f, white);
            AddText(root.transform, captions, font, text, "Interpretation label", "POSSIBLE READING", -2.2f, .49f, .18f, 8.5f, Accent);
            AddText(root.transform, captions, font, text, "Interpretation", "He may be asking\nif you're sure.", -2.20f, .05f, .87f, 8.7f, white);
            AddText(root.transform, captions, font, text, "Interpretation uncertainty", "One interpretation. You can still ask him.", -2.2f, -1.02f, .16f, 8.5f, muted);
            AddText(root.transform, captions, font, text, "Prompt label", "TRY ASKING", -1.6f, -1.52f, .16f, 7.7f, Accent);
            AddText(root.transform, captions, font, text, "Reply", "Are you asking why, or if I'm sure?", -1.6f, -1.94f, .32f, 7.7f, white);
            AddText(root.transform, captions, font, text, "Source status", "TRANSCRIPT     +     FACIAL CUES     /     PRIVATE AI GUIDANCE", -7.25f, -2.93f, .14f, 14.5f, muted);
            AddText(root.transform, captions, font, text, "Choices", "X   ANOTHER READING                    Y   MY OWN WORDS", -7.95f, -4.28f, .18f, 15.7f, white);
            AddText(root.transform, captions, font, text, "Disclosure", "DESKTOP SHADER STUDY  /  SCRIPTED CONTENT", -8.20f, -5.04f, .12f, 12f, muted);
            controller.captions = captions.ToArray();
            controller.RefreshAtlas();
            controller.SetPreviewTime(3.2f);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            return controller;
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }

    static void Quad(Transform parent, string name, Material material, float x, float y, float width, float height, float z)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name; go.layer = PreviewLayer; go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localScale = new Vector3(width, height, 1);
        DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        material.SetFloat("_Aspect", width / height);
    }

    static void AddText(Transform parent, List<TextMesh> all, Font font, Material material,
        string name, string content, float x, float y, float height, float width, Color color)
    {
        var go = new GameObject(name); go.layer = PreviewLayer; go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, 1.0f);
        var text = go.AddComponent<TextMesh>();
        text.font = font; text.fontSize = 96; text.fontStyle = FontStyle.Normal;
        text.anchor = TextAnchor.UpperLeft; text.alignment = TextAlignment.Left;
        text.characterSize = 1; text.lineSpacing = 1.16f; text.color = color; text.text = content;
        font.RequestCharactersInTexture(content, 96, FontStyle.Normal);
        var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        var size = renderer.localBounds.size;
        float scale = Mathf.Min(height / Mathf.Max(size.y, .01f), width / Mathf.Max(size.x, .01f));
        go.transform.localScale = Vector3.one * scale;
        all.Add(text);
    }

    static void Stroke(Transform parent, string name, Material material, IList<Vector3> points, float width, bool loop)
    {
        var go = new GameObject(name); go.layer = PreviewLayer; go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material;
        line.useWorldSpace = false; line.positionCount = points.Count; line.loop = loop;
        line.widthMultiplier = width; line.numCornerVertices = 3; line.numCapVertices = 2;
        for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
    }

    static void Grid(Transform parent, Material lines)
    {
        for (int i = -10; i <= 10; i++)
            Stroke(parent, "Backdrop vertical " + i, lines,
                new[] { new Vector3(i, -5.625f, 3), new Vector3(i, 5.625f, 3) }, .003f, false);
        for (int i = -5; i <= 5; i++)
            Stroke(parent, "Backdrop horizontal " + i, lines,
                new[] { new Vector3(-10, i, 3), new Vector3(10, i, 3) }, .003f, false);
    }

    static void FaceWire(Transform parent, Material material)
    {
        for (int meridian = -3; meridian <= 3; meridian++)
        {
            var points = new List<Vector3>();
            float angle = meridian * .32f;
            for (int i = 0; i <= 56; i++)
            {
                float latitude = Mathf.Lerp(-1.48f, 1.48f, i / 56f);
                points.Add(new Vector3(-5.36f + Mathf.Cos(latitude) * Mathf.Sin(angle) * 1.07f,
                    .10f + Mathf.Sin(latitude) * 1.30f, 1.4f));
            }
            Stroke(parent, "Illustrative scan meridian " + meridian, material, points, .010f, false);
        }
        for (int row = -4; row <= 4; row++)
        {
            var points = new List<Vector3>(); float v = row * .23f;
            float radius = Mathf.Sqrt(1 - v * v) * 1.07f;
            for (int i = 0; i <= 56; i++)
            {
                float a = i / 56f * Mathf.PI * 2;
                points.Add(new Vector3(-5.36f + Mathf.Cos(a) * radius,
                    .10f + v * 1.30f + Mathf.Sin(a) * .10f, 1.4f));
            }
            Stroke(parent, "Illustrative scan latitude " + row, material, points, .010f, false);
        }
        Stroke(parent, "Eye line", material,
            new[] { new Vector3(-6.0f, .35f, 1.3f), new Vector3(-4.72f, .35f, 1.3f) }, .018f, false);
        Stroke(parent, "Mouth cue", material,
            new[] { new Vector3(-5.79f, -.58f, 1.3f), new Vector3(-5.36f, -.64f, 1.3f), new Vector3(-4.93f, -.58f, 1.3f) }, .018f, false);
    }

    static void Render(PortfolioHudPreviewDriver controller, RenderTexture target, float time)
    {
        controller.SetPreviewTime(time);
        var camera = controller.previewCamera; var previous = camera.targetTexture;
        try { camera.targetTexture = target; camera.aspect = target.width / (float)target.height; camera.Render(); }
        finally { camera.targetTexture = previous; }
    }

    static string Capture(PortfolioHudPreviewDriver controller, float time, string filename)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PortfolioHudCaptures");
        Directory.CreateDirectory(directory);
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; Texture2D image = null;
        try
        {
            controller.RefreshAtlas(); target.Create(); Render(controller, target, time);
            RenderTexture.active = target; image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            string path = Path.Combine(directory, filename); File.WriteAllBytes(path, image.EncodeToPNG()); return path;
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) DestroyImmediate(image);
            target.Release(); DestroyImmediate(target);
        }
    }

    // Can run from the already-open editor or Unity -executeMethod, without Play Mode.
    public static void CaptureAndValidate()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var controller = EnsureScene();
        // Keep these labels inside their illuminated borders in the generated study.
        foreach (var item in new[] { new Vector2(0, 2.04f), new Vector2(1, 1.67f), new Vector2(2, -2.93f) })
        {
            string name = item.x == 0 ? "Context label" : item.x == 1 ? "Face qualifier" : "Source status";
            var label = controller.transform.Find(name);
            if (label != null) { var p = label.localPosition; p.y = item.y; label.localPosition = p; }
        }
        controller.RefreshAtlas();
        string first = Capture(controller, 3.2f, "BetweenUs-HUD-Coral.png");
        string second = Capture(controller, 7.4f, "BetweenUs-HUD-Coral-Second.png");
        var shaders = new[] { controller.panelMaterials[0].shader, controller.textMaterial.shader };
        foreach (var shader in shaders)
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader compiler error: " + shader.name);
        EditorSceneManager.SaveScene(controller.gameObject.scene, ScenePath);
        AssetDatabase.SaveAssets();
        string directory = Path.GetDirectoryName(first);
        File.WriteAllText(Path.Combine(directory, "validation.txt"),
            "Shader compiler errors: 0\nDesktop Camera.Render: 1920 x 1080\n" + first + "\n" + second +
            "\nIndependent desktop preview; no headset validation or live AI call.\n");
        Debug.Log("BETWEEN_US_HUD_CAPTURE_OK " + first);
    }
}

[InitializeOnLoad]
static class PortfolioHudStudioRequest
{
    static double nextCheck;
    static PortfolioHudStudioRequest() { EditorApplication.update += Update; }
    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        string root = Path.GetDirectoryName(Application.dataPath);
        string request = Path.Combine(root, "portfolio-hud-showcase.request");
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { PortfolioHudStudio.CaptureAndValidate(); PortfolioHudStudio.Open(); }
        catch (Exception e)
        {
            Directory.CreateDirectory(Path.Combine(root, "PortfolioHudCaptures"));
            File.WriteAllText(Path.Combine(root, "PortfolioHudCaptures", "error.txt"), e.ToString());
            Debug.LogException(e);
        }
    }
}
