using System.IO;
using UnityEditor;
using UnityEngine;

namespace TaoTie.RenderPipelines.Editor
{
    public static class TaoTieAssetCreator
    {
        const string DefaultMaterialGUID = "154398cb26997bf4ebf4af5ff1db036e";

        [MenuItem("Assets/Create/Rendering/TaoTie Pipeline")]
        static void CreatePipelineAssets()
        {
            string folder = GetSelectedFolder();

            string postFXPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Post FX Settings.asset");
            string pipelinePath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Tao Tie RP.asset");

            var postFX = CreatePostFXSettingsAsset(postFXPath);

            // Create Pipeline Asset
            var pipelineAsset = ScriptableObject.CreateInstance<TaoTieRenderPipelineAsset>();
            pipelineAsset.settings.postFXSettings = postFX;

            var defaultMat = AssetDatabase.LoadAssetAtPath<Material>(
                AssetDatabase.GUIDToAssetPath(DefaultMaterialGUID));
            if (defaultMat != null)
                pipelineAsset.settings.defaultMaterial = defaultMat;

            pipelineAsset.settings.cameraRendererShader =
                Shader.Find("Hidden/TaoTie RP/Camera Renderer");

            AssetDatabase.CreateAsset(pipelineAsset, pipelinePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = pipelineAsset;
            Debug.Log($"Created TaoTie RP pipeline asset and Post FX Settings at {folder}");
        }

        [MenuItem("Assets/Create/Rendering/TaoTie Post FX Settings")]
        static void CreatePostFXSettings()
        {
            string folder = GetSelectedFolder();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Post FX Settings.asset");

            var postFX = CreatePostFXSettingsAsset(path);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = postFX;
            Debug.Log($"Created Post FX Settings (Cold preset) at {path}");
        }

        static PostFXSettings CreatePostFXSettingsAsset(string path)
        {
            var postFX = ScriptableObject.CreateInstance<PostFXSettings>();
            // OnEnable has been called — effects list is populated with default effects

            postFX.shader = Shader.Find("Hidden/TaoTie RP/Post FX Stack");

            // Apply Cold preset overrides via direct C# access
            foreach (var effect in postFX.Effects)
            {
                if (effect is ColorGradingEffect cg)
                {
                    cg.colorAdjustments.saturation = 0f;
                    cg.whiteBalance.temperature = -50f;
                    cg.toneMapping.mode = ColorGradingEffect.ToneMappingSettings.Mode.Neutral;
                    cg.shadowsMidtonesHighlights.shadows = Color.white;
                    cg.shadowsMidtonesHighlights.midtones = Color.white;
                    cg.shadowsMidtonesHighlights.highlights = Color.white;
                }
            }

            AssetDatabase.CreateAsset(postFX, path);
            return postFX;
        }

        static string GetSelectedFolder()
        {
            foreach (var obj in Selection.GetFiltered(typeof(DefaultAsset), SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                    return path;
            }
            return "Assets";
        }
    }
}
