using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class ShadowSettings
    {
        public enum MapSize
        {
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("256")]
#endif
            _256 = 256,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("512")]
#endif
            _512 = 512,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("1024")]
#endif
            _1024 = 1024,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("2048")]
#endif
            _2048 = 2048,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("4096")]
#endif
            _4096 = 4096,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("8192")]
#endif
            _8192 = 8192
        }

        public enum FilterMode
        {
            PCF2x2,
            PCF3x3,
            PCF5x5,
            PCF7x7
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.MinValue(0.001f)]
#else
        [Min(0.001f)] 
#endif
        public float maxDistance = 100f;

        [Range(0.001f, 1f)] public float distanceFade = 0.1f;

        public enum FilterQuality
        {
            Low,
            Medium,
            High
        }

        public FilterQuality filterQuality = FilterQuality.Medium;

        public float DirectionalFilterSize => (float) filterQuality + 2f;

        public float OtherFilterSize => (float) filterQuality + 2f;

        [System.Serializable]
        public struct Directional
        {
            [Range(0f, 4f)]
            public int maxLightCount;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#endif
            public MapSize atlasSize;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#endif
            [Range(1, 4)] public int cascadeCount;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0 && "+ nameof(cascadeCount) + ">1")]
#endif
            [Range(0f, 1f)] public float cascadeRatio1;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0 && "+ nameof(cascadeCount) + ">2")]
#endif
            [Range(0f, 1f)] public float cascadeRatio2;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0 && "+ nameof(cascadeCount) + ">3")]
#endif
            [Range(0f, 1f)] public float cascadeRatio3;

            public readonly Vector3 CascadeRatios =>
                new(cascadeRatio1, cascadeRatio2, cascadeRatio3);
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#endif
            [Range(0.001f, 1f)] public float cascadeFade;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#endif
            public bool softCascadeBlend;
        }

        public Directional directional = new()
        {
            maxLightCount = 4,
            atlasSize = MapSize._1024,
            cascadeCount = 4,
            cascadeRatio1 = 0.1f,
            cascadeRatio2 = 0.25f,
            cascadeRatio3 = 0.5f,
            cascadeFade = 0.1f,
        };

        [System.Serializable]
        public struct Other
        {
#if UNITY_EDITOR
            private const string SHADER_CONTENT =
                "#ifndef TAOTIE_FORWARD_PLUS_SETTING_INCLUDED\r\n#define TAOTIE_FORWARD_PLUS_SETTING_INCLUDED\r\n#define MAX_TILES_COUNT {0}\r\n#endif";
            private const string CSHARP_CONTENT =
                "namespace TaoTie.RenderPipelines\r\n{{\r\npublic static class TaoTieRenderConst\r\n{{\r\npublic const int MAX_TILE_COUNT = {0};\r\n}}\r\n}}";
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
            [Sirenix.OdinInspector.OnValueChangedAttribute(nameof(Change))]
#endif    
            [Range(0, 128)]
            [Tooltip("Maximum allowed lights per tile")]
            public int maxLightsPerTile;
            
            public enum TileSize
            {
                Default, Small = 32, Normal = 64, Mid = 128, Large = 256, Off = 8192
            }
#if UNITY_EDITOR && ODIN_INSPECTOR
            [Sirenix.OdinInspector.OnValueChangedAttribute(nameof(Change))]
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightsPerTile)+"!=0")]
#endif
            [Tooltip("Tile size in pixels per dimension, default is Normal.")]
            public TileSize tileSize;
            
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightsPerTile)+"!=0")]
#endif
            public MapSize atlasSize;
            
#if UNITY_EDITOR
            public void Change()
            {
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is TaoTieRenderPipelineAsset asset)
                {
                    int count = (asset.settings.shadows.other.tileSize == TileSize.Off
                                 || asset.settings.shadows.other.maxLightsPerTile == 0)
                        ? 1
                        : 1280;
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

        public Other other = new()
        {
            maxLightsPerTile = 128,
            atlasSize = MapSize._1024,
        };
    }
}