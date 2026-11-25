using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConversationSpeechBubbleSetup
{
    const string ResourcesPath = "Assets/Resources/Conversation/";
    const string ScenePath = "Assets/Prototype/PicoConversation.unity";
    static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath), "DualFrameCaptures");

    static void PrepareArtwork()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader = Shader.Find("Between Us/Frosted Conversation Panel");
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Conversation artwork shader did not compile.");
        PrepareImage("SpeakerBubble", "SpeakerBubbleBackground", 1181, 216, shader);
        PrepareImage("AnalysisPanel", "AnalysisPanelBackground", 1181, 461, shader);
        var text = MaterialAsset("SpeakerBubbleText", Shader.Find("Prototype/StereoCaption"));
        text.SetFloat("_OutlinePixels", 0);
        text.SetColor("_OutlineColor", Color.clear);
        EditorUtility.SetDirty(text);
        PrepareTemplateAssets();
        AssetDatabase.SaveAssets();
    }

    static void PrepareImage(string imageName, string materialName, int width, int height, Shader shader)
    {
        string path = ResourcesPath + imageName + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Original PNG is missing: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        var image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (image == null || image.width != width || image.height != height)
            throw new InvalidOperationException("Original dimensions changed: " + path + "; expected " + width + " x " + height);
        var material = MaterialAsset(materialName, shader);
        material.mainTexture = image;
        material.SetFloat("_GlassStrength", 1);
        material.SetFloat("_Opacity", imageName == "SpeakerBubble" ? .46f : .54f);
        material.SetColor("_TintTop", new Color(.96f, .98f, 1, 1));
        material.SetColor("_TintBottom", new Color(.88f, .92f, .96f, 1));
        material.SetFloat("_BlurTint", .04f);
        material.SetFloat("_CameraGlassReady", 0);
        material.SetColor("_BorderColor", new Color(.90f, .96f, 1, 1));
        material.SetFloat("_BorderStrength", .9f);
        material.SetColor("_FlowColor", new Color(1, .99f, .97f, 1));
        material.SetFloat("_FlowSpeed", .085f);
        material.SetFloat("_FlowIntensity", 1);
        material.SetFloat("_FlowTime", -1);
        material.SetFloat("_GrainStrength", .006f);
        material.SetFloat("_SheenStrength", .018f);
        // Persistent materials stay neutral; the runtime component owns only local effect overrides.
        material.SetColor("_EmotionColor", Color.white);
        material.SetFloat("_EmotionStrength", 0);
        material.SetFloat("_VortexStrength", 0);
        material.SetFloat("_IdleIridescence", 0);
        material.SetFloat("_VortexTime", -1);
        EditorUtility.SetDirty(material);
    }

    static void PrepareTemplateAssets()
    {
        string[] names = { "GuidanceLayout", "Start", "Listening", "ConnectionInterrupted", "NoSpeech" };
        foreach (string name in names)
        {
            string path = ResourcesPath + "Template/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Supplied UI template missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        var artwork = Shader.Find("Between Us/Template Artwork");
        if (artwork == null || ShaderUtil.ShaderHasError(artwork))
            throw new InvalidOperationException("Template artwork shader did not compile.");
        // A Resources material plus a scene shader reference retain GLES3 variants.
        string materialPath = ResourcesPath + "Template/Artwork.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(artwork); AssetDatabase.CreateAsset(material, materialPath); }
        else material.shader = artwork;
        EditorUtility.SetDirty(material);
    }

    public static void InstallTemplateHud(PicoConversationOverlay overlay)
    {
        var hud = overlay.analysisPanel.GetComponent<ConversationTemplateHud>();
        if (hud == null) hud = overlay.analysisPanel.gameObject.AddComponent<ConversationTemplateHud>();
        hud.artworkShader = Shader.Find("Between Us/Template Artwork");
        hud.headingFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
        overlay.templateHud = hud;
        overlay.showFaceThumbnail = true;
        EditorUtility.SetDirty(hud);
        EditorUtility.SetDirty(overlay);
    }

    static Material MaterialAsset(string name, Shader shader)
    {
        if (shader == null) throw new InvalidOperationException("Required shader missing: " + name);
        string path = ResourcesPath + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null) { result = new Material(shader); AssetDatabase.CreateAsset(result, path); }
        else result.shader = shader;
        return result;
    }

    static void RequireCleanScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.isDirty)
                throw new InvalidOperationException("Save or discard scene edits before installing the conversation frames. Unsaved scene: " + scene.name);
        }
    }

    static PicoConversationOverlay FindOverlay(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var overlay = root.GetComponentInChildren<PicoConversationOverlay>(true);
            if (overlay != null) return overlay;
        }
        throw new InvalidOperationException("Existing conversation overlay is missing.");
    }

    static Camera FindCamera(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) return camera;
        throw new InvalidOperationException("The conversation scene has no MainCamera.");
    }

    [MenuItem("Prototype/Between Us/Install dual conversation frames")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play Mode.");
        RequireCleanScenes();
        PrepareArtwork();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var overlay = FindOverlay(scene);
        if (overlay.quoteText == null || overlay.analysisText == null)
            throw new InvalidOperationException("Existing conversation text references are missing.");
        var bodyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Regular.otf");
        var quoteFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
        if (bodyFont == null || quoteFont == null) throw new InvalidOperationException("Conversation Regular/Medium fonts missing.");
        overlay.quoteText.font = quoteFont;
        overlay.analysisText.font = bodyFont;
        EditorUtility.SetDirty(overlay.quoteText); EditorUtility.SetDirty(overlay.analysisText);
        bool foundManager = false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var manager in root.GetComponentsInChildren<Unity.XR.PXR.PXR_Manager>(true))
            {
                manager.usePremultipliedAlpha = true;
                EditorUtility.SetDirty(manager);
                foundManager = true;
            }
        if (!foundManager) throw new InvalidOperationException("The conversation scene has no PXR_Manager.");
        overlay.speechBubble = ConversationSpeechBubble.Attach(overlay.quoteText);
        overlay.speechBubble.ConfigureSpeaker();
        overlay.speechBubble.SetTranscript("");
        overlay.analysisPanel = ConversationSpeechBubble.AttachAnalysis(overlay.analysisText);
        overlay.analysisPanel.ConfigureAnalysis();
        overlay.analysisPanel.transform.SetParent(FindCamera(scene).transform, false);
        overlay.analysisPanel.transform.localPosition = new Vector3(0, -.43f, 1.8f);
        overlay.analysisPanel.transform.localRotation = Quaternion.identity;
        overlay.analysisPanel.SetTranscript("");
        InstallCameraGlass(overlay);
        InstallTemplateHud(overlay);
        // Empty saved captions reference a persistent asset. Their live text
        // material is cloned again at runtime, so no DontSave clone is serialized.
        var savedText = AssetDatabase.LoadAssetAtPath<Material>(ResourcesPath + "SpeakerBubbleText.mat");
        foreach (var caption in new[] { overlay.quoteText, overlay.analysisText })
        {
            var renderer = caption.GetComponent<Renderer>();
            renderer.sharedMaterial = savedText;
            EditorUtility.SetDirty(renderer);
        }
        EditorUtility.SetDirty(overlay);
        EditorUtility.SetDirty(overlay.speechBubble);
        EditorUtility.SetDirty(overlay.analysisPanel);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("BETWEEN_US_DUAL_FRAMES_INSTALLED " + ScenePath);
    }

    public static void InstallCameraGlass(PicoConversationOverlay overlay)
    {
        var glass = overlay.GetComponent<ConversationGlassCamera>();
        if (glass == null) glass = overlay.gameObject.AddComponent<ConversationGlassCamera>();
        glass.cameraSource = overlay.GetComponent<PicoSdkCameraCapture>();
        glass.panels = new[] { overlay.speechBubble.background, overlay.analysisPanel.background };
        glass.blurShader = Shader.Find("Hidden/Between Us/Camera Glass Blur");
        if (glass.cameraSource == null || glass.blurShader == null || ShaderUtil.ShaderHasError(glass.blurShader))
            throw new InvalidOperationException("Calibrated camera glass or its blur shader is missing.");
        EditorUtility.SetDirty(glass);
        var emotion = overlay.GetComponent<ConversationEmotionGlass>();
        if (emotion == null) emotion = overlay.gameObject.AddComponent<ConversationEmotionGlass>();
        emotion.panels = glass.panels;
        emotion.transitionSeconds = 3;
        emotion.standbyTransitionSeconds = .9f;
        emotion.vortexStrength = ConversationEmotionGlass.DefaultVortexStrength;
        overlay.emotionGlass = emotion;
        EditorUtility.SetDirty(emotion);
        EditorUtility.SetDirty(overlay);
    }

    [MenuItem("Prototype/Between Us/Capture dual-frame preview")]
    public static void CapturePreview() { InstallAndValidate(); EditorUtility.RevealInFinder(Output); }

    public static void InstallAndValidate()
    {
        ShaderUtil.allowAsyncCompilation = false;
        Directory.CreateDirectory(Output);
        string oldError = Path.Combine(Output, "error.txt");
        if (File.Exists(oldError)) File.Delete(oldError);
        string oldValidation = Path.Combine(Output, "validation.txt");
        if (File.Exists(oldValidation)) File.Delete(oldValidation);
        Install();
        Scene scene = EditorSceneManager.NewPreviewScene();
        var ownedMaterials = new List<Material>();
        try
        {
            var root = new GameObject("Actual dual-frame render validation");
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Preview camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.position = Vector3.zero;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = false; camera.fieldOfView = 60;
            camera.allowHDR = false;
            camera.aspect = 1920f / 1080; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.stereoTargetEye = StereoTargetEyeMask.None; camera.scene = scene;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Regular.otf");
            var mediumFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
            var plainTemplate = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prototype/ConversationCaption.mat");
            if (font == null || mediumFont == null || plainTemplate == null) throw new InvalidOperationException("Preview font or text material is missing.");
            TextMesh MakeCaption(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                var text = go.AddComponent<TextMesh>(); text.font = font; text.fontSize = 96;
                // This copy is owned by the preview, never by the AssetDatabase.
                var material = new Material(plainTemplate); ownedMaterials.Add(material);
                text.GetComponent<Renderer>().sharedMaterial = material;
                return text;
            }
            var quotedText = MakeCaption("Other speaker's actual words");
            quotedText.font = mediumFont;
            var quote = ConversationSpeechBubble.Attach(quotedText);
            quote.ConfigureSpeaker();
            quote.transform.localPosition = new Vector3((quote.tailPivot.x - .5f) * quote.width, .24f, 1.5f);
            var analysis = ConversationSpeechBubble.AttachAnalysis(MakeCaption("Private AI interpretation"));
            analysis.ConfigureAnalysis(); analysis.transform.localPosition = new Vector3(0, -.43f, 1.8f);
            // Material comparisons are preview-only. Do not toggle source assets.
            foreach (var frame in new[] { quote, analysis })
            {
                var material = new Material(frame.background.sharedMaterial) { hideFlags = HideFlags.DontSave };
                ownedMaterials.Add(material);
                frame.background.sharedMaterial = material;
            }
            var person = CreatePersonReference(root.transform, ownedMaterials);
            var complexBackground = CreateComplexBackground(root.transform, ownedMaterials);
            complexBackground.SetActive(false);

            const string englishQuote = "Can we talk about this later?";
            quote.SetTranscript(englishQuote);
            analysis.SetTranscript("POSSIBLE READING\nB may be under pressure, or avoiding the topic. This remains uncertain.\n\nTRY ASKING\nIs now a difficult time, or is there something about the plan you want to discuss?");
            CheckLayout(quote); CheckLayout(analysis);
            CaptureBothBackgrounds(camera, "DualFrames-English");
            CaptureMaterialComparison(camera, quote, analysis, complexBackground, "English");

            quote.SetTranscript("我们可以晚一点再聊吗？");
            analysis.SetTranscript("可能的理解\n对方可能正承受工作压力，也可能在回避话题。仅凭这一句话，还不能确定。\n\n可以尝试询问\n现在是不方便聊，还是你对周末的安排有别的顾虑？");
            CheckLayout(quote); CheckLayout(analysis);
            CaptureBothBackgrounds(camera, "DualFrames-Chinese");
            CaptureMaterialComparison(camera, quote, analysis, complexBackground, "Chinese");
            string glassReport = ConversationGlassPreview.Verify(camera, quote, analysis, complexBackground, person, Output);
            string rimReport = ConversationRimPreview.Verify(camera, quote, analysis, Output);

            analysis.SetStatus("正在分析…");
            if (analysis.caption.text != "正在分析…" || analysis.caption.alignment != TextAlignment.Center ||
                analysis.caption.anchor != TextAnchor.MiddleCenter || !analysis.background.gameObject.activeSelf)
                throw new InvalidOperationException("The processing status is not centred in the lower panel.");
            CheckLayout(analysis);
            CaptureBothBackgrounds(camera, "ShaderPreview-Status");
            complexBackground.SetActive(true);
            Capture(camera, "ShaderPreview-Status-Complex.png");
            complexBackground.SetActive(false);

            VerifyPagination(quote, "I do want to spend time together. I have a deadline tonight, and I cannot give this conversation my full attention until the work is finished. Could we set aside some time tomorrow?", true);
            VerifyPagination(analysis, "A POSSIBLE READING\nThe request to postpone may reflect deadline pressure rather than rejection. The visible facial cues and the words alone do not establish B's intention.\n\nANOTHER POSSIBILITY\nB may feel uncertain about the plan or find the conversation difficult. Treat this as a question to explore, rather than a conclusion about the relationship.\n\nTRY ASKING\nIs it the timing that makes this difficult, or is there something about our plans that you want to discuss?\n\nYOUR CHOICE\nYou can ask, use your own words, wait, or dismiss this suggestion. New information from B may change this interpretation.", true);
            CaptureBothBackgrounds(camera, "DualFrames-LongText-LastPage");
            VerifyPagination(analysis, "可能的理解：对方可能正在承受截止日期的压力。仅凭推迟对话的回答，不能把回避当作事实。另一种可能是，他对周末的安排有顾虑，但还没有说明。可以尝试询问：现在是不方便聊，还是你对我们的安排有别的想法？你也可以用自己的话表达，等待，或忽略这条建议。对方给出的新信息可能改变目前的理解。\n面部线索只能描述这一刻可见的动作，不能独立证明情绪或意图。即使一句解释听起来很合理，它仍然需要结合对话中尚未表达的信息。你可以先让对方说明情况，再决定是否调整自己的判断。请保留对不同解释的空间，也保留对当前建议提出质疑的自由。\n对话回顾：你提出了周末相处的计划，对方请求稍后再谈。接下来的回应可以是一句澄清，也可以是约定新的时间。这里的文字用于支持你的思考，最终的表达由你决定。", true);

            quote.SetTranscript(englishQuote);
            string untouchedQuote = quote.caption.text;
            analysis.SetTranscript("Conversation summary\nYou asked about timing. B explained the deadline and suggested talking tomorrow. This does not establish what either person felt.");
            if (quote.caption.text != untouchedQuote) throw new InvalidOperationException("Updating AI guidance changed the other speaker's transcript.");
            quote.SetTranscript("");
            if (quote.background.gameObject.activeSelf || !string.IsNullOrEmpty(quote.caption.text)) throw new InvalidOperationException("An empty speaker bubble is visible or contains stale speech.");
            if (!analysis.background.gameObject.activeSelf || string.IsNullOrWhiteSpace(analysis.caption.text)) throw new InvalidOperationException("The summary is not visible inside the lower analysis panel.");
            CheckLayout(analysis);
            CaptureBothBackgrounds(camera, "DualFrames-Summary-LowerPanel");
            quote.SetTranscript("New utterance.");
            if (quote.caption.text != "“New utterance.”" || !quote.background.gameObject.activeSelf) throw new InvalidOperationException("A new utterance did not replace the previous transcript.");
            string alphaReport = CheckTransparency(camera, quote, analysis, person);

            File.WriteAllText(Path.Combine(Output, "validation.txt"),
                "Unity editor import/compile and Camera.Render passed.\n" +
                "Speaker PNG: 1181 x 216; analysis PNG: 1181 x 461. Original PNG bytes and aspect ratios retained.\n" +
                "Speaker PNG SHA256: " + Hash(ResourcesPath + "SpeakerBubble.png") + "\n" +
                "Analysis PNG SHA256: " + Hash(ResourcesPath + "AnalysisPanel.png") + "\n" +
                "English and Chinese two-frame layouts rendered against dark and light backgrounds.\n" +
                "ShaderPreview original/enhanced material comparisons use identical geometry and generated colour blocks/stripes, without user photographs.\n" +
                "The lower processing status is centred and separately captured.\n" +
                "Long speaker and AI text retain all non-whitespace characters across pages. Each page fits its artwork's text bounds.\n" +
                "Empty transcript hides the upper artwork. Guidance and summary remain in the lower panel. Updating guidance does not alter quoted speech.\n" +
                "Preview materials are owned copies. Source text materials are not destroyed by preview cleanup.\n" +
                alphaReport + "\n" +
                glassReport + "\n" +
                rimReport + "\n" +
                "The figure is a Unity geometric scale reference, not a replacement for supplied artwork.\n" +
                "Desktop mono rendering only: headset stereo, passthrough alpha compositing, microphone, ASR, tracking and comfort need separate device checks.\n" +
                "The enhanced material uses the original PNG-derived contour mask. A local camera texture supplies blur only while calibrated frames are fresh; a translucent tint is the fallback. This is not a filter of system passthrough.\n", Encoding.UTF8);
            Debug.Log("BETWEEN_US_DUAL_FRAMES_VALIDATED " + Output);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var material in ownedMaterials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static GameObject CreatePersonReference(Transform parent, List<Material> ownedMaterials)
    {
        var person = new GameObject("Geometric person scale reference");
        person.transform.SetParent(parent, false);
        var material = new Material(Shader.Find("Unlit/Color"));
        material.color = new Color(.35f, .37f, .41f, 1); ownedMaterials.Add(material);
        void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(type); part.name = name;
            part.transform.SetParent(person.transform, false); part.transform.localPosition = position; part.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = material;
        }
        Part("Geometric head reference", PrimitiveType.Sphere, new Vector3(0, .13f, 1.9f), new Vector3(.25f, .30f, .20f));
        Part("Geometric torso reference", PrimitiveType.Capsule, new Vector3(0, -.4f, 1.95f), new Vector3(.39f, .36f, .20f));
        Part("Geometric shoulder reference", PrimitiveType.Cube, new Vector3(0, -.07f, 1.95f), new Vector3(.54f, .12f, .18f));
        return person;
    }

    static GameObject CreateComplexBackground(Transform parent, List<Material> ownedMaterials)
    {
        var backdrop = new GameObject("Generated material-test colour blocks and stripes");
        backdrop.transform.SetParent(parent, false);
        var colours = new[]
        {
            new Color(.08f, .13f, .20f), new Color(.77f, .82f, .86f),
            new Color(.55f, .32f, .30f), new Color(.24f, .37f, .43f),
            new Color(.91f, .88f, .81f), new Color(.16f, .19f, .25f)
        };
        var materials = new List<Material>();
        foreach (var colour in colours)
        {
            var material = new Material(Shader.Find("Unlit/Color")) { color = colour, hideFlags = HideFlags.DontSave };
            materials.Add(material); ownedMaterials.Add(material);
        }
        void Patch(string name, Vector3 position, Vector3 scale, Material material)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad); patch.name = name;
            patch.transform.SetParent(backdrop.transform, false);
            patch.transform.localPosition = position; patch.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(patch.GetComponent<Collider>());
            patch.GetComponent<Renderer>().sharedMaterial = material;
        }
        for (int row = 0; row < 4; row++) for (int column = 0; column < 8; column++)
            Patch("Colour block", new Vector3(-2.8f + column * .8f, -1.425f + row * .95f, 3.2f),
                new Vector3(.802f, .952f, 1), materials[(row * 3 + column) % materials.Count]);
        for (int i = 0; i < 54; i++)
            Patch("Fine vertical stripe", new Vector3(-3.18f + i * .12f, 0, 3.18f),
                new Vector3(.009f, 3.8f, 1), materials[i % 2 == 0 ? 1 : 0]);
        return backdrop;
    }

    static void CaptureMaterialComparison(Camera camera, ConversationSpeechBubble quote, ConversationSpeechBubble analysis, GameObject backdrop, string name)
    {
        var frames = new[] { quote, analysis };
        var strengths = new float[frames.Length]; var opacities = new float[frames.Length];
        bool visible = backdrop.activeSelf;
        for (int i = 0; i < frames.Length; i++)
        {
            strengths[i] = frames[i].background.sharedMaterial.GetFloat("_GlassStrength");
            opacities[i] = frames[i].background.sharedMaterial.GetFloat("_Opacity");
        }
        try
        {
            backdrop.SetActive(true);
            foreach (var frame in frames)
            {
                frame.background.sharedMaterial.SetFloat("_GlassStrength", 0);
                frame.background.sharedMaterial.SetFloat("_Opacity", 1);
            }
            Capture(camera, "ShaderPreview-" + name + "-Original.png");
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i].background.sharedMaterial.SetFloat("_GlassStrength", 1);
                frames[i].background.sharedMaterial.SetFloat("_Opacity", opacities[i]);
            }
            Capture(camera, "ShaderPreview-" + name + "-MilkyGlass.png");
        }
        finally
        {
            backdrop.SetActive(visible);
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i].background.sharedMaterial.SetFloat("_GlassStrength", strengths[i]);
                frames[i].background.sharedMaterial.SetFloat("_Opacity", opacities[i]);
            }
        }
    }

    static string CheckTransparency(Camera camera, ConversationSpeechBubble quote, ConversationSpeechBubble analysis, GameObject person)
    {
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var previousColor = camera.backgroundColor;
        bool quoteVisible = quote.gameObject.activeSelf, personVisible = person.activeSelf;
        bool textVisible = analysis.caption.gameObject.activeSelf, panelVisible = analysis.background.gameObject.activeSelf;
        var material = analysis.background.sharedMaterial;
        float previousStrength = material.GetFloat("_GlassStrength"), previousOpacity = material.GetFloat("_Opacity");
        var original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D rendered = null;
        try
        {
            if (!original.LoadImage(File.ReadAllBytes(ResourcesPath + "AnalysisPanel.png")))
                throw new InvalidOperationException("Original analysis PNG could not be decoded for alpha validation.");
            quote.gameObject.SetActive(false); person.SetActive(false);
            analysis.caption.gameObject.SetActive(false); analysis.background.gameObject.SetActive(true);
            camera.backgroundColor = Color.clear;
            target.Create(); camera.targetTexture = target;
            rendered = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            void Render()
            {
                camera.Render(); RenderTexture.active = target;
                rendered.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); rendered.Apply();
            }
            var panel = analysis.background.transform;
            Vector2Int Pixel(Vector2 uv, out Vector2 sampledUv)
            {
                var screen = camera.WorldToScreenPoint(panel.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0)));
                int x = Mathf.Clamp(Mathf.RoundToInt(screen.x), 0, target.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(screen.y), 0, target.height - 1);
                var ray = camera.ScreenPointToRay(new Vector3(x + .5f, y + .5f, 0));
                if (!new Plane(panel.forward, panel.position).Raycast(ray, out float distance))
                    throw new InvalidOperationException("Could not project an alpha validation pixel onto the panel.");
                var local = panel.InverseTransformPoint(ray.GetPoint(distance));
                sampledUv = new Vector2(local.x + .5f, local.y + .5f);
                return new Vector2Int(x, y);
            }
            // Raw artwork mode is checked at opacity 1 so the expected alpha is
            // exactly the source PNG, independently of the enhanced fill control.
            material.SetFloat("_GlassStrength", 0); material.SetFloat("_Opacity", 1);
            Render();
            var centre = Pixel(new Vector2(.5f, .5f), out var centreUv);
            float expected = original.GetPixelBilinear(centreUv.x, centreUv.y).a;
            float actual = rendered.GetPixel(centre.x, centre.y).a;
            if (expected <= .1f || expected >= .9f || Mathf.Abs(actual - expected) > .035f)
                throw new InvalidOperationException("Transparent panel alpha differs from its original PNG: original=" + expected + ", rendered=" + actual);
            File.WriteAllBytes(Path.Combine(Output, "ShaderPreview-Alpha-Original.png"), rendered.EncodeToPNG());

            material.SetFloat("_GlassStrength", 1); material.SetFloat("_Opacity", previousOpacity);
            Render();
            float enhanced = rendered.GetPixel(centre.x, centre.y).a;
            if (Mathf.Abs(enhanced - previousOpacity) > .035f)
                throw new InvalidOperationException("Empty-panel centre alpha differs from the configured fallback opacity: " + enhanced);
            float maximumCorner = 0;
            foreach (var uv in new[] { new Vector2(.008f, .008f), new Vector2(.008f, .992f), new Vector2(.992f, .008f), new Vector2(.992f, .992f) })
            {
                var pixel = Pixel(uv, out var sampledUv);
                if (original.GetPixelBilinear(sampledUv.x, sampledUv.y).a > .001f)
                    throw new InvalidOperationException("An alpha-test corner intersects source artwork or shadow; choose a transparent corner.");
                maximumCorner = Mathf.Max(maximumCorner, rendered.GetPixel(pixel.x, pixel.y).a);
            }
            if (maximumCorner >= .01f)
                throw new InvalidOperationException("Enhanced fill escaped the PNG contour into a transparent corner: " + maximumCorner);
            File.WriteAllBytes(Path.Combine(Output, "ShaderPreview-Alpha-MilkyGlass.png"), rendered.EncodeToPNG());
            return "Transparent RT alpha checks passed. Original mode: source=" + expected.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                ", rendered=" + actual.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                "; enhanced centre=" + enhanced.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                ", maximum transparent corner=" + maximumCorner.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) +
                ". These check source-over alpha and the PNG contour, not device passthrough compositing.";
        }
        finally
        {
            quote.gameObject.SetActive(quoteVisible); person.SetActive(personVisible);
            analysis.caption.gameObject.SetActive(textVisible); analysis.background.gameObject.SetActive(panelVisible);
            material.SetFloat("_GlassStrength", previousStrength); material.SetFloat("_Opacity", previousOpacity);
            camera.targetTexture = previousTarget; camera.backgroundColor = previousColor; RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(original);
            if (rendered != null) UnityEngine.Object.DestroyImmediate(rendered);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void VerifyPagination(ConversationSpeechBubble bubble, string value, bool requireMultiple)
    {
        bubble.SetTranscript(value);
        if (requireMultiple && bubble.PageCount < 2) throw new InvalidOperationException("Long text did not paginate: " + bubble.name);
        var displayed = new StringBuilder();
        for (int i = 0; i < bubble.PageCount; i++)
        {
            bubble.ShowPage(i); CheckLayout(bubble);
            string pageText = bubble.caption.text;
            if (!bubble.leftAligned && pageText.StartsWith("“") && pageText.EndsWith("”"))
                pageText = pageText.Substring(1, pageText.Length - 2);
            displayed.Append(pageText);
        }
        if (Regex.Replace(displayed.ToString(), @"\s+", "") != Regex.Replace(value, @"\s+", ""))
            throw new InvalidOperationException("Text was lost or changed during pagination: " + bubble.name);
    }

    static void CheckLayout(ConversationSpeechBubble bubble)
    {
        if (string.IsNullOrEmpty(bubble.caption.text)) return;
        var bounds = bubble.caption.GetComponent<Renderer>().localBounds;
        Rect body = bubble.CaptionBounds;
        float left = (body.xMin - bubble.tailPivot.x) * bubble.width;
        float right = (body.xMax - bubble.tailPivot.x) * bubble.width;
        float bottom = (body.yMin - bubble.tailPivot.y) * bubble.ArtworkHeight;
        float top = (body.yMax - bubble.tailPivot.y) * bubble.ArtworkHeight;
        const float tolerance = .004f;
        for (int x = 0; x <= 1; x++) for (int y = 0; y <= 1; y++)
        {
            var corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y, bounds.center.z);
            var local = bubble.transform.InverseTransformPoint(bubble.caption.transform.TransformPoint(corner));
            if (local.x < left - tolerance || local.x > right + tolerance || local.y < bottom - tolerance || local.y > top + tolerance)
                throw new InvalidOperationException("Caption exceeds artwork text bounds: " + bubble.name + " / " + bubble.caption.text);
        }
        if (bubble.caption.GetComponent<Renderer>().sharedMaterial == AssetDatabase.LoadAssetAtPath<Material>(ResourcesPath + "SpeakerBubbleText.mat"))
            throw new InvalidOperationException("Preview uses a source text material instead of an owned copy.");
    }

    static string Hash(string path)
    {
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    static void CaptureBothBackgrounds(Camera camera, string name)
    {
        camera.backgroundColor = new Color(.065f, .075f, .09f, 1); Capture(camera, name + "-Dark.png");
        camera.backgroundColor = new Color(.78f, .81f, .84f, 1); Capture(camera, name + "-Light.png");
    }

    static void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active; Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}

// An explicit request lets the already-open editor validate without entering
// Play Mode or making any upstream API requests.
[InitializeOnLoad]
static class ConversationSpeechBubbleRequest
{
    static double nextCheck;
    static ConversationSpeechBubbleRequest() { EditorApplication.update += Update; }
    static void Update()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        string root = Path.GetDirectoryName(Application.dataPath);
        string request = Path.Combine(root, "speaker-bubble.request");
        if (!File.Exists(request)) return;
        File.Delete(request);
        try { ConversationSpeechBubbleSetup.InstallAndValidate(); }
        catch (Exception error)
        {
            string output = Path.Combine(root, "DualFrameCaptures"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString()); Debug.LogException(error);
        }
    }
}
