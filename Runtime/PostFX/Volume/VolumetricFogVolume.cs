using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Volumetric Fog")]
    public class VolumetricFogVolume : VolumeComponent
    {
        public ClampedIntParameter sampleCount = new(32, 1, 64);
        public ClampedFloatParameter minStep = new(0.2f, 0.01f, 5f);
        public ClampedFloatParameter maxStep = new(3f, 0.1f, 20f);
        public ClampedFloatParameter stepIncrement = new(1.3f, 1f, 3f);
        public ClampedFloatParameter jitter = new(0.5f, 0f, 2f);
        public ClampedFloatParameter scattering = new(0.5f, 0f, 5f);
        public ClampedFloatParameter extinction = new(0.5f, 0f, 5f);
        public ClampedFloatParameter mieG = new(0.3f, 0f, 0.99f);
        public ClampedFloatParameter density = new(0.5f, 0f, 1f);
        public ClampedFloatParameter maxDistance = new(200f, 10f, 2000f);
        public ColorParameter color = new(new Color(0.5f, 0.6f, 0.7f, 1f), false, true, true);
        public ClampedFloatParameter fogBaseHeight = new(0f, -100f, 500f);
        public ClampedFloatParameter heightFalloff = new(0.1f, 0f, 2f);
    }
}
