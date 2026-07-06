using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class ShadowSettings
    {
        public enum MapSize
        {
            [LabelText("256")] _256 = 256,
            [LabelText("512")] _512 = 512,
            [LabelText("1024")] _1024 = 1024,
            [LabelText("2048")] _2048 = 2048,
            [LabelText("4096")] _4096 = 4096,
            [LabelText("8192")] _8192 = 8192
        }

        [Min(0.001f)]
        public float maxDistance = 30f;

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
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [EnumLabel]
            public MapSize atlasSize;
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [Range(1, 4)] public int cascadeCount;
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 1)]
            [Range(0f, 1f)] public float cascadeRatio1;
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 2)]
            [Range(0f, 1f)] public float cascadeRatio2;
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 3)]
            [Range(0f, 1f)] public float cascadeRatio3;

            public readonly Vector3 CascadeRatios =>
                new(cascadeRatio1, cascadeRatio2, cascadeRatio3);
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 1)]
            [Range(0.001f, 1f)] public float cascadeFade;
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            public bool softCascadeBlend;
        }

        public Directional directional = new()
        {
            maxLightCount = 1,
            atlasSize = MapSize._4096,
            cascadeCount = 2,
            cascadeRatio1 = 0.3f,
            cascadeRatio2 = 0.4f,
            cascadeRatio3 = 0.5f,
            cascadeFade = 0.1f,
        };

        [System.Serializable]
        public struct Other
        {
            [Range(0, 128)]
            [Tooltip("Maximum allowed lights per tile")]
            public int maxLightsPerTile;
            
            public enum TileSize
            {
                Default, Small = 32, Normal = 64, Mid = 128, Large = 256, Off = 8192
            }
            [ShowIf(nameof(maxLightsPerTile), ShowIfOperator.NotEqual, 0)]
            [Tooltip("Tile size in pixels per dimension, default is Normal.")]
            public TileSize tileSize;
            [ShowIf(nameof(maxLightsPerTile), ShowIfOperator.NotEqual, 0)]
            [EnumLabel]
            public MapSize atlasSize;
        }
        [Tooltip("In the Forward rendering path, support is provided for up to 8 other lights. When Forward+ is enabled, the maximum number of supported other lights increases to 256.")]
        public bool useForwardPlus = true;
        [ShowIf(nameof(useForwardPlus))]
        public Other other = new()
        {
            maxLightsPerTile = 128,
            atlasSize = MapSize._1024,
        };
    }
}