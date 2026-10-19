using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The 화질 setting (설정 on the title, ESC menu in game): five levels from 최저사양 to 최고사양, saved per PC.
/// Applies to a runtime copy of the URP asset (never the project asset) plus a few QualitySettings and the SSAO feature.
/// The first run picks a level from the GPU's memory. Not applied in batch mode (servers, QA).
/// </summary>
public static class GraphicsQuality
{
    public static readonly string[] Names = { "최저사양", "저사양", "일반", "고사양", "최고사양" };
    const string Key = "Graphics.Level";

    struct Preset
    {
        public float renderScale, shadowDistance, lodBias;
        public int msaa, shadowRes, cascades, extraLights, textureLimit;
        public bool hdr, ssao;
        public AnisotropicFiltering aniso;
    }

    // 고사양 is close to the project asset as authored (MSAA 4, 2048 shadows, 4 cascades, SSAO).
    static readonly Preset[] Presets =
    {
        new Preset { renderScale = 0.5f,  msaa = 1, hdr = false, shadowDistance = 0,   shadowRes = 512,  cascades = 1, extraLights = 1, ssao = false, textureLimit = 1, lodBias = 0.6f, aniso = AnisotropicFiltering.Disable },
        new Preset { renderScale = 0.75f, msaa = 1, hdr = false, shadowDistance = 30,  shadowRes = 1024, cascades = 1, extraLights = 2, ssao = false, textureLimit = 0, lodBias = 1f,   aniso = AnisotropicFiltering.Disable },
        new Preset { renderScale = 1f,    msaa = 2, hdr = true,  shadowDistance = 60,  shadowRes = 2048, cascades = 2, extraLights = 4, ssao = true,  textureLimit = 0, lodBias = 1.5f, aniso = AnisotropicFiltering.Enable },
        new Preset { renderScale = 1f,    msaa = 4, hdr = true,  shadowDistance = 100, shadowRes = 2048, cascades = 4, extraLights = 4, ssao = true,  textureLimit = 0, lodBias = 2f,   aniso = AnisotropicFiltering.ForceEnable },
        new Preset { renderScale = 1f,    msaa = 8, hdr = true,  shadowDistance = 160, shadowRes = 4096, cascades = 4, extraLights = 8, ssao = true,  textureLimit = 0, lodBias = 3f,   aniso = AnisotropicFiltering.ForceEnable },
    };

    static UniversalRenderPipelineAsset baseAsset, copy;

    public static int Level { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        // ponytail: GPU memory only (Intel/integrated report ~1.5GB). Add a GPU-name list if it guesses wrong.
        int vram = SystemInfo.graphicsMemorySize, auto = vram < 2048 ? 1 : vram < 4096 ? 2 : 3;
        Level = Mathf.Clamp(PlayerPrefs.GetInt(Key, auto), 0, Presets.Length - 1);
        if (!Application.isBatchMode) Set(Level);
    }

    public static void Set(int level)
    {
        Level = Mathf.Clamp(level, 0, Presets.Length - 1);
        PlayerPrefs.SetInt(Key, Level);
        PlayerPrefs.Save();
        var p = Presets[Level];
#if UNITY_EDITOR
        SaveEditorState();
#endif

        if (!baseAsset) baseAsset = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        if (baseAsset)
        {
            if (!copy) { copy = Object.Instantiate(baseAsset); copy.name = baseAsset.name + " (화질)"; }
            copy.renderScale = p.renderScale;
            copy.msaaSampleCount = p.msaa;
            copy.supportsHDR = p.hdr;
            copy.shadowDistance = p.shadowDistance; // 0 = no shadows
            copy.mainLightShadowmapResolution = p.shadowRes;
            copy.additionalLightsShadowmapResolution = Mathf.Min(p.shadowRes, 2048);
            copy.shadowCascadeCount = p.cascades;
            copy.maxAdditionalLightsCount = p.extraLights;
            QualitySettings.renderPipeline = copy;
            foreach (var data in baseAsset.rendererDataList)
                if (data) foreach (var feature in data.rendererFeatures)
                    if (feature is ScreenSpaceAmbientOcclusion) feature.SetActive(p.ssao);
        }
        QualitySettings.globalTextureMipmapLimit = p.textureLimit;
        QualitySettings.lodBias = p.lodBias;
        QualitySettings.anisotropicFiltering = p.aniso;
    }

#if UNITY_EDITOR
    // In the editor these are the project's own settings (QualitySettings.asset, the renderer data's SSAO) and would be
    // saved with the runtime values: put them back when play mode (or a QA run) ends.
    static System.Action restore;

    static void SaveEditorState()
    {
        if (restore != null) return;
        var pipeline = QualitySettings.renderPipeline;
        int mip = QualitySettings.globalTextureMipmapLimit;
        float lod = QualitySettings.lodBias;
        var aniso = QualitySettings.anisotropicFiltering;
        var ssao = new System.Collections.Generic.List<(ScriptableRendererFeature, bool)>();
        if (pipeline is UniversalRenderPipelineAsset urp)
            foreach (var data in urp.rendererDataList)
                if (data) foreach (var f in data.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion) ssao.Add((f, f.isActive));
        restore = () =>
        {
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.globalTextureMipmapLimit = mip; QualitySettings.lodBias = lod; QualitySettings.anisotropicFiltering = aniso;
            foreach (var (f, on) in ssao) f.SetActive(on);
            restore = null; baseAsset = null; copy = null;
        };
        Application.quitting += RestoreEditor;
    }

    /// <summary>Editor only: the project's settings as they were before the first <see cref="Set"/>.</summary>
    public static void RestoreEditor()
    {
        Application.quitting -= RestoreEditor;
        restore?.Invoke();
    }
#endif
}
