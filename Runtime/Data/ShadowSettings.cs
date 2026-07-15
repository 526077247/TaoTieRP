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
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.MinValue(0.001f)]
#else
        [Min(0.001f)]
#endif
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
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
#endif
            [EnumLabel]
            public MapSize atlasSize;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
#endif
            [Range(1, 4)] public int cascadeCount;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0&&"+nameof(cascadeCount)+">1")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 1)]
#endif
            [Range(0f, 1f)] public float cascadeRatio1;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0&&"+nameof(cascadeCount)+">2")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 2)]
#endif
            [Range(0f, 1f)] public float cascadeRatio2;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0&&"+nameof(cascadeCount)+">3")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 3)]
#endif
            [Range(0f, 1f)] public float cascadeRatio3;

            public readonly Vector3 CascadeRatios =>
                new(cascadeRatio1, cascadeRatio2, cascadeRatio3);
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0&&"+nameof(cascadeCount)+">1")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
            [ShowIf(nameof(cascadeCount), ShowIfOperator.GreaterThan, 1)]
#endif
            [Range(0.001f, 1f)] public float cascadeFade;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxLightCount)+"!=0")]
#else
            [ShowIf(nameof(maxLightCount), ShowIfOperator.NotEqual, 0)]
#endif
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
            [Range(0, 64)]
            [Tooltip("Maximum allowed lights per tile")]
            public int maxLightsPerTile;
            
            public enum TileSize
            {
                Default, 
                [LabelText("8")] _8 = 8, 
                [LabelText("16")] _16 = 16, 
                [LabelText("32")] _32 = 32, 
                [LabelText("64")] _64 = 64, 
                [LabelText("128")] _128 = 128, 
                [LabelText("256")] _256 = 256, 
                Off = 8192
            }
            [ShowIf(nameof(maxLightsPerTile), ShowIfOperator.NotEqual, 0)]
            [Tooltip("Tile size in pixels per dimension, default is 32.")]
            public TileSize tileSize;
            [ShowIf(nameof(maxLightsPerTile), ShowIfOperator.NotEqual, 0)]
            [EnumLabel]
            public MapSize atlasSize;
            [ShowIf(nameof(maxLightsPerTile), ShowIfOperator.NotEqual, 0)]
            [Range(8, 64)]
            [Tooltip("Number of depth bins for ZBin depth culling. " +
                     "ZBin bins lights by camera-space depth to reduce per-pixel light iterations. " +
                     "Note: 2.5D tile depth culling (compute shader depth sampling) only activates when " +
                     "DepthPrePass is enabled (e.g. via Forced mode, or in Forward path when " +
                     "SSAO, TAA, or MSAA depth priming is active). " +
                     "Without DepthPrePass, Forward+ falls back to pure 2D tile culling — ZBin still applies " +
                     "in the pixel shader, but the compute shader cannot skip occluded lights per tile.")]
            public int zBinCount;
        }
        public enum ForwardPlusMode
        {
            Off,
            Auto,
            Force
        }
        
        [Tooltip("Forward+ tile-based light culling mode. Auto enables it when other lights exceed maxOtherLights.")]
        public ForwardPlusMode forwardPlus = ForwardPlusMode.Auto;
        
        [ShowIf(nameof(forwardPlus), ShowIfOperator.Equal, (int)ForwardPlusMode.Off)]
        [Range(0, 64)]
        [Tooltip("Max per-pixel other lights. Excess Auto lights are demoted to per-vertex (Forward only). Deferred ignores this limit.")]
        public int maxOtherLights = 16;
        
        [ShowIf(nameof(forwardPlus), ShowIfOperator.NotEqual, (int)ForwardPlusMode.Off)]
        public Other other = new()
        {
            maxLightsPerTile = 128,
            atlasSize = MapSize._1024,
            zBinCount = 32,
        };

        public SSAOSettings ssao = new SSAOSettings();
    }
}