using UnityEngine;

// Supplies a local, blurred RGB camera sample to the two artwork masks. This is
// an approximation of the view behind a panel, not a copy of the runtime VST layer.
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class ConversationGlassCamera : MonoBehaviour
{
    public PicoSdkCameraCapture cameraSource;
    public Renderer[] panels;
    public Shader blurShader;
    [Range(128, 512)] public int sampleWidth = 320;
    [Range(.5f, 4f)] public float blurRadius = 2f;

    public string Mode { get; private set; } = "waiting";
    public int FrameId { get; private set; } = -1;
    public Vector4 PanelUV { get; private set; }
    // Camera acquisition age; the source enforces freshness before exposing it.
    public float FrameAgeMs => cameraSource != null ? cameraSource.GlassFrameAgeMs : -1;

    static readonly int BlurTexture = Shader.PropertyToID("_BlurTex");
    static readonly int WorldToCamera = Shader.PropertyToID("_GlassWorldToCamera");
    static readonly int Intrinsics = Shader.PropertyToID("_GlassIntrinsics");
    static readonly int FlipRows = Shader.PropertyToID("_GlassFlipRows");
    static readonly int Ready = Shader.PropertyToID("_CameraGlassReady");
    static readonly int Frame = Shader.PropertyToID("_GlassFrameId");
    static readonly int Radius = Shader.PropertyToID("_BlurRadius");
    MaterialPropertyBlock block;
    Material blurMaterial;
    RenderTexture intermediate, blurred;
    float readiness;
    int sourceWidth, sourceHeight;

    void OnEnable() { block = new MaterialPropertyBlock(); }

    void LateUpdate()
    {
        if (cameraSource == null || panels == null || panels.Length == 0)
        {
            Fallback("waiting_for_camera_or_panels");
            return;
        }
        if (!cameraSource.TryGetGlassFrame(out Texture2D source, out Matrix4x4 worldToOptical,
            out Vector4 intrinsics, out bool rowsReversed, out int frameId))
        {
            Fallback("camera_unavailable_or_stale");
            return;
        }
        if (source == null || source.width < 2 || source.height < 2 || !EnsureResources(source))
        {
            Fallback("blur_unavailable");
            return;
        }
        if (frameId != FrameId)
        {
            // One shared low-resolution camera texture serves both panels.
            // Only new camera frames trigger work; text remains unblurred.
            blurMaterial.SetFloat(Radius, blurRadius);
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                Graphics.Blit(source, intermediate, blurMaterial, 0);
                Graphics.Blit(intermediate, blurred, blurMaterial, 1);
                Graphics.Blit(blurred, intermediate, blurMaterial, 0);
                Graphics.Blit(intermediate, blurred, blurMaterial, 1);
            }
            finally { RenderTexture.active = previousTarget; }
            FrameId = frameId;
        }
        readiness = Mathf.MoveTowards(readiness, 1f, Time.unscaledDeltaTime * 6f);
        for (int panelIndex = 0; panelIndex < panels.Length; panelIndex++)
        {
            Renderer panel = panels[panelIndex];
            if (panel == null) continue;
            var optical = worldToOptical.MultiplyPoint3x4(panel.transform.position);
            float u = intrinsics.z + intrinsics.x * optical.x / Mathf.Max(.001f, optical.z);
            float v = 1 - (intrinsics.w - intrinsics.y * optical.y / Mathf.Max(.001f, optical.z));
            if (rowsReversed) v = 1 - v;
            var coordinates = PanelUV;
            if (panelIndex == 0) { coordinates.x = u; coordinates.y = v; }
            else if (panelIndex == 1) { coordinates.z = u; coordinates.w = v; }
            PanelUV = coordinates;
            panel.GetPropertyBlock(block);
            block.SetTexture(BlurTexture, blurred);
            block.SetMatrix(WorldToCamera, worldToOptical);
            block.SetVector(Intrinsics, intrinsics);
            block.SetFloat(FlipRows, rowsReversed ? 1f : 0f);
            block.SetFloat(Ready, readiness);
            block.SetFloat(Frame, frameId);
            panel.SetPropertyBlock(block);
        }
        SetMode("live_camera_sample");
    }

    bool EnsureResources(Texture2D source)
    {
        if (!SystemInfo.supportsRenderTextures) return false;
        if (blurMaterial == null)
        {
            if (blurShader == null) blurShader = Shader.Find("Hidden/Between Us/Camera Glass Blur");
            if (blurShader == null || !blurShader.isSupported) return false;
            blurMaterial = new Material(blurShader) { name = "Local camera glass blur", hideFlags = HideFlags.DontSave };
        }
        int width = Mathf.Clamp(sampleWidth, 128, 512);
        int height = Mathf.Max(2, Mathf.RoundToInt(width * source.height / (float)source.width));
        if (intermediate != null && blurred != null && intermediate.IsCreated() && blurred.IsCreated()
            && intermediate.width == width && intermediate.height == height
            && sourceWidth == source.width && sourceHeight == source.height) return true;
        ReleaseTextures();
        intermediate = MakeTexture(width, height, "Camera glass intermediate");
        blurred = MakeTexture(width, height, "Camera glass blurred");
        sourceWidth = source.width;
        sourceHeight = source.height;
        FrameId = -1;
        return intermediate.IsCreated() && blurred.IsCreated();
    }

    static RenderTexture MakeTexture(int width, int height, string name)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1,
            hideFlags = HideFlags.DontSave
        };
        texture.Create();
        return texture;
    }

    void Fallback(string mode)
    {
        // A stale image must never remain painted over a moving real scene.
        readiness = 0;
        ClearPanelOverrides();
        SetMode(mode);
    }

    void SetMode(string mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Debug.Log("CONVERSATION_GLASS mode=" + mode + " frame=" + FrameId);
    }

    void ClearPanelOverrides()
    {
        if (panels == null || block == null) return;
        foreach (Renderer panel in panels)
        {
            if (panel == null) continue;
            panel.GetPropertyBlock(block);
            // Unity's native MPB rejects a null texture. Readiness disables its
            // contribution; a built-in texture also releases the old RT binding.
            block.SetTexture(BlurTexture, Texture2D.whiteTexture);
            block.SetFloat(Ready, 0);
            block.SetFloat(Frame, -1);
            panel.SetPropertyBlock(block);
        }
    }

    void ReleaseTextures()
    {
        if (intermediate != null) { intermediate.Release(); Dispose(intermediate); intermediate = null; }
        if (blurred != null) { blurred.Release(); Dispose(blurred); blurred = null; }
    }

    static void Dispose(Object value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }

    void Release()
    {
        ClearPanelOverrides();
        ReleaseTextures();
        if (blurMaterial != null) { Dispose(blurMaterial); blurMaterial = null; }
        readiness = 0;
        FrameId = -1;
    }

    void OnDisable() { Release(); }
    void OnDestroy() { Release(); }
}
