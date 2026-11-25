using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Original supplied PNGs stay intact; live fields are independently paginated TextMeshes.
[DisallowMultipleComponent]
public sealed class ConversationTemplateHud : MonoBehaviour
{
    public Font bodyFont, headingFont;
    public Shader artworkShader;
    [Min(2)] public float secondsPerPage = 8f;
    const string ResourceRoot = "Conversation/Template/";
    const float DesignWidth = 966f, DesignHeight = 302f;
    readonly List<Material> ownedMaterials = new List<Material>();
    readonly List<TextMesh> textMeshes = new List<TextMesh>();
    readonly List<TextSlot> slots = new List<TextSlot>();
    readonly List<TextMesh> guidanceLabels = new List<TextMesh>();
    readonly Dictionary<string, Texture2D> artwork = new Dictionary<string, Texture2D>();
    ConversationSpeechBubble panel;
    Transform root;
    Renderer layoutRenderer, headerArtwork, statusRenderer, photoRenderer, contextDivider;
    TextMesh headerText, statusText, reviewHeader;
    TextSlot readingSlot, cuesSlot, askSlot, needsSlot, reviewSlot;
    float unit, top, left, lastPageTime, statusCharacterSize;
    string cachedContent, currentKind = "hidden";
    int reviewPage, reviewTotal;

    public string CurrentKind => currentKind;
    public Transform LayoutRoot => root;
    public Renderer ArtworkRenderer => layoutRenderer;
    public Renderer StatusRenderer => statusRenderer;
    public Renderer PhotoRenderer => photoRenderer;
    public TextMesh HeaderText => headerText;
    public TextMesh ReadingText => readingSlot != null ? readingSlot.text : null;
    public TextMesh CuesText => cuesSlot != null ? cuesSlot.text : null;
    public TextMesh AskText => askSlot != null ? askSlot.text : null;
    public TextMesh NeedsText => needsSlot != null ? needsSlot.text : null;
    public TextMesh ReviewText => reviewSlot != null ? reviewSlot.text : null;
    public TextMesh StatusText => statusText;
    public int ReadingPageCount => readingSlot != null ? readingSlot.pages.Count : 0;
    public int CuesPageCount => cuesSlot != null ? cuesSlot.pages.Count : 0;
    public int AskPageCount => askSlot != null ? askSlot.pages.Count : 0;
    public int NeedsPageCount => needsSlot != null ? needsSlot.pages.Count : 0;
    public int CurrentPage => readingSlot != null ? readingSlot.page : 0;
    public int TotalPages => Mathf.Max(ReadingPageCount, Mathf.Max(CuesPageCount, Mathf.Max(AskPageCount, NeedsPageCount)));
    public Rect ReadingDesignBounds => new Rect(704, 206, 262, 140);
    public Rect CuesDesignBounds => new Rect(790, 85, 176, 67);
    public Rect AskDesignBounds => new Rect(0, 94, 630, 106);
    public Rect NeedsDesignBounds => new Rect(0, 258, 630, 106);
    public Rect PhotoDesignBounds => new Rect(704, 60, 64, 84);
    public float ResponseLinePixels => 44;
    public float ReadingLinePixels => 24;
    public float CuesLinePixels => 22;
    public float ReviewLinePixels => 34;
    // Normalized top-left coordinates relative to the full original template quad.
    public Rect ReadingRect => Normalize(ReadingDesignBounds);
    public Rect CuesRect => Normalize(CuesDesignBounds);
    public Rect AskRect => Normalize(AskDesignBounds);
    public Rect NeedsRect => Normalize(NeedsDesignBounds);
    public string ReadingSource => readingSlot != null ? readingSlot.source : "";
    public string CuesSource => cuesSlot != null ? cuesSlot.source : "";
    public string AskSource => askSlot != null ? askSlot.source : "";
    public string NeedsSource => needsSlot != null ? needsSlot.source : "";
    public string VisibleText {
        get { var result = new StringBuilder(); foreach (var text in textMeshes)
            if (text != null && text.gameObject.activeInHierarchy && text.GetComponent<Renderer>().enabled) result.AppendLine(text.text);
            return result.ToString(); }
    }

    public void Configure(ConversationSpeechBubble value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (root != null && panel == value) { KeepBackplate(); return; }
        ReleaseChildren(); panel = value;
        if (bodyFont == null && panel.caption != null) bodyFont = panel.caption.font;
        if (headingFont == null) headingFont = bodyFont;
        if (artworkShader == null) artworkShader = Shader.Find("Between Us/Template Artwork");
        if (artworkShader == null) throw new InvalidOperationException("Template Artwork shader must be included in the scene.");
        if (bodyFont == null) throw new InvalidOperationException("Template HUD needs the existing Noto body font.");
        panel.SetStatus(" "); panel.enabled = false; KeepBackplate();
        unit = panel.width * .90f / DesignWidth; left = -.5f * panel.width * .90f;
        top = panel.ArtworkHeight * .5f - panel.ArtworkHeight * .055f;
        root = new GameObject("Supplied Figma template HUD").transform;
        root.gameObject.layer = gameObject.layer; root.SetParent(transform, false);
        var layout = LoadArtwork("GuidanceLayout");
        // Crop only the original horizontal rule. The flattened old labels stay untouched in the PNG,
        // but are not rendered, so the response-first native labels never appear twice.
        layoutRenderer = CreateArtwork("Original full-width header rule", layout, new Rect(0, 0, DesignWidth, DesignHeight));
        layoutRenderer.sharedMaterial.SetVector("_ClipRect", new Vector4(0, (DesignHeight - 44) / DesignHeight, 1, (DesignHeight - 40) / DesignHeight));
        headerArtwork = CreateArtwork("Original YOUR TURN pixels", layout, new Rect(0, 0, DesignWidth, DesignHeight));
        headerArtwork.sharedMaterial.SetVector("_ClipRect", new Vector4(0, (DesignHeight - 38) / DesignHeight, 1, 1));
        headerText = CreateText("Partner turn heading", headingFont, 29, Vector2.zero);
        statusRenderer = CreateArtwork("Original status artwork", LoadArtwork("Start"), new Rect(0, 0, 1, 1));
        statusText = CreateText("Additional session status", bodyFont, 24, Vector2.zero);
        statusText.anchor = TextAnchor.MiddleCenter; statusText.alignment = TextAlignment.Center;
        statusText.transform.localPosition = new Vector3(0, 0, -.004f); statusCharacterSize = statusText.characterSize;
        reviewHeader = CreateText("Review heading", headingFont, 29, Vector2.zero);
        guidanceLabels.Add(CreateLabel("Try Asking", 22, new Vector2(0, 60), 1));
        guidanceLabels.Add(CreateLabel("Express Your Needs", 22, new Vector2(0, 224), 1));
        guidanceLabels.Add(CreateLabel("Visible Cues", 16, new Vector2(790, 60), .75f));
        guidanceLabels.Add(CreateLabel("Possible Reading", 18, new Vector2(704, 178), .75f));
        contextDivider = CreateArtwork("Quiet context column divider", Texture2D.whiteTexture, new Rect(667, 60, 1, 306));
        contextDivider.sharedMaterial.SetColor("_Color", new Color(1, 1, 1, .18f));
        contextDivider.sharedMaterial.SetFloat("_OutlinePixels", 0);
        readingSlot = CreateSlot("Possible Reading content", ReadingDesignBounds, 4, ReadingLinePixels);
        cuesSlot = CreateSlot("Visible Cues content", CuesDesignBounds, 2, CuesLinePixels);
        askSlot = CreateSlot("Try Asking content", AskDesignBounds, 2, ResponseLinePixels);
        needsSlot = CreateSlot("Express Your Needs content", NeedsDesignBounds, 2, ResponseLinePixels);
        readingSlot.text.color = new Color(1, 1, 1, .83f);
        cuesSlot.text.color = new Color(1, 1, 1, .83f);
        reviewSlot = CreateSlot("Review content", new Rect(0, 70, 966, 300), 8, ReviewLinePixels);
        photoRenderer = CreateArtwork("Live rounded face crop", Texture2D.whiteTexture, PhotoDesignBounds);
        photoRenderer.transform.localPosition = Position(PhotoDesignBounds.center, -.003f);
        photoRenderer.sharedMaterial.renderQueue = 4003;
        photoRenderer.sharedMaterial.SetFloat("_Roundness", .105f); photoRenderer.sharedMaterial.SetFloat("_OutlinePixels", 0);
        cachedContent = null; Hide();
    }

    public void ShowStatus(string kind, string detail = "")
    {
        EnsureConfigured(); string key = "status\u001f" + kind + "\u001f" + detail;
        if (cachedContent == key) { KeepBackplate(); return; }
        BeginState(kind, key);
        string resource = kind == "start" ? "Start" : kind == "listening" ? "Listening" : kind == "connection" ? "ConnectionInterrupted" : kind == "no_speech" ? "NoSpeech" : null;
        if (resource != null) {
            var texture = LoadArtwork(resource); statusRenderer.sharedMaterial.mainTexture = texture;
            float width = panel.width * (kind == "start" || kind == "listening" ? .52f : .79f), height = width * texture.height / texture.width;
            float maximumHeight = panel.ArtworkHeight * (kind == "listening" ? .22f : .34f);
            if (height > maximumHeight) { width *= maximumHeight / height; height = maximumHeight; }
            statusRenderer.transform.localPosition = new Vector3(0, string.IsNullOrEmpty(detail) ? 0 : .025f, -.002f);
            statusRenderer.transform.localScale = new Vector3(width, height, 1); statusRenderer.gameObject.SetActive(true);
            if (!string.IsNullOrEmpty(detail)) SetStatusText(detail, -panel.ArtworkHeight * .30f, 19);
        } else {
            string message = kind == "paused" ? "Paused\nB Resume" : kind == "transcribing" ? "Transcribing…" : kind == "analyzing" ? "Preparing Guidance…" : kind == "summary_pending" ? "Preparing Your Review…" : kind == "microphone_error" ? "Microphone Unavailable" : kind == "retry" ? "Unable to Prepare Guidance\nX Retry" : detail;
            bool showDetail = kind == "microphone_error" || kind == "retry";
            if (showDetail && !string.IsNullOrEmpty(detail) && message.IndexOf(detail, StringComparison.OrdinalIgnoreCase) < 0) message += "\n" + detail;
            float statusPixels = kind == "analyzing" || kind == "transcribing" || kind == "summary_pending" ? 40 : 24;
            SetStatusText(message, 0, statusPixels);
        }
    }

    public void ShowGuidance(string turn, string reading, string cues, string ask, string needs, Texture2D photo)
    {
        EnsureConfigured();
        string key = string.Join("\u001f", new[] { "guidance", turn ?? "", reading ?? "", cues ?? "", ask ?? "", needs ?? "" });
        if (cachedContent != key) {
            BeginState("guidance", key); layoutRenderer.gameObject.SetActive(true);
            contextDivider.gameObject.SetActive(true);
            foreach (var label in guidanceLabels) label.gameObject.SetActive(true);
            bool yourTurn = string.Equals(turn, "A", StringComparison.OrdinalIgnoreCase) || string.Equals(turn, "YOUR TURN", StringComparison.OrdinalIgnoreCase);
            headerArtwork.gameObject.SetActive(yourTurn); headerText.gameObject.SetActive(!yourTurn);
            if (!yourTurn) SetText(headerText, "PARTNER’S TURN");
            SetSlot(readingSlot, reading); SetSlot(cuesSlot, string.IsNullOrWhiteSpace(cues) ? "Facial cues unavailable" : cues);
            SetSlot(askSlot, ask); SetSlot(needsSlot, needs);
        }
        SetPhoto(photo); KeepBackplate();
    }

    public void ShowReview(string text, int page, int total)
    {
        EnsureConfigured(); string key = "review\u001f" + page + "\u001f" + total + "\u001f" + text;
        if (cachedContent == key) { KeepBackplate(); return; }
        BeginState("review", key); reviewPage = page; reviewTotal = total;
        reviewHeader.gameObject.SetActive(true);
        SetText(reviewHeader, "Conversation Review  " + (Mathf.Max(0, page) + 1) + " / " + Mathf.Max(1, total)); SetSlot(reviewSlot, text);
    }

    public void SetPhoto(Texture2D value)
    {
        if (photoRenderer == null) return;
        bool visible = value != null && currentKind == "guidance"; photoRenderer.gameObject.SetActive(visible);
        if (!visible) return;
        var material = photoRenderer.sharedMaterial; material.mainTexture = value;
        float sourceAspect = (float)value.width / value.height, destinationAspect = PhotoDesignBounds.width / PhotoDesignBounds.height;
        var scale = sourceAspect > destinationAspect ? new Vector2(destinationAspect / sourceAspect, 1) : new Vector2(1, sourceAspect / destinationAspect);
        material.mainTextureScale = scale; material.mainTextureOffset = (Vector2.one - scale) * .5f;
    }

    public void Hide() { currentKind = "hidden"; cachedContent = null; if (root != null) root.gameObject.SetActive(false); }
    public void SetPageForValidation(int index) { foreach (var slot in slots) if (slot.text.gameObject.activeSelf) RenderSlotPage(slot, index); lastPageTime = Time.unscaledTime; }
    public string GetAllPageText(string field) { var slot = field == "reading" ? readingSlot : field == "ask" ? askSlot : field == "needs" ? needsSlot : field == "cues" ? cuesSlot : reviewSlot; return slot == null ? "" : string.Join("\n", slot.pages); }
    public string CaptureValidationSnapshot() {
        return "kind=" + currentKind + "; reading=" + ReadingPageCount + "; cues=" + CuesPageCount + "; ask=" + AskPageCount + "; needs=" + NeedsPageCount + "; photo=" + (photoRenderer != null && photoRenderer.gameObject.activeSelf) + "; sourcePixels=966x302; layoutWidth=" + (unit * DesignWidth).ToString("F3", CultureInfo.InvariantCulture) + "; bodyFont=" + (bodyFont != null ? bodyFont.name : "missing") + "; responsePixels=" + ResponseLinePixels +
            "; readingPixels=" + ReadingLinePixels + "; cuesPixels=" + CuesLinePixels + "; review=" + reviewPage + "/" + reviewTotal;
    }

    void EnsureConfigured() { if (root == null) { var existing = panel != null ? panel : GetComponent<ConversationSpeechBubble>(); if (existing == null) throw new InvalidOperationException("Configure Template HUD with the analysis panel first."); Configure(existing); } }
    void KeepBackplate() { if (panel == null) return; panel.enabled = false; if (panel.caption != null) panel.caption.GetComponent<Renderer>().enabled = false; if (panel.background != null) panel.background.gameObject.SetActive(true); }
    void BeginState(string kind, string key) {
        currentKind = kind; cachedContent = key; lastPageTime = Time.unscaledTime; root.gameObject.SetActive(true);
        foreach (Transform child in root) child.gameObject.SetActive(false);
        foreach (var slot in slots) { slot.text.text = ""; slot.pageNumber.text = ""; slot.pages.Clear(); slot.source = ""; }
        KeepBackplate();
    }
    Texture2D LoadArtwork(string name) {
        if (artwork.TryGetValue(name, out var cached) && cached != null) return cached;
        var loaded = Resources.Load<Texture2D>(ResourceRoot + name); if (loaded == null) throw new InvalidOperationException("Missing original Figma PNG: " + ResourceRoot + name);
        artwork[name] = loaded; return loaded;
    }
    Renderer CreateArtwork(string name, Texture2D texture, Rect pixels) {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name = name; quad.layer = gameObject.layer; quad.transform.SetParent(root, false);
        var collider = quad.GetComponent<Collider>(); collider.enabled = false; DestroyOwned(collider);
        quad.transform.localPosition = Position(pixels.center, -.002f); quad.transform.localScale = new Vector3(pixels.width * unit, pixels.height * unit, 1);
        var material = new Material(artworkShader) { name = "Template - " + name, hideFlags = HideFlags.DontSave }; material.mainTexture = texture;
        material.SetFloat("_Aspect", pixels.width / Mathf.Max(1, pixels.height)); ownedMaterials.Add(material);
        var renderer = quad.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; return renderer;
    }
    TextMesh CreateText(string name, Font font, float linePixels, Vector2 pixelPosition) {
        var child = new GameObject(name); child.layer = gameObject.layer; child.transform.SetParent(root, false); child.transform.localPosition = Position(pixelPosition, -.004f);
        var text = child.AddComponent<TextMesh>(); text.font = font; text.fontSize = 96; text.richText = false; text.fontStyle = FontStyle.Normal;
        text.anchor = TextAnchor.UpperLeft; text.alignment = TextAlignment.Left; text.color = Color.white; text.lineSpacing = 1.15f; text.characterSize = .01f;
        var source = Resources.Load<Material>("Conversation/SpeakerBubbleText"); var shader = source != null ? source.shader : Shader.Find("Prototype/StereoCaption");
        if (shader == null) throw new InvalidOperationException("StereoCaption shader is missing.");
        var material = new Material(shader) { name = "Template white text - " + name, hideFlags = HideFlags.DontSave, renderQueue = 4003 };
        material.SetColor("_OutlineColor", new Color(.03f, .04f, .055f, .55f)); material.SetFloat("_OutlinePixels", .55f); material.mainTexture = font.material.mainTexture;
        var renderer = text.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        ownedMaterials.Add(material); textMeshes.Add(text);
        // Calibrate the actual font's line advance, rather than assuming a pixel-to-world font scale.
        SetText(text, "Hg国"); float oneHeight = renderer.localBounds.size.y;
        SetText(text, "Hg国\nHg国"); float advance = renderer.localBounds.size.y - oneHeight;
        if (advance > .00001f) text.characterSize *= linePixels * unit / advance; SetText(text, ""); return text;
    }
    TextMesh CreateLabel(string wording, float linePixels, Vector2 position, float alpha) {
        var text = CreateText(wording + " label", headingFont, linePixels, position);
        text.color = new Color(1, 1, 1, alpha); SetText(text, wording); return text;
    }
    TextSlot CreateSlot(string name, Rect bounds, int lines, float linePixels) {
        var slot = new TextSlot { bounds = bounds, lines = lines, text = CreateText(name, bodyFont, linePixels, bounds.position) };
        slot.pageNumber = CreateText(name + " page number", bodyFont, 14, new Vector2(bounds.xMax, bounds.yMax - 14));
        slot.pageNumber.anchor = TextAnchor.UpperRight; slot.pageNumber.alignment = TextAlignment.Right;
        slots.Add(slot); return slot;
    }
    void SetSlot(TextSlot slot, string value) {
        slot.source = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); slot.pages.Clear(); slot.text.gameObject.SetActive(true);
        var lines = Wrap(slot, slot.source);
        for (int start = 0; start < lines.Count; start += slot.lines) slot.pages.Add(string.Join("\n", lines.GetRange(start, Math.Min(slot.lines, lines.Count - start))));
        if (slot.pages.Count == 0) slot.pages.Add(""); RenderSlotPage(slot, 0);
    }
    List<string> Wrap(TextSlot slot, string value) {
        var lines = new List<string>(); var line = new StringBuilder(); float maximumWidth = slot.bounds.width * unit * .96f;
        var renderer = slot.text.GetComponent<Renderer>(); var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext()) {
            string element = elements.GetTextElement(); if (element == "\n") { lines.Add(line.ToString()); line.Clear(); continue; }
            SetText(slot.text, line.ToString() + element);
            if (renderer.localBounds.size.x > maximumWidth && line.Length > 0) {
                bool closing = IsClosingPunctuation(element);
                int boundary = LastWrapBoundary(line, closing);
                if (boundary > 0) {
                    string full = line.ToString(); lines.Add(full.Substring(0, boundary));
                    line.Clear(); line.Append(full.Substring(boundary));
                } else if (closing) {
                    // Move the previous complete glyph with its closing punctuation; never start a line with a lone closing mark.
                    string full = line.ToString(); int carryStart = ClosingCarryStart(full);
                    if (carryStart > 0) { lines.Add(full.Substring(0, carryStart)); line.Clear(); line.Append(full.Substring(carryStart)); }
                } else { lines.Add(line.ToString()); line.Clear(); }
            }
            line.Append(element);
        }
        if (line.Length > 0 || value.EndsWith("\n", StringComparison.Ordinal)) lines.Add(line.ToString()); return lines;
    }
    static int LastWrapBoundary(StringBuilder value, bool nextIsClosing) {
        for (int index = value.Length - 1; index >= 0; index--) {
            char character = value[index];
            bool separator = character == ' ' || character == '\t' || "，；、。！？".IndexOf(character) >= 0;
            if (!separator) continue;
            int boundary = index + 1;
            if (nextIsClosing) {
                bool hasPrecedingGlyph = false;
                for (int tail = boundary; tail < value.Length; tail++)
                    if (!char.IsWhiteSpace(value[tail]) && !IsClosingPunctuation(value[tail].ToString())) { hasPrecedingGlyph = true; break; }
                if (!hasPrecedingGlyph) continue;
            }
            return boundary;
        }
        return -1;
    }
    static bool IsClosingPunctuation(string value) {
        return !string.IsNullOrEmpty(value) && "，。；：、！？…）》】〕〉」』〗〙〛”’.,;:!?)]}»›".IndexOf(value[0]) >= 0;
    }
    static int ClosingCarryStart(string value) {
        int[] starts = StringInfo.ParseCombiningCharacters(value);
        int element = starts.Length - 1;
        while (element > 0) {
            string glyph = value.Substring(starts[element], element + 1 < starts.Length ? starts[element + 1] - starts[element] : value.Length - starts[element]);
            if (!string.IsNullOrWhiteSpace(glyph) && !IsClosingPunctuation(glyph)) break;
            element--;
        }
        return starts.Length > 0 ? starts[element] : 0;
    }
    void RenderSlotPage(TextSlot slot, int index) {
        slot.page = slot.pages.Count > 0 ? Mathf.Abs(index) % slot.pages.Count : 0;
        SetText(slot.text, slot.pages.Count > 0 ? slot.pages[slot.page] : ""); slot.pageNumber.gameObject.SetActive(slot.pages.Count > 1);
        SetText(slot.pageNumber, slot.pages.Count > 1 ? (slot.page + 1) + " / " + slot.pages.Count : "");
        // Reserve each slot's own bottom margin for the footer. No footer crosses into another field.
        var footerBounds = slot.pageNumber.GetComponent<Renderer>().localBounds;
        float footerHeight = Mathf.Max(10, footerBounds.size.y / unit);
        float bodyBottom = (top - slot.text.transform.localPosition.y - slot.text.GetComponent<Renderer>().localBounds.min.y) / unit;
        float footerY = Mathf.Max(slot.bounds.yMax - footerHeight - 2, bodyBottom + 2);
        footerY = Mathf.Min(footerY, slot.bounds.yMax - footerHeight);
        slot.pageNumber.transform.localPosition = Position(new Vector2(slot.bounds.xMax, footerY), -.004f);
    }
    void SetStatusText(string value, float y, float pixels) {
        statusText.gameObject.SetActive(true); statusText.transform.localPosition = new Vector3(0, y, -.004f); statusText.characterSize = statusCharacterSize * pixels / 24;
        SetText(statusText, value ?? ""); var bounds = statusText.GetComponent<Renderer>().localBounds.size;
        if (bounds.x > panel.width * .84f) statusText.characterSize *= panel.width * .84f / bounds.x;
    }
    void SetText(TextMesh text, string value) {
        if (text.font != null) text.font.RequestCharactersInTexture(value, text.fontSize, text.fontStyle); text.text = value;
        var material = text.GetComponent<Renderer>().sharedMaterial; if (material != null && text.font != null) material.mainTexture = text.font.material.mainTexture;
    }
    Vector3 Position(Vector2 pixels, float z) => new Vector3(left + pixels.x * unit, top - pixels.y * unit, z);
    static Rect Normalize(Rect value) => new Rect(value.x / DesignWidth, value.y / DesignHeight, value.width / DesignWidth, value.height / DesignHeight);
    void Update() {
        if (root == null || !root.gameObject.activeSelf) return; KeepBackplate(); if (Time.unscaledTime - lastPageTime < secondsPerPage) return;
        foreach (var slot in slots) if (slot.text.gameObject.activeSelf && slot.pages.Count > 1) RenderSlotPage(slot, slot.page + 1); lastPageTime = Time.unscaledTime;
    }
    void OnEnable() { Font.textureRebuilt += FontRebuilt; }
    void OnDisable() { Font.textureRebuilt -= FontRebuilt; }
    void FontRebuilt(Font font) { foreach (var text in textMeshes) if (text != null && text.font == font && text.GetComponent<Renderer>().sharedMaterial != null) text.GetComponent<Renderer>().sharedMaterial.mainTexture = font.material.mainTexture; }
    void ReleaseChildren() {
        if (root != null) DestroyOwned(root.gameObject); root = null;
        foreach (var material in ownedMaterials) if (material != null) DestroyOwned(material);
        ownedMaterials.Clear(); textMeshes.Clear(); slots.Clear(); guidanceLabels.Clear(); artwork.Clear();
    }
    void OnDestroy() { ReleaseChildren(); }
    static void DestroyOwned(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    sealed class TextSlot { public Rect bounds; public TextMesh text, pageNumber; public int lines, page; public string source = ""; public readonly List<string> pages = new List<string>(); }
}




