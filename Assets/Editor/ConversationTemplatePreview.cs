using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit offline mono validation. Synthetic patches are never user photos or session data.
public static class ConversationTemplatePreview
{
    const int Width = 2560, Height = 1440;
    const string ResourcesPath = "Assets/Resources/Conversation/";
    static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TemplateCaptures");

    [MenuItem("Prototype/Between Us/Capture template UI validation")]
    public static void InstallAndValidate()
    {
        Directory.CreateDirectory(Output);
        foreach (string file in new[] { "error.txt", "validation.txt" })
            if (File.Exists(Path.Combine(Output, file))) File.Delete(Path.Combine(Output, file));
        try { Run(); }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString(), Encoding.UTF8);
            Debug.LogException(error);
        }
    }

    static void Run()
    {
        bool previousAsync = ShaderUtil.allowAsyncCompilation;
        Scene scene = default;
        var owned = new List<UnityEngine.Object>();
        var report = new StringBuilder();
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            ConversationSpeechBubbleSetup.Install();
            CheckArtwork(report);
            foreach (string name in new[] { "Between Us/Frosted Conversation Panel", "Prototype/StereoCaption", "Hidden/Between Us/Camera Glass Blur", "Between Us/Template Artwork" })
            {
                var shader = Shader.Find(name);
                Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Shader missing or has errors: " + name);
            }
            scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Offline template UI preview"); SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("Template preview camera"); cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 44; camera.aspect = (float)Width / Height;
            camera.allowHDR = false; camera.allowMSAA = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.scene = scene;
            var regular = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Regular.otf");
            var medium = AssetDatabase.LoadAssetAtPath<Font>("Assets/Prototype/Fonts/NotoSansCJKsc-Medium.otf");
            var textTemplate = AssetDatabase.LoadAssetAtPath<Material>(ResourcesPath + "SpeakerBubbleText.mat");
            Require(regular != null && medium != null && textTemplate != null, "Existing fonts or material missing.");
            TextMesh Caption(string name, Font font)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                var text = go.AddComponent<TextMesh>(); text.font = font; text.fontSize = 96;
                var material = new Material(textTemplate) { hideFlags = HideFlags.DontSave }; owned.Add(material);
                text.GetComponent<Renderer>().sharedMaterial = material; return text;
            }
            var quote = ConversationSpeechBubble.Attach(Caption("Actual quoted words", medium)); quote.ConfigureSpeaker();
            quote.transform.localPosition = new Vector3((quote.tailPivot.x - .5f) * quote.width, .25f, 1.5f);
            var panel = ConversationSpeechBubble.AttachAnalysis(Caption("Legacy analysis caption", regular));
            panel.ConfigureAnalysis(); panel.transform.localPosition = new Vector3(0, -.25f, 1.8f);
            foreach (var frame in new[] { quote, panel })
            {
                var material = new Material(frame.background.sharedMaterial) { hideFlags = HideFlags.DontSave };
                material.SetFloat("_FlowTime", 2); owned.Add(material); frame.background.sharedMaterial = material;
            }
            var hud = panel.gameObject.AddComponent<ConversationTemplateHud>(); hud.bodyFont = regular; hud.headingFont = medium; hud.Configure(panel);
            var backdrop = CreateBackdrop(root.transform, owned);
            var photo = MakeSyntheticPhoto(); owned.Add(photo);
            var cameraSource = MakeTarget(640, 360, 24, owned);
            var intermediate = MakeTarget(320, 180, 0, owned); var blur = MakeTarget(320, 180, 0, owned);
            var blurMaterial = new Material(Shader.Find("Hidden/Between Us/Camera Glass Blur")) { hideFlags = HideFlags.DontSave };
            blurMaterial.SetFloat("_BlurRadius", 2); owned.Add(blurMaterial);
            void ProjectBackground(bool blocks, Color background)
            {
                backdrop.SetActive(blocks); camera.backgroundColor = background;
                bool quoteActive = quote.gameObject.activeSelf, panelActive = panel.gameObject.activeSelf;
                quote.gameObject.SetActive(false); panel.gameObject.SetActive(false);
                var previousTarget = camera.targetTexture;
                try
                {
                    camera.targetTexture = cameraSource; camera.Render();
                    Graphics.Blit(cameraSource, intermediate, blurMaterial, 0); Graphics.Blit(intermediate, blur, blurMaterial, 1);
                    Graphics.Blit(blur, intermediate, blurMaterial, 0); Graphics.Blit(intermediate, blur, blurMaterial, 1);
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    quote.gameObject.SetActive(quoteActive); panel.gameObject.SetActive(panelActive);
                }
                float tangent = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
                var intrinsics = new Vector4(1 / (2 * camera.aspect * tangent), 1 / (2 * tangent), .5f, .5f);
                var matrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) * camera.worldToCameraMatrix;
                foreach (var frame in new[] { quote, panel })
                {
                    var block = new MaterialPropertyBlock(); frame.background.GetPropertyBlock(block);
                    block.SetTexture("_BlurTex", blur); block.SetMatrix("_GlassWorldToCamera", matrix);
                    block.SetVector("_GlassIntrinsics", intrinsics); block.SetFloat("_GlassFlipRows", 0);
                    block.SetFloat("_CameraGlassReady", 1); block.SetFloat("_GlassStrength", 1); frame.background.SetPropertyBlock(block);
                }
            }
            void CaptureVariants(string name)
            {
                ProjectBackground(true, new Color(.12f, .15f, .19f)); Capture(camera, name + ".png");
                ProjectBackground(false, Color.black); Capture(camera, name + "-Black.png");
                ProjectBackground(false, new Color(.65f, .65f, .65f)); Capture(camera, name + "-Gray.png");
            }
            void Status(string kind, string file)
            {
                quote.SetTranscript(""); hud.ShowStatus(kind);
                Require(hud.CurrentKind == kind, "Wrong status: " + kind + " -> " + hud.CurrentKind);
                Require(!Visible(hud.PhotoRenderer), "Stale photo in status " + kind); CheckClearedGuidance(hud);
                CheckStatusSize(hud, panel, kind, report); CaptureVariants(file); report.AppendLine(hud.CaptureValidationSnapshot());
            }
            Status("start", "Ready"); Status("listening", "Listening");
            Status("no_speech", "NoSpeech"); Status("connection", "ConnectionInterrupted");
            Status("analyzing", "Analyzing"); Status("transcribing", "Transcribing"); Status("summary_pending", "SummaryPending");
            const string reading = "They may want to postpone the conversation. The reason is still unclear.";
            const string cues = "Furrowed brows. Emotion remains uncertain.";
            const string ask = "Is now a difficult time, or is something else on your mind?";
            const string needs = "I would like us to find a time to talk.";
            VerifyQuotedSpeech(quote, "I want to spend time together. I have a deadline tonight, and I cannot give this conversation my full attention until the work is finished. Could we talk tomorrow? 我愿意和你一起度过周末，只是需要先完成工作。", report);
            quote.SetTranscript("Can we talk about this later?"); CheckQuotedPage(quote);
            hud.ShowGuidance("A", reading, cues, ask, needs, photo);
            Require(Visible(hud.ArtworkRenderer), "Guidance template invisible.");
            Require(!Visible(hud.StatusRenderer), "Status artwork remains over guidance.");
            Require(Visible(hud.PhotoRenderer), "Synthetic face-slot texture invisible.");
            CheckSlots(hud); float designUnit = hud.ArtworkRenderer.transform.localScale.x / 966f;
            float replyAdvance = MeasureLineAdvance(hud.AskText) / designUnit;
            float needsAdvance = MeasureLineAdvance(hud.NeedsText) / designUnit;
            float readingAdvance = MeasureLineAdvance(hud.ReadingText) / designUnit;
            float cuesAdvance = MeasureLineAdvance(hud.CuesText) / designUnit;
            Require(replyAdvance >= 42 && needsAdvance >= 42, "Primary responses remain too small.");
            Require(replyAdvance >= readingAdvance * 1.7f && replyAdvance >= cuesAdvance * 1.7f, "Primary responses do not lead the context typography.");
            Require(hud.AskDesignBounds.width > hud.ReadingDesignBounds.width * 2, "Replies have not received the main reading area.");
            report.AppendLine("Response-first layout: actual line advance ask/needs/reading/cues=" + F(replyAdvance) + "/" + F(needsAdvance) + "/" + F(readingAdvance) + "/" + F(cuesAdvance) + " design px; responses must be at least 1.7x context typography and twice its column width.");
            CaptureVariants("Guidance-English"); report.AppendLine(hud.CaptureValidationSnapshot());
            string originalQuote = quote.caption.text;
            hud.ShowGuidance("A", "对方可能希望稍后再谈，但原因仍不确定。", "眉头微皱，情绪难以判断。", "现在是不方便聊，还是你对周末的安排有别的顾虑？", "我希望我们能约一个时间好好聊。", photo);
            Require(quote.caption.text == originalQuote, "Updating guidance changed the partner quote.");
            CheckSlots(hud); CaptureVariants("Guidance-Chinese");
            quote.SetTranscript("啊，被看穿了，我只是想攻击你而已。");
            hud.ShowGuidance("A", "对方可能在调侃，也可能借玩笑表达不满；目前仍不确定。", "眼部微弯，情绪仍需核实。", "你说的攻击，是指刚才那句，还是整体聊天态度？", "我不接受攻击，但如果你是在开玩笑，这并不好笑。", photo);
            CheckSlots(hud); CaptureVariants("Guidance-ResponseFirst");
            quote.SetTranscript("Can we talk about this later?");
            string longReading = Repeat("The request to postpone could reflect deadline pressure. Visible cues alone cannot establish the other person's intention. 新的信息也可能改变目前的理解。 ", 9);
            string longAsk = Repeat("Is it the timing that makes this difficult, or is there something about our plans you want to discuss? 现在是不方便聊吗？ ", 7);
            string longNeeds = Repeat("I would like us to choose a time when we can both pay attention. 我也希望自己的需要能够被听见。 ", 7);
            string longCues = Repeat("Brow movement is visible; emotion is uncertain. 仅描述可见动作。 ", 8);
            hud.ShowGuidance("A", longReading, longCues, longAsk, longNeeds, photo);
            VerifyPages(hud, longReading, longCues, longAsk, longNeeds, report);
            hud.SetPageForValidation(0); CaptureVariants("Guidance-Long"); Status("paused", "Paused");
            hud.ShowGuidance("A", "STALE_READING_MARKER", "STALE_CUES_MARKER", "STALE_ASK_MARKER", "STALE_NEEDS_MARKER", photo);
            hud.ShowStatus("listening"); CheckClearedGuidance(hud);
            Require(!hud.VisibleText.Contains("STALE_"), "Guidance leaked into listening.");
            Require(!Visible(hud.PhotoRenderer), "Photo leaked into listening.");
            const string review = "Conversation Review\nYou asked about timing. B explained the deadline and suggested talking tomorrow. This describes the exchange without establishing either person's private feelings.";
            quote.SetTranscript(""); hud.ShowReview(review, 0, 4);
            Require(!hud.VisibleText.Contains("STALE_"), "Guidance leaked into review.");
            Require(!Visible(hud.PhotoRenderer), "Photo leaked into review.");
            CaptureVariants("Review"); report.AppendLine(hud.CaptureValidationSnapshot()); hud.Hide();
            Require(!Visible(hud.ArtworkRenderer) && !Visible(hud.StatusRenderer) && !Visible(hud.PhotoRenderer), "Hide left artwork/photo visible.");
            foreach (var mesh in hud.GetComponentsInChildren<TextMesh>(true))
                Require(!mesh.gameObject.activeInHierarchy || !mesh.GetComponent<Renderer>().enabled || string.IsNullOrWhiteSpace(mesh.text), "Hide left text visible: " + mesh.name);
            VerifyEmotionGlass(camera, root, quote, panel, hud, photo, ProjectBackground, report);
            VerifyStandbyGlass(camera, root, quote, panel, hud, ProjectBackground, report);
            CheckArtwork(report);
            report.Insert(0, "Template validation finished UTC: " + DateTime.UtcNow.ToString("O") + "\nUnity Camera.Render and synchronous shader compilation passed.\n");
            report.AppendLine("Response-first guidance plus twelve states including Analyzing, Transcribing and SummaryPending rendered on generated blocks, black, and gray backgrounds. Independent field pagination preserves all non-whitespace characters and every rendered page remains within its slot.");
            report.AppendLine("Status-to-guidance-to-status-to-review clears stale fields/photo. Only generated color patches were used. No actual photo, service, camera, microphone, network, or Play Mode was used.");
            report.AppendLine("Existing approved glass materials were cloned for preview. Preview resources are not serialized to the saved scene.");
            report.AppendLine("Desktop mono rendering only. Headset stereo, passthrough registration, speech, comfort, and visual acceptance need a separate device check. White-text contrast remains subject to visual review.");
            File.WriteAllText(Path.Combine(Output, "validation.txt"), report.ToString(), Encoding.UTF8);
            Debug.Log("BETWEEN_US_TEMPLATE_VALIDATED " + Output);
        }
        finally
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                if (owned[i] == null) continue;
                if (owned[i] is RenderTexture target)
                {
                    if (RenderTexture.active == target) RenderTexture.active = null;
                    target.Release();
                }
                UnityEngine.Object.DestroyImmediate(owned[i]);
            }
            ShaderUtil.allowAsyncCompilation = previousAsync;
        }
    }

    // All effect comparisons use generated patches and frozen rim time. Text and
    // face renderers are hidden for pixel metrics, then restored for six palette previews.
    static void VerifyEmotionGlass(Camera camera, GameObject root, ConversationSpeechBubble quote,
        ConversationSpeechBubble panel, ConversationTemplateHud hud, Texture2D photo,
        Action<bool, Color> projectBackground, StringBuilder report)
    {
        hud.Hide(); quote.SetTranscript("");
        projectBackground(true, new Color(.12f, .15f, .19f));
        var renderers = new[] { quote.background, panel.background };
        var original = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        var sentinels = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        bool quoteActive = quote.gameObject.activeSelf;
        ConversationEmotionGlass emotion = null;
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(original[i]);
            var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block);
            block.SetFloat("_PreviewCameraSentinel", 173.25f + i);
            block.SetFloat("_FlowTime", 2); block.SetFloat("_VortexTime", 0);
            block.SetColor("_EmotionColor", Color.white); block.SetFloat("_EmotionStrength", 0);
            block.SetFloat("_VortexStrength", 0);
            renderers[i].SetPropertyBlock(block); renderers[i].GetPropertyBlock(sentinels[i]);
        }
        void SetEffectTime(float seconds)
        {
            foreach (var renderer in renderers)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                block.SetFloat("_FlowTime", 2); block.SetFloat("_VortexTime", seconds); renderer.SetPropertyBlock(block);
            }
        }
        void SentinelCheck()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block); var before = sentinels[i];
                Require(block.GetTexture("_BlurTex") == before.GetTexture("_BlurTex"), "Emotion update replaced camera texture.");
                Require(Mathf.Abs(block.GetFloat("_CameraGlassReady") - before.GetFloat("_CameraGlassReady")) < .000001f, "Emotion update replaced camera readiness.");
                Require((block.GetVector("_GlassIntrinsics") - before.GetVector("_GlassIntrinsics")).sqrMagnitude < .0000001f, "Emotion update replaced camera intrinsics.");
                var first = block.GetMatrix("_GlassWorldToCamera"); var second = before.GetMatrix("_GlassWorldToCamera");
                for (int j = 0; j < 16; j++) Require(Mathf.Abs(first[j] - second[j]) < .000001f, "Emotion update replaced camera transform.");
                Require(Mathf.Abs(block.GetFloat("_PreviewCameraSentinel") - (173.25f + i)) < .000001f, "Emotion update replaced an unrelated MPB value.");
            }
        }
        void NeutralCheck(string description)
        {
            Require(emotion.CurrentHypothesis == "难判断" && Mathf.Abs(emotion.CurrentStrength) < .000001f && ColorDifference(emotion.CurrentColor, Color.white) < .000001f,
                "Old emotion tint survived " + description);
            SentinelCheck();
        }
        try
        {
            quote.gameObject.SetActive(false);
            var baseline = CapturePixels(camera, "Emotion-DisabledBaseline.png");
            emotion = root.AddComponent<ConversationEmotionGlass>();
            emotion.Configure(renderers); emotion.vortexStrength = 0; emotion.ResetContext(true);
            SentinelCheck(); SetEffectTime(0);
            var resetDisabled = CapturePixels(camera, "Emotion-ResetDisabled.png");
            int resetMaximum = MaximumPixelDifference(baseline, resetDisabled);
            Require(resetMaximum == 0, "Zero-strength reset differs from explicitly disabled baseline: " + resetMaximum);
            report.AppendLine("Emotion/vortex both-zero baseline versus component ResetContext with vortex disabled: maximum RGB difference=" + resetMaximum + "/255 (exact).");

            emotion.vortexStrength = ConversationEmotionGlass.DefaultVortexStrength; emotion.ResetContext(true);
            SetEffectTime(0); var neutral0 = CapturePixels(camera, "Vortex-Neutral-t0.png");
            SetEffectTime(8); var neutral8 = CapturePixels(camera, "Vortex-Neutral-t8.png");
            SetEffectTime(4); var neutral4 = CapturePixels(camera, "Vortex-Neutral-t4.png");
            VerifyVortexPixels(camera, panel.background, neutral0, neutral4, report);
            string acceptedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "..", "work", "before-standby-iridescence-20261024", "Assets", "Prototype", "SpeechBubbleBackground.shader"));
            if (File.Exists(acceptedPath))
            {
                // Render both shaders in this same scene/frame. A previous-run
                // PNG also contains independently rasterised backdrop edges.
                Shader acceptedShader = null;
                var acceptedMaterials = new Material[renderers.Length];
                var currentMaterials = new Material[renderers.Length];
                try
                {
                    string source = File.ReadAllText(acceptedPath).Replace("Shader \"Between Us/Frosted Conversation Panel\"", "Shader \"Hidden/Between Us/Accepted Glass Comparison\"");
                    acceptedShader = ShaderUtil.CreateShaderAsset(source, true);
                    Require(acceptedShader != null && acceptedShader.isSupported && !ShaderUtil.ShaderHasError(acceptedShader), "Accepted comparison shader did not compile.");
                    acceptedShader.hideFlags = HideFlags.HideAndDontSave;
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        currentMaterials[i] = renderers[i].sharedMaterial;
                        acceptedMaterials[i] = new Material(currentMaterials[i]) { shader = acceptedShader, hideFlags = HideFlags.HideAndDontSave };
                        renderers[i].sharedMaterial = acceptedMaterials[i];
                    }
                    SetEffectTime(0);
                    int acceptedDifference = MaximumPixelDifference(neutral0, CapturePixels(camera, "Accepted-Conversation-Comparison.png"));
                    Require(acceptedDifference == 0, "Standby addition changed accepted conversation glass pixels: " + acceptedDifference);
                    report.AppendLine("Idle strength zero versus wearer-accepted shader rendered in the same scene: exact RGB difference=0/255 at t0.");
                }
                finally
                {
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (currentMaterials[i] != null) renderers[i].sharedMaterial = currentMaterials[i];
                        if (acceptedMaterials[i] != null) UnityEngine.Object.DestroyImmediate(acceptedMaterials[i]);
                    }
                    if (acceptedShader != null) UnityEngine.Object.DestroyImmediate(acceptedShader);
                }
            }

            emotion.SetHypothesis(101, 1, true, true, "平静", "仅凭单帧无法确定真实情绪"); emotion.StepForValidation(3);
            float calmStrength = emotion.CurrentStrength; Color calmColor = emotion.CurrentColor;
            Require(calmStrength > 0, "A generic single-frame caveat incorrectly suppressed the emotion presentation.");
            emotion.ResetContext(true); emotion.SetHypothesis(201, 1, true, true, "平静", "仅凭单帧无法确定真实情绪");
            emotion.StepForValidation(1); float firstStep = emotion.CurrentStrength;
            Require(firstStep > 0 && firstStep < calmStrength, "The emotion transition does not fade over time.");
            emotion.SetHypothesis(201, 1, true, true, "平静", "仅凭单帧无法确定真实情绪");
            Require(Mathf.Abs(emotion.CurrentStrength - firstStep) < .000001f, "An unchanged poll restarted or jumped the transition.");
            emotion.StepForValidation(1); float secondStep = emotion.CurrentStrength;
            emotion.SetHypothesis(201, 1, true, true, "平静", "仅凭单帧无法确定真实情绪"); emotion.StepForValidation(1);
            Require(secondStep > firstStep && Mathf.Abs(emotion.CurrentStrength - calmStrength) < .000001f && ColorDifference(emotion.CurrentColor, calmColor) < .000001f,
                "Repeated same-context polling prevented the transition from completing in three seconds.");
            report.AppendLine("Same session/photo/label polls at 1s and 2s preserve the transition; 3s result matches one uninterrupted 3s step. Generic single-frame caveats remain valid.");
            SentinelCheck();

            emotion.SetHypothesis(202, 1, true, true, "愉快", "");
            Require(emotion.CurrentStrength == 0 && ColorDifference(emotion.CurrentColor, Color.white) < .000001f, "A new session inherited the preceding tint.");
            emotion.StepForValidation(3); Require(emotion.CurrentStrength > 0, "New-session emotion did not fade in.");
            emotion.SetHypothesis(202, 2, true, true, "紧张", "");
            Require(emotion.CurrentStrength == 0 && ColorDifference(emotion.CurrentColor, Color.white) < .000001f, "A new photo inherited the preceding tint.");
            emotion.StepForValidation(3); Require(emotion.CurrentStrength > 0, "New-photo emotion did not fade in."); SentinelCheck();
            emotion.SetHypothesis(202, 2, true, true, "unknown-category", ""); emotion.StepForValidation(1); NeutralCheck("unknown hypothesis");
            SetEffectTime(8); var unknown = CapturePixels(camera, "Emotion-UnknownReset.png");
            Require(MaximumPixelDifference(neutral8, unknown) == 0, "Unknown hypothesis retains visible old tint.");
            emotion.SetHypothesis(203, 1, true, true, "不悦", ""); emotion.StepForValidation(3);
            emotion.SetHypothesis(203, 1, true, true, "不悦", "画面不清，无法判读面部"); emotion.StepForValidation(1); NeutralCheck("explicitly unreadable image");
            SetEffectTime(8); var unreadable = CapturePixels(camera, "Emotion-Unreadable.png");
            Require(MaximumPixelDifference(neutral8, unreadable) == 0, "Unreadable photo retains visible old tint.");
            emotion.SetHypothesis(204, 1, false, true, "愉快", ""); emotion.StepForValidation(3); NeutralCheck("missing photo");
            emotion.SetHypothesis(204, 2, true, false, "愉快", ""); emotion.StepForValidation(3); NeutralCheck("unobserved face");
            emotion.SetHypothesis(-1, 3, true, true, "愉快", ""); emotion.StepForValidation(3); NeutralCheck("invalid session");
            emotion.SetHypothesis(205, 0, true, true, "愉快", ""); emotion.StepForValidation(3); NeutralCheck("invalid photo id");
            emotion.ResetContext(true); NeutralCheck("immediate reset");
            report.AppendLine("New session/photo snapshots start at white/zero before fading in. Unknown, unreadable, missing, unobserved and invalid contexts return to neutral; unknown/unreadable renders exactly match neutral t8. Camera texture/readiness/intrinsics/transform and unrelated MPB sentinels survive all transitions/resets.");

            hud.ShowGuidance("A", "目前的解读仍需结合对话核实。", "仅描述可见动作，情绪仍不确定。", "你现在是想稍后再聊，还是有什么顾虑？", "我希望我们约个能认真交流的时间。", photo);
            CheckSlots(hud); SetEffectTime(8); emotion.ResetContext(true);
            var textGeometry = new List<(TextMesh text, string content, Matrix4x4 transform, Bounds bounds)>();
            foreach (var text in hud.GetComponentsInChildren<TextMesh>(true))
                if (Visible(text.GetComponent<Renderer>())) textGeometry.Add((text, text.text, text.transform.localToWorldMatrix, text.GetComponent<Renderer>().localBounds));
            SetEffectTime(0); CapturePixels(camera, null);
            SetEffectTime(8); var paletteNeutral = CapturePixels(camera, "Emotion-Neutral.png");
            foreach (var entry in textGeometry)
            {
                Require(entry.text.text == entry.content, "Vortex animation changed a displayed text field.");
                var matrix = entry.text.transform.localToWorldMatrix;
                for (int element = 0; element < 16; element++) Require(Mathf.Abs(matrix[element] - entry.transform[element]) < .000001f, "Vortex animation moved a text transform.");
                var bounds = entry.text.GetComponent<Renderer>().localBounds;
                Require((bounds.center - entry.bounds.center).sqrMagnitude < .0000000001f && (bounds.size - entry.bounds.size).sqrMagnitude < .0000000001f, "Vortex animation changed text geometry bounds.");
            }
            CheckSlots(hud);
            report.AppendLine("Native renders at vortex t0/t8 preserve every visible text field's content, world transform and renderer bounds. The full frosted background moves beneath separate static text layers; response-first slot bounds still pass.");
            string[] labels = { "平静", "愉快", "不悦", "惊讶", "紧张" };
            string[] files = { "Calm", "Happy", "Upset", "Surprised", "Tense" };
            for (int i = 0; i < labels.Length; i++)
            {
                emotion.SetHypothesis(301, i + 1, true, true, labels[i], "仅凭单帧无法确定真实情绪");
                Require(emotion.CurrentStrength == 0, "A palette snapshot reused the preceding tint."); emotion.StepForValidation(3); SetEffectTime(8); SentinelCheck();
                Require(emotion.CurrentHypothesis == labels[i] && emotion.CurrentStrength > 0, "Valid palette hypothesis is not shown: " + labels[i]);
                var pixels = CapturePixels(camera, "Emotion-" + files[i] + ".png"); int difference = MaximumPixelDifference(paletteNeutral, pixels);
                Require(difference >= 2, "Palette state has no measurable native RGB effect: " + labels[i]);
                var color = emotion.CurrentColor;
                report.AppendLine("Palette " + labels[i] + ": strength=" + F(emotion.CurrentStrength) + "; RGB=" + F(color.r) + "/" + F(color.g) + "/" + F(color.b) + "; maximum difference from neutral=" + difference + "/255.");
            }
            emotion.enabled = false;
            // PreviewScene does not automatically dispatch ordinary MonoBehaviour
            // lifecycle callbacks in edit mode. Exercise the real cleanup body;
            // this does not claim automatic runtime dispatch has been verified.
            var cleanupCallback = typeof(ConversationEmotionGlass).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(cleanupCallback != null, "The effect cleanup callback is missing.");
            cleanupCallback.Invoke(emotion, null);
            SentinelCheck();
            foreach (var renderer in renderers)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_EmotionStrength") == 0 && block.GetFloat("_VortexStrength") == 0, "Disabling the effect left active overrides.");
            }
            // Overlay polling can still call a disabled optional component. Such
            // public calls must not restore an effect after the opt-out cleanup.
            emotion.SetHypothesis(901, 1, true, true, "愉快", "仅凭单帧无法确定真实情绪");
            emotion.ResetContext(); emotion.StepForValidation(3); SentinelCheck();
            Require(emotion.CurrentStrength == 0, "Disabled public calls restored internal emotion strength.");
            foreach (var renderer in renderers)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_EmotionStrength") == 0 && block.GetFloat("_VortexStrength") == 0,
                    "Disabled public calls restored emotion/vortex overrides.");
            }
            report.AppendLine("Disabled-component regression: valid SetHypothesis, ResetContext and StepForValidation(3) leave CurrentStrength and both shader strengths at zero while camera/unrelated MPB sentinels remain unchanged.");            report.AppendLine("The actual cleanup callback body was invoked through managed reflection in edit mode; automatic runtime lifecycle dispatch was not tested. Its body clears only the emotion/vortex fields and preserves camera/unrelated MPB sentinels.");
            report.AppendLine("Six palette-state review images preserve response-first text, original PNG artwork, panel bounds and the fixed _FlowTime=2 rim. _VortexTime=0/8 makes motion deterministic. The 39 response-first baseline captures remain separate and unchanged.");
        }
        finally
        {
            if (emotion != null) { emotion.enabled = false; UnityEngine.Object.DestroyImmediate(emotion); }
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].SetPropertyBlock(original[i]);
            hud.Hide(); quote.gameObject.SetActive(quoteActive);
        }
    }
    static void VerifyStandbyGlass(Camera camera, GameObject root, ConversationSpeechBubble quote,
        ConversationSpeechBubble panel, ConversationTemplateHud hud, Action<bool, Color> projectBackground, StringBuilder report)
    {
        hud.Hide(); quote.SetTranscript("");
        projectBackground(true, new Color(.12f, .15f, .19f));
        var renderers = new[] { quote.background, panel.background };
        var original = new[] { new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        bool quoteActive = quote.gameObject.activeSelf;
        ConversationEmotionGlass effect = null;
        for (int i = 0; i < renderers.Length; i++) renderers[i].GetPropertyBlock(original[i]);
        void TimeAt(float seconds)
        {
            foreach (var renderer in renderers)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                block.SetFloat("_FlowTime", 2); block.SetFloat("_VortexTime", seconds); renderer.SetPropertyBlock(block);
            }
        }
        void PreserveCamera()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block);
                Require(block.GetTexture("_BlurTex") == original[i].GetTexture("_BlurTex"), "Standby replaced the blurred camera texture.");
                Require(Mathf.Abs(block.GetFloat("_CameraGlassReady") - original[i].GetFloat("_CameraGlassReady")) < .000001f, "Standby changed camera readiness.");
                Require((block.GetVector("_GlassIntrinsics") - original[i].GetVector("_GlassIntrinsics")).sqrMagnitude < .0000001f, "Standby changed intrinsics.");
                var before = original[i].GetMatrix("_GlassWorldToCamera"); var after = block.GetMatrix("_GlassWorldToCamera");
                for (int j = 0; j < 16; j++) Require(Mathf.Abs(before[j] - after[j]) < .000001f, "Standby changed camera pose.");
            }
        }
        try
        {
            quote.gameObject.SetActive(false);
            effect = root.AddComponent<ConversationEmotionGlass>(); effect.Configure(renderers);
            // Isolate the new rotating film from the accepted background flow.
            effect.vortexStrength = 0; effect.SetStandby(false, true); effect.ResetContext(); TimeAt(0);
            var baseline = CapturePixels(camera, "Standby-Off.png");
            effect.SetStandby(true); effect.StepForValidation(.3f);
            float first = effect.CurrentStandbyStrength;
            Require(first > 0 && first < 1, "Standby tint did not begin a gradual transition.");
            effect.SetStandby(true); effect.ResetContext(); effect.StepForValidation(.3f);
            float second = effect.CurrentStandbyStrength;
            effect.SetStandby(true); effect.ResetContext(); effect.StepForValidation(.6f);
            Require(second > first && effect.CurrentStandbyStrength > .999f, "Repeated idle status polls restarted its transition.");
            Require(effect.CurrentStrength == 0, "Decorative standby created an emotion reading.");
            PreserveCamera(); TimeAt(0); var firstFrame = CapturePixels(camera, "Standby-Film-t0.png");
            TimeAt(4); var secondFrame = CapturePixels(camera, "Standby-Film-t4.png");
            int addedDifference = MaximumPixelDifference(baseline, firstFrame);
            Require(addedDifference >= 12, "Standby rainbow film is too faint compared with neutral glass.");
            report.AppendLine("Standby film isolated with accepted vortex strength zero; extra colour difference from neutral=" + addedDifference + "/255. Following motion metrics measure the rotating film alone.");
            VerifyVortexPixels(camera, panel.background, firstFrame, secondFrame, report, "Standby rotating rainbow only at deterministic t0/t4");
            PreserveCamera();
            effect.vortexStrength = ConversationEmotionGlass.DefaultVortexStrength;
            hud.ShowStatus("start");
            var panelTransform = panel.background.transform.localToWorldMatrix;
            var artworkTransform = hud.StatusRenderer.transform.localToWorldMatrix;
            var artworkTexture = hud.StatusRenderer.sharedMaterial.mainTexture;
            foreach (float seconds in new[] { 0f, 4f, 8f })
            {
                TimeAt(seconds); CapturePixels(camera, "Standby-Start-t" + seconds.ToString("0", CultureInfo.InvariantCulture) + ".png");
                Require(hud.CurrentKind == "start" && hud.StatusRenderer.sharedMaterial.mainTexture == artworkTexture, "Standby flow replaced the start artwork.");
                for (int j = 0; j < 16; j++)
                {
                    Require(Mathf.Abs(panel.background.transform.localToWorldMatrix[j] - panelTransform[j]) < .000001f, "Standby rotated the UI geometry.");
                    Require(Mathf.Abs(hud.StatusRenderer.transform.localToWorldMatrix[j] - artworkTransform[j]) < .000001f, "Standby rotated the PRESS X artwork.");
                }
            }
            hud.Hide(); effect.vortexStrength = 0;
            effect.SetStandby(false); effect.StepForValidation(1.2f); TimeAt(0);
            Require(effect.CurrentStandbyStrength == 0, "Standby did not fade out when leaving idle.");
            var ended = CapturePixels(camera, "Standby-Exited.png");
            Require(MaximumPixelDifference(baseline, ended) == 0, "Standby left residual tint after exit.");
            effect.SetStandby(true, true); effect.SetStandby(false, true);
            Require(effect.CurrentStandbyStrength == 0, "Immediate focus/pause-style standby reset failed.");
            effect.SetStandby(true, true); effect.enabled = false;
            typeof(ConversationEmotionGlass).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(effect, null);
            effect.SetStandby(true, true); effect.ResetContext(); effect.StepForValidation(2);
            Require(effect.CurrentStandbyStrength == 0, "Disabled public calls restored standby film.");
            foreach (var renderer in renderers)
            {
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                Require(block.GetFloat("_IdleIridescence") == 0 && block.GetFloat("_VortexStrength") == 0 && block.GetFloat("_EmotionStrength") == 0, "Disabled component left visual effects active.");
            }
            PreserveCamera();
            report.AppendLine("Standby: 0.9s independent fade survives repeated ResetContext/status polls; no emotion label is created; exit restores exact neutral pixels; focus/pause-style immediate reset and disabled public calls clear the film. PRESS X source texture and UI transforms stay fixed at t0/t4/t8. Camera block survives all changes. Automatic Overlay phase gating and device comfort require separate runtime checks.");
        }
        finally
        {
            if (effect != null) { effect.enabled = false; UnityEngine.Object.DestroyImmediate(effect); }
            for (int i = 0; i < renderers.Length; i++) renderers[i].SetPropertyBlock(original[i]);
            hud.Hide(); quote.gameObject.SetActive(quoteActive);
        }
    }
    static void VerifyVortexPixels(Camera camera, Renderer panel, Color32[] first, Color32[] second, StringBuilder report, string description = "Full-glass continuous neutral flow .85 at deterministic t0/t4")
    {
        var bottomLeft = camera.WorldToViewportPoint(panel.transform.TransformPoint(new Vector3(-.5f, -.5f, 0)));
        var topRight = camera.WorldToViewportPoint(panel.transform.TransformPoint(new Vector3(.5f, .5f, 0)));
        Require(topRight.x > bottomLeft.x && topRight.y > bottomLeft.y, "Effect preview panel is not axis-aligned in the test camera.");
        int outsideMaximum = 0, centreMaximum = 0, centreChanged = 0, centreCount = 0, beltMaximum = 0, beltChanged = 0, beltCount = 0;
        long beltSum = 0, centreSum = 0;
        for (int y = 0; y < Height; y += 2) for (int x = 0; x < Width; x += 2)
        {
            int index = y * Width + x; int difference = PixelDifference(first[index], second[index]);
            float u = ((x + .5f) / Width - bottomLeft.x) / (topRight.x - bottomLeft.x);
            float v = ((y + .5f) / Height - bottomLeft.y) / (topRight.y - bottomLeft.y);
            if (u < -.01f || u > 1.01f || v < -.01f || v > 1.01f) outsideMaximum = Math.Max(outsideMaximum, difference);
                        else if (u > .23f && u < .77f && v > .31f && v < .77f)
            {
                centreMaximum = Math.Max(centreMaximum, difference); centreSum += difference; centreCount++;
                if (difference >= 2) centreChanged++;
            }
            else if (u > .035f && u < .965f && v > .035f && v < .965f && Mathf.Max(Mathf.Abs(u - .5f) / .37f, Mathf.Abs(v - .54f) / .33f) > 1.18f)
            {
                beltMaximum = Math.Max(beltMaximum, difference); beltSum += difference; beltCount++;
                if (difference >= 2) beltChanged++;
            }
        }
        Require(outsideMaximum == 0, "Soft glass flow changed pixels outside the panel: " + outsideMaximum);
        Require(centreCount > 0 && centreChanged > centreCount / 2 && centreMaximum >= 18 && centreSum / (double)centreCount >= 5, "The full-glass vortex remains too faint in the central frosted background.");
        Require(beltCount > 0 && beltChanged > beltCount / 2 && beltMaximum >= 18 && beltSum / (double)beltCount >= 5, "Neutral soft flow remains too faint on the outer frosted background.");
        report.AppendLine(description + ": central changed samples=" + centreChanged + "/" + centreCount + "; centre maximum RGB difference=" + centreMaximum + "/255; centre mean maximum-channel difference=" + (centreSum / (double)centreCount).ToString("F3", CultureInfo.InvariantCulture) + "/255; changed outer-glass samples=" + beltChanged + "/" + beltCount + "; outer-glass maximum=" + beltMaximum + "/255; outer-glass mean=" + (beltSum / (double)beltCount).ToString("F3", CultureInfo.InvariantCulture) + "/255; outside maximum=" + outsideMaximum + "/255. Metrics hide text/photo and freeze rim time. Both central UV .23-.77/.31-.77 and outer glass must move; pixels outside the panel remain identical.");
    }
    static float ColorDifference(Color first, Color second) => Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Max(Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));
    static int PixelDifference(Color32 first, Color32 second) => Math.Max(Math.Abs(first.r - second.r), Math.Max(Math.Abs(first.g - second.g), Math.Abs(first.b - second.b)));
    static int MaximumPixelDifference(Color32[] first, Color32[] second)
    {
        Require(first.Length == second.Length, "Pixel comparison sizes differ."); int maximum = 0;
        for (int i = 0; i < first.Length; i++) maximum = Math.Max(maximum, PixelDifference(first[i], second[i])); return maximum;
    }
    static Color32[] CapturePixels(Camera camera, string name)
    {
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
            if (!string.IsNullOrEmpty(name)) File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG()); return image.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    // Measures the actual TextMesh/font line advance, then restores its shown text.
    // This runs only on preview-owned objects; font rebuild callbacks keep their atlas textures current.
    static float MeasureLineAdvance(TextMesh text)
    {
        Require(text != null && text.font != null, "Cannot measure missing preview text.");
        string previous = text.text;
        var renderer = text.GetComponent<Renderer>();
        try
        {
            text.font.RequestCharactersInTexture("Hg国\nHg国", text.fontSize, text.fontStyle);
            text.text = "Hg国"; float one = renderer.localBounds.size.y;
            text.text = "Hg国\nHg国"; return renderer.localBounds.size.y - one;
        }
        finally
        {
            text.font.RequestCharactersInTexture(previous, text.fontSize, text.fontStyle);
            text.text = previous;
            if (renderer.sharedMaterial != null) renderer.sharedMaterial.mainTexture = text.font.material.mainTexture;
        }
    }
    static void CheckStatusSize(ConversationTemplateHud hud, ConversationSpeechBubble panel, string kind, StringBuilder report)
    {
        if (kind == "start" || kind == "listening")
        {
            var renderer = hud.StatusRenderer;
            Require(Visible(renderer), "The status artwork is missing for size validation: " + kind);
            var texture = renderer.sharedMaterial.mainTexture;
            float aspect = (float)texture.width / texture.height;
            float baselineWidth = panel.width * (kind == "start" ? .72f : .79f);
            float baselineHeight = baselineWidth / aspect;
            if (baselineHeight > panel.ArtworkHeight * .34f) baselineWidth *= panel.ArtworkHeight * .34f / baselineHeight;
            float actual = renderer.transform.localScale.x;
            Require(actual < baselineWidth * .95f, "The start/listening graphic was not reduced: " + kind);
            report.AppendLine(kind + " artwork width/panel=" + F(actual / panel.width) + "; previous source policy effective ratio=" + F(baselineWidth / panel.width) + "; current/previous=" + F(actual / baselineWidth) + "x. Baseline is the prior .72/.79 width with .34 panel-height cap, not a device pixel measurement.");
        }
        if (kind == "analyzing" || kind == "transcribing" || kind == "summary_pending")
        {
            Require(Visible(hud.StatusText.GetComponent<Renderer>()), "The processing status is invisible: " + kind);
            float pixels = MeasureLineAdvance(hud.StatusText) / (hud.ArtworkRenderer.transform.localScale.x / 966f);
            Require(pixels >= 38, "Processing status remains too small: " + kind + "=" + pixels);
            report.AppendLine(kind + " actual TextMesh line advance=" + F(pixels) + " design px; relative to previous 24 px=" + F(pixels / 24) + "x.");
        }
    }
    static void VerifyQuotedSpeech(ConversationSpeechBubble quote, string raw, StringBuilder report)
    {
        quote.SetTranscript(raw); Require(quote.PageCount > 1, "Long quoted speech did not paginate.");
        var assembled = new StringBuilder();
        for (int i = 0; i < quote.PageCount; i++)
        {
            quote.ShowPage(i); CheckQuotedPage(quote);
            assembled.Append(quote.caption.text.Substring(1, quote.caption.text.Length - 2));
        }
        Require(Compact(assembled.ToString()) == Compact(raw), "Decorating quoted speech changed the raw transcript content.");
        report.AppendLine("All " + quote.PageCount + " long-speech pages have white text and opening/closing curly double quotes, fit bubble text bounds, and preserve raw non-whitespace characters.");
    }
    static void CheckQuotedPage(ConversationSpeechBubble quote)
    {
        string shown = quote.caption.text;
        if (string.IsNullOrEmpty(shown)) return;
        Require(shown.StartsWith("“", StringComparison.Ordinal) && shown.EndsWith("”", StringComparison.Ordinal), "A speech page is missing double quotes.");
        var color = quote.caption.color;
        Require(color.r > .99f && color.g > .99f && color.b > .99f, "Quoted speech is not white.");
        var bounds = quote.caption.GetComponent<Renderer>().localBounds; Rect box = quote.CaptionBounds;
        float left = (box.xMin - quote.tailPivot.x) * quote.width, right = (box.xMax - quote.tailPivot.x) * quote.width;
        float bottom = (box.yMin - quote.tailPivot.y) * quote.ArtworkHeight, top = (box.yMax - quote.tailPivot.y) * quote.ArtworkHeight;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
        {
            var corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y, bounds.center.z);
            var local = quote.transform.InverseTransformPoint(quote.caption.transform.TransformPoint(corner));
            Require(local.x >= left - .004f && local.x <= right + .004f && local.y >= bottom - .004f && local.y <= top + .004f, "Double-quoted speech exceeds its bubble bounds.");
        }
    }
    static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
    static void CheckArtwork(StringBuilder report)
    {
        var inputs = new[] { new[] { "Start", "Group 7.png" }, new[] { "GuidanceLayout", "Group 3.png" },
            new[] { "Listening", "Group 4 (1).png" }, new[] { "ConnectionInterrupted", "Group 5.png" }, new[] { "NoSpeech", "Group 6.png" } };
        foreach (var input in inputs)
        {
            string path = ResourcesPath + "Template/" + input[0] + ".png";
            string source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", input[1]);
            Require(File.Exists(source) && File.Exists(path), "Template missing: " + input[0]);
            string hash = Hash(path); Require(hash == Hash(source), "Supplied PNG bytes changed: " + input[0]);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Require(texture.LoadImage(File.ReadAllBytes(path)), "Cannot decode: " + input[0]);
                int transparent = 0, visible = 0;
                foreach (var pixel in texture.GetPixels32()) { if (pixel.a <= 1) transparent++; if (pixel.a >= 128) visible++; }
                Require(transparent > 0 && visible > 0, "Template alpha opaque or empty: " + input[0]);
                report.AppendLine(input[0] + " PNG " + texture.width + "x" + texture.height + "; transparent pixels=" + transparent + "; SHA256=" + hash);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        Require(Hash(ResourcesPath + "SpeakerBubble.png") == "de4c96e6c01cb09c4dc61eee97196da54fc26315b4071c19838e1dd14877500c", "Original speaker PNG changed.");
        Require(Hash(ResourcesPath + "AnalysisPanel.png") == "fa4dc9563ac315298b2348232e88a249b9bf44a470e90115dbed73a3a1612b5c", "Original panel PNG changed.");
    }
    static void CheckClearedGuidance(ConversationTemplateHud hud)
    {
        foreach (var text in new[] { hud.ReadingText, hud.CuesText, hud.AskText, hud.NeedsText })
            Require(text == null || string.IsNullOrEmpty(text.text), "Old guidance retained in status.");
    }
    static void CheckSlots(ConversationTemplateHud hud)
    {
        CheckSlot(hud, hud.ReadingText, hud.ReadingRect); CheckSlot(hud, hud.CuesText, hud.CuesRect);
        CheckSlot(hud, hud.AskText, hud.AskRect); CheckSlot(hud, hud.NeedsText, hud.NeedsRect);
    }
    static void CheckSlot(ConversationTemplateHud hud, TextMesh text, Rect rect)
    {
        if (text == null || string.IsNullOrWhiteSpace(text.text)) return;
        Require(!Regex.IsMatch(text.text, @"(^|\n)[，。！？；：、）】》」』]"), "Closing punctuation starts a line: " + text.name);
        Require(text.color.r > .9f && text.color.g > .9f && text.color.b > .9f, "Dynamic content not white: " + text.name);
        var bounds = text.GetComponent<Renderer>().localBounds; var artwork = hud.ArtworkRenderer.transform;
        const float tolerance = .008f;
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
        {
            var corner = new Vector3(x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y, bounds.center.z);
            var local = artwork.InverseTransformPoint(text.transform.TransformPoint(corner));
            float u = local.x + .5f, v = .5f - local.y;
            Require(u >= rect.xMin - tolerance && u <= rect.xMax + tolerance && v >= rect.yMin - tolerance && v <= rect.yMax + tolerance,
                "Text exceeds slot: " + text.name + " at " + u.ToString("F4", CultureInfo.InvariantCulture) + "," + v.ToString("F4", CultureInfo.InvariantCulture));
        }
    }
    static void VerifyPages(ConversationTemplateHud hud, string reading, string cues, string ask, string needs, StringBuilder report)
    {
        int[] counts = { hud.ReadingPageCount, hud.CuesPageCount, hud.AskPageCount, hud.NeedsPageCount };
        foreach (int count in counts) Require(count > 1, "Long field did not paginate.");
        var accumulated = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder(), new StringBuilder() };
        int maximum = Mathf.Max(Mathf.Max(counts[0], counts[1]), Mathf.Max(counts[2], counts[3]));
        for (int i = 0; i < maximum; i++)
        {
            hud.SetPageForValidation(i); CheckSlots(hud);
            TextMesh[] fields = { hud.ReadingText, hud.CuesText, hud.AskText, hud.NeedsText };
            for (int j = 0; j < fields.Length; j++) if (i < counts[j]) accumulated[j].Append(fields[j].text);
        }
        string[] originals = { reading, cues, ask, needs };
        for (int i = 0; i < originals.Length; i++) Require(Compact(accumulated[i].ToString()) == Compact(originals[i]), "Pagination lost characters in field " + i);
        report.AppendLine("Long-text page counts reading/cues/ask/needs=" + string.Join("/", counts));
    }
    static RenderTexture MakeTarget(int width, int height, int depth, List<UnityEngine.Object> owned)
    {
        var target = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.DontSave };
        target.Create(); owned.Add(target); return target;
    }
    static Texture2D MakeSyntheticPhoto()
    {
        var texture = new Texture2D(132, 174, TextureFormat.RGBA32, false) { name = "Synthetic face-slot patches", hideFlags = HideFlags.DontSave };
        var pixels = new Color[132 * 174];
        for (int y = 0; y < 174; y++) for (int x = 0; x < 132; x++) pixels[y * 132 + x] = (x / 22 + y / 29) % 2 == 0 ? new Color(.27f, .49f, .57f) : new Color(.68f, .44f, .32f);
        texture.SetPixels(pixels); texture.Apply(); return texture;
    }
    static GameObject CreateBackdrop(Transform parent, List<UnityEngine.Object> owned)
    {
        var root = new GameObject("Synthetic background patches"); root.transform.SetParent(parent, false);
        Color[] colours = { new Color(.12f, .20f, .28f), new Color(.70f, .75f, .80f), new Color(.66f, .40f, .30f), new Color(.30f, .48f, .42f), new Color(.88f, .84f, .75f) };
        var materials = new Material[colours.Length];
        for (int i = 0; i < colours.Length; i++) { materials[i] = new Material(Shader.Find("Unlit/Color")) { color = colours[i], hideFlags = HideFlags.DontSave }; owned.Add(materials[i]); }
        for (int y = 0; y < 5; y++) for (int x = 0; x < 10; x++)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Quad); patch.transform.SetParent(root.transform, false);
            patch.transform.localPosition = new Vector3(-2.7f + x * .6f, -1.2f + y * .6f, 3.2f); patch.transform.localScale = new Vector3(.602f, .602f, 1);
            UnityEngine.Object.DestroyImmediate(patch.GetComponent<Collider>()); patch.GetComponent<Renderer>().sharedMaterial = materials[(x + y * 2) % materials.Length];
        }
        return root;
    }
    static void Capture(Camera camera, string name)
    {
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static bool Visible(Renderer renderer) => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy;
    static string Repeat(string value, int count) { var result = new StringBuilder(); for (int i = 0; i < count; i++) result.Append(value); return result.ToString(); }
    static string Compact(string value) => Regex.Replace(value ?? "", @"\s+", "");
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Hash(string path) { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
}

[InitializeOnLoad]
static class ConversationTemplatePreviewRequest
{
    static bool running; static double nextCheck;
    static ConversationTemplatePreviewRequest() { EditorApplication.update += Update; }
    static void Update()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < nextCheck) return; nextCheck = EditorApplication.timeSinceStartup + 1;
        string request = Path.Combine(Path.GetDirectoryName(Application.dataPath), "template-ui.request");
        if (!File.Exists(request)) return; File.Delete(request); running = true;
        try { ConversationTemplatePreview.InstallAndValidate(); } finally { running = false; }
    }
}