using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

// Runtime-only world-space controls. No Canvas, EventSystem, TMP or saved credentials.
[DisallowMultipleComponent]
public sealed class ConversationApiKeySettings : MonoBehaviour
{
    enum ActionId { Open, Key, TextModel, VisionModel, Consent, Apply, Clear, Close, Confirm, Cancel }
    enum EditField { None, Key, TextModel, VisionModel }
    sealed class Hotspot
    {
        public ActionId action;
        public BoxCollider collider;
        public Mesh mesh;
        public TextMesh label;
        public Color lastColor;
        public readonly Color[] colors = new Color[4];
    }

    const int KeyLimit = 4096;
    const float GazeSelectSeconds = 1.2f, GazeConfirmSeconds = 1.0f;
    static bool sceneHooked;
    readonly List<Hotspot> hotspots = new List<Hotspot>();
    readonly List<Mesh> ownedMeshes = new List<Mesh>();
    readonly List<Material> ownedMaterials = new List<Material>();
    PicoConversationOnDevice service;
    Transform anchor, mainPanel, confirmationPanel, entryRoot, cursor;
    TextMesh stateLabel, messageLabel, inputHint, confirmationLabel;
    Hotspot keyButton, textModelButton, visionModelButton, consentButton, entryButton;
    Material fontMaterial, shapeMaterial, pointerMaterial;
    Font font;
    LineRenderer leftRay, rightRay;
    TouchScreenKeyboard keyboard;
    EditField editing;
    ActionId pendingAction;
    bool confirming, isOpen, autoShown, wasConfigured, wasEditable, leftHeld, rightHeld, menuHeld;
    bool oldHideInput;
    float keyboardOpenedAt, gazeStartedAt;
    Hotspot gazeTarget;
    string message = "输入完整 API Key 后，点“应用（仅本次）”。";
    // These private, explicitly non-serialized strings are never assigned to a TextMesh or log.
    [NonSerialized] string draftKey = "";
    [NonSerialized] string draftTextModel = "";
    [NonSerialized] string draftVisionModel = "";
    [NonSerialized] bool draftConsent;

    public bool IsOpen => isOpen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        sceneHooked = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!sceneHooked) { SceneManager.sceneLoaded += OnSceneLoaded; sceneHooked = true; }
        AttachToServices();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { AttachToServices(); }

    static void AttachToServices()
    {
        foreach (var target in FindObjectsByType<PicoConversationOnDevice>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (target.GetComponent<ConversationApiKeySettings>() == null)
                target.gameObject.AddComponent<ConversationApiKeySettings>();
    }

    void Awake() { service = GetComponent<PicoConversationOnDevice>(); }
    void OnEnable()
    {
        Font.textureRebuilt += FontRebuilt;
        if (entryRoot != null && !isOpen) entryRoot.gameObject.SetActive(true);
    }

    void Update()
    {
        if (service == null || Camera.main == null) return;
        if (anchor == null && !Build()) return;

        bool configured = service.CredentialsConfigured;
        bool editable = service.CanConfigureCredentials;
        if (wasEditable && !editable && isOpen)
        {
            ClosePanel();
            message = "交流进行中；结束交流后才能修改 API 设置。";
        }
        if (configured != wasConfigured)
        {
            ClearDraft();
            draftTextModel = service.ConfiguredTextModel;
            draftVisionModel = service.ConfiguredVisionModel;
            if (configured)
            {
                CancelKeyboard();
                message = "配置已更新。密钥仅用于本次运行。";
            }
            else autoShown = false;
        }
        if (!configured && !autoShown)
        {
            autoShown = true;
            OpenPanel();
        }
        wasConfigured = configured;
        wasEditable = editable;

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool menu);
        bool desktopToggle = false, desktopCancel = false, desktopClick = false;
#if ENABLE_LEGACY_INPUT_MANAGER
        desktopToggle = Input.GetKeyDown(KeyCode.F2);
        desktopCancel = Input.GetKeyDown(KeyCode.Escape);
        desktopClick = Input.GetMouseButtonDown(0);
#endif
        if ((menu && !menuHeld) || desktopToggle)
        {
            if (isOpen) ClosePanel(); else OpenPanel();
        }
        menuHeld = menu;
        if (desktopCancel && isOpen)
        {
            if (keyboard != null) CancelKeyboard();
            else if (confirming) CancelConfirmation();
            else ClosePanel();
        }

        PollKeyboard();
        RefreshLabels();
        Hotspot leftHover = null, rightHover = null, gazeHover = null;
        bool hasLeft = ControllerRay(XRNode.LeftHand, leftRay, ref leftHeld, out leftHover);
        bool hasRight = ControllerRay(XRNode.RightHand, rightRay, ref rightHeld, out rightHover);
        if (!hasLeft && !hasRight)
            gazeHover = UpdateGaze();
        else { ResetGaze(); if (cursor != null) cursor.gameObject.SetActive(false); }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (desktopClick && keyboard == null)
        {
            var target = Hit(Camera.main.ScreenPointToRay(Input.mousePosition), out _);
            if (target != null) Select(target.action, false);
        }
#endif
        UpdateColors(leftHover, rightHover, gazeHover);
    }

    bool Build()
    {
        TextMesh source = null;
        foreach (var text in FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.font == null) continue;
            if (source == null) source = text;
            if (text.font.name.IndexOf("Noto", StringComparison.OrdinalIgnoreCase) >= 0) { source = text; break; }
        }
        if (source == null) return false; // The scene already holds the included CJK font.
        font = source.font;
        var sourceMaterial = source.GetComponent<Renderer>().sharedMaterial;
        if (sourceMaterial == null) sourceMaterial = font.material;
        fontMaterial = Own(new Material(sourceMaterial) { name = "API settings text", renderQueue = 4022 });
        fontMaterial.mainTexture = font.material.mainTexture;
        // Reuse the already-referenced stereo text shader, with opaque white alpha and vertex colours.
        shapeMaterial = Own(new Material(sourceMaterial) { name = "API settings shapes", renderQueue = 4020 });
        shapeMaterial.mainTexture = Texture2D.whiteTexture;
        if (shapeMaterial.HasProperty("_OutlinePixels")) shapeMaterial.SetFloat("_OutlinePixels", 0);
        pointerMaterial = Own(new Material(shapeMaterial) { name = "API settings pointers", renderQueue = 4024 });

        anchor = NewRoot("API settings - independent world space", transform);
        mainPanel = NewRoot("Session-only settings", anchor);
        Quad(mainPanel, "Panel", Vector3.zero, new Vector2(1.42f, 1.48f), new Color(.028f, .047f, .072f, .98f));
        Label(mainPanel, "Title", new Vector2(-.64f, .67f), "智谱 API · 本次运行", .043f, 1.28f);
        stateLabel = Label(mainPanel, "Configuration state", new Vector2(-.64f, .59f), "", .029f, 1.28f);
        keyButton = Button(mainPanel, ActionId.Key, new Vector2(0, .445f), new Vector2(1.28f, .13f), "", .032f);
        textModelButton = Button(mainPanel, ActionId.TextModel, new Vector2(-.33f, .275f), new Vector2(.62f, .13f), "", .028f);
        visionModelButton = Button(mainPanel, ActionId.VisionModel, new Vector2(.33f, .275f), new Vector2(.62f, .13f), "", .028f);
        consentButton = Button(mainPanel, ActionId.Consent, new Vector2(0, .10f), new Vector2(1.28f, .14f), "", .029f);
        Label(mainPanel, "Privacy", new Vector2(-.64f, -.005f),
            "语音 / 文本发往智谱；面部图像另行同意。\nKey 不写入磁盘，退出应用后失效。\n空闲、总结完成或错误停止后可配置。\n应用新配置会清除当前交流记录。", .029f, 1.28f);
        messageLabel = Label(mainPanel, "Input status", new Vector2(-.64f, -.15f), "", .028f, 1.28f);
        Button(mainPanel, ActionId.Apply, new Vector2(-.33f, -.375f), new Vector2(.62f, .095f), "应用（仅本次）", .033f);
        Button(mainPanel, ActionId.Clear, new Vector2(.33f, -.375f), new Vector2(.62f, .095f), "清除 Key", .033f);
        Button(mainPanel, ActionId.Close, new Vector2(0, -.505f), new Vector2(1.28f, .095f), "关闭 · 丢弃未提交输入", .031f);
        inputHint = Label(mainPanel, "Interaction hint", new Vector2(-.64f, -.59f), "", .026f, 1.28f);

        confirmationPanel = NewRoot("Explicit confirmation", anchor);
        Quad(confirmationPanel, "Confirmation backing", Vector3.zero, new Vector2(1.35f, .62f), new Color(.028f, .047f, .072f, 1));
        confirmationLabel = Label(confirmationPanel, "Confirm selected action", new Vector2(-.60f, .235f), "", .036f, 1.20f);
        Button(confirmationPanel, ActionId.Confirm, new Vector2(-.31f, -.17f), new Vector2(.56f, .115f), "确认", .037f);
        Button(confirmationPanel, ActionId.Cancel, new Vector2(.31f, -.17f), new Vector2(.56f, .115f), "取消", .037f);
        confirmationPanel.gameObject.SetActive(false);
        mainPanel.gameObject.SetActive(false);
        entryRoot = NewRoot("Reopen API settings", transform);
        entryButton = Button(entryRoot, ActionId.Open, Vector2.zero, new Vector2(.40f, .11f), "API 设置 · Y", .029f);
        PlaceEntry();
        leftRay = NewRay("Left API pointer");
        rightRay = NewRay("Right API pointer");
        cursor = Quad(anchor, "Gaze selection cursor", Vector3.zero, new Vector2(.012f, .012f), new Color(.35f, .94f, 1, 1)).transform;
        cursor.GetComponent<Renderer>().sharedMaterial = pointerMaterial;
        cursor.gameObject.SetActive(false);
        wasConfigured = service.CredentialsConfigured;
        wasEditable = service.CanConfigureCredentials;
        return true;
    }

    Material Own(Material material) { material.hideFlags = HideFlags.DontSave; ownedMaterials.Add(material); return material; }

    Transform NewRoot(string objectName, Transform parent)
    {
        var result = new GameObject(objectName).transform;
        result.SetParent(parent, false);
        result.gameObject.layer = gameObject.layer;
        return result;
    }

    GameObject Quad(Transform parent, string objectName, Vector3 position, Vector2 size, Color color)
    {
        var child = NewRoot(objectName, parent);
        child.localPosition = position;
        var mesh = new Mesh { name = objectName + " mesh", hideFlags = HideFlags.DontSave };
        mesh.vertices = new[] { new Vector3(-size.x / 2, -size.y / 2, 0), new Vector3(size.x / 2, -size.y / 2, 0), new Vector3(size.x / 2, size.y / 2, 0), new Vector3(-size.x / 2, size.y / 2, 0) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.colors = new[] { color, color, color, color };
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);
        child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = shapeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return child.gameObject;
    }

    TextMesh Label(Transform parent, string objectName, Vector2 position, string value, float lineHeight, float maxWidth)
    {
        var child = NewRoot(objectName, parent);
        child.localPosition = new Vector3(position.x, position.y, -.025f);
        var text = child.gameObject.AddComponent<TextMesh>();
        text.font = font; text.fontSize = 96; text.fontStyle = FontStyle.Normal;
        text.anchor = TextAnchor.UpperLeft; text.alignment = TextAlignment.Left;
        text.richText = false; text.color = new Color(.92f, .96f, 1, 1); text.lineSpacing = 1.14f;
        text.characterSize = .01f;
        var renderer = text.GetComponent<Renderer>();
        renderer.sharedMaterial = fontMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        SetText(text, "国Ag"); float single = renderer.localBounds.size.y;
        SetText(text, "国Ag\n国Ag"); float advance = renderer.localBounds.size.y - single;
        if (advance > .00001f) text.characterSize *= lineHeight / advance;
        SetText(text, value);
        FitWidth(text, maxWidth);
        return text;
    }

    Hotspot Button(Transform parent, ActionId action, Vector2 center, Vector2 size, string value, float lineHeight)
    {
        var quad = Quad(parent, action + " button", new Vector3(center.x, center.y, -.008f), size, new Color(.10f, .19f, .27f, 1));
        var collider = quad.AddComponent<BoxCollider>();
        collider.size = new Vector3(size.x, size.y, .035f);
        var text = Label(quad.transform, "Button label", Vector2.zero, value, lineHeight, size.x * .91f);
        text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
        var result = new Hotspot { action = action, collider = collider, mesh = quad.GetComponent<MeshFilter>().sharedMesh, label = text };
        hotspots.Add(result);
        return result;
    }

    void SetText(TextMesh target, string value)
    {
        if (target == null || target.text == value) return;
        font.RequestCharactersInTexture(value ?? "", 96, FontStyle.Normal);
        target.text = value ?? "";
        FontRebuilt(font);
    }

    static void FitWidth(TextMesh text, float width)
    {
        float actual = text.GetComponent<Renderer>().localBounds.size.x;
        if (actual > width) text.characterSize *= width / actual;
    }

    void FontRebuilt(Font rebuilt) { if (rebuilt == font && fontMaterial != null) fontMaterial.mainTexture = font.material.mainTexture; }

    void OpenPanel()
    {
        if (anchor == null) return;
        // Keep the conversation's central view free of a read-only settings sheet.
        if (!service.CanConfigureCredentials)
        {
            ClosePanel();
            message = "交流进行中；结束交流后才能打开 API 设置。";
            return;
        }
        CancelKeyboard(); ClearDraft(); ResetGaze(); confirming = false; isOpen = true;
        draftTextModel = service.ConfiguredTextModel;
        draftVisionModel = service.ConfiguredVisionModel;
        // A new submission always requires its own explicit opt-in, even after reopening.
        draftConsent = false;
        var camera = Camera.main.transform;
        anchor.position = camera.TransformPoint(new Vector3(-.96f, .10f, 1.85f));
        anchor.rotation = Quaternion.LookRotation(anchor.position - camera.position, Vector3.up);
        anchor.localScale = Vector3.one;
        mainPanel.gameObject.SetActive(true); confirmationPanel.gameObject.SetActive(false); entryRoot.gameObject.SetActive(false);
        message = "点击密码框输入完整 Key；更改配置须重输。\n没有系统键盘时，可用电脑 USB 配对工具录入。";
    }

    void ClosePanel()
    {
        CancelKeyboard(); ClearDraft(); ResetGaze(); isOpen = false; confirming = false;
        if (mainPanel != null) mainPanel.gameObject.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.gameObject.SetActive(false);
        if (entryRoot != null) { PlaceEntry(); entryRoot.gameObject.SetActive(true); }
        if (cursor != null) cursor.gameObject.SetActive(false);
    }

    void PlaceEntry()
    {
        if (Camera.main == null || entryRoot == null) return;
        var camera = Camera.main.transform;
        entryRoot.position = camera.TransformPoint(new Vector3(-.89f, .43f, 1.8f));
        entryRoot.rotation = Quaternion.LookRotation(entryRoot.position - camera.position, Vector3.up);
        entryRoot.localScale = Vector3.one;
    }

    void ClearDraft() { draftKey = ""; draftTextModel = ""; draftVisionModel = ""; draftConsent = false; }

    void RefreshLabels()
    {
        string state = service.CredentialsConfigured ? "已配置 · " + (service.VisualUploadAllowed ? "面部上传已同意" : "面部上传关闭") : "尚未配置 Key";
        SetText(stateLabel, state + "\n电脑 USB 配对码：" + (string.IsNullOrEmpty(service.ConfigurationPairingCode) ? "准备中" : service.ConfigurationPairingCode));
        string mask = string.IsNullOrEmpty(draftKey) ? "未输入（点击打开安全输入）" : new string('●', Math.Min(14, draftKey.Length)) + " · " + draftKey.Length + " 字符";
        SetText(keyButton.label, "API Key · 仅显示掩码\n" + mask);
        SetText(textModelButton.label, "文本模型\n" + ShortModel(draftTextModel));
        SetText(visionModelButton.label, "视觉模型\n" + ShortModel(draftVisionModel));
        SetText(consentButton.label, (draftConsent ? "☑" : "□") + " 我同意本次上传单帧面部裁剪\n可关闭；语音 / 文本处理仍使用智谱");
        SetText(entryButton.label, service.CanConfigureCredentials ? "API 设置 · Y" : "交流中 · 设置锁定\n结束后再按 Y");
        SetText(messageLabel, message);
        FitWidth(messageLabel, 1.28f);
        if (keyboard != null) SetText(inputHint, "系统输入中：完成 / 取消可返回。\n也可注视“关闭”，再确认退出；或按 Y。\nKey 始终使用密码模式。");
        else if (gazeTarget == null) SetText(inputHint, "手柄：任一扳机点击 · Y 重开 / 关闭\n无手柄：注视选择后，再注视“确认”执行。\n电脑：F2 重开，Esc 取消 / 关闭。");
    }

    static string ShortModel(string value) { return string.IsNullOrEmpty(value) ? "默认模型" : value.Length <= 30 ? value : value.Substring(0, 27) + "…"; }

    bool Enabled(ActionId action)
    {
        if (action == ActionId.Open) return service.CanConfigureCredentials;
        if (action == ActionId.Close || action == ActionId.Cancel) return true;
        if (action == ActionId.Confirm) return service.CanConfigureCredentials || pendingAction == ActionId.Close || pendingAction == ActionId.Open;
        if (!service.CanConfigureCredentials || keyboard != null) return false;
        if (action == ActionId.Apply) return !string.IsNullOrWhiteSpace(draftKey);
        if (action == ActionId.Clear) return service.CredentialsConfigured;
        return true;
    }

    void Select(ActionId action, bool byGaze)
    {
        if (!Enabled(action)) return;
        if (action == ActionId.Cancel) { CancelConfirmation(); return; }
        if (action == ActionId.Confirm)
        {
            var selected = pendingAction;
            CancelConfirmation(); Execute(selected); return;
        }
        // Opening a masked settings panel does not submit or reveal any credential.
        if (action == ActionId.Open) { OpenPanel(); return; }
        if (byGaze || action == ActionId.Clear) { AskConfirmation(action); return; }
        Execute(action);
    }

    void AskConfirmation(ActionId action)
    {
        pendingAction = action; confirming = true; ResetGaze();
        SetText(confirmationLabel, "已选择：" + ActionName(action) + "\n注视“确认”执行，或“取消”返回。\n不会显示或朗读 Key。");
        mainPanel.gameObject.SetActive(false); confirmationPanel.gameObject.SetActive(true);
    }

    void CancelConfirmation()
    {
        confirming = false; ResetGaze();
        confirmationPanel.gameObject.SetActive(false); mainPanel.gameObject.SetActive(isOpen);
    }

    static string ActionName(ActionId action)
    {
        switch (action)
        {
            case ActionId.Key: return "安全输入 API Key";
            case ActionId.TextModel: return "输入文本模型名称";
            case ActionId.VisionModel: return "输入视觉模型名称";
            case ActionId.Consent: return "切换本次面部上传同意";
            case ActionId.Apply: return "应用配置（仅本次）";
            case ActionId.Clear: return "清除本次 Key";
            case ActionId.Close: return "关闭并丢弃未提交输入";
            default: return "打开 API 设置";
        }
    }

    void Execute(ActionId action)
    {
        if (!Enabled(action)) { message = "当前无法修改设置，请先结束交流。"; return; }
        switch (action)
        {
            case ActionId.Open: OpenPanel(); break;
            case ActionId.Key: OpenKeyboard(EditField.Key); break;
            case ActionId.TextModel: OpenKeyboard(EditField.TextModel); break;
            case ActionId.VisionModel: OpenKeyboard(EditField.VisionModel); break;
            case ActionId.Consent:
                draftConsent = !draftConsent;
                message = draftConsent ? "已勾选面部上传同意；点“应用”后生效。" : "面部上传同意已取消；点“应用”后生效。";
                break;
            case ActionId.Apply:
                if (service.TryConfigureCredentials(draftKey, draftTextModel, draftVisionModel, draftConsent, out var configureError)) ClosePanel();
                else message = configureError ?? "配置未应用，请检查输入。";
                break;
            case ActionId.Clear:
                if (service.TryClearCredentials(out var clearError))
                {
                    CancelKeyboard(); ClearDraft();
                    draftTextModel = service.ConfiguredTextModel; draftVisionModel = service.ConfiguredVisionModel;
                    message = "本次 Key 已清除；面部上传同意已关闭。";
                }
                else message = clearError ?? "未能清除，请先结束交流。";
                break;
            case ActionId.Close: ClosePanel(); break;
        }
    }

    void OpenKeyboard(EditField field)
    {
        if (!service.CanConfigureCredentials) return;
        CancelKeyboard();
        if (!TouchScreenKeyboard.isSupported) { KeyboardUnavailable(); return; }
        editing = field;
        oldHideInput = TouchScreenKeyboard.hideInput;
        try
        {
            if (field == EditField.Key) TouchScreenKeyboard.hideInput = true;
            string initial = field == EditField.Key ? draftKey : field == EditField.TextModel ? draftTextModel : draftVisionModel;
            keyboard = TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, false, false, field == EditField.Key, false, "", field == EditField.Key ? KeyLimit : 128);
            keyboardOpenedAt = Time.realtimeSinceStartup;
            if (keyboard == null) { CancelKeyboard(); KeyboardUnavailable(); return; }
            message = field == EditField.Key ? "正在请求系统密码键盘。\n完成后仍须点“应用（仅本次）”。" : "输入模型名称后点键盘的“完成”。";
        }
        catch { CancelKeyboard(); KeyboardUnavailable(); }
    }

    void PollKeyboard()
    {
        if (keyboard == null) return;
        if (!service.CanConfigureCredentials) { CancelKeyboard(); ClearDraft(); return; }
        if (keyboard.status == TouchScreenKeyboard.Status.Done)
        {
            string entered = keyboard.text ?? "";
            if (editing == EditField.Key) draftKey = entered.Length <= KeyLimit ? entered : "";
            else if (editing == EditField.TextModel) draftTextModel = entered.Length <= 128 ? entered : "";
            else if (editing == EditField.VisionModel) draftVisionModel = entered.Length <= 128 ? entered : "";
            CancelKeyboard();
            message = "输入已暂存，尚未应用。\nKey 只显示掩码；关闭会丢弃未提交输入。";
        }
        else if (keyboard.status == TouchScreenKeyboard.Status.Canceled || keyboard.status == TouchScreenKeyboard.Status.LostFocus)
        {
            CancelKeyboard(); message = "已取消本次输入，未应用配置。";
        }
        else if (Time.realtimeSinceStartup - keyboardOpenedAt > 5f && !TouchScreenKeyboard.visible)
        {
            CancelKeyboard(); KeyboardUnavailable();
        }
    }

    void KeyboardUnavailable()
    {
        message = "未能打开系统键盘。\n可用电脑 USB 配对工具录入，配对码见上方。";
    }

    void CancelKeyboard()
    {
        if (keyboard != null)
        {
            try { keyboard.text = ""; } catch { }
            try { keyboard.active = false; } catch { }
            keyboard = null;
        }
        if (editing != EditField.None) TouchScreenKeyboard.hideInput = oldHideInput;
        editing = EditField.None;
    }

    LineRenderer NewRay(string objectName)
    {
        var child = NewRoot(objectName, transform);
        var line = child.gameObject.AddComponent<LineRenderer>();
        line.sharedMaterial = pointerMaterial; line.useWorldSpace = true; line.positionCount = 2;
        line.startWidth = .0025f; line.endWidth = .0015f;
        line.startColor = line.endColor = new Color(.35f, .94f, 1, .85f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        line.enabled = false; return line;
    }

    bool ControllerRay(XRNode node, LineRenderer line, ref bool held, out Hotspot hover)
    {
        hover = null;
        var device = InputDevices.GetDeviceAtXRNode(node);
        bool tracked = device.isValid;
        if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked)) tracked &= isTracked;
        bool hasPose = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
            & device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
        if (!tracked || !hasPose) { held = false; line.enabled = false; return false; }
        var space = Camera.main.transform.parent;
        var ray = new Ray(space != null ? space.TransformPoint(position) : position,
            (space != null ? space.rotation * rotation : rotation) * Vector3.forward);
        bool down;
        if (!device.TryGetFeatureValue(CommonUsages.triggerButton, out down) && device.TryGetFeatureValue(CommonUsages.trigger, out float pressure)) down = pressure > .75f;
        if (keyboard == null) hover = Hit(ray, out _);
        line.enabled = keyboard == null && (isOpen || hover != null);
        if (line.enabled)
        {
            Vector3 end = ray.GetPoint(3f);
            if (hover != null && hover.collider.Raycast(ray, out var point, 5f)) end = point.point;
            line.SetPosition(0, ray.origin); line.SetPosition(1, end);
        }
        if (down && !held && hover != null && keyboard == null) Select(hover.action, false);
        held = down;
        return true;
    }

    Hotspot Hit(Ray ray, out Vector3 point)
    {
        Hotspot nearest = null; float distance = 5f; point = ray.GetPoint(distance);
        foreach (var target in hotspots)
        {
            if (!target.collider.gameObject.activeInHierarchy || !Enabled(target.action)) continue;
            if (target.collider.Raycast(ray, out var hit, distance)) { distance = hit.distance; point = hit.point; nearest = target; }
        }
        return nearest;
    }

    Hotspot UpdateGaze()
    {
        var camera = Camera.main.transform;
        var target = Hit(new Ray(camera.position, camera.forward), out var point);
        cursor.gameObject.SetActive(target != null);
        if (target == null) { ResetGaze(); return null; }
        cursor.position = point - camera.forward * .025f; cursor.rotation = camera.rotation;
        if (target != gazeTarget) { gazeTarget = target; gazeStartedAt = Time.realtimeSinceStartup; }
        float duration = target.action == ActionId.Confirm || target.action == ActionId.Cancel ? GazeConfirmSeconds : GazeSelectSeconds;
        float progress = Mathf.Clamp01((Time.realtimeSinceStartup - gazeStartedAt) / duration);
        cursor.localScale = Vector3.one * (1f + progress);
        if (isOpen && !confirming) SetText(inputHint, "正在注视：" + ActionName(target.action) + " · " + Mathf.RoundToInt(progress * 100) + "%\n选中后须看向“确认”；看向别处可取消选择。");
        if (progress >= 1f) { ResetGaze(); Select(target.action, true); }
        return target;
    }

    void ResetGaze() { gazeTarget = null; gazeStartedAt = Time.realtimeSinceStartup; }

    void UpdateColors(Hotspot left, Hotspot right, Hotspot gaze)
    {
        foreach (var target in hotspots)
        {
            bool enabled = Enabled(target.action);
            bool hover = target == left || target == right || target == gaze;
            var color = !enabled ? new Color(.065f, .09f, .12f, 1) : hover ? new Color(.12f, .43f, .53f, 1) : new Color(.10f, .19f, .27f, 1);
            if (color != target.lastColor)
            {
                for (int i = 0; i < 4; i++) target.colors[i] = color;
                target.mesh.colors = target.colors; target.lastColor = color;
                target.label.color = enabled ? new Color(.92f, .96f, 1, 1) : new Color(.46f, .51f, .57f, 1);
            }
        }
    }

#if UNITY_EDITOR
    // Desktop-only test aid. The headset UI and input path above do not depend on IMGUI.
    void OnGUI()
    {
        if (!isOpen || service == null || confirming) return;
        GUILayout.BeginArea(new Rect(16, 16, 430, 345), GUI.skin.box);
        GUILayout.Label("Editor 辅助输入（设备使用 3D 设置面板）");
        GUILayout.Label("Key 仅内存；退出应用清除。语音 / 文本发往智谱。");
        GUI.enabled = service.CanConfigureCredentials && keyboard == null;
        GUILayout.Label("API Key（密码框）");
        draftKey = GUILayout.PasswordField(draftKey, '●', KeyLimit);
        GUILayout.Label("文本模型"); draftTextModel = GUILayout.TextField(draftTextModel, 128);
        GUILayout.Label("视觉模型"); draftVisionModel = GUILayout.TextField(draftVisionModel, 128);
        draftConsent = GUILayout.Toggle(draftConsent, "明确同意本次上传单帧面部裁剪至智谱（默认关闭）");
        GUI.enabled = Enabled(ActionId.Apply);
        if (GUILayout.Button("应用（仅本次）")) Execute(ActionId.Apply);
        GUI.enabled = Enabled(ActionId.Clear);
        if (GUILayout.Button("清除 Key…")) AskConfirmation(ActionId.Clear);
        GUI.enabled = true;
        if (GUILayout.Button("关闭 / 丢弃未提交输入")) ClosePanel();
        GUILayout.EndArea();
    }
#endif

    void OnApplicationPause(bool paused) { if (paused && isOpen) ClosePanel(); }
    void OnDisable()
    {
        Font.textureRebuilt -= FontRebuilt;
        ClosePanel();
        if (entryRoot != null) entryRoot.gameObject.SetActive(false);
        if (leftRay != null) leftRay.enabled = false;
        if (rightRay != null) rightRay.enabled = false;
    }

    void OnDestroy()
    {
        CancelKeyboard(); ClearDraft();
        if (anchor != null) Destroy(anchor.gameObject);
        if (entryRoot != null) Destroy(entryRoot.gameObject);
        if (leftRay != null) Destroy(leftRay.gameObject);
        if (rightRay != null) Destroy(rightRay.gameObject);
        foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
        foreach (var material in ownedMaterials) if (material != null) Destroy(material);
    }
}
