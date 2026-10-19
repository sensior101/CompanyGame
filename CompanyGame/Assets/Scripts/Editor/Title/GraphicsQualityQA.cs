using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// QA of the 화질 setting (GraphicsQuality): every level applies to a runtime copy, the project URP asset is untouched.
///   -executeMethod GraphicsQualityQA.Run   (with -quit)
/// </summary>
public static class GraphicsQualityQA
{
    public static void Run()
    {
        int saved = PlayerPrefs.GetInt("Graphics.Level", -1);
        var project = (UniversalRenderPipelineAsset)(QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline);
        string before = EditorJsonUtility.ToJson(project);
        try
        {
            float[] scales = { 0.5f, 0.75f, 1f, 1f, 1f };
            int[] msaa = { 1, 1, 2, 4, 8 };
            float[] shadows = { 0, 30, 60, 100, 160 };
            for (int level = 0; level < 5; level++)
            {
                GraphicsQuality.Set(level);
                var copy = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
                Check(copy && copy != project, level + ": a runtime copy is the active pipeline");
                Check(Mathf.Approximately(copy.renderScale, scales[level]) && copy.msaaSampleCount == msaa[level]
                      && Mathf.Approximately(copy.shadowDistance, shadows[level]), level + ": render scale, MSAA, shadow distance");
                Check(PlayerPrefs.GetInt("Graphics.Level") == level, level + ": saved");
            }
            GraphicsQuality.Set(99);
            Check(GraphicsQuality.Level == 4, "out of range clamps to 최고사양");
            Check(EditorJsonUtility.ToJson(project) == before, "the project URP asset is unchanged");
            float lod = QualitySettings.lodBias;
            GraphicsQuality.RestoreEditor();
            Check(QualitySettings.renderPipeline == project && QualitySettings.lodBias != lod, "RestoreEditor puts the project's pipeline and quality settings back");
            Debug.Log("[GraphicsQualityQA] RESULT passed");
        }
        finally
        {
            GraphicsQuality.RestoreEditor();
            if (saved < 0) PlayerPrefs.DeleteKey("Graphics.Level"); else PlayerPrefs.SetInt("Graphics.Level", saved);
        }
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[GraphicsQualityQA] FAILED: " + what);
        Debug.Log("[GraphicsQualityQA] ok " + what);
    }
}
