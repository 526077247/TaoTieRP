using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class ColorGradingEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Color LUT");

        static readonly string[] requiredPasses =
        {
            PostFXPassNames.ColorGradingNone,
            PostFXPassNames.ColorGradingACES,
            PostFXPassNames.ColorGradingNeutral,
            PostFXPassNames.ColorGradingReinhard
        };

        static readonly string[] colorGradingPassNames =
        {
            PostFXPassNames.ColorGradingNone,
            PostFXPassNames.ColorGradingACES,
            PostFXPassNames.ColorGradingNeutral,
            PostFXPassNames.ColorGradingReinhard
        };

        static GraphicsFormat? colorFormatCache;
        static GraphicsFormat ColorFormat => colorFormatCache ??=
            SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

        static readonly int
            colorGradingLUTId = Shader.PropertyToID("_ColorGradingLUT"),
            colorGradingLUTParametersId = Shader.PropertyToID("_ColorGradingLUTParameters"),
            colorGradingLUTInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC"),
            colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
            colorFilterId = Shader.PropertyToID("_ColorFilter"),
            whiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
            splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
            splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
            channelMixerRedId = Shader.PropertyToID("_ChannelMixerRed"),
            channelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen"),
            channelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue"),
            smhShadowsId = Shader.PropertyToID("_SMHShadows"),
            smhMidtonesId = Shader.PropertyToID("_SMHMidtones"),
            smhHighlightsId = Shader.PropertyToID("_SMHHighlights"),
            smhRangeId = Shader.PropertyToID("_SMHRange");

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

        [System.Serializable]
        public struct ColorAdjustmentsSettings
        {
            public float postExposure;
            [Range(-100f, 100f)] public float contrast;
            [ColorUsage(false, true)] public Color colorFilter;
            [Range(-180f, 180f)] public float hueShift;
            [Range(-100f, 100f)] public float saturation;
        }

        [System.Serializable]
        public struct WhiteBalanceSettings
        {
            [Range(-100f, 100f)] public float temperature, tint;
        }

        [System.Serializable]
        public struct SplitToningSettings
        {
            [ColorUsage(false)] public Color shadows, highlights;
            [Range(-100f, 100f)] public float balance;
        }

        [System.Serializable]
        public struct ChannelMixerSettings
        {
            public Vector3 red, green, blue;
        }

        [System.Serializable]
        public struct ShadowsMidtonesHighlightsSettings
        {
            [ColorUsage(false, true)] public Color shadows, midtones, highlights;
            [Range(0f, 2f)] public float shadowsStart, shadowsEnd, highlightsStart, highLightsEnd;
        }

        [SerializeField] public ToneMappingSettings toneMapping = new ToneMappingSettings
        {
            mode = ToneMappingSettings.Mode.ACES
        };
        public ToneMappingSettings ToneMapping => toneMapping;

        [SerializeField] public ColorAdjustmentsSettings colorAdjustments = new ColorAdjustmentsSettings
        {
            colorFilter = Color.white,
            saturation = 28f
        };
        public ColorAdjustmentsSettings ColorAdjustments => colorAdjustments;

        [SerializeField] public WhiteBalanceSettings whiteBalance = default;
        public WhiteBalanceSettings WhiteBalance => whiteBalance;

        [SerializeField] public SplitToningSettings splitToning = new SplitToningSettings
        {
            shadows = Color.gray,
            highlights = Color.gray
        };
        public SplitToningSettings SplitToning => splitToning;

        [SerializeField] public ChannelMixerSettings channelMixer = new ChannelMixerSettings
        {
            red = Vector3.right,
            green = Vector3.up,
            blue = Vector3.forward
        };
        public ChannelMixerSettings ChannelMixer => channelMixer;

        [SerializeField] public ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights =
            new ShadowsMidtonesHighlightsSettings
            {
                shadows = new Color(0.7490196f, 0.6156863f, 1f, 1f),
                midtones = new Color(1f, 0.8509804f, 0.8509804f, 1f),
                highlights = new Color(1f, 0.89411765f, 0.7921569f, 1f),
                shadowsEnd = 0.3f,
                highlightsStart = 0.55f,
                highLightsEnd = 1f
            };
        public ShadowsMidtonesHighlightsSettings ShadowsMidtonesHighlights => shadowsMidtonesHighlights;

        public override string DisplayName => "Color Grading";

        public override IReadOnlyList<string> RequiredPassNames => requiredPasses;

        /// <summary>是否启用了 ToneMapping（非 None 模式）</summary>
        public bool HasToneMapping => IsEnabled && toneMapping.mode != ToneMappingSettings.Mode.None;

        /// <summary>Execute 后生成的 Color LUT 纹理（如果 LUT 关闭则为 default）</summary>
        public TextureHandle ColorGradingLUT { get; private set; }

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            ColorGradingLUT = default;

            if (!IsEnabled || stack.ColorLUTResolution <= 0)
                return source;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out ColorGradingRenderPass pass, sampler);
            pass.stack = stack;
            pass.effect = this;
            pass.colorLUTResolution = stack.ColorLUTResolution;

            int lutHeight = stack.ColorLUTResolution;
            int lutWidth = lutHeight * lutHeight;
            var desc = new TextureDesc(lutWidth, lutHeight)
            {
                colorFormat = ColorFormat,
                filterMode = FilterMode.Bilinear,
                name = "Color LUT"
            };
            pass.colorLUT = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.SetRenderFunc<ColorGradingRenderPass>(
                static (pass, context) => pass.Render(context));

            ColorGradingLUT = pass.colorLUT;
            return source;
        }

        class ColorGradingRenderPass
        {
            public PostFXStack stack;
            public ColorGradingEffect effect;
            public int colorLUTResolution;
            public TextureHandle colorLUT;

            void ConfigureColorAdjustments(CommandBuffer buffer)
            {
                ColorAdjustmentsSettings ca = effect.ColorAdjustments;
                buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
                    Mathf.Pow(2f, ca.postExposure),
                    ca.contrast * 0.01f + 1f,
                    ca.hueShift * (1f / 360f),
                    ca.saturation * 0.01f + 1f));
                buffer.SetGlobalColor(colorFilterId, ca.colorFilter.linear);
            }

            void ConfigureWhiteBalance(CommandBuffer buffer)
            {
                WhiteBalanceSettings wb = effect.WhiteBalance;
                buffer.SetGlobalVector(whiteBalanceId,
                    ColorUtils.ColorBalanceToLMSCoeffs(wb.temperature, wb.tint));
            }

            void ConfigureSplitToning(CommandBuffer buffer)
            {
                SplitToningSettings st = effect.SplitToning;
                Color splitColor = st.shadows;
                splitColor.a = st.balance * 0.01f;
                buffer.SetGlobalColor(splitToningShadowsId, splitColor);
                buffer.SetGlobalColor(splitToningHighlightsId, st.highlights);
            }

            void ConfigureChannelMixer(CommandBuffer buffer)
            {
                ChannelMixerSettings cm = effect.ChannelMixer;
                buffer.SetGlobalVector(channelMixerRedId, cm.red);
                buffer.SetGlobalVector(channelMixerGreenId, cm.green);
                buffer.SetGlobalVector(channelMixerBlueId, cm.blue);
            }

            void ConfigureShadowsMidtonesHighlights(CommandBuffer buffer)
            {
                ShadowsMidtonesHighlightsSettings smh = effect.ShadowsMidtonesHighlights;
                buffer.SetGlobalColor(smhShadowsId, smh.shadows.linear);
                buffer.SetGlobalColor(smhMidtonesId, smh.midtones.linear);
                buffer.SetGlobalColor(smhHighlightsId, smh.highlights.linear);
                buffer.SetGlobalVector(smhRangeId, new Vector4(
                    smh.shadowsStart,
                    smh.shadowsEnd,
                    smh.highlightsStart,
                    smh.highLightsEnd));
            }

            public void Render(RenderGraphContext context)
            {
                CommandBuffer buffer = context.cmd;
                ConfigureColorAdjustments(buffer);
                ConfigureWhiteBalance(buffer);
                ConfigureSplitToning(buffer);
                ConfigureChannelMixer(buffer);
                ConfigureShadowsMidtonesHighlights(buffer);

                int lutHeight = colorLUTResolution;
                int lutWidth = lutHeight * lutHeight;
                buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
                    lutHeight,
                    0.5f / lutWidth, 0.5f / lutHeight,
                    lutHeight / (lutHeight - 1f)));

                ToneMappingSettings.Mode mode = effect.ToneMapping.mode;
                int passIndex = stack.GetPassIndex(colorGradingPassNames[(int) mode]);
                buffer.SetGlobalFloat(colorGradingLUTInLogId,
                    stack.BufferSettings.allowHDR && mode != ToneMappingSettings.Mode.None ? 1f : 0f);
                stack.Draw(buffer, colorLUT, passIndex);
                buffer.SetGlobalVector(colorGradingLUTParametersId,
                    new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f));
                buffer.SetGlobalTexture(colorGradingLUTId, colorLUT);
            }
        }
    }
}
