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
            public MapSize atlasSize;

            [Range(1, 4)] public int cascadeCount;

            [Range(0f, 1f)] public float cascadeRatio1, cascadeRatio2, cascadeRatio3;

            public readonly Vector3 CascadeRatios =>
                new(cascadeRatio1, cascadeRatio2, cascadeRatio3);

            [Range(0.001f, 1f)] public float cascadeFade;

            public bool softCascadeBlend;
        }

        public Directional directional = new()
        {
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
            public MapSize atlasSize;
        }

        public Other other = new()
        {
            atlasSize = MapSize._1024,
        };
    }
}