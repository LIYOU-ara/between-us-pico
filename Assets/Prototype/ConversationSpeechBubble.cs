using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Speaker artwork is anchored at its tail; the private analysis card at its centre.
[DisallowMultipleComponent]
public sealed class ConversationSpeechBubble : MonoBehaviour
{
    public TextMesh caption;
    public Renderer background;
    public Material plainMaterial;
    [Min(.2f)] public float width = 1.25f;
    [Min(8)] public int lineWidth = 25;
    [Min(1)] public int linesPerPage = 2;
    [Min(2)] public float secondsPerPage = 5f;
    public Vector2 tailPivot = new Vector2(.675f, .075f);
    public string artworkResource = "SpeakerBubble";
    public string backgroundMaterialResource = "SpeakerBubbleBackground";
    public Rect captionBounds = new Rect(.075f, .335f, .85f, .61f);
    public float maximumLineHeight = .045f;
    public bool leftAligned, loopPages;

    readonly List<string> pages = new List<string>();
    string transcript;
    bool plain;
    bool status;
    int page;
    float pageStarted;
    Material textMaterial;
    const string Root = "Conversation/";
    public int PageCount => pages.Count;
    public bool IsTranscript => !plain;
    public Rect CaptionBounds => captionBounds;
    public float ArtworkHeight
    {
        get
        {
            var texture = Resources.Load<Texture2D>(Root + artworkResource);
            return texture != null ? width * texture.height / texture.width : width * 216f / 1181;
        }
    }

    public void ConfigureSpeaker()
    {
        width = 1.25f; lineWidth = 25; linesPerPage = 2; secondsPerPage = 5f;
        tailPivot = new Vector2(.675f, .075f);
        artworkResource = "SpeakerBubble"; backgroundMaterialResource = "SpeakerBubbleBackground";
        captionBounds = new Rect(.075f, .335f, .85f, .61f);
        maximumLineHeight = .06f; leftAligned = false; loopPages = false;
        transcript = null;
        if (background != null) background.sharedMaterial = Resources.Load<Material>(Root + backgroundMaterialResource);
    }

    public void ConfigureAnalysis()
    {
        width = 1.5f; lineWidth = 28; linesPerPage = 6; secondsPerPage = 8f;
        tailPivot = new Vector2(.5f, .5f);
        artworkResource = "AnalysisPanel"; backgroundMaterialResource = "AnalysisPanelBackground";
        captionBounds = new Rect(.065f, .12f, .87f, .76f);
        maximumLineHeight = .065f; leftAligned = true; loopPages = true;
        transcript = null;
        if (background != null) background.sharedMaterial = Resources.Load<Material>(Root + backgroundMaterialResource);
    }

    public static ConversationSpeechBubble Attach(TextMesh text)
    {
        var panel = AttachPanel(text, "Other speaker bubble - tail anchor");
        panel.ConfigureSpeaker();
        return panel;
    }

    public static ConversationSpeechBubble AttachAnalysis(TextMesh text)
    {
        var panel = AttachPanel(text, "Private AI guidance - lower card");
        panel.ConfigureAnalysis();
        return panel;
    }

    static ConversationSpeechBubble AttachPanel(TextMesh text, string name)
    {
        var existing = text.GetComponentInParent<ConversationSpeechBubble>(true);
        if (existing != null) return existing;
        var anchor = new GameObject(name);
        anchor.layer = text.gameObject.layer;
        anchor.transform.SetParent(text.transform.parent, false);
        anchor.transform.localPosition = text.transform.localPosition;
        anchor.transform.localRotation = text.transform.localRotation;
        var bubble = anchor.AddComponent<ConversationSpeechBubble>();
        bubble.caption = text;
        bubble.plainMaterial = text.GetComponent<Renderer>().sharedMaterial;
        text.transform.SetParent(anchor.transform, false);
        text.transform.localPosition = Vector3.zero;
        text.transform.localRotation = Quaternion.identity;
        text.transform.localScale = Vector3.one;
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "Original Figma artwork";
        panel.layer = anchor.layer;
        panel.transform.SetParent(anchor.transform, false);
        var collider = panel.GetComponent<Collider>();
        collider.enabled = false;
        if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        bubble.background = panel.GetComponent<Renderer>();
        bubble.background.sharedMaterial = Resources.Load<Material>(Root + bubble.backgroundMaterialResource);
        bubble.background.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bubble.background.receiveShadows = false;
        bubble.SetTranscript("");
        return bubble;
    }

    void OnEnable() { Font.textureRebuilt += FontRebuilt; }
    void OnDisable() { Font.textureRebuilt -= FontRebuilt; }
    void FontRebuilt(Font font)
    {
        if (caption != null && font == caption.font && textMaterial != null)
            textMaterial.mainTexture = font.material.mainTexture;
    }

    public void SetTranscript(string value)
    {
        SetContent(value, false);
    }

    public void SetStatus(string value)
    {
        SetContent(value, true);
    }

    void SetContent(string value, bool isStatus)
    {
        value = value ?? "";
        if (!plain && transcript == value && status == isStatus) return;
        plain = false;
        status = isStatus;
        transcript = value;
        pages.Clear();
        // Preserve complete speech and guidance; each panel has its own page size.
        var lines = Wrap(value, Math.Max(8, lineWidth));
        int pageSize = Math.Max(1, linesPerPage);
        for (int i = 0; i < lines.Count; i += pageSize)
            pages.Add(string.Join("\n", lines.GetRange(i, Math.Min(pageSize, lines.Count - i))));
        page = 0;
        pageStarted = Time.unscaledTime;
        ShowPage(0);
    }

    public void ShowPage(int index)
    {
        if (caption == null || background == null) return;
        plain = false;
        page = Mathf.Clamp(index, 0, Math.Max(0, pages.Count - 1));
        caption.text = pages.Count > 0 ? pages[page] : "";
        bool quotedSpeech = !leftAligned && !status && !string.IsNullOrWhiteSpace(caption.text);
        if (quotedSpeech) caption.text = "“" + caption.text + "”";
        caption.richText = false;
        bool alignLeft = leftAligned && !status;
        caption.anchor = alignLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
        caption.alignment = alignLeft ? TextAlignment.Left : TextAlignment.Center;
        caption.fontStyle = FontStyle.Normal;
        caption.fontSize = 96;
        caption.lineSpacing = leftAligned ? 1.20f : 1.08f;
        caption.color = quotedSpeech ? Color.white : status ? new Color(.20f, .23f, .27f, 1) : new Color(.065f, .075f, .09f, 1);
        if (textMaterial == null)
        {
            var template = Resources.Load<Material>(Root + "SpeakerBubbleText");
            if (template != null)
            {
                textMaterial = new Material(template) { hideFlags = HideFlags.DontSave };
                caption.GetComponent<Renderer>().sharedMaterial = textMaterial;
            }
        }
        else caption.GetComponent<Renderer>().sharedMaterial = textMaterial;
        if (textMaterial != null && !leftAligned)
        {
            textMaterial.SetFloat("_OutlinePixels", quotedSpeech ? .65f : 0);
            textMaterial.SetColor("_OutlineColor", quotedSpeech ? new Color(.03f, .04f, .055f, .6f) : Color.clear);
        }
        if (caption.font != null)
        {
            caption.font.RequestCharactersInTexture(caption.text, 96, FontStyle.Normal);
            FontRebuilt(caption.font);
        }
        float height = ArtworkHeight;
        // Keep the original aspect ratio and tail. The material controls the glass opacity.
        background.transform.localPosition = new Vector3((.5f - tailPivot.x) * width, (.5f - tailPivot.y) * height, .004f);
        background.transform.localRotation = Quaternion.identity;
        background.transform.localScale = new Vector3(width, height, 1);
        float textX = alignLeft ? captionBounds.xMin : captionBounds.center.x;
        caption.transform.localPosition = new Vector3((textX - tailPivot.x) * width, (captionBounds.center.y - tailPivot.y) * height, 0);
        caption.transform.localRotation = Quaternion.identity;
        caption.characterSize = .01f;
        var bounds = caption.GetComponent<Renderer>().localBounds.size;
        if (bounds.x > 0 && bounds.y > 0)
        {
            int lineCount = caption.text.Split('\n').Length;
            float maxHeight = Mathf.Min(height * captionBounds.height, maximumLineHeight * lineCount * 1.12f);
            caption.characterSize *= Mathf.Min(width * captionBounds.width / bounds.x, maxHeight / bounds.y);
        }
        background.gameObject.SetActive(!string.IsNullOrWhiteSpace(caption.text));
    }

    public void SetPlainCaption(string value)
    {
        plain = true;
        transcript = null;
        if (background != null) background.gameObject.SetActive(false);
        caption.transform.localPosition = Vector3.zero;
        caption.color = Color.white;
        caption.alignment = TextAlignment.Center;
        caption.characterSize = .01f;
        caption.text = value;
        caption.GetComponent<Renderer>().sharedMaterial = plainMaterial;
        caption.font.RequestCharactersInTexture(value, 96, FontStyle.Normal);
        if (plainMaterial != null) plainMaterial.mainTexture = caption.font.material.mainTexture;
    }

    void Update()
    {
        if (!plain && pages.Count > 1 && (loopPages || page < pages.Count - 1) && Time.unscaledTime - pageStarted >= secondsPerPage)
        {
            ShowPage((page + 1) % pages.Count);
            pageStarted = Time.unscaledTime;
        }
    }

    static List<string> Wrap(string value, int width)
    {
        var tokens = new List<string>();
        var word = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(value.Replace("\r\n", "\n"));
        while (elements.MoveNext())
        {
            string part = elements.GetTextElement();
            bool latin = part.Length == 1 && (part[0] >= 'a' && part[0] <= 'z' || part[0] >= 'A' && part[0] <= 'Z' || char.IsDigit(part[0]) || part == "'" || part == "’");
            if (latin) { word.Append(part); continue; }
            if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); }
            tokens.Add(part);
        }
        if (word.Length > 0) tokens.Add(word.ToString());
        var result = new List<string>();
        var line = new StringBuilder();
        float used = 0;
        foreach (string token in tokens)
        {
            if (token == "\n") { result.Add(line.ToString().Trim()); line.Clear(); used = 0; continue; }
            foreach (string fragment in SplitLongToken(token, width))
            {
                bool space = string.IsNullOrWhiteSpace(fragment);
                if (space && line.Length == 0) continue;
                float length = Units(fragment);
                if (used + length > width && line.Length > 0)
                {
                    result.Add(line.ToString().Trim()); line.Clear(); used = 0;
                    if (space) continue;
                }
                line.Append(fragment); used += length;
            }
        }
        if (line.Length > 0) result.Add(line.ToString().Trim());
        return result;
    }

    static float Units(string value)
    {
        float units = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext()) { string part = elements.GetTextElement(); units += part.Length == 1 && part[0] < 256 ? .55f : 1; }
        return units;
    }

    static IEnumerable<string> SplitLongToken(string token, int width)
    {
        if (Units(token) <= width) { yield return token; yield break; }
        var piece = new StringBuilder(); float used = 0;
        var elements = StringInfo.GetTextElementEnumerator(token);
        while (elements.MoveNext())
        {
            string part = elements.GetTextElement(); float length = Units(part);
            if (used + length > width && piece.Length > 0) { yield return piece.ToString(); piece.Clear(); used = 0; }
            piece.Append(part); used += length;
        }
        if (piece.Length > 0) yield return piece.ToString();
    }

    void OnDestroy()
    {
        if (textMaterial == null) return;
        if (Application.isPlaying) Destroy(textMaterial); else DestroyImmediate(textMaterial);
    }
}
