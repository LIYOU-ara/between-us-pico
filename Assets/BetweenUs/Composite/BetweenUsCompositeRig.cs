using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Standalone desktop composition. No XR, camera capture or AI service dependencies.
[ExecuteAlways]
public sealed class BetweenUsCompositeRig : MonoBehaviour
{
    public Camera captureCamera;
    public Material[] glassMaterials;
    public Font regularFont, mediumFont;
    public TextMesh[] captions;
    public float previewTime = 3.2f;
    public bool animate = true;

    [Serializable]
    sealed class CurvedTextBinding
    {
        public TextMesh text;
        public Transform panel;
        public float radius;
    }
    [SerializeField] List<CurvedTextBinding> curvedTextBindings = new List<CurvedTextBinding>();
    Dictionary<Font, Material> fontMaterials = new Dictionary<Font, Material>();
    List<Material> ownedMaterials = new List<Material>();
    Dictionary<Renderer, Material> originalMaterials = new Dictionary<Renderer, Material>();
    MaterialPropertyBlock propertyBlock;
    bool refreshingAtlas;
    static readonly int ShowcaseTime = Shader.PropertyToID("_ShowcaseTime");
    static readonly int PanelWorldToLocal = Shader.PropertyToID("_PanelWorldToLocal");
    static readonly int PanelLocalToWorld = Shader.PropertyToID("_PanelLocalToWorld");
    static readonly int CurveRadius = Shader.PropertyToID("_CurveRadius");
    const string TextShaderName = "Between Us/Composite/Curved Text";

    void EnsureState()
    {
        if (curvedTextBindings == null) curvedTextBindings = new List<CurvedTextBinding>();
        if (fontMaterials == null) fontMaterials = new Dictionary<Font, Material>();
        if (ownedMaterials == null) ownedMaterials = new List<Material>();
        if (originalMaterials == null) originalMaterials = new Dictionary<Renderer, Material>();
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        EnsureState();
        if (Application.isPlaying && captureCamera != null) captureCamera.enabled = true;
        Font.textureRebuilt += FontRebuilt;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += EditorTick;
#endif
        RefreshAtlas();
        SetPreviewTime(previewTime);
    }

    void OnDisable()
    {
        EnsureState();
        Font.textureRebuilt -= FontRebuilt;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        foreach (var pair in originalMaterials)
            if (pair.Key != null && ownedMaterials.Contains(pair.Key.sharedMaterial))
                pair.Key.sharedMaterial = pair.Value;
        originalMaterials.Clear();
        foreach (var material in ownedMaterials)
        {
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
        ownedMaterials.Clear();
        fontMaterials.Clear();
    }

    void Update()
    {
        if (Application.isPlaying)
        {
            if (captureCamera != null && !captureCamera.enabled) captureCamera.enabled = true;
            SetPreviewTime(animate ? Time.realtimeSinceStartup : previewTime);
        }
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || UnityEditor.EditorApplication.isCompiling) return;
        SetPreviewTime(animate ? (float)UnityEditor.EditorApplication.timeSinceStartup : previewTime);
    }
#endif

    void FontRebuilt(Font font)
    {
        if (!refreshingAtlas) BindAtlases(font);
    }

    public void SetPreviewTime(float t)
    {
        EnsureState();
        if (float.IsNaN(t) || float.IsInfinity(t)) return;
        previewTime = t;
        if (glassMaterials != null)
            foreach (var material in glassMaterials)
                if (material != null && material.HasProperty(ShowcaseTime))
                    material.SetFloat(ShowcaseTime, t);
        BindAtlases(null);
        foreach (var binding in curvedTextBindings) ApplyBinding(binding);
    }

    public void RefreshAtlas()
    {
        EnsureState();
        if (refreshingAtlas) return;
        refreshingAtlas = true;
        try
        {
            var requested = new Dictionary<Font, StringBuilder>();
            if (captions != null)
                foreach (var text in captions)
                {
                    if (text == null) continue;
                    if (text.font == null) text.font = regularFont != null ? regularFont : mediumFont;
                    var font = text.font;
                    if (font == null) continue;
                    if (!requested.TryGetValue(font, out var characters))
                    {
                        characters = new StringBuilder();
                        requested.Add(font, characters);
                    }
                    characters.Append(text.text);
                    EnsureTextMaterial(text);
                }
            // Builder uses 96 px normal glyphs. Request each font's complete
            // character set together so atlas growth retains every caption.
            foreach (var pair in requested)
                pair.Key.RequestCharactersInTexture(pair.Value.ToString(), 96, FontStyle.Normal);
            BindAtlases(null);
        }
        finally { refreshingAtlas = false; }
    }

    void EnsureTextMaterial(TextMesh text)
    {
        EnsureState();
        var font = text.font;
        var renderer = text.GetComponent<Renderer>();
        if (font == null || renderer == null) return;
        if (!fontMaterials.TryGetValue(font, out var material) || material == null)
        {
            var candidate = renderer.sharedMaterial;
            bool usedByAnotherFont = false;
            foreach (var pair in fontMaterials)
                if (pair.Key != font && pair.Value == candidate) usedByAnotherFont = true;
            if (candidate != null && candidate.shader != null &&
                candidate.shader.name == TextShaderName && !usedByAnotherFont)
                material = candidate;
            else
            {
                var shader = Shader.Find(TextShaderName);
                if (shader == null) return;
                material = new Material(shader)
                {
                    name = "Composite text atlas / " + font.name,
                    hideFlags = HideFlags.HideAndDontSave
                };
                ownedMaterials.Add(material);
            }
            fontMaterials[font] = material;
        }
        if (renderer.sharedMaterial != material)
        {
            if (!originalMaterials.ContainsKey(renderer))
                originalMaterials.Add(renderer, renderer.sharedMaterial);
            renderer.sharedMaterial = material;
        }
        material.mainTexture = font.material.mainTexture;
    }

    void BindAtlases(Font changedFont)
    {
        EnsureState();
        foreach (var pair in fontMaterials)
            if (pair.Key != null && pair.Value != null && (changedFont == null || pair.Key == changedFont))
                pair.Value.mainTexture = pair.Key.material.mainTexture;
    }

    public void BindCurvedText(TextMesh text, Transform panel, float radius)
    {
        EnsureState();
        if (text == null || panel == null)
            throw new ArgumentNullException("Curved text and its panel are required.");
        var binding = curvedTextBindings.Find(item => item.text == text);
        if (binding == null)
        {
            binding = new CurvedTextBinding { text = text };
            curvedTextBindings.Add(binding);
        }
        binding.panel = panel;
        binding.radius = Mathf.Max(0, radius);
        EnsureTextMaterial(text);
        ApplyBinding(binding);
    }

    void ApplyBinding(CurvedTextBinding binding)
    {
        EnsureState();
        if (binding == null || binding.text == null || binding.panel == null) return;
        var renderer = binding.text.GetComponent<Renderer>();
        if (renderer == null) return;
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetMatrix(PanelWorldToLocal, binding.panel.worldToLocalMatrix);
        propertyBlock.SetMatrix(PanelLocalToWorld, binding.panel.localToWorldMatrix);
        propertyBlock.SetFloat(CurveRadius, binding.radius);
        renderer.SetPropertyBlock(propertyBlock);
    }

    public static Mesh MakeCurvedMesh(float width, float height, float radius, int segments = 96)
    {
        if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height) ||
            float.IsInfinity(width) || float.IsInfinity(height) || float.IsNaN(radius) || float.IsInfinity(radius))
            throw new ArgumentOutOfRangeException("Finite positive dimensions and a finite radius are required.");
        segments = Mathf.Clamp(segments, 4, 2048);
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int row = 0; row < 2; row++)
            for (int col = 0; col <= segments; col++)
            {
                float u = col / (float)segments;
                float flatX = (u - .5f) * width;
                float x = flatX, z = 0;
                if (radius > .0001f)
                {
                    float angle = flatX / radius;
                    x = radius * Mathf.Sin(angle);
                    z = -radius * (1 - Mathf.Cos(angle));
                }
                int index = row * (segments + 1) + col;
                vertices[index] = new Vector3(x, (row - .5f) * height, z);
                uv[index] = new Vector2(u, row);
            }
        for (int col = 0; col < segments; col++)
        {
            int a = col, b = segments + 1 + col, t = col * 6;
            triangles[t] = a; triangles[t + 1] = b; triangles[t + 2] = a + 1;
            triangles[t + 3] = a + 1; triangles[t + 4] = b; triangles[t + 5] = b + 1;
        }
        var mesh = new Mesh { name = "Between Us / curved composition panel" };
        mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}

