using UnityEngine;
using System;
using UnityEditor;

namespace TaoTie.RenderPipelines
{
    [CreateAssetMenu(menuName = "Rendering/TaoTie Post FX Settings")]
    public class PostFXSettings : ScriptableObject
    {

        [HideInInspector]
        [SerializeField] Shader shader = default;

        [Serializable]
        public struct BloomSettings
        {
            [Range(0f, 16f)] public int maxIterations;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            public bool ignoreRenderScale;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.MinValue(1)]
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#else
            [Min(1f)] 
#endif
           
            public int downscaleLimit;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            public bool bicubicUpsampling;

#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.MinValue(0)]
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#else
            [Min(0f)] 
#endif
            public float threshold;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            [Range(0f, 1f)] public float thresholdKnee;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.MinValue(0)]
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#else
            [Min(0f)] 
#endif
            public float intensity;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            public bool fadeFireflies;

            public enum Mode
            {
                Additive,
                Scattering
            }
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            public Mode mode;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf("@"+nameof(maxIterations)+"!=0")]
#endif
            [Range(0.05f, 0.95f)] public float scatter;
        }

        [SerializeField] BloomSettings bloom = new BloomSettings
        {
            scatter = 0.7f
        };

        public BloomSettings Bloom => bloom;

        [System.Serializable]
        public struct ToneMappingSettings
        {
            public enum Mode
            {
                None,
                ACES,
                Neutral,
                Reinhard
            }

            public Mode mode;
        }

        [Serializable]
        public struct ColorAdjustmentsSettings
        {
            public float postExposure;

            [Range(-100f, 100f)] public float contrast;

            [ColorUsage(false, true)] public Color colorFilter;

            [Range(-180f, 180f)] public float hueShift;

            [Range(-100f, 100f)] public float saturation;
        }

        [SerializeField] ColorAdjustmentsSettings colorAdjustments = new ColorAdjustmentsSettings
        {
            colorFilter = Color.white
        };

        public ColorAdjustmentsSettings ColorAdjustments => colorAdjustments;

        [Serializable]
        public struct WhiteBalanceSettings
        {

            [Range(-100f, 100f)] public float temperature, tint;
        }

        [SerializeField] WhiteBalanceSettings whiteBalance = default;

        public WhiteBalanceSettings WhiteBalance => whiteBalance;

        [Serializable]
        public struct SplitToningSettings
        {

            [ColorUsage(false)] public Color shadows, highlights;

            [Range(-100f, 100f)] public float balance;
        }

        [SerializeField] SplitToningSettings splitToning = new SplitToningSettings
        {
            shadows = Color.gray,
            highlights = Color.gray
        };

        public SplitToningSettings SplitToning => splitToning;

        [Serializable]
        public struct ChannelMixerSettings
        {

            public Vector3 red, green, blue;
        }

        [SerializeField] ChannelMixerSettings channelMixer = new ChannelMixerSettings
        {
            red = Vector3.right,
            green = Vector3.up,
            blue = Vector3.forward
        };

        public ChannelMixerSettings ChannelMixer => channelMixer;

        [Serializable]
        public struct ShadowsMidtonesHighlightsSettings
        {

            [ColorUsage(false, true)] public Color shadows, midtones, highlights;

            [Range(0f, 2f)] public float shadowsStart, shadowsEnd, highlightsStart, highLightsEnd;
        }

        [SerializeField] ShadowsMidtonesHighlightsSettings
            shadowsMidtonesHighlights = new ShadowsMidtonesHighlightsSettings
            {
                shadows = Color.white,
                midtones = Color.white,
                highlights = Color.white,
                shadowsEnd = 0.3f,
                highlightsStart = 0.55f,
                highLightsEnd = 1f
            };

        public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights =>
            shadowsMidtonesHighlights;

        [SerializeField] ToneMappingSettings toneMapping = default;

        public ToneMappingSettings ToneMapping => toneMapping;


        [System.NonSerialized] Material material;

        public Material Material
        {
            get
            {
                if (shader == null)
                {
                    shader = Shader.Find("Hidden/TaoTie RP/Post FX Stack");
                    if (shader == null)
                    {
                        Debug.LogError("Hidden/TaoTie RP/Post FX Stack shader not found!");
                    }
                }
                if (material == null && shader != null)
                {
                    material = new Material(shader);
                    material.hideFlags = HideFlags.HideAndDontSave;
                }

                return material;
            }
        }

        public static bool AreApplicableTo(Camera camera)
        {
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView &&
                !SceneView.currentDrawingSceneView.sceneViewState.showImageEffects)
            {
                return false;
            }
#endif
            return camera.cameraType <= CameraType.SceneView;
        }
    }
}