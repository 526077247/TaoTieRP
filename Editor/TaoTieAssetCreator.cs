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
            var so = new SerializedObject(postFX);

            var shaderProp = so.FindProperty("shader");
            if (shaderProp != null)
                shaderProp.objectReferenceValue = Shader.Find("Hidden/TaoTie RP/Post FX Stack");

            // Cold preset overrides
            so.FindProperty("colorAdjustments.saturation").floatValue = 0f;
            so.FindProperty("whiteBalance.temperature").floatValue = -50f;
            so.FindProperty("toneMapping.mode").enumValueIndex = 2; // Neutral

            so.FindProperty("shadowsMidtonesHighlights.shadows").colorValue = Color.white;
            so.FindProperty("shadowsMidtonesHighlights.midtones").colorValue = Color.white;
            so.FindProperty("shadowsMidtonesHighlights.highlights").colorValue = Color.white;

            so.ApplyModifiedPropertiesWithoutUndo();
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
