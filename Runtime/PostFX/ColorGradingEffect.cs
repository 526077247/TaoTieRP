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
            public float contrast;
            [ColorUsage(false, true)] public Color colorFilter;
            public float hueShift;
            public float saturation;
        }

        [System.Serializable]
        public struct WhiteBalanceSettings
        {
            public float temperature, tint;
        }

        [System.Serializable]
        public struct SplitToningSettings
        {
            [ColorUsage(false)] public Color shadows, highlights;
            public float balance;
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
            public float shadowsStart, shadowsEnd, highlightsStart, highLightsEnd;
        }

        [System.NonSerialized] public ToneMappingSettings toneMapping;
        [System.NonSerialized] public ColorAdjustmentsSettings colorAdjustments;
        [System.NonSerialized] public WhiteBalanceSettings whiteBalance;
        [System.NonSerialized] public SplitToningSettings splitToning;
        [System.NonSerialized] public ChannelMixerSettings channelMixer;
        [System.NonSerialized] public ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights;

        public override string DisplayName => "Color Grading";

        public override IReadOnlyList<string> RequiredPassNames => requiredPasses;

        /// <summary>Execute 后生成的 Color LUT 纹理（如�?LUT 关闭则为 default�?/summary>
        public TextureHandle ColorGradingLUT { get; private set; }

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            ColorGradingLUT = default;

            var vol = stack.GetActiveVolume<ColorGradingVolume>();
            if (vol == null) return source;

            toneMapping = new ToneMappingSettings { mode = vol.toneMappingMode.value };
            colorAdjustments = new ColorAdjustmentsSettings
            {
                postExposure = vol.postExposure.value,
                contrast = vol.contrast.value,
                colorFilter = vol.colorFilter.value,
                hueShift = vol.hueShift.value,
                saturation = vol.saturation.value
            };
            whiteBalance = new WhiteBalanceSettings
            {
                temperature = vol.temperature.value,
                tint = vol.tint.value
            };
            splitToning = new SplitToningSettings
            {
                shadows = vol.splitToningShadows.value,
                highlights = vol.splitToningHighlights.value,
                balance = vol.splitToningBalance.value
            };
            channelMixer = new ChannelMixerSettings
            {
                red = vol.channelMixerRed.value,
                green = vol.channelMixerGreen.value,
                blue = vol.channelMixerBlue.value
            };
            shadowsMidtonesHighlights = new ShadowsMidtonesHighlightsSettings
            {
                shadows = vol.smhShadows.value,
                midtones = vol.smhMidtones.value,
                highlights = vol.smhHighlights.value,
                shadowsStart = vol.smhShadowsStart.value,
                shadowsEnd = vol.smhShadowsEnd.value,
                highlightsStart = vol.smhHighlightsStart.value,
                highLightsEnd = vol.smhHighlightsEnd.value
            };

            if (!IsEnabled || stack.ColorLUTResolution <= 0)
                return source;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out ColorGradingRenderPass pass, sampler);
            pass.stack = stack;
            pass.toneMapping = toneMapping;
            pass.colorAdjustments = colorAdjustments;
            pass.whiteBalance = whiteBalance;
            pass.splitToning = splitToning;
            pass.channelMixer = channelMixer;
            pass.shadowsMidtonesHighlights = shadowsMidtonesHighlights;
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
            public ToneMappingSettings toneMapping;
            public ColorAdjustmentsSettings colorAdjustments;
            public WhiteBalanceSettings whiteBalance;
            public SplitToningSettings splitToning;
            public ChannelMixerSettings channelMixer;
            public ShadowsMidtonesHighlightsSettings shadowsMidtonesHighlights;
            public int colorLUTResolution;
            public TextureHandle colorLUT;

            void ConfigureColorAdjustments(CommandBuffer buffer)
            {
                ColorAdjustmentsSettings ca = colorAdjustments;
                buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
                    Mathf.Pow(2f, ca.postExposure),
                    ca.contrast * 0.01f + 1f,
                    ca.hueShift * (1f / 360f),
                    ca.saturation * 0.01f + 1f));
                buffer.SetGlobalColor(colorFilterId, ca.colorFilter.linear);
            }

            void ConfigureWhiteBalance(CommandBuffer buffer)
            {
                WhiteBalanceSettings wb = whiteBalance;
                buffer.SetGlobalVector(whiteBalanceId,
                    ColorUtils.ColorBalanceToLMSCoeffs(wb.temperature, wb.tint));
            }

            void ConfigureSplitToning(CommandBuffer buffer)
            {
                SplitToningSettings st = splitToning;
                Color splitColor = st.shadows;
                splitColor.a = st.balance * 0.01f;
                buffer.SetGlobalColor(splitToningShadowsId, splitColor);
                buffer.SetGlobalColor(splitToningHighlightsId, st.highlights);
            }

            void ConfigureChannelMixer(CommandBuffer buffer)
            {
                ChannelMixerSettings cm = channelMixer;
                buffer.SetGlobalVector(channelMixerRedId, cm.red);
                buffer.SetGlobalVector(channelMixerGreenId, cm.green);
                buffer.SetGlobalVector(channelMixerBlueId, cm.blue);
            }

            void ConfigureShadowsMidtonesHighlights(CommandBuffer buffer)
            {
                ShadowsMidtonesHighlightsSettings smh = shadowsMidtonesHighlights;
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

                ToneMappingSettings.Mode mode = toneMapping.mode;
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
