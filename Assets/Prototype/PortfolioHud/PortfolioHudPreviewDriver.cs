using System.Text;
using UnityEngine;

// A desktop material study. It does not connect to the conversation service.
[ExecuteAlways]
public sealed class PortfolioHudPreviewDriver : MonoBehaviour
{
    public Camera previewCamera;
    public Material[] panelMaterials;
    public Material textMaterial;
    public Font captionFont;
    public TextMesh[] captions;

    void OnEnable()
    {
        Font.textureRebuilt += FontRebuilt;
        RefreshAtlas();
    }

    void OnDisable() { Font.textureRebuilt -= FontRebuilt; }
    void FontRebuilt(Font font) { if (font == captionFont) BindAtlas(); }

    public void RefreshAtlas()
    {
        if (captionFont == null || captions == null) return;
        var characters = new StringBuilder();
        foreach (var caption in captions)
            if (caption != null) characters.Append(caption.text);
        captionFont.RequestCharactersInTexture(characters.ToString(), 96, FontStyle.Normal);
        BindAtlas();
    }

    void BindAtlas()
    {
        if (captionFont != null && textMaterial != null)
            textMaterial.mainTexture = captionFont.material.mainTexture;
    }

    public void SetPreviewTime(float time)
    {
        if (panelMaterials != null)
            foreach (var material in panelMaterials)
                if (material != null) material.SetFloat("_ShowcaseTime", time);
        if (textMaterial != null) textMaterial.SetFloat("_ShowcaseTime", time);
        BindAtlas();
    }
}
