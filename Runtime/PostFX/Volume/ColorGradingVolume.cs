using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public sealed class ToneMappingModeParameter
        : VolumeParameter<ColorGradingEffect.ToneMappingSettings.Mode>
    {
        public ToneMappingModeParameter(
            ColorGradingEffect.ToneMappingSettings.Mode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [VolumeComponentMenu("TaoTie RP/Color Grading")]
    public class ColorGradingVolume : VolumeComponent
    {
        [Header("Tone Mapping")]
        public ToneMappingModeParameter toneMappingMode =
            new(ColorGradingEffect.ToneMappingSettings.Mode.ACES);

        [Header("Color Adjustments")]
        public FloatParameter postExposure = new(0f);
        public ClampedFloatParameter contrast = new(0f, -100f, 100f);
        public ColorParameter colorFilter = new(Color.white, false, true, true);
        public ClampedFloatParameter hueShift = new(0f, -180f, 180f);
        public ClampedFloatParameter saturation = new(0f, -100f, 100f);

        [Header("White Balance")]
        public ClampedFloatParameter temperature = new(0f, -100f, 100f);
        public ClampedFloatParameter tint = new(0f, -100f, 100f);

        [Header("Split Toning")]
        public ColorParameter splitToningShadows = new(Color.gray, false, false, true);
        public ColorParameter splitToningHighlights = new(Color.gray, false, false, true);
        public ClampedFloatParameter splitToningBalance = new(0f, -100f, 100f);

        [Header("Channel Mixer")]
        public Vector3Parameter channelMixerRed = new(Vector3.right);
        public Vector3Parameter channelMixerGreen = new(Vector3.up);
        public Vector3Parameter channelMixerBlue = new(Vector3.forward);

        [Header("Shadows Midtones Highlights")]
        public ColorParameter smhShadows =
            new(new Color(0.7490196f, 0.6156863f, 1f, 1f), false, true, true);
        public ColorParameter smhMidtones =
            new(new Color(1f, 0.8509804f, 0.8509804f, 1f), false, true, true);
        public ColorParameter smhHighlights =
            new(new Color(1f, 0.89411765f, 0.7921569f, 1f), false, true, true);
        public ClampedFloatParameter smhShadowsStart = new(0f, 0f, 2f);
        public ClampedFloatParameter smhShadowsEnd = new(0.3f, 0f, 2f);
        public ClampedFloatParameter smhHighlightsStart = new(0.55f, 0f, 2f);
        public ClampedFloatParameter smhHighlightsEnd = new(1f, 0f, 2f);
    }
}
