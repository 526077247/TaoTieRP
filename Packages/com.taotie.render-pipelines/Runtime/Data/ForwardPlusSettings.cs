using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public struct ForwardPlusSettings
    {
#if UNITY_EDITOR
        private const string SHADER_CONTENT =
            "#ifndef TAOTIE_FORWARD_PLUS_SETTING_INCLUDED\r\n#define TAOTIE_FORWARD_PLUS_SETTING_INCLUDED\r\n#define MAX_TILES_COUNT {0}\r\n#endif";
        private const string CSHARP_CONTENT =
            "namespace TaoTie.RenderPipelines\r\n{{\r\npublic static class TaoTieRenderConst\r\n{{\r\npublic const int MAX_TILE_COUNT = {0};\r\n}}\r\n}}";
#endif
        public enum TileSize
        {
            Default, Small = 32, Normal = 64, Mid = 128, Large = 256, Off = 8192
        }
#if UNITY_EDITOR && ODIN_INSPECTOR
        [Sirenix.OdinInspector.OnValueChangedAttribute(nameof(Change))]
#endif
        [Tooltip("Tile size in pixels per dimension, default is Normal.")]
        public TileSize tileSize;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowIf("@"+nameof(tileSize)+"!=TileSize.Off")]
#endif
        [Range(0, 128)]
        [Tooltip("Maximum allowed lights per tile")]
        public int maxLightsPerTile;
#if UNITY_EDITOR
        public void Change()
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is TaoTieRenderPipelineAsset asset)
            {
                int count = asset.settings.forwardPlus.tileSize == TileSize.Off?1:1280;
                System.IO.File.WriteAllText(
                    "Packages/com.taotie.render-pipelines/ShaderLibrary/ForwardPlusSetting.hlsl",
                    string.Format(SHADER_CONTENT, count));
                System.IO.File.WriteAllText(
                    "Packages/com.taotie.render-pipelines/Runtime/TaoTieRenderConst.cs",
                    string.Format(CSHARP_CONTENT, count));
                UnityEditor.AssetDatabase.Refresh();
            }
        }
#endif
    }
}