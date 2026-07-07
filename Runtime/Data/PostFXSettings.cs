using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEditor;

namespace TaoTie.RenderPipelines
{
    public class PostFXSettings : ScriptableObject
    {
        [SerializeReference]
        [SerializeField]
        List<PostFXEffect> effects = new();

        public IReadOnlyList<PostFXEffect> Effects => effects;

        // 旧字段保留用于自动迁移 (使用效果类中的新类型)
        [HideInInspector] [SerializeField] BloomEffect.BloomSettings bloom;
        [HideInInspector] [SerializeField] ColorGradingEffect.ToneMappingSettings toneMapping;
        [HideInInspector] [SerializeField] ColorGradingEffect.ColorAdjustmentsSettings colorAdjustments;
        [HideInInspector] [SerializeField] ColorGradingEffect.WhiteBalanceSettings whiteBalance;
        [HideInInspector] [SerializeField] ColorGradingEffect.SplitToningSettings splitToning;
        [HideInInspector] [SerializeField] ColorGradingEffect.ChannelMixerSettings channelMixer;
        [HideInInspector] [SerializeField] ColorGradingEffect.ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights;

        [HideInInspector]
        public Shader shader = default;

        [System.NonSerialized] Material material;

        public Material Material
        {
            get
            {
                if (shader == null || shader.name != "Hidden/TaoTie RP/Post FX Stack")
                {
                    shader = Shader.Find("Hidden/TaoTie RP/Post FX Stack");
                    if (shader == null || shader.name != "Hidden/TaoTie RP/Post FX Stack")
                    {
                        Debug.LogError("Hidden/TaoTie RP/Post FX Stack shader not found!");
                        return null;
                    }
                    // shader changed — discard stale material so it gets recreated
                    material = null;
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
            return true;
        }

        void OnEnable()
        {
            if (effects == null || effects.Count == 0)
            {
                effects = new List<PostFXEffect>();
                effects.Add(new BloomEffect { settings = bloom });
                effects.Add(new ColorGradingEffect
                {
                    toneMapping = toneMapping,
                    colorAdjustments = colorAdjustments,
                    whiteBalance = whiteBalance,
                    splitToning = splitToning,
                    channelMixer = channelMixer,
                    shadowsMidtonesHighlights = shadowsMidtonesHighlights
                });
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            UnityEditor.SceneView.RepaintAll();
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }
}
