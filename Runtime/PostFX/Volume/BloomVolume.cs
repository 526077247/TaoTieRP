using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public sealed class BloomModeParameter : VolumeParameter<BloomEffect.BloomSettings.Mode>
    {
        public BloomModeParameter(BloomEffect.BloomSettings.Mode value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [VolumeComponentMenu("TaoTie RP/Bloom")]
    public class BloomVolume : VolumeComponent
    {
        public ClampedIntParameter maxIterations = new(16, 0, 16);

        public BoolParameter ignoreRenderScale = new(false);

        public ClampedIntParameter downscaleLimit = new(2, 1, 8);

        public BoolParameter bicubicUpsampling = new(true);

        public ClampedFloatParameter threshold = new(1f, 0f, float.MaxValue);

        public ClampedFloatParameter thresholdKnee = new(0.5f, 0f, 1f);

        public ClampedFloatParameter intensity = new(0.2f, 0f, 10);

        public BoolParameter fadeFireflies = new(true);

        public BloomModeParameter mode = new(BloomEffect.BloomSettings.Mode.Scattering);

        public ClampedFloatParameter scatter = new(0.7f, 0.05f, 0.95f);
    }
}
